#Requires -Version 5.1
[CmdletBinding()]
param([string]$InstallPath, [switch]$Once)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Resolve after parameter binding: Windows PowerShell can leave $PSScriptRoot empty
# when it is evaluated as a parameter default.
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($InstallPath)) { $InstallPath = $scriptDirectory }
$InstallPath = [IO.Path]::GetFullPath($InstallPath)
if (-not $Once) {
    # The scheduled task starts the poller every 2 minutes; it checks twice, a minute apart, so the server looks for a
    # new build every minute without changing the task. Each check runs in its own process (and holds poll.lock).
    # After a failed check it stops: the task's next run tries again.
    $shell = (Get-Process -Id $PID).Path
    $started = [DateTime]::UtcNow
    foreach ($check in 1..2) {
        if ($check -gt 1) {
            $wait = 60 - ([DateTime]::UtcNow - $started).TotalSeconds
            if ($wait -gt 0) { Start-Sleep -Milliseconds ([int]($wait * 1000)) }
        }
        & $shell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $MyInvocation.MyCommand.Path -InstallPath $InstallPath -Once
        if ($LASTEXITCODE) { exit $LASTEXITCODE }
    }
    exit 0
}
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
function Write-DeployLog([string]$Message) {
    $log = Join-Path $InstallPath 'deploy.log'
    if ((Test-Path $log) -and (Get-Item $log).Length -gt 10485760) { Move-Item $log ($log + '.previous') -Force }
    ([DateTime]::UtcNow.ToString('o') + ' ' + $Message) | Add-Content -LiteralPath $log -Encoding UTF8
}
$lock = $null; $stage = $null; $zipPath = $null; $token = $null; $config = $null; $status = $null
$deploymentStage = 'startup'
try {
    Import-Module (Join-Path $scriptDirectory 'Package.psm1') -Force
    try { $lock = [IO.File]::Open((Join-Path $InstallPath 'poll.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
    catch [IO.IOException] { exit 0 }
    $config = Get-Content (Join-Path $InstallPath 'config.json') -Raw | ConvertFrom-Json
    if ($config.repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or $config.workflow -ne 'publish.yml') { throw 'Invalid deployment repository or workflow configuration.' }
    # DPAPI: only the Windows account that installed this task can decrypt this token.
    $token = Read-RgToken -Path (Join-Path $InstallPath 'token.dpapi')
    $headers = @{Authorization=('Bearer ' + $token); Accept='application/vnd.github+json'; 'X-GitHub-Api-Version'='2022-11-28'; 'User-Agent'='MyCRMRecords-Deploy'}
    $api = 'https://api.github.com/repos/' + $config.repository
    function Get-GitHub([string]$Path) { Invoke-RestMethod -Uri ($api + $Path) -Headers $headers -TimeoutSec 30 }
    # The same, or nothing when GitHub answers 404 (Windows PowerShell 5.1 throws a WebException with the response).
    function Get-GitHubOrNothing([string]$Path) {
        try { Get-GitHub $Path }
        catch {
            $response = $_.Exception.PSObject.Properties['Response']
            if ($null -ne $response -and $null -ne $response.Value -and [int]$response.Value.StatusCode -eq 404) { return $null }
            throw
        }
    }
    $deploymentStage = 'GitHub build lookup'
    $revision = (Get-GitHub '/git/ref/heads/master').object.sha
    if ($revision -notmatch '^[a-f0-9]{40}$') { throw 'Invalid master revision.' }
    $markerPath = Join-Path $config.destination 'Content/deployment-version.txt'
    if (-not (Test-Path -LiteralPath $markerPath)) { $markerPath = Join-Path $config.destination 'Content/deployment-version.json' }
    $statePath = Join-Path $InstallPath 'state.json'
    $installed = $false; $running = $null
    if (Test-Path $markerPath) {
        $running = [string](Get-Content $markerPath -Raw | ConvertFrom-Json).revision
        if (Test-Path $statePath) { $installed = (Get-Content $statePath -Raw | ConvertFrom-Json).revision -eq $revision -and $running -eq $revision }
    }
    # All statuses, so the Admin overview can say whether the latest build is running, failed or ready.
    $runs = (Get-GitHub ('/actions/workflows/' + $config.workflow + '/runs?branch=master&per_page=30')).workflow_runs
    $status = Get-RgBuildStatus -Runs $runs -Revision $revision -Repository $config.repository -Installed $installed
    if (-not $installed -and $running -match '^[a-f0-9]{40}$' -and $running -ne $revision) {
        # The commits waiting to deploy, for the Deployment page. Only a nice-to-have: it never stops a deployment.
        try { $status.pending = Get-RgPendingChanges -Compare (Get-GitHub ('/compare/' + $running + '...' + $revision)) -Base $running -Head $revision }
        catch { }
    }
    if ($installed) { Write-RgDeploymentStatus -Destination $config.destination -Status $status; exit 0 }
    $selected = @(Select-RgRelease -Runs $runs -Revision $revision -Repository $config.repository)
    # A newer master build may still be running or may have failed.
    if ($selected.Count -eq 0) { Write-RgDeploymentStatus -Destination $config.destination -Status $status; exit 0 }
    $run = $selected[0]
    # A build attaches its package to the GitHub release website-builds: a private repository has only 0.5 GB of
    # Actions storage, and GitHub refuses artifact uploads for the rest of the month once it is used up. Builds from
    # before that uploaded an Actions artifact.
    $asset = @(Select-RgReleaseAsset -Release (Get-GitHubOrNothing '/releases/tags/website-builds') -Run $run)
    if ($asset.Count -eq 1) {
        if ($asset[0].size -gt 268435456) { throw 'Compressed release exceeds 256 MB.' }
        $download = $api + '/releases/assets/' + $asset[0].id
    } else {
        $artifacts = (Get-GitHub ('/actions/runs/' + $run.id + '/artifacts?per_page=100')).artifacts
        $artifact = @($artifacts | Where-Object { $_.name -eq 'mycrmrecords-release' -and -not $_.expired })
        if ($artifact.Count -ne 1) { throw ('Successful build #' + $run.run_number + ' has no release package (mycrmrecords-release-' + $run.run_number + '.zip on the GitHub release website-builds, or an unexpired artifact). Rerun the workflow.') }
        if ($artifact[0].size_in_bytes -gt 268435456) { throw 'Compressed release exceeds 256 MB.' }
        $download = $api + '/actions/artifacts/' + $artifact[0].id + '/zip'
    }
    $cache = Join-Path $InstallPath 'staging'
    New-Item -ItemType Directory -Path $cache -Force | Out-Null
    $key = [Guid]::NewGuid().ToString('N')
    $stage = Join-Path $cache $key
    $zipPath = Join-Path $cache ($key + '.zip')
    $deploymentStage = 'release download and extraction'
    # Retrieve the redirect explicitly so GitHub credentials never reach artifact or release storage.
    $request = [Net.HttpWebRequest]::Create($download)
    $request.AllowAutoRedirect = $false
    $request.Timeout = 30000
    $request.UserAgent = 'MyCRMRecords-Deploy'
    if ($asset.Count -eq 1) { $request.Accept = 'application/octet-stream' }
    $request.Headers['Authorization'] = $headers.Authorization
    $request.Headers['X-GitHub-Api-Version'] = '2022-11-28'
    $response = $request.GetResponse()
    $location = $null
    try {
        if ([int]$response.StatusCode -eq 302) {
            $location = [uri]$response.Headers['Location']
            if ($location.Scheme -ne 'https' -or $location.UserInfo -or $location.Host -notmatch '(^|\.)(blob\.core\.windows\.net|actions\.githubusercontent\.com|githubusercontent\.com|github\.com|githubusercontent\.net)$') { throw 'Unexpected artifact storage host.' }
        } elseif ([int]$response.StatusCode -eq 200 -and $asset.Count -eq 1) {
            # GitHub may send a release asset itself instead of redirecting to its storage.
            $file = [IO.File]::Create($zipPath)
            try { $response.GetResponseStream().CopyTo($file) } finally { $file.Dispose() }
        } else { throw 'GitHub release download did not return a redirect.' }
    } finally { $response.Close() }
    # No Authorization header on the signed storage URL.
    if ($null -ne $location) { Invoke-WebRequest -Uri $location -UseBasicParsing -OutFile $zipPath -TimeoutSec 180 | Out-Null }
    Expand-RgPackage -Archive $zipPath -Destination $stage
    $site = Join-Path $stage 'site'
    $version = Get-Content (Join-Path $site 'Content/deployment-version.txt') -Raw | ConvertFrom-Json
    if ($version.revision -ne $revision) { throw 'Release artifact does not match master.' }
    if ((Get-GitHub '/git/ref/heads/master').object.sha -ne $revision) {
        Write-DeployLog 'Master changed during download; deferred deployment.'
        $status.state = 'waiting'; $status.message = 'Master changed while the release was downloading. The newer commit deploys after its build succeeds.'
        Write-RgDeploymentStatus -Destination $config.destination -Status $status
        exit 0
    }
    $deploymentStage = 'IIS deployment and website health checks'
    $status.state = 'deploying'; $status.message = 'Deploying build #' + $status.build.number + '. The website is briefly unavailable while files are replaced.'
    Write-RgDeploymentStatus -Destination $config.destination -Status $status
    Write-DeployLog ('Deploying master ' + $revision + ', build ' + $run.id)
    & (Join-Path $stage 'deploy/Deploy.ps1') -Source $site -Revision $revision -Destination $config.destination -SiteName $config.siteName -ApplicationPath $config.applicationPath -Url $config.url -BackupRoot (Join-Path $InstallPath 'backups')
    $deploymentStage = 'deployment state recording'
    @{revision=$revision;runId=$run.id;deployedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content ($statePath + '.tmp') -Encoding UTF8
    Move-Item ($statePath + '.tmp') $statePath -Force
    Write-DeployLog ('Deployment passed: ' + $revision)
    $status.state = 'current'; $status.message = 'The website runs the latest master commit.'
    Write-RgDeploymentStatus -Destination $config.destination -Status $status
} catch {
    # Avoid recording HTTP exceptions, which can contain signed storage URLs.
    if (Get-Command Get-RgFailureMessage -ErrorAction SilentlyContinue) { $reason = Get-RgFailureMessage -Exception $_.Exception }
    else { $reason = 'Poller initialization failed: ' + $_.Exception.GetType().Name }
    Write-DeployLog ('Deployment failed during ' + $deploymentStage + ': ' + $reason)
    if ($null -ne $config -and $config.PSObject.Properties['destination'] -and (Get-Command Write-RgDeploymentStatus -ErrorAction SilentlyContinue)) {
        if ($null -eq $status) { $status = [ordered]@{ latestRevision = $null; build = $null } }
        $status.state = 'failed'
        # The same sanitized reason as the private log (no signed URLs or response bodies).
        $after = if ($deploymentStage -eq 'IIS deployment and website health checks') { ' Changed files were restored from the backup; see the private deployment log.' } else { ' The website keeps the installed version; see the private deployment log.' }
        $status.message = 'Deployment failed during ' + $deploymentStage + ': ' + $reason + $after
        Write-RgDeploymentStatus -Destination $config.destination -Status $status
    }
    Write-Error 'Automatic deployment failed. Review the private deployment log and run Poll.ps1 interactively under the task account.'
    exit 1
} finally {
    $token = $null
    if ($stage -and (Test-Path -LiteralPath $stage)) { Remove-Item -LiteralPath $stage -Recurse -Force }
    if ($zipPath -and (Test-Path -LiteralPath $zipPath)) { Remove-Item -LiteralPath $zipPath -Force }
    if ($null -ne $lock) { $lock.Dispose() }
}
