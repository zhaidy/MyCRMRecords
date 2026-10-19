Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-RgRevisionMarker {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$Revision,
          [ValidateRange(0, 2147483647)][int]$BuildNumber = 0)
    $marker = @{revision=$Revision;builtUtc=[DateTime]::UtcNow.ToString('o')}
    if ($BuildNumber -gt 0) { $marker.buildNumber = $BuildNumber }
    $json = $marker | ConvertTo-Json
    [IO.File]::WriteAllText($Path, $json, [Text.UTF8Encoding]::new($false))
}
function Read-RgRevisionMarker {
    param([Parameter(Mandatory)][IO.Stream]$Stream)
    if ($Stream.CanSeek) { $Stream.Position = 0 }
    $reader = [IO.StreamReader]::new($Stream, [Text.Encoding]::UTF8, $true)
    try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
    try { $marker = $text | ConvertFrom-Json }
    catch { throw 'Website version marker is not valid UTF-8 JSON. Check whether the URL serves the version file or an HTML/error page.' }
    if (-not $marker -or -not $marker.PSObject.Properties['revision'] -or $marker.revision -notmatch '^[a-fA-F0-9]{40}$') { throw 'Website version marker does not contain a valid commit SHA.' }
    $marker
}
function Get-RgRoot([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if ($full.TrimEnd([char[]]'\/') -eq [IO.Path]::GetPathRoot($full).TrimEnd([char[]]'\/')) { throw 'Deployment cannot use a filesystem root.' }
    $cursor = $full
    while ($cursor) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Deployment paths cannot traverse links.' }
        $cursor = Split-Path $cursor -Parent
    }
    $full.TrimEnd([char[]]'\/')
}
function Get-RgChild([string]$Root, [string]$Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or $Relative -match '(^|[\\/])\.\.([\\/]|$)') { throw 'Invalid deployment file path.' }
    $path = [IO.Path]::GetFullPath((Join-Path $Root $Relative))
    if (-not $path.StartsWith($Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'A deployment file escapes its directory.' }
    $cursor = $path
    while ($cursor.Length -gt $Root.Length) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Deployment cannot traverse a link: $cursor" }
        }
        $cursor = Split-Path $cursor -Parent
    }
    $path
}
function Test-RgPreserved([string]$Relative) {
    # Unlike ReportGenerator3, MyCRMRecords's Web.config holds no secrets, so each release ships it. Server-only settings
    # go in Web.Local.config (any root Web.*.config), which deployments never touch, like App_Data and logs.
    $parts = $Relative -split '[\\/]'
    ($parts[0] -in @('App_Data', 'Uploads', 'UploadedFiles', 'Logs', 'Log', 'app_offline.htm')) -or ($parts.Count -eq 1 -and $parts[0] -like 'Web.*.config')
}
function Copy-RgFile([string]$From, [string]$To) {
    New-Item -ItemType Directory -Path (Split-Path $To -Parent) -Force | Out-Null
    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        try { Copy-Item -LiteralPath $From -Destination $To -Force -ErrorAction Stop; return }
        catch { if ($attempt -eq 9) { throw }; Start-Sleep -Seconds 1 }
    }
}
function Restore-RgFiles {
    param([Parameter(Mandatory)][string]$BackupPath, [Parameter(Mandatory)][string]$Destination)
    $backup = Get-RgRoot $BackupPath; $dest = Get-RgRoot $Destination
    $manifest = Get-Content -LiteralPath (Join-Path $backup 'manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.schema -ne 1 -or (Get-RgRoot $manifest.destination) -ne $dest) { throw 'Backup does not match this website.' }
    # Validate the entire manifest before restoring anything.
    foreach ($file in $manifest.files) {
        if (Test-RgPreserved $file.path) { throw 'Backup attempts to overwrite a preserved file.' }
        if ($file.path -eq 'Web.config' -and -not $file.existed) { throw 'Backup attempts to delete Web.config.' }
        $null = Get-RgChild $dest $file.path
        $saved = Get-RgChild (Join-Path $backup 'files') $file.path
        if ($file.existed -and -not (Test-Path -LiteralPath $saved -PathType Leaf)) { throw "Backup file is missing: $($file.path)" }
    }
    foreach ($file in $manifest.files) {
        $target = Get-RgChild $dest $file.path
        if ($file.existed) { Copy-RgFile (Get-RgChild (Join-Path $backup 'files') $file.path) $target }
        elseif (Test-Path -LiteralPath $target -PathType Leaf) { Remove-Item -LiteralPath $target -Force }
    }
}
function Invoke-RgFileDeployment {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][string]$BackupRoot,
        [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$Revision,
        [Parameter(Mandatory)][scriptblock]$HealthCheck,
        [int]$OfflineWaitSeconds = 5
    )
    $src = Get-RgRoot $Source; $dest = Get-RgRoot $Destination; $backups = Get-RgRoot $BackupRoot
    foreach ($pair in @(@($src,$dest),@($src,$backups),@($dest,$backups))) {
        if ($pair[0] -eq $pair[1] -or $pair[0].StartsWith($pair[1] + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $pair[1].StartsWith($pair[0] + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Source, website and backups must be separate directories.' }
    }
    foreach ($root in @($src,$dest)) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "Directory is missing: $root" }
        if ((Get-Item -LiteralPath $root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Deployment roots cannot be links.' }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $dest 'Web.config') -PathType Leaf)) { throw 'Web.config is missing from the application folder. Run Install.ps1 first.' }
    if (-not (Test-Path -LiteralPath (Join-Path $src 'Web.config') -PathType Leaf)) { throw 'Publish output does not contain Web.config.' }
    if (-not (Test-Path -LiteralPath (Join-Path $src 'bin/MyCRMRecords.Web.dll') -PathType Leaf)) { throw 'Publish output does not contain MyCRMRecords.Web.dll.' }
    $offline = Join-Path $dest 'app_offline.htm'
    if (Test-Path -LiteralPath $offline) { throw 'The application is already offline. No deployment was attempted.' }
    New-Item -ItemType Directory -Path $backups -Force | Out-Null
    $lock = $null; $offlineOwned = $false; $changed = $false; $keepOffline = $false
    try {
        $lock = [IO.File]::Open((Join-Path $backups 'deployment.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $backup = Join-Path $backups (([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')) + '-' + $Revision.Substring(0,12) + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
        New-Item -ItemType Directory -Path (Join-Path $backup 'files') -Force | Out-Null
        $files = [Collections.Generic.List[object]]::new()
        foreach ($item in Get-ChildItem -LiteralPath $src -Recurse -File -Force) {
            $relative = $item.FullName.Substring($src.Length).TrimStart([char[]]'\/')
            if (Test-RgPreserved $relative) { continue }
            $from = Get-RgChild $src $relative; $to = Get-RgChild $dest $relative
            if (Test-Path -LiteralPath $to -PathType Container) { throw "A published file conflicts with a directory: $relative" }
            $existed = Test-Path -LiteralPath $to -PathType Leaf
            $files.Add([pscustomobject]@{path=$relative;existed=$existed})
            if ($existed) { Copy-RgFile $to (Get-RgChild (Join-Path $backup 'files') $relative) }
        }
        if ($files.Count -lt 2) { throw 'Publish output is incomplete.' }
        [ordered]@{schema=1;destination=$dest;revision=$Revision;files=@($files.ToArray())} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $backup 'manifest.json') -Encoding UTF8
        # CreateNew ensures we never replace another administrator's maintenance page.
        $stream = [IO.File]::Open($offline,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
        $offlineOwned = $true
        try { $bytes = [Text.Encoding]::UTF8.GetBytes('<!doctype html><meta charset="utf-8"><title>Updating</title><p>MyCRMRecords is being updated. Please refresh shortly.</p>'); $stream.Write($bytes,0,$bytes.Length) } finally { $stream.Dispose() }
        if ($OfflineWaitSeconds -gt 0) { Start-Sleep -Seconds $OfflineWaitSeconds }
        $changed = $true
        foreach ($file in $files) { Copy-RgFile (Get-RgChild $src $file.path) (Get-RgChild $dest $file.path) }
        # Stamp the actual install time, independently of the earlier CI build time.
        # The marker is a managed file, so failed health checks restore its previous value.
        $markerPath = Get-RgChild $dest 'Content/deployment-version.txt'
        if (Test-Path -LiteralPath (Join-Path $src 'Content/deployment-version.txt') -PathType Leaf) {
            $marker = Read-RgRevisionMarker -Stream ([IO.File]::OpenRead($markerPath))
            if ($marker.revision -ne $Revision) { throw 'The package revision does not match the requested deployment.' }
            $marker | Add-Member -NotePropertyName deployedUtc -NotePropertyValue ([DateTime]::UtcNow.ToString('o')) -Force
            [IO.File]::WriteAllText($markerPath, ($marker | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
        }
        Remove-Item -LiteralPath $offline -Force; $offlineOwned = $false
        & $HealthCheck
        Write-Output ([pscustomobject]@{revision=$Revision;files=$files.Count;backup=$backup})
    } catch {
        $deploymentError = $_
        if ($changed) {
            try {
                if (-not (Test-Path -LiteralPath $offline)) { Set-Content -LiteralPath $offline -Value 'Restoring the previous MyCRMRecords release. Please refresh shortly.' -Encoding UTF8; $offlineOwned = $true }
                Restore-RgFiles -BackupPath $backup -Destination $dest
                Write-Warning "Deployment failed; previous files restored. Backup: $backup"
            } catch { $keepOffline = $true; throw "Deployment and rollback failed. Application remains offline for repair. Backup: $backup. Restore error: $($_.Exception.Message)" }
        }
        throw $deploymentError
    } finally {
        if ($offlineOwned -and -not $keepOffline -and (Test-Path -LiteralPath $offline)) { Remove-Item -LiteralPath $offline -Force }
        if ($null -ne $lock) { $lock.Dispose() }
    }
}
function Update-RgPoller {
    # The poller (Poll.ps1 and Package.psm1) is installed by Install.ps1 next to the backups folder. A deployment
    # replaces it with the release's copy, so changes to the poller reach the server without rerunning the installer.
    # Does nothing unless the folder holds an installed poller (a deployment run by hand elsewhere).
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$BackupRoot)
    $installation = Split-Path ([IO.Path]::GetFullPath($BackupRoot).TrimEnd([char[]]'\/')) -Parent
    if (-not (Test-Path -LiteralPath (Join-Path $installation 'Poll.ps1')) -or -not (Test-Path -LiteralPath (Join-Path $installation 'config.json'))) { return $false }
    $files = @('Poll.ps1', 'Package.psm1')
    foreach ($name in $files) { if (-not (Test-Path -LiteralPath (Join-Path $Source $name))) { throw "Release is missing deploy/$name." } }
    foreach ($name in $files) {
        $target = Join-Path $installation $name
        if ((Test-Path -LiteralPath $target) -and ((Get-Item -LiteralPath $target -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Installed deployment files cannot be links.' }
        # Overwriting keeps the installed file's private permissions. A running Poll.ps1 has already read its script.
        Copy-Item -LiteralPath (Join-Path $Source $name) -Destination $target -Force
    }
    $true
}
Export-ModuleMember -Function Invoke-RgFileDeployment, Restore-RgFiles, Write-RgRevisionMarker, Read-RgRevisionMarker, Update-RgPoller
