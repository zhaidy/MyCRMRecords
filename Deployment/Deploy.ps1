#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$Revision,
    [string]$Destination = 'C:\inetpub\wwwroot\MyCRMRecords',
    [string]$SiteName = 'Default Web Site',
    [string]$ApplicationPath = '/MyCRMRecords',
    [uri]$Url = 'https://apps.ricoh.sg/MyCRMRecords/',
    [string]$BackupRoot = 'C:\ProgramData\MyCRMRecordsDeploy\backups'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Import-Module WebAdministration -ErrorAction Stop
Import-Module (Join-Path $PSScriptRoot 'Files.psm1') -Force
$app = Get-WebApplication -Site $SiteName | Where-Object { $_.Path -eq $ApplicationPath }
if (@($app).Count -ne 1) { throw "IIS application not found: $SiteName$ApplicationPath. Check the configured site/application names." }
$physical = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($app.PhysicalPath)).TrimEnd('\')
if ($physical -ne [IO.Path]::GetFullPath($Destination).TrimEnd('\')) { throw 'The destination does not match the IIS application physical path.' }
if (-not $Url.IsAbsoluteUri -or $Url.Scheme -ne 'https' -or $Url.Host -ne 'apps.ricoh.sg' -or $Url.AbsolutePath.TrimEnd('/') -ne $ApplicationPath.TrimEnd('/')) { throw "Health URL must be https://apps.ricoh.sg$ApplicationPath/." }
$Url = [uri]($Url.AbsoluteUri.TrimEnd('/') + '/')
Write-Host "Deploying $Revision to $SiteName$ApplicationPath (pool: $($app.ApplicationPool)). Web.config is replaced by the release's copy; Web.Local.config and data folders are preserved."
$readRevisionMarker = Get-Command Read-RgRevisionMarker -CommandType Function -ErrorAction Stop
$health = {
    $markerUrl = [uri]::new($Url, 'Content/deployment-version.txt')
    for ($attempt=1; $attempt -le 4; $attempt++) {
        try {
            $marker = Invoke-WebRequest -Uri ($markerUrl.AbsoluteUri + '?revision=' + $Revision) -UseDefaultCredentials -UseBasicParsing -TimeoutSec 20
            $version = & $readRevisionMarker -Stream $marker.RawContentStream
            if ($version.revision -ne $Revision) { throw 'The public website is serving a different deployment revision.' }
            $applicationResponse = Invoke-WebRequest -Uri $Url -UseDefaultCredentials -UseBasicParsing -TimeoutSec 20
            if ($applicationResponse.StatusCode -ne 200) { throw "Application health status: $($applicationResponse.StatusCode)" }
            # The application page redirects to the sign-in page (login.aspx), which must load.
            if ($applicationResponse.Content -notmatch 'name="password"') { throw 'The sign-in page did not load.' }
            # The API runs the application code (bin DLLs, Web.config, sign-in check). Without signing in it must
            # answer 401 with its own JSON, which proves the handler ran rather than an IIS error page.
            $apiStatus = 0; $apiContent = ''
            try {
                $apiResponse = Invoke-WebRequest -Uri ([uri]::new($Url, 'api.ashx/session')) -UseBasicParsing -TimeoutSec 30
                $apiStatus = [int]$apiResponse.StatusCode; $apiContent = $apiResponse.Content
            } catch {
                if ($null -eq $_.Exception.Response) { throw }
                $apiStatus = [int]$_.Exception.Response.StatusCode
                if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $apiContent = $_.ErrorDetails.Message }
            }
            $apiCode = $null
            try { $apiCode = ($apiContent | ConvertFrom-Json).result.code } catch { }
            if ($apiStatus -ne 401 -or $apiCode -ne 'UNAUTHORIZED') { throw "The MyCRMRecords API did not answer as expected (HTTP $apiStatus)." }
            Write-Host 'Public deployment revision, sign-in page and API checks passed.'
            return
        } catch {
            if ($attempt -eq 4) { throw }
            Start-Sleep -Seconds 5
        }
    }
}.GetNewClosure()
$result = Invoke-RgFileDeployment -Source $Source -Destination $Destination -BackupRoot $BackupRoot -Revision $Revision -HealthCheck $health
Write-Host "Deployed $($result.files) files. Rollback copy: $($result.backup)"
if (Update-RgPoller -Source $PSScriptRoot -BackupRoot $BackupRoot) { Write-Host 'Updated the installed poller (Poll.ps1, Package.psm1) from the release.' }
if ($env:GITHUB_STEP_SUMMARY) {
    @("### MyCRMRecords deployed", "Commit: ``$Revision``", "URL: $($Url.AbsoluteUri)", "Published files: $($result.files)", "Server backup: ``$($result.backup)``") | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
}
