# The first page after a quiet spell no longer waits

- MyCRMRecords keeps MyCRM's list of people and its choice lists ready and refreshes them in the background, instead of
  making the next page wait while it reads them again (every two and ten minutes). They are read once as the website
  starts, so the first pages after a deployment are quick too.
- The Activities list reads the types and company names of the 50 activities shown only (unless you search or choose a
  type), not of every activity in your history.
