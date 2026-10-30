#Requires -Version 5.1
<#
    Builds, tests and packages a MyCRMRecords release (the same layout ReportGenerator3's poller deploys):
      $env:RUNNER_TEMP/MyCRMRecords-release/site     files for the IIS application folder, with Content/deployment-version.txt
                                                     and bin/changelog.json (read by the Deployment admin page)
      $env:RUNNER_TEMP/MyCRMRecords-release/deploy   Deploy.ps1 and Files.psm1, run by Poll.ps1 on the server,
                                                     and Poll.ps1 and Package.psm1, which Deploy.ps1 installs as the poller
    The site includes Web.config (it holds no secrets); App_Data and Web.Local.config stay on the server.
    -SkipTests leaves out the tests, for a master commit that the pull request checks (TestAndMerge.ps1) already
    tested: they put exactly the commit they tested on master, so the master build does not test it again.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$Revision,
      [Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$BuildNumber,
      [switch]$SkipTests)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $env:RUNNER_TEMP) { throw 'Build.ps1 must run in the GitHub build job.' }
Import-Module (Join-Path $PSScriptRoot 'Files.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'ChangeLog.psm1') -Force
$repo = Split-Path $PSScriptRoot -Parent

<#
    Runs a dotnet command, showing its output as it comes, and returns its exit code. When it fails, its compile errors
    and failed tests (or else its last lines) are also written as annotations of the run, which can be read where the
    log itself cannot be downloaded. Standard error is shown too, without stopping the script.
#>
function Invoke-Reported([scriptblock]$Command) {
    $lines = New-Object System.Collections.Generic.List[string]
    & {
        $ErrorActionPreference = 'Continue'
        & $Command 2>&1 | ForEach-Object { $line = "$_"; Write-Host $line; $lines.Add($line) }
    }
    $code = $LASTEXITCODE
    if (-not $code) { return 0 }
    $errors = New-Object System.Collections.Generic.List[string]
    foreach ($line in $lines) { if ($line -match 'error (CS|MSB|NU|NETSDK)\d+' -and -not $errors.Contains($line.Trim())) { $errors.Add($line.Trim()) } }
    $failure = $null
    foreach ($line in $lines) {
        if ($line -match '^\s*Failed [A-Za-z_][\w.]*') { if ($failure) { $errors.Add($failure) }; $failure = $line.Trim() }
        elseif ($failure -and $failure.Length -lt 3000 -and $line -notmatch '^\s*(Passed|Skipped) ') { $failure += '%0A' + $line.Trim() }
    }
    if ($failure) { $errors.Add($failure) }
    if ($errors.Count -eq 0) { $errors.Add((($lines | Select-Object -Last 25) | ForEach-Object { $_.Trim() }) -join '%0A') }
    foreach ($e in ($errors | Select-Object -First 10)) { Write-Host ('::error::' + $e) }
    return $code
}
$release = Join-Path $env:RUNNER_TEMP 'MyCRMRecords-release'
$site = Join-Path $release 'site'
if (Test-Path -LiteralPath $release) { Remove-Item -LiteralPath $release -Recurse -Force }
New-Item -ItemType Directory -Path $site -Force | Out-Null

Push-Location $repo
try {
    Write-Host ('.NET SDK ' + (& dotnet --version))
    # The whole solution, test packages included, so the NuGet package cache the master build saves (publish.yml) has
    # every package the pull request checks need, also when this build skips the tests.
    & dotnet restore
    if ($LASTEXITCODE) { throw 'NuGet restore failed.' }
    if ($SkipTests) { Write-Host "Not testing $Revision again: the pull request checks tested this exact commit." }
    else {
        if (Invoke-Reported { & dotnet test --configuration Release }) { throw 'Tests failed.' }
    }
    if (Invoke-Reported { & dotnet build src/MyCRMRecords.Web/MyCRMRecords.Web.csproj --configuration Release }) { throw 'Web build failed.' }

    $web = Join-Path $repo 'src/MyCRMRecords.Web'
    New-Item -ItemType Directory -Path (Join-Path $site 'bin') | Out-Null
    Copy-Item (Join-Path $web 'bin/*.dll') (Join-Path $site 'bin')
    $pages = @('activities.aspx', 'admin.aspx', 'audit.aspx', 'company.aspx', 'companyprint.aspx', 'companymachines.aspx', 'costingsheet.aspx', 'costingsheets.aspx', 'default.aspx', 'deployment.aspx', 'dormant.aspx', 'files.aspx', 'guide.aspx', 'machine.aspx', 'machineaccess.aspx', 'machines.aspx', 'login.aspx', 'logout.aspx', 'people.aspx', 'print.aspx', 'project.aspx', 'projects.aspx', 'prospects.aspx', 'requestform.aspx', 'requestformprint.aspx', 'requestforms.aspx', 'territories.aspx', 'usage.aspx', 'users.aspx', 'api.ashx', 'file.ashx', 'app.css', 'print.css', 'companyprint.css', 'requestformprint.css', 'activities.js', 'activityform.js', 'admin.js', 'audit.js', 'common.js', 'companies.js', 'company.js', 'companyprint.js', 'companymachines.js', 'costingsheet.js', 'costingsheets.js', 'crm.js', 'deployment.js', 'dormant.js', 'files.js', 'guide.js', 'machine.js', 'machineaccess.js', 'machines.js', 'people.js', 'print.js', 'project.js', 'projects.js', 'prospects.js', 'qrcode.js', 'requestform.js', 'requestformprint.js', 'requestforms.js', 'territories.js', 'usage.js', 'users.js', 'ricoh-logo.png', 'restricted.png', 'manifest.json', 'sw.js', 'offline.html', 'Web.config')
    Copy-Item ($pages | ForEach-Object { Join-Path $web $_ }) $site
    # The installed app's icons (manifest.json).
    $icons = @('pwa/icon-192.png', 'pwa/icon-512.png', 'pwa/icon-maskable-512.png', 'pwa/apple-touch-icon.png')
    New-Item -ItemType Directory -Path (Join-Path $site 'pwa') | Out-Null
    Copy-Item ($icons | ForEach-Object { Join-Path $web $_ }) (Join-Path $site 'pwa')
    # The Inter font (app.css) and its licence.
    New-Item -ItemType Directory -Path (Join-Path $site 'fonts') | Out-Null
    Copy-Item (Join-Path $web 'fonts/*') (Join-Path $site 'fonts')
    foreach ($required in @('bin/MyCRMRecords.Web.dll', 'bin/MyCRMRecords.Core.dll', 'bin/Newtonsoft.Json.dll') + $pages + $icons) {
        if (-not (Test-Path -LiteralPath (Join-Path $site $required))) { throw "Release is missing $required." }
    }
    New-Item -ItemType Directory -Path (Join-Path $site 'Content') | Out-Null
    Write-RgRevisionMarker -Path (Join-Path $site 'Content/deployment-version.txt') -Revision $Revision -BuildNumber $BuildNumber
    # The Deployment admin page's change log: the master history up to this commit (needs the full git history).
    # In bin\, which IIS never serves, so only administrators see it (through api.ashx/admin/deployment).
    Write-RgChangeLog -Path (Join-Path $site 'bin/changelog.json') -Source $repo -Revision $Revision -Repository $env:GITHUB_REPOSITORY

    $scripts = Join-Path $release 'deploy'
    New-Item -ItemType Directory -Path $scripts | Out-Null
    Copy-Item (Join-Path $PSScriptRoot 'Deploy.ps1'), (Join-Path $PSScriptRoot 'Files.psm1'), (Join-Path $PSScriptRoot 'Poll.ps1'), (Join-Path $PSScriptRoot 'Package.psm1') $scripts

    if (-not $SkipTests) {
        $shell = (Get-Process -Id $PID).Path
        & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'tests/Deployment/DeploymentTests.ps1')
        if ($LASTEXITCODE) { throw 'Deployment script checks failed.' }
    }
    Write-Host "Release for $Revision (build $BuildNumber) is ready in $release."
} finally { Pop-Location }
