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
        & dotnet test --configuration Release
        if ($LASTEXITCODE) { throw 'Tests failed.' }
    }
    & dotnet build src/MyCRMRecords.Web/MyCRMRecords.Web.csproj --configuration Release
    if ($LASTEXITCODE) { throw 'Web build failed.' }

    $web = Join-Path $repo 'src/MyCRMRecords.Web'
    New-Item -ItemType Directory -Path (Join-Path $site 'bin') | Out-Null
    Copy-Item (Join-Path $web 'bin/*.dll') (Join-Path $site 'bin')
    $pages = @('admin.aspx', 'audit.aspx', 'default.aspx', 'deployment.aspx', 'feedback.aspx', 'guide.aspx', 'lists.aspx', 'login.aspx', 'logout.aspx', 'statistics.aspx', 'users.aspx', 'api.ashx', 'file.ashx', 'app.css', 'admin.js', 'audit.js', 'common.js', 'deployment.js', 'feedback.js', 'feedbacks.js', 'guide.js', 'lists.js', 'statistics.js', 'users.js', 'Web.config')
    Copy-Item ($pages | ForEach-Object { Join-Path $web $_ }) $site
    foreach ($required in @('bin/MyCRMRecords.Web.dll', 'bin/MyCRMRecords.Core.dll', 'bin/Newtonsoft.Json.dll') + $pages) {
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
