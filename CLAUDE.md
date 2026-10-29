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
- What was sent, received or changed gets a table of its own (`dbo.CrEmail`, `dbo.CrAudit`, `dbo.CrChange`),
  never `dbo.CrMigrations`.
- Write the migration so it is correct on the data as it is in production, and add a test for it in
  `tests/MyCRMRecords.Tests/SqlServerTests.cs` (the SQL Server tests run in CI against SQL Server LocalDB).

## Never change the other applications unless switched on

MyCRMRecords reads and writes MyCRM's tables in the OutSystems database (the fifteen entities of `MyCrmMap`: companies,
addresses, contacts, phones, emails, CUser, Team, permanent territories and their company links, territory
movements...), and reads AdvanceConnector's `Customer_Supp` (the scheduled task *Customer numbers from Advance*). Every
write outside MyCRMRecords is behind a switch that starts off (`MyCrmSettings.WritesEnabled`, `EmailSettings.Enabled`,
`JobSettings.Enabled`) and is recorded (`dbo.CrChange`, `dbo.CrEmail`). No write ever deletes in another application:
update or mark instead. A new write follows the same rules: a switch that starts off, and a record of every value written
with the value it replaced.

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

## MyCRM's data stays in MyCRM: never delete there

The rest of MyCRM stays in OutSystems and uses these tables, so MyCRMRecords keeps no copy and has no sync: its pages'
records of MyCRM (`MyCrmMap.All`) are read and written in MyCRM's own tables by `MyCrmStore`, behind the same
`IRecordStore` as its own records (`RoutedStore`). MyCRM must never lose data:

- Nothing is ever deleted in MyCRM: `MyCrmStore` adds and changes rows only; keep it that way. What is taken away is
  **marked** (`ISACTIVE = 0`). A CRM user record taken away is inactive with no team (and no role when the person has no
  team left): MyCRM reads every record's role and team whatever its `IsActive`.
- What MyCRM has no column for (a territory's name, removal dates and names, a CRM user's former team, a group name or
  company address link taken away) is a `CrmFieldKind.Side` field, kept in `dbo.CrRecordNote` by MyCRM's entity and Id.
  A new property of a MyCRM record goes in `MyCrmMap` (a column of MyCRM's, or a side field); every property is mapped.
- Writes happen only when *Let MyCRMRecords change MyCRM's data* is on; otherwise `MyCrmStore` refuses with
  `MyCrmRefusedException` (`WritesOff`). A batch is one transaction in MyCRM; every value written is recorded with the
  value it replaced in `dbo.CrChange`, with who made it (`MyCrmStore.As`, set by the API and the scheduled task).
- Only MyCRM's active permanent territories (and those noted here) and the active company links of active permanent
  territories are read (`CrmRows`): the temporary territories belong to MyCRM's Projects and CSST parts, which stay in
  OutSystems, and the millions of inactive rows are never read. A query MyCRM's columns cannot answer (a side field, a
  person or code searched as text) is finished in memory after the SQL part: keep such queries narrow.
- MyCRM's static entities (`Role`, `CompanyGroup`, `AddressType`...) are matched by their label (`ChoiceList`), never
  by Id: the Ids differ between environments, and the tests give them Ids no list starts with.
- `OldAppSync.cs`, `OldTables.cs`, `Records.cs`, `ExternalTable.cs` and `BackgroundRuns.cs` are the same in every app
  that replaces an OutSystems app (MyWarehouse, MyServiceParts, MyDeliveries, MyVendors, MyProjectManagement,
  MyCustomers): MyCRMRecords keeps the sync engine files though it does not sync; a fix to the engine goes to all of them.

## MyOrders' costing sheets and request forms are only read

MyCRMRecords also keeps MyOrders' costing sheets and request forms readable (merged from MyOrdersRecords, 2026-10-30).
It reads MyOrders' and MyCRM's tables for them where they are and **never writes to them**: `IMyOrdersData`
(`Data/MyOrdersData.cs`, `Data/RequestFormData.cs`) is the only way to them and has no method that writes, and
`The_costing_sheets_and_request_forms_have_no_way_to_change_MyOrders_or_MyCRM` fails on an INSERT, UPDATE, DELETE or a
writer there. MyCRMRecords' switch for MyCRM's data has nothing to do with them.

- Every entity read and its columns are in `Entities.Checked` (the Connections tab's *Test* checks each, in the
  *MyOrders module* setting). A new column or entity goes there, and in `OrdersSqlServerTests.OutSystemsSql`.
- MyOrders' tables are found even after MyOrders is retired (`EntityTable(..., retiredToo: true)`); MyCRM's never are.
- A file column (Binary Data) is read as its length (`DATALENGTH`); only `File` reads one file's bytes, for its download.
- Long lists are searched and paged in SQL (`SqlMyOrdersData.Search`, `SearchRequestForms`); `FakeMyOrders` answers the
  same searches in memory, and `OrdersSqlServerTests` checks both answer alike: a new filter or sort goes in both.
- Who sees which is MyOrders' rule (`OrdersAccess`, by OutSystems user, with `SamePerson` for someone OutSystems keeps
  twice), read from `Actor.Orders` (`ApiUser.ToActor(people, app.OrdersAccess)`), applied alike to a list, a record,
  its attachments and its print. It is not MyCRMRecords' company rule (`Access`): keep the two apart.
- The prints (`print.js`, `requestformprint.js`) keep MyOrders' texts verbatim; `docs/outsystems/myorders/` says how
  MyOrders works and lists every rule changed on purpose.

## Keep MyCRM's behaviour unless there is a reason

MyCRMRecords replaces the OutSystems application MyCRM. `docs/outsystems/` says how MyCRM works, and
`docs/outsystems/README.md` lists every rule MyCRMRecords changed on purpose; a new difference is added to that list.

## Compiles for .NET Framework 4.8

`MyCRMRecords.Core` builds for net48 and net8.0 and the website runs on net48: do not use APIs net48 lacks
(`Dictionary.TryAdd`, `GetValueOrDefault`, ranges and indices, `KeyValuePair` deconstruction, `string.Contains(char)`,
`Enumerable.Chunk`, `required` members...). `Polyfills.cs` has the language features C# 12 needs there. The `.aspx`
pages are compiled by ASP.NET's own compiler: keep C# in them to simple expressions (no `?.`, no string interpolation).
