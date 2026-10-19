# MyCRMRecords shares MyCRM's data instead of keeping a copy

MyCRMRecords now reads and writes the companies, addresses, contacts, phones, emails, relationships, group names, CRM
users, teams, territories and territory movements where MyCRM keeps them. MyCRM, MyCRMRecords and the other apps that
read MyCRM's companies see a change at once, and there is no import or sync to run any more.

Until an administrator ticks "Let MyCRMRecords change MyCRM's data" (Settings, MyCRM tab), MyCRMRecords only shows
MyCRM's data: a bar at the top of every page says so, and saving is refused. Once it is ticked, MyCRMRecords adds and
changes rows in MyCRM and never deletes one, and every value it writes is listed under "Changes made in MyCRM" with the
value it replaced.

The data migration "MyCRM's data stays in MyCRM" removes the copy MyCRMRecords used to keep, keeping what MyCRM has no
place for (territory names, who took something away and when).
