Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Build-time only (Build.ps1, TestAndMerge.ps1): the change log the Deployment admin page shows. Not copied to the server.
#
# Like ReportGenerator3's ChangelogNext/, a pull request describes its change in a file of its own,
# Changelog/<branch name>.md, and never edits a shared change log file: two open pull requests then never change the
# same lines, so the change log never makes them conflict. Unlike ReportGenerator3, nothing has to move or number the
# entry when it merges (no one-at-a-time job, no extra commit): the build reads the entries from the git history, and
# an entry's version is its master commit's position on master (git rev-list --first-parent --count), which the
# tested merge commit already has before it is pushed.

function Invoke-RgGit {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string[]]$Arguments)
    # git's exit code decides, not what it writes to stderr (which Windows PowerShell can turn into an error).
    $ErrorActionPreference = 'Continue'
    # Windows PowerShell decodes native output with the console encoding; git writes UTF-8 (names, messages).
    $previous = $null
    try { $previous = [Console]::OutputEncoding; [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false) } catch { $previous = $null }
    try {
        $output = & git -C $Path -c i18n.logOutputEncoding=UTF-8 -c core.quotepath=off @Arguments
        if ($LASTEXITCODE) { throw ('git ' + $Arguments[0] + ' failed with exit code ' + $LASTEXITCODE + '. The build needs the full history (actions/checkout fetch-depth: 0).') }
    } finally { if ($null -ne $previous) { try { [Console]::OutputEncoding = $previous } catch { } } }
    @($output) -join "`n"
}

function Get-RgCommits {
    # Fields separated by U+001F, records by U+001E, so messages can hold any text.
    param([string]$Path, [string[]]$Range, [string]$Fields)
    $text = Invoke-RgGit -Path $Path -Arguments (@('log', ('--format=' + $Fields + '%x1e')) + $Range)
    @($text -split [char]0x1e | ForEach-Object { $_.Trim([char[]]"`r`n") } | Where-Object { $_ } | ForEach-Object { , ($_ -split [char]0x1f) })
}

function Get-RgVersion {
    # The website version of a master commit: how many commits the first-parent line of master has up to it. Each merge
    # adds one, so versions only go up, and two pull requests can never get the same one.
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Revision)
    [int](Invoke-RgGit -Path $Path -Arguments @('rev-list', '--first-parent', '--count', $Revision)).Trim()
}

function Get-RgEntryFiles {
    # The change log entry files (Changelog/*.md except README.md) that $Revision adds compared with $Parent.
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Parent, [Parameter(Mandatory)][string]$Revision)
    $names = Invoke-RgGit -Path $Path -Arguments @('diff', '--name-only', '--no-renames', '--diff-filter=A', $Parent, $Revision, '--', 'Changelog/')
    @($names -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^Changelog/[^/]+\.md$' -and $_ -ne 'Changelog/README.md' })
}

function Read-RgEntry {
    # "# Title" on the first line, then what changed for the people using the website, as plain text.
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Revision, [Parameter(Mandatory)][string]$File)
    $lines = @((Invoke-RgGit -Path $Path -Arguments @('show', ($Revision + ':' + $File))).TrimStart([char]0xFEFF) -split "`n" | ForEach-Object { $_.TrimEnd() })
    $first = 0
    while ($first -lt $lines.Count -and -not $lines[$first].Trim()) { $first++ }
    if ($first -ge $lines.Count) { return $null }
    $title = $lines[$first].Trim() -replace '^#+\s*', ''
    # Paragraphs are separated by blank lines; lines within a paragraph are joined.
    $paragraphs = @((($lines | Select-Object -Skip ($first + 1)) -join "`n") -split '\n\s*\n' | ForEach-Object { (($_ -split "`n") | ForEach-Object { $_.Trim() }) -join ' ' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    [ordered]@{ title = $title; description = ($paragraphs -join "`n`n") }
}

function Get-RgChangeLog {
    <#
        The master history up to $Revision, newest first: one entry per commit on the first-parent line, with its website
        version. A pull request merge ("Merge pull request #12 from owner/branch", with the title on the next line, as
        GitHub and pr-checks.yml write it) becomes one entry with the pull request's number and the commits it brought
        in. Its title and description come from the Changelog/*.md file it added, or else the pull request's title.
    #>
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Revision, [string]$Repository = '',
          [ValidateRange(1, 1000)][int]$Limit = 100)
    $version = Get-RgVersion -Path $Path -Revision $Revision
    $next = $version
    $entries = foreach ($record in (Get-RgCommits -Path $Path -Fields '%H%x1f%P%x1f%aI%x1f%an%x1f%s%x1f%b' -Range @('--first-parent', '-n', [string]$Limit, $Revision))) {
        $sha = $record[0]; $parents = @($record[1] -split ' ' | Where-Object { $_ }); $subject = $record[4]
        $body = if ($record.Count -gt 5) { $record[5] } else { '' }
        $pullRequest = $null; $title = $subject; $description = ''
        if ($subject -match '^Merge pull request #(\d+) from ') {
            $pullRequest = [int]$Matches[1]
            $firstLine = @($body -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ }) | Select-Object -First 1
            if ($firstLine) { $title = $firstLine }
        }
        if ($parents.Count -ge 1) {
            $written = @(Get-RgEntryFiles -Path $Path -Parent $parents[0] -Revision $sha | ForEach-Object { Read-RgEntry -Path $Path -Revision $sha -File $_ } | Where-Object { $_ })
            if ($written.Count) {
                $title = $written[0].title
                $description = @($written | ForEach-Object { $_.description } | Where-Object { $_ }) -join "`n`n"
            }
        }
        $commits = if ($parents.Count -ge 2) {
            @(Get-RgCommits -Path $Path -Fields '%H%x1f%aI%x1f%an%x1f%s' -Range @('--no-merges', '-n', '50', ($parents[0] + '..' + $parents[1])) | ForEach-Object {
                [ordered]@{ revision = $_[0]; date = $_[1]; author = $_[2]; subject = $_[3] }
            })
        } else {
            @([ordered]@{ revision = $sha; date = $record[2]; author = $record[3]; subject = $subject })
        }
        [ordered]@{ version = $next; revision = $sha; date = $record[2]; author = $record[3]; pullRequest = $pullRequest; title = $title; description = $description; commits = @($commits | Where-Object { $_ }) }
        $next--
    }
    [ordered]@{ repository = $Repository; revision = $Revision; version = $version; generatedUtc = [DateTime]::UtcNow.ToString('o'); entries = @($entries | Where-Object { $_ }) }
}

function Write-RgChangeLog {
    # $Path: the JSON file to write; $Source: the git checkout; $Repository: owner/name on GitHub, for links.
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Source,
          [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$Revision, [string]$Repository = '')
    $log = Get-RgChangeLog -Path $Source -Revision $Revision -Repository $Repository
    [IO.File]::WriteAllText($Path, ($log | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
}

Export-ModuleMember -Function Get-RgChangeLog, Write-RgChangeLog, Get-RgEntryFiles, Read-RgEntry, Get-RgVersion
