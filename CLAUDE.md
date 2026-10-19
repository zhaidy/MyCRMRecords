# Working on MyCRMRecords

## Data migrations: run from the admin page, never by hand

The production database is SQL Server, connected through the admin page. Any change that has to be made to existing
data or to the database schema must ship as a `DataMigration` in `src/MyCRMRecords.Core/Data/DataMigrations.cs`, so
an administrator runs it with the *Run migration* button on the **Data migrations** tab of `admin.aspx`. Never ask the
user to paste SQL into SQL Server Management Studio.

- Append to `DataMigrations.All`; ids start with the date (`2026-10-18-short-name`) and never change once deployed.
  Never edit or remove a migration that has been deployed (the first, `2026-10-19-create-tables`, included): add a new
  one instead.
- The migration's `Sql` is T-SQL (separate batches with `GO` lines). It runs in one transaction with its record in
  `dbo.CrMigrations`, so a failure changes nothing.
- Data lives in real tables, one row per record (`Domain/Models.cs`, listed in `Tables.All`): never a JSON document or
  list in a column. A new property of a record gets a column: a new migration with `ALTER TABLE`, and the property is
  mapped automatically (give text a `[Len]`). `Every_table_is_created_by_the_migrations_exactly_as_it_is_mapped` and
  the SQL Server tests fail until the migrations create it. A property that must not be stored gets `[NotStored]`.
- What was sent, received or changed gets a table of its own (`dbo.CrEmail`, `dbo.CrAudit`, `dbo.CrSyncChange`),
  never `dbo.CrMigrations`.
- Write the migration so it is correct on the data as it is in production, and add a test for it in
  `tests/MyCRMRecords.Tests/SqlServerTests.cs` (the SQL Server tests run in CI against SQL Server LocalDB).

## Never change the other applications unless switched on

MyCRMRecords reads and writes MyCRM's tables in the OutSystems database (the import reads them, the sync below writes
the fifteen entities of `CrmSync`: companies, addresses, contacts, phones, emails, CUser, Team, permanent territories
and their company links, territory movements...), and reads AdvanceConnector's `Customer_Supp` (the scheduled task
*Customer numbers from Advance*). Every write outside MyCRMRecords is behind a switch that starts off
(`SyncSettings.Enabled`, `EmailSettings.Enabled`, `JobSettings.Enabled`) and is recorded (`dbo.CrSyncChange`,
`dbo.CrEmail`). No write ever
deletes in another application: update or mark instead. A new write follows the same rules: a switch that starts off,
and a record of every value written with the value it replaced.

## Reading other databases: real column names, not OutSystems names

MyCRM reached AdvanceConnector through an OutSystems extension, whose attribute names hold only letters, digits and
"_". The real columns can have spaces or hyphens, or another case (`[Date]` for `date`), and some tables have
look-alike columns. Tests with tables made from the OutSystems names pass while production fails, so:

- In SQL for those databases, write a column through `ExternalTable` (`var t = ExternalTable.Of(connection, table);`
  then `t["date"]`), never `[date]`; alias selected columns (`AS [date]`).
  `SQL_for_the_other_databases_names_columns_through_ExternalTable` fails on a typed name with "_".
- Read values of `SELECT *` rows with `ExternalTable.TryGetValue(row, "Customer_Name", out var v)`: exact name first,
  the loose match only when one column fits. Never compare column names loosely in a dictionary.
- The connection test (`ExternalDatabases.Test`) lists every table and column read, so a wrong name shows on the
  Connections tab (`ExternalColumns` lists AdvanceConnector's). SQL Server tests create those tables with other names.
- `Data/ExternalTable.cs` is the same in MyWarehouse, MyServiceParts, MyDeliveries, MyVendors, MyCustomers and MyCRMRecords:
  a fix goes to all of them. The OutSystems entity tables (`OSUSR_...`) are different: their columns are the attribute
  names.

## Never delete a record

Nothing in MyCRMRecords deletes a record from its own tables: what people delete or take away is **marked**
(`IsActive = false`, with when and by whom) and the pages read only active records. A new kind of record that can be
deleted gets an `IsActive` column (a migration with `ALTER TABLE`). Only the stores (`Records.cs`, `Storage.cs`)
delete, and `Nothing_in_MyCRMRecords_deletes_a_record` fails on any other `Delete`.

## Sync with the old app: never delete there

The rest of MyCRM stays in OutSystems and uses these tables, so `OldAppSync` keeps MyCRMRecords and MyCRM in step both
ways for good (`CrmSync` says what), behind *Sync with MyCRM* on the admin page, which starts off. MyCRM must never
lose data:

- Nothing is ever deleted in MyCRM. `IOldTablesWriter` can add and change rows, not delete them; keep it that way.
  What is removed here is **marked** there instead (`ISACTIVE = 0`), and stays paired in `dbo.CrSyncLink` so it is not
  copied back. A CRM user record taken away is inactive with no team (and no role when the person has no team left):
  MyCRM reads every record's role and team whatever its `IsActive`.
- Only MyCRM's active permanent territories and their active company links are read (`CrmTables`): the temporary
  territories belong to MyCRM's Projects and CSST parts, which stay in OutSystems. Never sync them.
- MyCRM's static entities (`Role`, `CompanyGroup`, `AddressType`...) are matched by their label (`ChoiceList`), never
  by Id: the Ids differ between environments, and the tests give them Ids no list starts with.
- Every value the sync writes there is recorded with the value it replaced (`dbo.CrSyncChange`), every sync in
  `dbo.CrSyncRun`; a sync that would change more than usual stops before changing anything.
- New data MyCRMRecords keeps that MyCRM has a place for goes into the sync the same way: synced both ways, marked
  rather than deleted there, recorded. `OldAppSync.cs`, `OldTables.cs`, `Records.cs`, `ExternalTable.cs` and
  `BackgroundRuns.cs` are the same in every app that replaces an OutSystems app (MyWarehouse, MyServiceParts,
  MyDeliveries, MyVendors, MyProjectManagement, MyCustomers): a fix to the engine goes to all of them.

## Keep MyCRM's behaviour unless there is a reason

MyCRMRecords replaces the OutSystems application MyCRM. `docs/outsystems/` says how MyCRM works, and
`docs/outsystems/README.md` lists every rule MyCRMRecords changed on purpose; a new difference is added to that list.

## Compiles for .NET Framework 4.8

`MyCRMRecords.Core` builds for net48 and net8.0 and the website runs on net48: do not use APIs net48 lacks
(`Dictionary.TryAdd`, `GetValueOrDefault`, ranges and indices, `KeyValuePair` deconstruction, `string.Contains(char)`,
`Enumerable.Chunk`, `required` members...). `Polyfills.cs` has the language features C# 12 needs there. The `.aspx`
pages are compiled by ASP.NET's own compiler: keep C# in them to simple expressions (no `?.`, no string interpolation).
