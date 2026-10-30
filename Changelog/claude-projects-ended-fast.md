# Projects that have ended runs in seconds; closing a big project is quick

When a project closes, ends or is deleted, or a member leaves it, the companies of their temporary territory are taken
away in one step instead of one by one. A project with tens of thousands of companies took many minutes (and kept
MyCRM's territory table busy all that time); each company taken away is still recorded in Changes made in MyCRM.

The scheduled task *Projects that have ended* no longer compares every active temporary territory with every project
member one by one (thousands of each), which kept it running for many minutes; MyCRM's own timer takes about half a minute.
