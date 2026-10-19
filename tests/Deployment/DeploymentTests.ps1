#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../../Deployment/Files.psm1') -Force
Import-Module (Join-Path $PSScriptRoot '../../Deployment/Package.psm1') -Force
Import-Module (Join-Path $PSScriptRoot '../../Deployment/ChangeLog.psm1') -Force
$count = 0
function Check([bool]$Condition, [string]$Name) { if (-not $Condition) { throw "FAIL: $Name" }; $script:count++ }
function Denied([scriptblock]$Action, [string]$Name) { $failed = $false; try { & $Action | Out-Null } catch { $failed = $true }; Check $failed $Name }
function Put([string]$Path, [string]$Value) { New-Item -ItemType Directory -Path (Split-Path $Path -Parent) -Force | Out-Null; [IO.File]::WriteAllText($Path, $Value) }
function Read([string]$Path) { [IO.File]::ReadAllText($Path) }
$root = Join-Path ([IO.Path]::GetTempPath()) ('mycrmrecords-deploy-tests-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $root 'source'; $dest = Join-Path $root 'site'; $backups = Join-Path $root 'backups'
$revision = 'a' * 40
try {
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $tokenFile = Join-Path $root 'test-token.dpapi'
    ConvertTo-SecureString 'synthetic-deployment-test-token' -AsPlainText -Force | ConvertFrom-SecureString | Set-Content $tokenFile -Encoding UTF8
    Check ((Read-RgToken $tokenFile) -eq 'synthetic-deployment-test-token') 'encrypted token file round trip handles newline and UTF8'
    Put "$source/bin/MyCRMRecords.Web.dll" 'new assembly'
    Put "$source/Content/asset.js" 'new script'
    Put "$source/Content/new.js" 'introduced'
    Write-RgRevisionMarker -Path "$source/Content/deployment-version.txt" -Revision $revision -BuildNumber 42
    $markerBytes = [IO.File]::ReadAllBytes("$source/Content/deployment-version.txt")
    Check (-not ($markerBytes.Length -ge 3 -and $markerBytes[0] -eq 239 -and $markerBytes[1] -eq 187 -and $markerBytes[2] -eq 191)) 'build marker is UTF8 without BOM'
    foreach ($withBom in @($false,$true)) {
        [byte[]]$bytes = if ($withBom) { [Text.Encoding]::UTF8.GetPreamble() + $markerBytes } else { $markerBytes }
        $stream = [IO.MemoryStream]::new($bytes)
        $stream.Position = $stream.Length
        $parsed = Read-RgRevisionMarker -Stream $stream
        Check ($parsed.revision -eq $revision) 'raw HTTP stream decodes marker with or without BOM even after Content was read'
    }
    Denied { Read-RgRevisionMarker -Stream ([IO.MemoryStream]::new([Text.Encoding]::UTF8.GetBytes('<html>Sign in</html>'))) } 'HTML response cannot pass revision validation'
    Denied { Read-RgRevisionMarker -Stream ([IO.MemoryStream]::new([Text.Encoding]::UTF8.GetBytes('{"revision":"wrong"}'))) } 'invalid commit cannot pass revision validation'
    Put "$source/Web.config" 'release config'
    Put "$source/Web.Release.config" 'must never be published'
    Put "$source/Web.Local.config" 'must never be published'
    Put "$source/App_Data/data.db" 'must never replace data'
    Put "$source/Uploads/logo.png" 'must never replace logo'
    Put "$source/Logs/log.txt" 'must never replace logs'
    Put "$dest/bin/MyCRMRecords.Web.dll" 'old assembly'
    Put "$dest/Content/asset.js" 'old script'
    Put "$dest/Content/stale.js" 'keep stale file'
    Put "$dest/Web.config" 'old release config'
    Put "$dest/Web.Local.config" 'server settings'
    Put "$dest/App_Data/data.db" 'production data'
    Put "$dest/Uploads/logo.png" 'production logo'
    Put "$dest/Logs/log.txt" 'production log'
    $params = @{Source=$source;Destination=$dest;BackupRoot=$backups;Revision=$revision;OfflineWaitSeconds=0}
    Denied { Invoke-RgFileDeployment @params -HealthCheck { throw 'Simulated health failure' } } 'failed health check rejects deployment'
    Check ((Read "$dest/bin/MyCRMRecords.Web.dll") -eq 'old assembly') 'rollback restores assembly'
    Check ((Read "$dest/Web.config") -eq 'old release config') 'rollback restores Web.config'
    Check ((Read "$dest/Content/asset.js") -eq 'old script') 'rollback restores script'
    Check (-not (Test-Path "$dest/Content/new.js")) 'rollback removes introduced file'
    Check (-not (Test-Path "$dest/app_offline.htm")) 'rollback brings application online'
    Check (-not (Test-Path "$dest/Content/deployment-version.txt")) 'failed release does not leave new version marker'
    $health = { Check (-not (Test-Path "$dest/app_offline.htm")) 'health runs after maintenance ends'; Check ((Read "$dest/Content/asset.js") -eq 'new script') 'health sees published script' }.GetNewClosure()
    $result = Invoke-RgFileDeployment @params -HealthCheck $health
    Check ($result.files -eq 5) 'only managed release files published'
    $publishedMarker = Read "$dest/Content/deployment-version.txt" | ConvertFrom-Json
    Check ($publishedMarker.revision -eq $revision) 'plain-text static marker retains commit verification'
    Check ($publishedMarker.buildNumber -eq 42) 'numeric build version survives deployment'
    Check ($publishedMarker.builtUtc -eq ((Read "$source/Content/deployment-version.txt" | ConvertFrom-Json).builtUtc)) 'deployment preserves CI build timestamp'
    Check ([DateTimeOffset]::Parse($publishedMarker.deployedUtc) -le [DateTimeOffset]::UtcNow -and [DateTimeOffset]::Parse($publishedMarker.deployedUtc) -ge [DateTimeOffset]::UtcNow.AddMinutes(-1)) 'deployment records actual install timestamp'
    $previousMarker = Read "$dest/Content/deployment-version.txt"
    Denied { Invoke-RgFileDeployment @params -HealthCheck { throw 'Simulated subsequent failure' } } 'subsequent failed deployment rolls back'
    Check ((Read "$dest/Content/deployment-version.txt") -eq $previousMarker) 'rollback restores exact previous version and deployment time'

    Check ((Read "$dest/Web.config") -eq 'release config') 'Web.config ships with the release'
    Check ((Read "$dest/Web.Local.config") -eq 'server settings') 'server-only Web.Local.config preserved'
    Check ((Read (Join-Path $result.backup 'files/Web.config')) -eq 'old release config') 'previous Web.config backed up'
    Check (-not (Test-Path "$dest/Web.Release.config")) 'configuration transform excluded'
    Check ((Read "$dest/App_Data/data.db") -eq 'production data') 'data preserved'
    Check ((Read "$dest/Uploads/logo.png") -eq 'production logo') 'logo preserved'
    Check ((Read "$dest/Logs/log.txt") -eq 'production log') 'logs preserved'
    Check ((Read "$dest/Content/stale.js") -eq 'keep stale file') 'unmanaged files retained'
    Check ((Read (Join-Path $result.backup 'files/bin/MyCRMRecords.Web.dll')) -eq 'old assembly') 'private backup contains previous release'
    Put "$dest/app_offline.htm" 'manual maintenance'
    Denied { Invoke-RgFileDeployment @params -HealthCheck {} } 'existing maintenance blocks deployment'
    Check ((Read "$dest/app_offline.htm") -eq 'manual maintenance') 'existing maintenance retained'
    Remove-Item "$dest/app_offline.htm"
    Denied { Invoke-RgFileDeployment -Source $source -Destination $dest -BackupRoot "$dest/backups" -Revision $revision -OfflineWaitSeconds 0 -HealthCheck {} } 'backups inside website rejected'
    Denied { Invoke-RgFileDeployment -Source $source -Destination $dest -BackupRoot ([IO.Path]::GetPathRoot($root)) -Revision $revision -OfflineWaitSeconds 0 -HealthCheck {} } 'filesystem root rejected'
    $linked = Join-Path $root 'linked-backups'
    $linkType = if ($env:OS -eq 'Windows_NT') { 'Junction' } else { 'SymbolicLink' }
    New-Item -ItemType $linkType -Path $linked -Target $backups | Out-Null
    Denied { Invoke-RgFileDeployment -Source $source -Destination $dest -BackupRoot $linked -Revision $revision -OfflineWaitSeconds 0 -HealthCheck {} } 'linked backup directory rejected'
    # Windows PowerShell 5.1 Remove-Item can throw NullReferenceException on
    # junctions. Nonrecursive Directory.Delete removes the link, not its target.
    $backupManifests = @(Get-ChildItem -LiteralPath $backups -Filter manifest.json -Recurse).Count
    [IO.Directory]::Delete($linked, $false)
    Check (-not (Test-Path -LiteralPath $linked)) 'temporary link removed'
    Check (@(Get-ChildItem -LiteralPath $backups -Filter manifest.json -Recurse).Count -eq $backupManifests) 'removing link leaves backup target intact'
    $invalid = Join-Path $root 'invalid-backup'
    Put "$invalid/manifest.json" (@{schema=1;destination=$dest;files=@(@{path='Web.config';existed=$false})} | ConvertTo-Json -Depth 4)
    Denied { Restore-RgFiles -BackupPath $invalid -Destination $dest } 'restore cannot delete config'
    Put "$invalid/manifest.json" (@{schema=1;destination=$dest;files=@(@{path='Web.Local.config';existed=$true})} | ConvertTo-Json -Depth 4)
    Denied { Restore-RgFiles -BackupPath $invalid -Destination $dest } 'restore cannot overwrite server settings'
    Put "$invalid/manifest.json" (@{schema=1;destination=$dest;files=@(@{path='../outside';existed=$false})} | ConvertTo-Json -Depth 4)
    Denied { Restore-RgFiles -BackupPath $invalid -Destination $dest } 'restore traversal rejected'
    Put "$invalid/manifest.json" (@{schema=1;destination=$dest;files=@(@{path='Content/new.js';existed=$false},@{path='missing.js';existed=$true})} | ConvertTo-Json -Depth 4)
    Denied { Restore-RgFiles -BackupPath $invalid -Destination $dest } 'incomplete backup rejected'

    Check (Test-Path "$dest/Content/new.js") 'entire manifest validated before any restore'
    function Run([int]$Id,[string]$Sha,[string]$Branch,[string]$Event,[string]$Conclusion) { [pscustomobject]@{id=$Id;head_sha=$Sha;head_branch=$Branch;event=$Event;status='completed';conclusion=$Conclusion;head_repository=@{full_name='zhaidy/MyCRMRecords'}} }
    $runs = @((Run 1 $revision 'master' 'push' 'success'), (Run 2 ('b'*40) 'master' 'push' 'success'), (Run 3 $revision 'feature' 'push' 'success'), (Run 4 $revision 'master' 'pull_request' 'success'), (Run 5 $revision 'master' 'push' 'failure'))
    $selected = @(Select-RgRelease $runs $revision 'zhaidy/MyCRMRecords')
    Check ($selected.Count -eq 1 -and $selected[0].id -eq 1) 'only successful current master push selected'
    Check (@(Select-RgRelease $runs $revision 'wrong/repo').Count -eq 0) 'other repository rejected'
    $runs += Run 6 $revision 'master' 'workflow_dispatch' 'success'
    Check (@(Select-RgRelease $runs $revision 'zhaidy/MyCRMRecords')[0].id -eq 6) 'latest successful manual master build eligible'
    # The package of a build on the release website-builds (publish.yml); without it the poller uses the Actions artifact.
    function Asset([int]$Id,[string]$Name,[string]$State) { [pscustomobject]@{id=$Id;name=$Name;state=$State;size=1024} }
    $build = [pscustomobject]@{id=6;run_number=330;head_sha=$revision}
    $packages = [pscustomobject]@{tag_name='website-builds';draft=$false;assets=@((Asset 1 'mycrmrecords-release-329.zip' 'uploaded'), (Asset 2 'mycrmrecords-release-330.zip' 'uploaded'), (Asset 3 'mycrmrecords-release-3300.zip' 'uploaded'))}
    $asset = @(Select-RgReleaseAsset -Release $packages -Run $build)
    Check ($asset.Count -eq 1 -and $asset[0].id -eq 2) 'the package of the selected build is found on the release by its build number'
    $packages.assets = @((Asset 1 'mycrmrecords-release-329.zip' 'uploaded'), (Asset 2 'mycrmrecords-release-330.zip' 'starter'))
    Check (@(Select-RgReleaseAsset -Release $packages -Run $build).Count -eq 0) 'a package still uploading or of another build is not used'
    Check (@(Select-RgReleaseAsset -Release $null -Run $build).Count -eq 0) 'no release yet: the artifact is used'
    $packages.assets = @((Asset 2 'mycrmrecords-release-330.zip' 'uploaded'))
    $packages.draft = $true; Check (@(Select-RgReleaseAsset -Release $packages -Run $build).Count -eq 0) 'a draft release is not used'
    $packages.draft = $false; $packages.tag_name = 'v1'; Check (@(Select-RgReleaseAsset -Release $packages -Run $build).Count -eq 0) 'only the release website-builds holds build packages'
    # The commits waiting to deploy, from GitHub's comparison (oldest first there), for the Deployment page.
    function Commit([string]$Sha,[string]$Message,[int]$Parents) { [pscustomobject]@{sha=$Sha;parents=@(1..$Parents);commit=@{message=$Message;author=@{name='A Person';date='2026-10-16T01:00:00Z'}}} }
    $compare = [pscustomobject]@{commits=@((Commit ('1'*40) "First change`n`nDetails" 1), (Commit ('2'*40) 'Merge pull request #5' 2), (Commit ('3'*40) 'Second change' 1))}
    $pending = Get-RgPendingChanges -Compare $compare -Base ('b'*40) -Head $revision
    Check ($pending.base -eq ('b'*40) -and $pending.head -eq $revision -and $pending.total -eq 2) 'waiting commits name both ends and leave out merges'
    Check ($pending.commits[0].revision -eq ('3'*40) -and $pending.commits[1].subject -eq 'First change' -and $pending.commits[1].author -eq 'A Person') 'waiting commits are newest first, with the first line of their message'
    $pending = Get-RgPendingChanges -Compare $compare -Base ('b'*40) -Head $revision -Maximum 1
    Check ($pending.total -eq 2 -and @($pending.commits).Count -eq 1) 'only the newest waiting commits are kept, with the total'
    Check ((Get-RgPendingChanges -Compare ([pscustomobject]@{commits=@()}) -Base ('b'*40) -Head $revision).total -eq 0) 'no waiting commits'
    # Admin overview deployment status: why the website is or is not on the latest master build.
    function StatusRun([int]$Id,[string]$Status,[string]$Conclusion) { [pscustomobject]@{id=$Id;run_number=$Id+100;head_sha=$revision;head_branch='master';event='push';status=$Status;conclusion=$Conclusion;html_url="https://github.com/zhaidy/MyCRMRecords/actions/runs/$Id";created_at='2026-10-07T01:00:00Z';head_repository=@{full_name='zhaidy/MyCRMRecords'}} }
    $status = Get-RgBuildStatus -Runs @() -Revision $revision -Repository 'zhaidy/MyCRMRecords's -Installed $false
    Check ($status.state -eq 'waiting' -and $null -eq $status.build -and $status.message -match 'No build has started') 'status waits when master has no build yet'
    $status = Get-RgBuildStatus -Runs @((StatusRun 7 'in_progress' $null)) -Revision $revision -Repository 'zhaidy/MyCRMRecords's -Installed $false
    Check ($status.state -eq 'waiting' -and $status.build.number -eq 107 -and $status.message -match 'Build #107 .* in progress') 'status explains a running build'
    $status = Get-RgBuildStatus -Runs @((StatusRun 7 'completed' 'success'), (StatusRun 8 'completed' 'failure'), (Run 9 ('c'*40) 'master' 'push' 'failure')) -Revision $revision -Repository 'zhaidy/MyCRMRecords's -Installed $false
    Check ($status.state -eq 'failed' -and $status.build.number -eq 108 -and $status.message -match 'finished as failure') 'status reports the latest build of the current master commit failed'
    $status = Get-RgBuildStatus -Runs @((StatusRun 8 'completed' 'success')) -Revision $revision -Repository 'zhaidy/MyCRMRecords's -Installed $true
    Check ($status.state -eq 'current' -and $status.latestRevision -eq $revision) 'installed master build is current'
    Check ((Get-RgBuildStatus -Runs @((StatusRun 8 'completed' 'success')) -Revision $revision -Repository 'other/repo' -Installed $false).build -eq $null) 'status ignores other repositories'
    Write-RgDeploymentStatus -Destination $dest -Status $status
    $raw = Get-Content -LiteralPath "$dest/App_Data/deployment-status.json" -Raw; $written = $raw | ConvertFrom-Json
    # PowerShell 7 reads ISO strings as dates, 5.1 does not: check the raw text.
    Check ($written.state -eq 'current' -and $written.build.number -eq 108 -and $raw -match '"checkedUtc":\s*"\d{4}-\d{2}-\d{2}T') 'status file is written to the preserved App_Data folder'
    Check (-not (Test-Path "$dest/App_Data/deployment-status.json.tmp")) 'status file is replaced atomically'
    $waiting = Get-RgBuildStatus -Runs @((StatusRun 8 'in_progress' $null)) -Revision $revision -Repository 'zhaidy/MyCRMRecords's -Installed $false
    $waiting.pending = Get-RgPendingChanges -Compare ([pscustomobject]@{commits=@((Commit ('3'*40) 'Only change' 1))}) -Base ('b'*40) -Head $revision
    Write-RgDeploymentStatus -Destination $dest -Status $waiting
    $written = Get-Content -LiteralPath "$dest/App_Data/deployment-status.json" -Raw | ConvertFrom-Json
    Check ($written.pending.total -eq 1 -and @($written.pending.commits).Count -eq 1 -and $written.pending.commits[0].subject -eq 'Only change' -and (Get-Content -LiteralPath "$dest/App_Data/deployment-status.json" -Raw) -match '"commits":\s*\[') 'the waiting commits are written to the status file as a list'
    Write-RgDeploymentStatus -Destination (Join-Path $root 'missing/<bad>') -Status $status
    Check $true 'status reporting failures never stop the poller'
    # Windows PowerShell 5.1 does not preload the ZIP archive types.
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    function Zip([string]$Name, [string[]]$Entries) {
        $path = Join-Path $root ($Name + '.zip')
        $archive = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
        try { foreach ($entry in $Entries) { $writer = [IO.StreamWriter]::new($archive.CreateEntry($entry).Open()); try { $writer.Write('test bytes') } finally { $writer.Dispose() } } } finally { $archive.Dispose() }
        $path
    }
    $good = Zip 'good' @('site/bin/MyCRMRecords.Web.dll','deploy/Deploy.ps1')
    Expand-RgPackage $good "$root/extracted"
    Check ((Read "$root/extracted/site/bin/MyCRMRecords.Web.dll") -eq 'test bytes') 'normal artifact extracted'
    Denied { Expand-RgPackage $good "$root/extracted" } 'existing extraction destination rejected'
    foreach ($bad in @('../escaped.txt', '/absolute.txt', 'site/../../escaped.txt', 'site\..\..\escaped.txt', 'C:/outside.txt', 'site/file.txt:stream', 'site/NUL.txt', 'site/trailing./x')) {
        $name = 'bad-' + [Guid]::NewGuid().ToString('N')
        $zip = Zip $name @('site/normal.txt',$bad)
        Denied { Expand-RgPackage $zip "$root/$name" } ('reject package path ' + $bad)
        Check (-not (Test-Path "$root/$name")) 'package rejected before extraction'
    }
    $duplicate = Zip 'duplicate' @('site/X.txt','site/x.txt')
    Denied { Expand-RgPackage $duplicate "$root/duplicate" } 'Windows case-insensitive duplicates rejected'
    Denied { Expand-RgPackage $good "$root/size" -MaximumBytes 1 } 'uncompressed size bound enforced'
    Denied { Expand-RgPackage $good "$root/count" -MaximumFiles 1 } 'entry count bound enforced'
    # Execute the real poller from a different working directory. Invalid fixture
    # configuration stops before any token decryption, network request or IIS change.
    Add-Type -IgnoreWarnings -TypeDefinition @'
public class RgDiagnosticResponse : System.Net.WebResponse {
    private readonly System.Uri uri;
    public RgDiagnosticResponse(string address) { uri = new System.Uri(address); }
    public System.Net.HttpStatusCode StatusCode { get { return System.Net.HttpStatusCode.Unauthorized; } }
    public override System.Uri ResponseUri { get { return uri; } }
}
'@
    $failureResponse = [RgDiagnosticResponse]::new('https://apps.ricoh.sg/MyCRMRecords/Content/deployment-version.txt?secret=do-not-log')
    $webFailure = [Net.WebException]::new('do not print response content', $null, [Net.WebExceptionStatus]::ProtocolError, $failureResponse)
    $diagnostic = Get-RgFailureMessage $webFailure
    Check ($diagnostic -match 'HTTP 401') 'HTTP failure records status'
    Check ($diagnostic -match 'apps.ricoh.sg/MyCRMRecords/Content/deployment-version.txt') 'HTTP failure identifies website path'
    Check ($diagnostic -notmatch 'secret|do-not-log|response content') 'HTTP diagnostic excludes query and response contents'
    $storageResponse = [RgDiagnosticResponse]::new('https://example.blob.core.windows.net/private-path?sig=do-not-log')
    $storageFailure = [Net.WebException]::new('secret URL', $null, [Net.WebExceptionStatus]::ProtocolError, $storageResponse)
    Check ((Get-RgFailureMessage $storageFailure) -notmatch 'blob.core|private-path|sig=') 'storage URL remains private'
    $tlsFailure = [Net.WebException]::new('secret URL', [Net.WebExceptionStatus]::TrustFailure)
    Check ((Get-RgFailureMessage $tlsFailure) -match 'TrustFailure') 'certificate failure remains diagnosable without a URL'
    $pollFixture = Join-Path $root 'poll fixture'
    New-Item -ItemType Directory -Path $pollFixture | Out-Null
    Copy-Item (Join-Path $PSScriptRoot '../../Deployment/Poll.ps1'), (Join-Path $PSScriptRoot '../../Deployment/Package.psm1') $pollFixture
    Put (Join-Path $pollFixture 'config.json') '{"repository":"invalid","workflow":"publish.yml"}'
    foreach ($explicitPath in @($false, $true)) {
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = (Get-Process -Id $PID).Path
        $start.Arguments = '-NoProfile -NonInteractive -File "' + (Join-Path $pollFixture 'Poll.ps1') + '"'
        if ($explicitPath) { $start.Arguments += ' -InstallPath "' + $pollFixture + '"' }
        $start.WorkingDirectory = $root
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
        $process = [Diagnostics.Process]::Start($start)
        try {
            $output = $process.StandardOutput.ReadToEnd() + $process.StandardError.ReadToEnd()
            $process.WaitForExit()
            Check ($process.ExitCode -ne 0) 'poller rejects invalid configuration'
            Check ($output -notmatch 'Cannot bind argument.*Path') 'poller startup resolves nonempty paths'
            Check ((Read (Join-Path $pollFixture 'deploy.log')) -match 'Invalid deployment repository or workflow configuration') 'poller loads its own configuration and logs startup failure'
        } finally { $process.Dispose() }
        Remove-Item (Join-Path $pollFixture 'deploy.log')
    }
    # A deployment replaces the installed poller (next to the backups folder) with the release's copy.
    $installed = Join-Path $root 'installed poller'
    $releaseScripts = Join-Path $root 'release deploy'
    Put "$installed/Poll.ps1" 'old poller'; Put "$installed/Package.psm1" 'old module'; Put "$installed/config.json" '{}'
    Put "$releaseScripts/Poll.ps1" 'new poller'; Put "$releaseScripts/Package.psm1" 'new module'
    Check (Update-RgPoller -Source $releaseScripts -BackupRoot "$installed/backups") 'deployment reports poller update'
    Check ((Read "$installed/Poll.ps1") -eq 'new poller' -and (Read "$installed/Package.psm1") -eq 'new module') 'deployment installs the release poller'
    Check (-not (Update-RgPoller -Source $releaseScripts -BackupRoot "$root/no poller/backups")) 'deployment without an installed poller leaves it alone'
    Check (-not (Test-Path "$root/no poller")) 'no poller files created outside an installation'
    Remove-Item "$releaseScripts/Package.psm1"
    Denied { Update-RgPoller -Source $releaseScripts -BackupRoot "$installed/backups" } 'release missing poller files rejected'
    Check ((Read "$installed/Poll.ps1") -eq 'new poller') 'incomplete release changes no poller file'
    # Change log: a pull request merge becomes one entry with its title and commits; direct commits are their own entry.
    $gitRepo = Join-Path $root 'changelog-repo'
    New-Item -ItemType Directory -Path $gitRepo | Out-Null
    function Invoke-TestGit { $ErrorActionPreference = 'Continue'; & git -C $gitRepo -c user.name='Jamie Lee' -c user.email='jlee@example.com' -c commit.gpgsign=false @args | Out-Null; if ($LASTEXITCODE) { throw ('git ' + $args[0] + ' failed') } }
    Invoke-TestGit init -q -b master
    Invoke-TestGit commit -q --allow-empty -m 'First commit'
    Invoke-TestGit checkout -q -b feature
    Invoke-TestGit commit -q --allow-empty -m 'Add the deployment page' -m 'With details.'
    Put "$gitRepo/Changelog/feature.md" "# Deployment page`n`nAdministrators see whether the website`nis up to date.`n`n  Second paragraph.  `n"
    Put "$gitRepo/Changelog/README.md" 'How to write entries (not an entry).'
    Invoke-TestGit add Changelog
    Invoke-TestGit commit -q --allow-empty -m "Show the change log $([char]0x2014) caf$([char]0xE9)"
    Invoke-TestGit checkout -q master
    Invoke-TestGit commit -q --allow-empty -m 'Fix a typo directly on master'
    Invoke-TestGit merge -q --no-ff feature -m 'Merge pull request #12 from zhaidy/feature' -m 'Deployment page with change log'
    $head = (& git -C $gitRepo rev-parse HEAD).Trim()
    $changeLogPath = Join-Path $root 'changelog.json'
    Write-RgChangeLog -Path $changeLogPath -Source $gitRepo -Revision $head -Repository 'zhaidy/MyCRMRecords'
    $changeLog = [IO.File]::ReadAllText($changeLogPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
    Check ($changeLog.revision -eq $head -and $changeLog.repository -eq 'zhaidy/MyCRMRecords') 'change log names its revision and repository'
    Check (@($changeLog.entries).Count -eq 3) 'change log has one entry per first-parent commit'
    $merge = @($changeLog.entries)[0]
    Check ($merge.pullRequest -eq 12 -and $merge.title -eq 'Deployment page' -and $merge.revision -eq $head) 'pull request merge uses its Changelog entry title and the pull request number'
    Check ($merge.description -eq "Administrators see whether the website is up to date.`n`nSecond paragraph.") 'entry description keeps paragraphs and joins wrapped lines'
    Check ($changeLog.version -eq 3 -and $merge.version -eq 3 -and @($changeLog.entries)[1].version -eq 2 -and @($changeLog.entries)[2].version -eq 1) 'versions count the commits on master'
    Check (@(Get-RgEntryFiles -Path $gitRepo -Parent (& git -C $gitRepo rev-parse 'HEAD^1').Trim() -Revision $head).Count -eq 1) 'README.md is not an entry'
    Check (@($merge.commits).Count -eq 2 -and @($merge.commits)[0].subject -eq "Show the change log $([char]0x2014) caf$([char]0xE9)" -and @($merge.commits)[1].subject -eq 'Add the deployment page') 'pull request entry lists its commits, newest first, in UTF-8'
    Check (@($changeLog.entries)[1].title -eq 'Fix a typo directly on master' -and -not @($changeLog.entries)[1].description -and $null -eq @($changeLog.entries)[1].pullRequest -and @(@($changeLog.entries)[1].commits).Count -eq 1) 'direct commit is its own entry'
    Check (@($changeLog.entries)[2].author -eq 'Jamie Lee' -and @($changeLog.entries)[2].date) 'entries record author and date'
    Denied { Write-RgChangeLog -Path $changeLogPath -Source $gitRepo -Revision ('f' * 40) } 'change log fails for a commit that is not in the history'
    $protectedNames = @(Get-Variable | Where-Object { $_.Name -ne 'null' -and ($_.Options -band ([Management.Automation.ScopedItemOptions]::ReadOnly -bor [Management.Automation.ScopedItemOptions]::Constant)) } | Select-Object -ExpandProperty Name)
    foreach ($script in Get-ChildItem (Join-Path $PSScriptRoot '../../Deployment') -File | Where-Object { $_.Extension -in @('.ps1','.psm1') }) {
        $tokens = $null; $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$errors)
        Check ($errors.Count -eq 0) ('PowerShell parses: ' + $script.Name)
        $conflicts = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] }, $true) | ForEach-Object {
            if ($_.Left -is [Management.Automation.Language.VariableExpressionAst] -and $_.Left.VariablePath.UserPath -in $protectedNames) { $_.Left.VariablePath.UserPath }
        })
        Check ($conflicts.Count -eq 0) ('no assignments to protected PowerShell variables: ' + $script.Name)

    }
    Write-Host "$count deployment checks passed."
} finally { # best-effort: a just-finished process may still hold a file on Windows
    for ($attempt = 0; $attempt -lt 5 -and (Test-Path -LiteralPath $root); $attempt++) { try { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction Stop } catch { Start-Sleep -Milliseconds 500 } } }
