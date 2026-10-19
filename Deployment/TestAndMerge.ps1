#Requires -Version 5.1
<#
    Runs in pr-checks.yml. Tests a pull request merged with the current master and, with -Merge, puts exactly that
    tested merge commit on master.

    Several pull requests can be ready at the same time. Each one's push is a fast-forward from the master it was
    tested with, so it only succeeds if master has not moved since; GitHub then marks the pull request as merged.
    When another pull request merged first, the push is refused, and this one merges with the new master and is
    tested again. So master only ever receives a combination that passed the tests, whatever the order.

    The token ($env:MERGE_TOKEN) is removed from the environment before any build or test runs, so the code under
    test never sees it; only the GitHub API calls and the final push use it.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$PullRequest,
      [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$Head,
      [Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$BuildNumber,
      [switch]$Merge,
      [ValidateRange(1, 20)][int]$MaxAttempts = 6)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 does not offer TLS 1.2 by default, and the GitHub API needs it.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$token = $env:MERGE_TOKEN
Remove-Item Env:MERGE_TOKEN -ErrorAction SilentlyContinue
$repository = $env:GITHUB_REPOSITORY
if ($repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'TestAndMerge.ps1 must run in GitHub Actions.' }
if (-not $token) { throw 'MERGE_TOKEN is not set.' }
$root = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $PSScriptRoot 'ChangeLog.psm1') -Force
$headers = @{Authorization=('Bearer ' + $token); Accept='application/vnd.github+json'; 'X-GitHub-Api-Version'='2022-11-28'; 'User-Agent'='MyCRMRecords-PR-checks'}
$api = 'https://api.github.com/repos/' + $repository

# git reports on stderr (a refused push, a conflict); with 'Stop', Windows PowerShell can turn that into an error before
# the exit code is checked, so git runs with 'Continue' and its exit code decides.
function Invoke-Git {
    $ErrorActionPreference = 'Continue'
    $output = & git -C $root @args
    if ($LASTEXITCODE) { throw ('git ' + $args[0] + ' failed with exit code ' + $LASTEXITCODE + '.') }
    $output
}
function Get-Master {
    Invoke-Git fetch -q --no-tags origin '+refs/heads/master:refs/remotes/origin/master' | Out-Null
    ([string](Invoke-Git rev-parse 'refs/remotes/origin/master')).Trim()
}

# The pull request as it is now: it may have been closed, marked as draft, labelled or pushed to since the run started.
function Test-StillMergeable {
    $pull = Invoke-RestMethod -Uri ($api + '/pulls/' + $PullRequest) -Headers $headers -TimeoutSec 30
    if ($pull.state -ne 'open') { Write-Host "Pull request #$PullRequest is $($pull.state); nothing to merge."; return $false }
    if ($pull.head.sha -ne $Head) { Write-Host "Pull request #$PullRequest has a newer commit; the run for that commit tests and merges it."; return $false }
    if ($pull.draft) { Write-Host "Pull request #$PullRequest is a draft; it merges once it is marked ready for review."; return $false }
    if (@($pull.labels | Where-Object { $_.name -eq 'no-automerge' }).Count) { Write-Host "Pull request #$PullRequest has the no-automerge label; not merging."; return $false }
    $true
}

$pull = Invoke-RestMethod -Uri ($api + '/pulls/' + $PullRequest) -Headers $headers -TimeoutSec 30
# The same message GitHub's merge button writes; the Deployment page's change log reads the number and title from it.
# Through a file: Windows PowerShell 5.1 passes arguments that contain double quotes to git wrongly.
$messageFile = Join-Path ([IO.Path]::GetTempPath()) ('merge-message-' + $PullRequest + '.txt')
$message = 'Merge pull request #' + $PullRequest + ' from ' + ($pull.head.label -replace ':', '/')
if ($pull.title) { $message += "`n`n" + $pull.title }
[IO.File]::WriteAllText($messageFile, $message + "`n", [Text.UTF8Encoding]::new($false))

for ($attempt = 1; ; $attempt++) {
    $base = Get-Master
    Write-Host "Attempt $attempt`: testing $Head merged with master $base."
    Invoke-Git checkout -q --force --detach $base | Out-Null
    & { $ErrorActionPreference = 'Continue'; & git -C $root -c user.name='github-actions[bot]' -c user.email='41898282+github-actions[bot]@users.noreply.github.com' -c commit.gpgsign=false merge -q --no-ff --no-edit -F $messageFile $Head }
    if ($LASTEXITCODE) {
        & { $ErrorActionPreference = 'Continue'; & git -C $root merge --abort }
        throw "Pull request #$PullRequest conflicts with master. Merge master into the branch, resolve the conflicts and push."
    }
    $merged = ([string](Invoke-Git rev-parse HEAD)).Trim()

    # The change log entry: a file of the pull request's own in Changelog/ (see Changelog/README.md), so pull requests
    # never conflict over it. Without one, the change log shows the pull request's title.
    $entries = @(Get-RgEntryFiles -Path $root -Parent $base -Revision $merged)
    if ($entries.Count -gt 1) { throw ('A pull request adds one change log entry; this one adds ' + ($entries -join ', ') + '.') }
    if ($entries.Count -eq 1) {
        $entry = Read-RgEntry -Path $root -Revision $merged -File $entries[0]
        if ($null -eq $entry -or -not $entry.title) { throw ($entries[0] + ' is empty. Start it with "# " and a short title, then describe the change.') }
        Write-Host "Change log entry ($($entries[0])): $($entry.title)"
    } else {
        Write-Host ('::warning::Pull request #' + $PullRequest + ' adds no change log entry, so the change log shows its title. Add Changelog/<branch name>.md (see Changelog/README.md).')
    }
    $version = Get-RgVersion -Path $root -Revision $merged

    & (Join-Path $root 'Deployment/Build.ps1') -Revision $merged -BuildNumber $BuildNumber

    if (-not $Merge) { Write-Host "Checks passed for $Head merged with master $base. Not merging automatically."; exit 0 }
    if (-not (Test-StillMergeable)) { exit 0 }

    # Fast-forward only (no --force): GitHub refuses the push if master moved after $base was fetched.
    # The credentials go to this one git process through its environment, never through the command line.
    $basic = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes('x-access-token:' + $token))
    $env:GIT_CONFIG_COUNT = '1'; $env:GIT_CONFIG_KEY_0 = 'http.https://github.com/.extraheader'; $env:GIT_CONFIG_VALUE_0 = 'AUTHORIZATION: basic ' + $basic
    try { & { $ErrorActionPreference = 'Continue'; & git -C $root push -q origin ($merged + ':refs/heads/master') }; $pushed = $LASTEXITCODE -eq 0 }
    finally { Remove-Item Env:GIT_CONFIG_COUNT, Env:GIT_CONFIG_KEY_0, Env:GIT_CONFIG_VALUE_0 -ErrorAction SilentlyContinue }

    if ($pushed) {
        Write-Host "Merged pull request #$PullRequest into master as $merged (website version $version)."
        # A push made with the workflow's token does not start other workflows, so start the master build here;
        # the IIS server deploys that build. "tested" names the commit just tested: when master is still that commit,
        # the build packages it without running the tests again.
        $dispatch = '{"ref":"master","inputs":{"tested":"' + $merged + '"}}'
        Invoke-RestMethod -Method Post -Uri ($api + '/actions/workflows/publish.yml/dispatches') -Headers $headers -ContentType 'application/json' -Body $dispatch -TimeoutSec 30 | Out-Null
        Write-Host 'Started the master build (Build IIS release).'
        if ($env:GITHUB_STEP_SUMMARY) { "### Merged into master`nCommit ``$merged`` (website version $version; tested as #$PullRequest merged with ``$base``)." | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY }
        exit 0
    }
    if ((Get-Master) -eq $base) { throw 'Pushing to master failed although master did not change. Check the workflow permissions (contents: write).' }
    if ($attempt -ge $MaxAttempts) { throw "Master changed $attempt times while this pull request was being tested. Re-run the checks to try again." }
    Write-Host 'Another pull request merged into master first. Merging with the new master and testing again.'
}
