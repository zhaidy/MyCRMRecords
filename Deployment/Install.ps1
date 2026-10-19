#Requires -Version 5.1
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$InstallPath = 'C:\ProgramData\MyCRMRecordsDeploy',
    [string]$Repository = 'zhaidy/MyCRMRecords',
    [string]$Destination = 'C:\inetpub\wwwroot\MyCRMRecords',
    [string]$SiteName = 'Default Web Site',
    [string]$ApplicationPath = '/MyCRMRecords',
    [string]$Url = 'https://apps.ricoh.sg/MyCRMRecords/'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Run the installer on the Windows IIS server.' }
$framework = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release -ErrorAction Stop
if ($framework.Release -lt 528040) { throw 'Install .NET Framework 4.8 on Windows Server 2016 first, then rerun the installer.' }
$taskName = 'MyCRMRecords - Deploy master'
$existing = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($existing -and $existing.State -eq 'Running') { throw 'The deployment task is running. Wait until it finishes before reinstalling.' }
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid repository name.' }
Import-Module WebAdministration -ErrorAction Stop
$app = @(Get-WebApplication -Site $SiteName | Where-Object { $_.Path -eq $ApplicationPath })
if ($app.Count -ne 1) { throw 'IIS application not found. Check SiteName and ApplicationPath.' }
$destinationRoot = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
$installRoot = [IO.Path]::GetFullPath($InstallPath).TrimEnd('\')
$cursor = $installRoot
while ($cursor) {
    if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Deployment installation cannot traverse directory links.' }
    $cursor = Split-Path $cursor -Parent
}
foreach ($website in Get-Website) {
    $publicRoot = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($website.PhysicalPath)).TrimEnd('\')
    if ($installRoot -eq $publicRoot -or $installRoot.StartsWith($publicRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Deployment installation must be outside every IIS website root.' }
}
if ([IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($app[0].PhysicalPath)).TrimEnd('\') -ne $destinationRoot) { throw 'Destination must match the IIS application physical path.' }
if ($installRoot -eq $destinationRoot -or $installRoot.StartsWith($destinationRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or $destinationRoot.StartsWith($installRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Deployment installation must be outside the website.' }
# First installation: the deployment expects a Web.config in the folder; every deployment then installs the release's
# copy (it holds no secrets). Server-only settings go in Web.Local.config, which deployments never touch.
$productionConfig = Join-Path $Destination 'Web.config'
if (-not (Test-Path -LiteralPath $productionConfig)) {
    Copy-Item (Join-Path $PSScriptRoot '../src/MyCRMRecords.Web/Web.config') $productionConfig
    Write-Host "Created $productionConfig from the repository."
}
# The application writes its data file to App_Data, so the application pool identity needs Modify there (folder permission only).
$appData = Join-Path $Destination 'App_Data'
New-Item -ItemType Directory -Path $appData -Force | Out-Null
$pool = Get-Item ('IIS:\AppPools\' + $app[0].ApplicationPool)
$poolIdentity = switch ($pool.processModel.identityType) {
    'ApplicationPoolIdentity' { 'IIS AppPool\' + $pool.Name }
    'NetworkService' { 'NT AUTHORITY\NETWORK SERVICE' }
    'LocalSystem' { 'NT AUTHORITY\SYSTEM' }
    'LocalService' { 'NT AUTHORITY\LOCAL SERVICE' }
    default { $pool.processModel.userName }
}
& icacls $appData /grant ($poolIdentity + ':(OI)(CI)M') | Out-Null
if ($LASTEXITCODE) { throw "Could not give $poolIdentity Modify permission on $appData." }
Write-Host "Application pool '$($pool.Name)' runs as $poolIdentity; it can now write $appData."
$firstInstall = -not (Test-Path -LiteralPath (Join-Path $Destination 'bin/MyCRMRecords.Web.dll'))
$health = [uri]$Url
if ($health.Scheme -ne 'https' -or $health.Host -ne 'apps.ricoh.sg' -or $health.AbsolutePath.TrimEnd('/') -ne $ApplicationPath.TrimEnd('/')) { throw "URL must be https://apps.ricoh.sg$ApplicationPath/." }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
Write-Host "Task account: $identity. It must have website access, modify permission on the application folder, and Log on as a batch job."
# Before the first deployment the folder holds only Web.config, so there is no page to check yet.
if (-not $firstInstall) {
    $applicationResponse = Invoke-WebRequest -Uri $health -UseDefaultCredentials -UseBasicParsing -TimeoutSec 30
    if ($applicationResponse.StatusCode -ne 200) { throw 'Current website health check failed.' }
}
$token = Read-Host 'Paste a GitHub fine-grained token for this repository (Actions: read; Contents: read)' -AsSecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($token)
try { $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
try {
    $headers = @{Authorization=('Bearer ' + $plain);Accept='application/vnd.github+json';'User-Agent'='MyCRMRecords-Deploy';'X-GitHub-Api-Version'='2022-11-28'}
    $null = Invoke-RestMethod -Uri ('https://api.github.com/repos/' + $Repository + '/git/ref/heads/master') -Headers $headers -TimeoutSec 30
    $null = Invoke-RestMethod -Uri ('https://api.github.com/repos/' + $Repository + '/actions/workflows/publish.yml/runs?per_page=1') -Headers $headers -TimeoutSec 30
} finally { $plain = $null; $headers = $null }
$credential = Get-Credential -UserName $identity -Message 'Enter this same Windows account password for Task Scheduler (stored by Windows).'
if (-not $credential -or $credential.UserName -ne $identity) { throw 'Use the same account running this installer; its DPAPI key encrypts the token.' }
New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
$installLock = [IO.File]::Open((Join-Path $installRoot 'poll.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    # Backups contain production assemblies; keep everything private, outside wwwroot.
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($principal in @($identity, 'S-1-5-18', 'S-1-5-32-544')) {
        $account = if ($principal.StartsWith('S-1-')) { [Security.Principal.SecurityIdentifier]::new($principal) } else { [Security.Principal.NTAccount]::new($principal) }
        $rule = [Security.AccessControl.FileSystemAccessRule]::new($account, [Security.AccessControl.FileSystemRights]::FullControl, ([Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit), [Security.AccessControl.PropagationFlags]::None, [Security.AccessControl.AccessControlType]::Allow)
        $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $installRoot -AclObject $acl

    foreach ($name in @('Poll.ps1','Package.psm1','config.json','token.dpapi')) {
        $ownedFile = Join-Path $installRoot $name
        if (Test-Path -LiteralPath $ownedFile) {
            if ((Get-Item -LiteralPath $ownedFile -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Installed deployment files cannot be links.' }
            $fileAcl = [Security.AccessControl.FileSecurity]::new()
            $fileAcl.SetAccessRuleProtection($true, $false)
            foreach ($principal in @($identity, 'S-1-5-18', 'S-1-5-32-544')) {
                $account = if ($principal.StartsWith('S-1-')) { [Security.Principal.SecurityIdentifier]::new($principal) } else { [Security.Principal.NTAccount]::new($principal) }
                $fileAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($account, [Security.AccessControl.FileSystemRights]::FullControl, [Security.AccessControl.AccessControlType]::Allow))
            }
            Set-Acl -LiteralPath $ownedFile -AclObject $fileAcl
        }
    }
    Copy-Item (Join-Path $PSScriptRoot 'Poll.ps1'), (Join-Path $PSScriptRoot 'Package.psm1') $installRoot -Force
    $tokenPath = Join-Path $installRoot 'token.dpapi'
    if (Test-Path -LiteralPath $tokenPath) { Remove-Item -LiteralPath $tokenPath -Force }
    $token | ConvertFrom-SecureString | Set-Content (Join-Path $installRoot 'token.dpapi') -Encoding UTF8
    @{repository=$Repository;workflow='publish.yml';destination=$destinationRoot;siteName=$SiteName;applicationPath=$ApplicationPath;url=$health.AbsoluteUri} | ConvertTo-Json | Set-Content (Join-Path $installRoot 'config.json') -Encoding UTF8
    $action = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -Argument ('-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + (Join-Path $installRoot 'Poll.ps1') + '" -InstallPath "' + $installRoot + '"') -WorkingDirectory $installRoot
    $trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 2)
    $settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero)
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -User $identity -Password $credential.GetNetworkCredential().Password -RunLevel Highest -Description 'Deploy successful GitHub master builds only; preserve IIS config and data; rollback failed deployments.' -Force | Out-Null
} finally { $installLock.Dispose() }
Start-ScheduledTask -TaskName $taskName
Write-Host "Installed and started '$taskName'. The first run deploys the current successful master build. Log: $installRoot\deploy.log"
