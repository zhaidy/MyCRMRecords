# Projects that have ended keeps projects with no end date, stops when too much would change, and can be rolled back

- The scheduled task *Projects that have ended* took away the temporary territories of projects with no end date (the
  CSST part's projects), which MyCRM keeps: a project with no end date is no longer treated as ended.
- A run that would close more than 50 projects or take away more than 50 temporary territories stops before changing
  anything and says why.
- On Settings › Scheduled tasks, **Roll back** switches back on the temporary territories the task switched off today
  that it should have kept (their member is still in a project that is not over), after asking how many; each is
  recorded in Changes made in MyCRM.
