# Change log entry (every pull request)

Add the change log entry for your change as a new file `Changelog/<branch name>.md` (`/` replaced by `-`): a first
line `# Short title`, then what changed for the people using MyCRMRecords, in plain text. See `Changelog/README.md`.

- Never edit another pull request's entry or any shared change log file: one file per pull request is what keeps
  parallel pull requests from conflicting.
- The version number is added automatically from the git history; do not write one.

# User guide (every commit people can see)

`docs/user-guide.md` is the user guide shown in MyCRMRecords: the ? button in the top bar of every page opens it
beside the page (guide.js), and `guide.aspx` shows it whole. Every commit that changes what people see or do in
MyCRMRecords updates the guide in the same commit, or at the latest in the same pull request: a new feature gets
its steps in the section of its page, a changed button, label, rule or role is changed where the guide describes it,
and a removed feature is taken out. Write for the people using MyCRMRecords, task by task and in plain language,
naming buttons and labels exactly as the screen shows them; technical detail belongs in the README. A commit people
cannot see (a refactor, tests, the deployment scripts) leaves the guide as it is.

- Keep the headings the pages link to (each page's ? button opens `guide.aspx#<heading id>`, GitHub's id for the
  heading): renaming one means changing the page's link too (`TopBar(current, guideSection)` in the page). A new page
  gets the ? button and `guide.js` like the others, and a section of its own. `UserGuideTests` fails until the links
  match.
- After a commit that changes `src/` without the guide, a hook (`.claude/hooks/user-guide-reminder.sh`) reminds you to
  check whether the guide needs updating.
