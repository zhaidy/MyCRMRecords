#!/usr/bin/env bash
# PostToolUse hook (Bash), set up in .claude/settings.json. After Claude commits, when the commit changes the application
# (src/) but not the user guide (docs/user-guide.md), it reminds Claude to check whether the guide needs updating (see
# .claude/CLAUDE.md). It never blocks anything: a commit people cannot see needs no guide change. It reminds once per
# commit, and never for a merge commit (merging master brings other pull requests' changes).
input=$(cat)
printf '%s' "$input" | grep -Eq 'git( -C [^ ]+)? commit' || exit 0
cd "${CLAUDE_PROJECT_DIR:-.}" 2>/dev/null || exit 0
head=$(git rev-parse -q --verify HEAD 2>/dev/null) || exit 0
git rev-parse -q --verify 'HEAD^2' >/dev/null 2>&1 && exit 0
marker=$(git rev-parse --git-path claude-user-guide-reminded 2>/dev/null) || exit 0
[ "$(cat "$marker" 2>/dev/null)" = "$head" ] && exit 0
files=$(git diff-tree --root --no-commit-id --name-only -r HEAD 2>/dev/null)
printf '%s\n' "$files" | grep -qx 'docs/user-guide.md' && exit 0
printf '%s\n' "$files" | grep -q '^src/' || exit 0
printf '%s\n' "$head" > "$marker" 2>/dev/null
cat <<'JSON'
{"hookSpecificOutput":{"hookEventName":"PostToolUse","additionalContext":"The commit just made changes src/ but not docs/user-guide.md, the user guide shown in MyCRMRecords (the ? panel and guide.aspx). If it changes what people see or do (a page, button, label, rule, role or workflow), update the section of the guide that describes it, in plain language for the people using MyCRMRecords, and commit that in this pull request; keep the headings the pages' ? buttons link to (UserGuideTests checks them). If people cannot see the change (a refactor, tests, an internal fix), leave the guide as it is. See .claude/CLAUDE.md."}}
JSON
