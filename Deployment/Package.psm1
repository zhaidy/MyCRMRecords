Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Get-RgFailureMessage {
    param([Parameter(Mandatory)][Exception]$Exception)
    if ($Exception -is [Net.WebException]) {
        $message = 'HTTP/network failure: ' + $Exception.Status
        $response = $Exception.Response
        if ($null -ne $response) {
            $status = $response.PSObject.Properties['StatusCode']
            if ($null -ne $status) { $message += '; HTTP ' + [int]$status.Value }
            $uri = $response.ResponseUri
            # Only public application/GitHub API paths; never signed storage URLs,
            # query strings, credentials, or response bodies.
            if ($null -ne $uri -and $uri.Host -in @('apps.ricoh.sg','api.github.com')) {
                $message += ' at ' + $uri.Host + $uri.AbsolutePath
            }
        }
        return $message
    }
    [regex]::Replace($Exception.Message, 'https?://[^\s]+', '[URL]')
}
function Read-RgToken {
    param([Parameter(Mandatory)][string]$Path)
    $secure = (Get-Content -LiteralPath $Path -Raw).Trim() | ConvertTo-SecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer); $secure.Dispose() }
}
function Select-RgRelease {
    param($Runs, [string]$Revision, [string]$Repository)
    @($Runs | Where-Object {
        $_.head_sha -eq $Revision -and $_.head_branch -eq 'master' -and
        $_.status -eq 'completed' -and $_.conclusion -eq 'success' -and
        $_.event -in @('push', 'workflow_dispatch') -and $_.head_repository.full_name -eq $Repository
    } | Sort-Object id -Descending | Select-Object -First 1)
}
function Select-RgReleaseAsset {
    # The package a build attached to the GitHub release website-builds (publish.yml), or nothing for a build that only
    # uploaded an Actions artifact. Releases use no Actions storage, which a private repository has only 0.5 GB of.
    param($Release, $Run)
    if ($null -eq $Release -or $Release.tag_name -ne 'website-builds' -or $Release.draft) { return @() }
    $name = 'mycrmrecords-release-' + [int]$Run.run_number + '.zip'
    @($Release.assets | Where-Object { $_.name -eq $name -and $_.state -eq 'uploaded' } | Select-Object -First 1)
}
function Get-RgPendingChanges {
    # The commits on master that the website does not run yet, from GitHub's comparison of the two commits, for the
    # Deployment page's "Waiting to deploy" (the browser cannot ask GitHub itself: the repository is private).
    # Merge commits are left out, as on the page; newest first, at most $Maximum of them.
    param($Compare, [string]$Base, [string]$Head, [int]$Maximum = 50)
    $commits = @($Compare.commits | Where-Object { @($_.parents).Count -lt 2 })
    [array]::Reverse($commits)
    [ordered]@{
        base = $Base; head = $Head; total = $commits.Count
        commits = @($commits | Select-Object -First $Maximum | ForEach-Object {
            [ordered]@{ revision = [string]$_.sha; subject = ([string]$_.commit.message -split "`n")[0]; author = [string]$_.commit.author.name; date = [string]$_.commit.author.date }
        })
    }
}
function Get-RgBuildStatus {
    # Why the website is or is not on the latest master commit, from the workflow runs (any status).
    param($Runs, [string]$Revision, [string]$Repository, [bool]$Installed)
    $short = $Revision.Substring(0, [Math]::Min(12, $Revision.Length))
    $run = @($Runs | Where-Object {
        $_.head_sha -eq $Revision -and $_.head_branch -eq 'master' -and
        $_.event -in @('push', 'workflow_dispatch') -and $_.head_repository.full_name -eq $Repository
    } | Sort-Object id -Descending | Select-Object -First 1)
    $build = $null
    if ($run.Count -eq 1) {
        $r = $run[0]
        $build = [ordered]@{ number = [int]$r.run_number; status = [string]$r.status; conclusion = [string]$r.conclusion; url = [string]$r.html_url; createdUtc = [string]$r.created_at }
    }
    if ($Installed) { $state = 'current'; $message = 'The website runs the latest master commit.' }
    elseif ($null -eq $build) { $state = 'waiting'; $message = "No build has started yet for master commit $short. The website keeps the installed version until one succeeds." }
    elseif ($build.status -ne 'completed') { $state = 'waiting'; $message = "Build #$($build.number) for master commit $short is $($build.status -replace '_', ' '). The website updates after it succeeds." }
    elseif ($build.conclusion -ne 'success') { $state = 'failed'; $message = "Build #$($build.number) for master commit $short finished as $($build.conclusion -replace '_', ' '). The website keeps the installed version; fix the build in GitHub Actions and push to master again." }
    else { $state = 'ready'; $message = "Build #$($build.number) succeeded and is ready to deploy." }
    [ordered]@{ state = $state; message = $message; latestRevision = $Revision; build = $build }
}
function Write-RgDeploymentStatus {
    # The Admin overview reads this file. App_Data is preserved by deployments and never served by IIS.
    # It holds only build numbers, states and sanitized messages: no token, URL query or log content.
    param([Parameter(Mandatory)][string]$Destination, [Parameter(Mandatory)]$Status)
    try {
        $folder = Join-Path $Destination 'App_Data'
        if (-not (Test-Path -LiteralPath $folder)) { New-Item -ItemType Directory -Path $folder | Out-Null }
        $Status['checkedUtc'] = [DateTime]::UtcNow.ToString('o')
        $path = Join-Path $folder 'deployment-status.json'
        [IO.File]::WriteAllText($path + '.tmp', ($Status | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath ($path + '.tmp') -Destination $path -Force
    } catch {
        # Reporting must never stop or fail a deployment.
    }
}
function Expand-RgPackage {
    param([string]$Archive, [string]$Destination, [long]$MaximumBytes = 536870912, [int]$MaximumFiles = 10000)
    # Windows PowerShell 5.1 does not preload the ZIP archive types.
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $root = [IO.Path]::GetFullPath($Destination).TrimEnd([char[]]'\/')
    if (Test-Path -LiteralPath $root) { throw 'Package destination must be a new directory.' }
    $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        [long]$total = 0
        if ($zip.Entries.Count -gt $MaximumFiles) { throw 'Package contains too many entries.' }
        # Validate every entry before creating any files (including Windows path rules).
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName.Replace('\','/')
            if ([string]::IsNullOrWhiteSpace($name) -or $name.StartsWith('/') -or $name -match '(^|/)\.\.(/|$)|[:\x00-\x1f]' -or $name -match '(^|/)(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|/|$)' -or $name -match '[. ](/|$)') { throw 'Unsafe path in release package.' }
            $path = [IO.Path]::GetFullPath((Join-Path $root $name))
            if (-not $path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or -not $paths.Add($path.TrimEnd([char[]]'\/'))) { throw 'Escaping or duplicate package path.' }
            if (($entry.ExternalAttributes -shr 16 -band 0xF000) -eq 0xA000) { throw 'Package links are not allowed.' }
            $total += $entry.Length
            if ($total -gt $MaximumBytes) { throw 'Uncompressed release is too large.' }
        }
        New-Item -ItemType Directory -Path $root | Out-Null
        foreach ($entry in $zip.Entries) {
            $path = Join-Path $root ($entry.FullName.Replace('\','/'))
            if ($entry.FullName.EndsWith('/') -or $entry.FullName.EndsWith('\')) { New-Item -ItemType Directory -Path $path -Force | Out-Null; continue }
            New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $path, $false)
        }
    } finally { $zip.Dispose() }
}
Export-ModuleMember -Function Select-RgRelease, Select-RgReleaseAsset, Get-RgPendingChanges, Expand-RgPackage, Read-RgToken, Get-RgFailureMessage, Get-RgBuildStatus, Write-RgDeploymentStatus
