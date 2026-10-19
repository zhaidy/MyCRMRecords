# Change log entries

Each pull request describes its change for the people using MyCRMRecords in **a file of its own** here,
`Changelog/<branch name>.md` (with `/` replaced by `-`, for example `claude-charming-cerf-uh0d3q.md`). There is no
shared change log file to edit, so two open pull requests never change the same lines and the change log never makes
them conflict (the same idea as ReportGenerator3's `ChangelogNext/`).

```markdown
# Short title

What changed for the people using the website, and why it helps. Plain text; a blank line starts a new paragraph.
```

- One file per pull request. Never edit or delete an entry once it has merged: it is the record of that version.
- Nothing has to number or move the entry. Each build reads the entries from the git history
  (`Deployment/ChangeLog.psm1`), and an entry's **version** is its merge commit's position on `master`, so versions
  only go up and two pull requests can never get the same one. The *Deployment* admin page shows them.
- A pull request without an entry still merges; the change log then shows the pull request's title, and the check
  warns. A pull request that adds more than one entry, or an empty one, fails the check.
