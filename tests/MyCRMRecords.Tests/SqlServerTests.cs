using System.Data.Common;
using System.Reflection;
using Microsoft.Data.SqlClient;

namespace MyCRMRecords.Tests;

/// <summary>A new SQL Server database for one test, dropped after it.</summary>
public sealed class TestDatabase : IDisposable
{
    public static readonly string? Server = Environment.GetEnvironmentVariable("MYCRMRECORDS_TEST_SQLSERVER");
    public static Func<string, DbConnection> Connect { get; } = cs => new SqlConnection(cs);

    public string Name { get; } = "MyCRMRecordsTest_" + Guid.NewGuid().ToString("N").Substring(0, 12);
    public SqlServerSettings Settings { get; }

    public TestDatabase()
    {
        using (var master = new SqlConnection(Server))
        {
            master.Open();
            using var create = new SqlCommand($"CREATE DATABASE [{Name}]", master);
            create.ExecuteNonQuery();
        }
        Settings = new SqlServerSettings
        {
            Server = new SqlConnectionStringBuilder(Server).DataSource, Database = Name, Authentication = SqlAuthentication.Windows, TrustServerCertificate = true,
        };
    }

    public void Execute(string sql)
    {
        using var connection = new SqlConnection(Settings.ToConnectionString());
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    public object? Scalar(string sql)
    {
        using var connection = new SqlConnection(Settings.ToConnectionString());
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        return command.ExecuteScalar();
    }

    public void Dispose()
    {
        SqlConnection.ClearAllPools();
        using var master = new SqlConnection(Server);
        master.Open();
        using var drop = new SqlCommand($"ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}]", master);
        drop.ExecuteNonQuery();
    }
}

/// <summary>Against a real SQL Server: run when MYCRMRECORDS_TEST_SQLSERVER is set (CI sets it to LocalDB).</summary>
public sealed class SqlServerTests
{
    private static DatabaseStorage Storage(string folder) =>
        new(new DatabaseSettingsFile(Path.Combine(folder, "database-settings.json"), new FakeProtector()), new JsonRecordStore(Path.Combine(folder, "data")), TestDatabase.Connect);

    /// <summary>Every stored property set to a value of its type (different per record).</summary>
    private static T Filled<T>(int seed) where T : class, IRecord, new()
    {
        var record = new T();
        foreach (var column in Table.Of<T>().Columns.Skip(1))
        {
            var type = Nullable.GetUnderlyingType(column.Type) ?? column.Type;
            var property = typeof(T).GetProperty(column.Name)!;
            var length = property.GetCustomAttribute<LenAttribute>()?.Length ?? 0;
            object? value = type == typeof(string) ? ("é" + column.Name + seed).Substring(0, Math.Min(length, column.Name.Length + 2))
                : type == typeof(long) ? 1_000_000_000_000L + seed
                : type == typeof(int) ? 10 + seed
                : type == typeof(decimal) ? 1234.5678m + seed
                : type == typeof(bool) ? true
                : type == typeof(DateTime) ? new DateTime(2026, 1, 1, 8, 30, 15, 123).AddDays(seed)
                : type == typeof(byte[]) ? new byte[] { 0, 1, 2, 255, (byte)seed }
                : null;
            property.SetValue(record, value);
        }
        return record;
    }

    private static void RoundTrip<T>(IRecordStore store) where T : class, IRecord, new()
    {
        var record = store.Insert(Filled<T>(3));
        var read = store.Get<T>(record.Id)!;
        Assert.Equal(MyCRMRecords.Json.Serialize(record), MyCRMRecords.Json.Serialize(read));
        var empty = store.Insert(new T());
        Assert.Equal(MyCRMRecords.Json.Serialize(empty), MyCRMRecords.Json.Serialize(store.Get<T>(empty.Id)!));
    }

    [Fact]
    public void The_migrations_create_every_table_and_each_holds_every_property_exactly()
    {
        if (string.IsNullOrEmpty(TestDatabase.Server)) return;
        using var database = new TestDatabase();
        using var world = new TestWorld();
        var storage = Storage(world.Folder);
        Assert.Equal(DataMigrations.All.Select(m => m.Id), storage.RunPendingMigrations(database.Settings, "Test"));
        Assert.Empty(storage.RunPendingMigrations(database.Settings, "Test"));
        Assert.Equal(DataMigrations.All.Count, storage.AppliedMigrations(database.Settings).Count);
        var store = storage.SqlServer(database.Settings);
        var roundTrip = typeof(SqlServerTests).GetMethod(nameof(RoundTrip), BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var type in Tables.All)
            roundTrip.MakeGenericMethod(type).Invoke(null, [store]);
        Assert.All(storage.TableCounts(database.Settings), t => Assert.True(t.Rows >= 0, t.Table));
        Assert.Throws<InvalidOperationException>(() => storage.RunMigration(database.Settings, DataMigrations.All[0], "Test"));
    }

    /// <summary>MyCRM's tables in the OutSystems database, as MyCRMRecords reads and writes them (writing switched on).</summary>
    private static MyCrmStore MyCrm(TestWorld world, TestDatabase outSystems, bool writes = true)
    {
        world.SettingsFile.Change(s =>
        {
            s.Connections.OutSystems.Configured = true;
            s.Connections.OutSystems.SqlServer = outSystems.Settings;
        });
        return new MyCrmStore(world.Store, new ExternalDatabases(world.SettingsFile.Load, TestDatabase.Connect), () => "MyCRM", () => writes, () => world.Now);
    }

    [Fact]
    public void MyCRM_tables_answer_queries_as_the_json_store_does()
    {
        if (string.IsNullOrEmpty(TestDatabase.Server)) return;
        using var outSystems = new TestDatabase();
        outSystems.Execute(FakeOldTables.MyCRM().OutSystemsSql("MyCRM"));
        using var world = new TestWorld(crmUsers: false);
        using var other = new TestWorld(crmUsers: false);
        var day = world.Now.Date;
        foreach (var store in new IRecordStore[] { other.Store, MyCrm(world, outSystems) })
        {
            var a = store.Insert(new Company { Name = "A", UpdatedDate = day, Website = "Logistics", Brn = "a_b", SalesRemarks = "100% done", IsActive = true });
            var b = store.Insert(new Company { Name = "B", UpdatedDate = day.AddDays(1), Website = "IT", Brn = "axb", SalesRemarks = "Could not be reached", IsActive = false });
            var c = store.Insert(new Company { Name = "C", UpdatedDate = day.AddDays(2), Website = "Logistics", Brn = "c", CompanyGroup = CompanyGroups.List.All[1].Code, IsActive = false });
            Assert.Equal(new[] { c.Id, a.Id }, store.Find<Company>(Query.Where("Website", "logistics").Newest()).Select(m => m.Id));
            Assert.Equal(new[] { a.Id }, store.Find<Company>(Query.All().Search("a_b", "Brn")).Select(m => m.Id));
            Assert.Equal(new[] { a.Id }, store.Find<Company>(Query.All().Search("100%", "SalesRemarks")).Select(m => m.Id));
            Assert.Equal(2, store.Count<Company>(Query.All().Not("Website", "IT")));
            Assert.Equal(new[] { a.Id }, store.Find<Company>(Query.Where("IsActive", true)).Select(m => m.Id));
            Assert.Equal(2, store.Count<Company>(Query.Where("IsActive", false)));
            Assert.Equal(new[] { b.Id, c.Id }, store.Find<Company>(Query.All().AtLeast("UpdatedDate", day.AddDays(1)).Sort("UpdatedDate")).Select(m => m.Id));
            Assert.Equal(new[] { a.Id, c.Id }, store.Find<Company>(Query.All().In("Id", new object[] { a.Id, c.Id })).Select(m => m.Id));
            // A code of a list (MyCRM's static record of that label), and none.
            Assert.Equal(new[] { c.Id }, store.Find<Company>(Query.Where("CompanyGroup", CompanyGroups.List.All[1].Code)).Select(m => m.Id));
            Assert.Equal(new[] { a.Id, b.Id }, store.Find<Company>(Query.Where("CompanyGroup", "").Sort("Id")).Select(m => m.Id));
            // More values than SQL Server takes as parameters (2100), and text with quotes.
            var ids = Enumerable.Range(1, 3000).Select(i => (object)(i * 1000000L)).Append(a.Id).Append(c.Id).ToList();
            Assert.Equal(new[] { a.Id, c.Id }, store.Find<Company>(Query.All().In("Id", ids).Sort("Id")).Select(m => m.Id));
            var codes = Enumerable.Range(1, 2500).Select(i => (object)("x'" + i.ToString(System.Globalization.CultureInfo.InvariantCulture))).Append("a_b").Append("c").ToList();
            Assert.Equal(new[] { a.Id, c.Id }, store.Find<Company>(Query.All().In("Brn", codes).Sort("Id")).Select(m => m.Id));
            Assert.Equal(2, store.Count<Company>(Query.All().In("Id", ids)));
            Assert.Equal(new[] { b.Id }, store.Find<Company>(Query.All().Sort("Id").Page(1, 1)).Select(m => m.Id));
            // A nullable reference (long?) found in a list; people by login; what MyCRM has no column for (a territory's name).
            store.Insert(new Phone { ContactId = 42, Number = "1" });
            Assert.Single(store.Find<Phone>(Query.All().In("ContactId", new object[] { 42L, 43L })));
            var north = store.Insert(new Territory { Name = "North", Number = 3, Owner = TestWorld.Sam.Login, IsActive = true });
            store.Insert(new Territory { Name = "South", Number = 4, Owner = TestWorld.Ken.Login, IsActive = true });
            Assert.Equal(4, store.Find<Territory>(Query.All().Sort("Number", descending: true)).First().Number);
            Assert.Equal(new[] { north.Id }, store.Find<Territory>(Query.Where("Owner", TestWorld.Sam.Login)).Select(t => t.Id));
            Assert.Equal(new[] { north.Id }, store.Find<Territory>(Query.Where("Name", "north")).Select(t => t.Id));
            Assert.Equal(new[] { "North", "South" }, store.Find<Territory>(Query.Where("IsActive", true).Sort("Name")).Select(t => t.Name));
            Assert.Throws<InvalidOperationException>(() => store.Batch(s =>
            {
                s.Insert(new Company { Name = "Lost" });
                throw new InvalidOperationException("stop");
            }));
            Assert.Equal(0, store.Count<Company>(Query.Where("Name", "Lost")));
        }
    }

    [Fact]
    public void Switching_to_sql_server_copies_the_data_in()
    {
        if (string.IsNullOrEmpty(TestDatabase.Server)) return;
        using var database = new TestDatabase();
        using var world = new TestWorld();
        var people = world.Store.Count<AppUser>();
        Assert.True(people > 0);
        world.Store.Insert(new AuditEntry { At = world.Now, Login = TestWorld.Sam.Login, Action = "Sign in" });
        var storage = new DatabaseStorage(new DatabaseSettingsFile(Path.Combine(world.Folder, "database-settings.json"), new FakeProtector()), world.Store, TestDatabase.Connect);
        var d = CrmFactory.Create(storage, world.SettingsFile, TestDatabase.Connect, Path.Combine(world.Folder, "tasks.json"), () => world.Now, myCrm: world.Store);
        var admin = new AdminApi(d, storage, demoTools: false);
        var answer = admin.Handle("POST", "admin/database", MyCRMRecords.Json.Serialize(new
        {
            provider = StorageProviders.SqlServer,
            sqlServer = new { server = database.Settings.Server, database = database.Settings.Database, authentication = "Windows", trustServerCertificate = true },
        }), new Dictionary<string, string>(), TestWorld.Admin);
        Assert.True(answer.Status == 200, answer.Json);
        Assert.NotNull(storage.CurrentSqlServer());
        Assert.Equal(people, storage.Count<AppUser>());
        Assert.Equal((long)people, Convert.ToInt64(database.Scalar("SELECT COUNT(*) FROM dbo.CrUser")));
        Assert.Equal("Sign in", storage.Find<AuditEntry>(Query.Where("Login", TestWorld.Sam.Login)).Single().Action);
    }

    private static CrmApp Connected(TestWorld world, TestDatabase outSystems, TestDatabase? advance, bool writes)
    {
        world.SettingsFile.Change(s =>
        {
            s.Connections.OutSystems.Configured = true;
            s.Connections.OutSystems.SqlServer = outSystems.Settings;
            if (advance is not null) s.Connections.AdvanceConnector = new ExternalDatabase { Configured = true, SqlServer = advance.Settings };
            s.MyCrm.WritesEnabled = writes;
        });
        return CrmFactory.Create(world.Store, world.SettingsFile, TestDatabase.Connect, Path.Combine(world.Folder, "tasks.json"), () => world.Now, world.Email);
    }

    /// <summary>
    /// MyCRM in OutSystems: Mia's team (1001) with Sam's CUser (Sales), the group Lim Group, company 7 (created by Sara, 102)
    /// with its address, primary contact, phone and email, Sam's permanent territory 1203 (1001) with company 7 and a
    /// movement; and rows that are not MyCRMRecords': Ken's temporary territory of a project (1002) with company 7, an
    /// inactive territory (1003) and an inactive link.
    /// </summary>
    private static void FillMyCRM(TestDatabase outSystems)
    {
        var old = FakeOldTables.MyCRM();
        outSystems.Execute(old.OutSystemsSql("MyCRM"));
        outSystems.Execute($"""
            INSERT INTO dbo.[OSUSR_ab1_TEAM] (NAME, SHORTNAME, ISACTIVE) VALUES (N'Mia Manager', N'', 1);
            INSERT INTO dbo.[OSUSR_ab1_CUSER] (USERID, ROLEID, TEAMID, ISACTIVE, CREATEDAT, DESIGNATION, CID)
              VALUES (101, {old.StaticId("Role", "Sales")}, 1001, 1, '2026-01-05 08:00', N'Sales Executive', 12);
            INSERT INTO dbo.[OSUSR_ab1_COMPANYGROUPNAME] (GROUPNAME) VALUES (N'Lim Group');
            SET IDENTITY_INSERT dbo.[OSUSR_ab1_COMPANY] ON;
            INSERT INTO dbo.[OSUSR_ab1_COMPANY] (ID, COMPANY, BRN, ISACTIVE, COMPANYGROUPID, COMPANYGROUPNAMEID, COMPACCOUNTTYPEID, CREATEDDATE, CREATEDBY, PRECOMPANYNAME,
              REVENUE, SALESREMARKS)
              VALUES (7, N'Lim Pte Ltd', N'199912345K', 1, {old.StaticId("CompanyGroup", "SME")}, 1001, {old.StaticId("CompAccountType", "Non-User")}, '2025-03-01', 102,
              N'Lim Ltd', 1000.25, N'Call first');
            SET IDENTITY_INSERT dbo.[OSUSR_ab1_COMPANY] OFF;
            INSERT INTO dbo.[OSUSR_ab1_ADDRESS] (ADDRESSTYPEID, BLOCK, STREET, POSTALCODE, ISACTIVE, ISDEFAULT, COUNTRY, CREATEDAT, CREATEDBY)
              VALUES ({old.StaticId("AddressType", "Billing")}, N'2', N'Lim Street', N'654321', 1, 1, N'Singapore', '2025-03-01 10:00', 102);
            INSERT INTO dbo.[OSUSR_ab1_COMPANYADDRESSLINK] (COMPANYID, ADDRESSID) VALUES (7, 1001);
            INSERT INTO dbo.[OSUSR_ab1_CONTACTS] (FIRSTNAME, LASTNAME, ISACTIVE, CREATEDAT, CREATEDBY) VALUES (N'Mei', N'Lim', 1, '2025-03-01 10:05', 102);
            INSERT INTO dbo.[OSUSR_ab1_COMPANYCONTACTLINK] (COMPANYID, CONTACTID, ISDEFAULT) VALUES (7, 1001, 1);
            INSERT INTO dbo.[OSUSR_ab1_PHONE] (COMPANYID, CONTACTID, PHONETYPEID, PHONE, ISACTIVE, CREATEDAT)
              VALUES (7, 1001, {old.StaticId("PhoneType", "Mobile")}, N'91234567', 1, '2025-03-01 10:06');
            INSERT INTO dbo.[OSUSR_ab1_EMAIL] (EMAIL, COMPANYID, CONTACTID, ISACTIVE, CONSENTTOMARKETINGINFO, CREATEDAT)
              VALUES (N'mei@lim.example', 7, 1001, 1, 1, '2025-03-01 10:07');
            INSERT INTO dbo.[OSUSR_ab1_TERRITORY] (TERRITORY, OWNERID, ISACTIVE, ISTEMPORARY, ISOWNER) VALUES (1203, 101, 1, 0, 1), (1071007, 107, 1, 1, 0), (1203, 101, 0, 0, 1);
            INSERT INTO dbo.[OSUSR_ab1_COMPANYTERRITORY] (TERRITORYID, COMPANYID, ISACTIVE) VALUES (1001, 7, 1), (1002, 7, 1), (1001, 7, 0);
            INSERT INTO dbo.[OSUSR_ab1_TERRITORYMOVEMENT] (COMPANYID, FROMUSERID, TOUSERID, UPDATEDBY, UPDATEDAT) VALUES (7, 107, 101, 102, '2026-10-01 09:00');
            """);
    }

    [Fact]
    public void MyCRM_data_is_read_where_MyCRM_keeps_it_with_only_the_permanent_territories()
    {
        if (string.IsNullOrEmpty(TestDatabase.Server)) return;
        using var outSystems = new TestDatabase();
        FillMyCRM(outSystems);
        using var world = new TestWorld(crmUsers: false);
        var d = Connected(world, outSystems, null, writes: false);
        var c = d.Store.Get<Company>(7)!;
        Assert.Equal("Lim Pte Ltd", c.Name);
        Assert.Equal("SME", c.CompanyGroup);
        Assert.Equal("NonUser", c.CompAccountType);
        // MyCRM's users are RSP\name@ricoh.sg: the logins are RSP\name.
        Assert.Equal(@"RSP\sara", c.CreatedBy);
        Assert.Equal(1000.25m, c.Revenue);
        Assert.Equal(new DateTime(2025, 3, 1), c.CreatedDate);
        Assert.Equal("Lim Group", d.Store.Get<CompanyGroupName>(c.CompanyGroupNameId!.Value)!.GroupName);
        var sam = d.Store.Find<CrmUser>().Single();
        Assert.Equal(@"RSP\sam", sam.Login);
        Assert.Equal(CrmRoles.Sales, sam.Role);
        Assert.Equal(1001, sam.TeamId);
        // Of the three territories and three links, the active permanent one and its active link.
        var territory = d.Store.Find<Territory>().Single();
        Assert.Equal((1001L, @"RSP\sam", 1203L), (territory.Id, territory.Owner, territory.Number));
        Assert.Equal(7, d.Store.Find<CompanyTerritory>().Single().CompanyId);
        Assert.Equal(@"RSP\sam", d.Territories.OwnerOf(7));
        Assert.Equal(AddressTypes.Billing, d.Companies.Addresses(7).Single().AddressType);
        Assert.Equal("Mobile", d.Store.Find<Phone>().Single().PhoneType);
        Assert.Equal((@"RSP\ken", @"RSP\sam"), (d.Territories.Movements(7).Single().FromUser, d.Territories.Movements(7).Single().ToUser));
        // Ken reaches company 7 through MyCRM's temporary territory of his project; Olga does not.
        Assert.True(d.Access.CanOpen(new ApiUser(@"RSP\ken", "Ken Kwan").ToActor(d.People), c));
        Assert.False(d.Access.CanOpen(new ApiUser(@"RSP\olga", "Olga Other").ToActor(d.People), c));
        var list = new CrmApi(d).Handle("GET", "companies", "", new Dictionary<string, string>(), TestWorld.Admin);
        Assert.True(list.Status == 200, list.Json);
        Assert.Contains("Lim Pte Ltd", list.Json);
        Assert.All(d.Databases.Test(ExternalNames.OutSystems), l => Assert.True(l.Ok, l.Line));

        // Writing is off: a change is refused and nothing changes in MyCRM.
        var saved = new CrmApi(d).Handle("POST", "companies/7/employees", MyCRMRecords.Json.Serialize(new { noOfEmployees = 12 }), new Dictionary<string, string>(), TestWorld.Admin);
        Assert.Equal(409, saved.Status);
        Assert.Contains("only reads MyCRM", saved.Json);
        Assert.Equal(0, Convert.ToInt32(outSystems.Scalar("SELECT NOOFEMPLOYEES FROM dbo.[OSUSR_ab1_COMPANY] WHERE ID = 7")));
        Assert.Empty(world.Store.Find<MyCrmChange>());
        Assert.Contains("only reads MyCRM", new CrmApi(d).Handle("GET", "session", "", new Dictionary<string, string>(), TestWorld.Admin).Json);
    }

    [Fact]
    public void MyCRMRecords_writes_MyCRM_tables_when_let_never_deletes_there_and_records_every_value()
    {
        if (string.IsNullOrEmpty(TestDatabase.Server)) return;
        using var outSystems = new TestDatabase();
        FillMyCRM(outSystems);
        using var world = new TestWorld(crmUsers: false);
        var d = Connected(world, outSystems, null, writes: true);
        int Count(string table, string where = "1 = 1") => Convert.ToInt32(outSystems.Scalar($"SELECT COUNT(*) FROM dbo.[OSUSR_ab1_{table}] WHERE {where}"));
        var rows = new[] { "COMPANY", "ADDRESS", "COMPANYADDRESSLINK", "TERRITORY", "COMPANYTERRITORY", "CUSER", "TEAM" }.ToDictionary(t => t, t => Count(t));
        var admin = world.Actor(TestWorld.Admin);
        using var author = MyCrmStore.As(admin.ChangedBy);

        // A new company takes MyCRM's number, with MyCRM's static record and user.
        var made = d.Companies.Save(admin, 0, new Companies.CompanyInput { Name = "Tan & Sons", Brn = "200012345K", CompanyGroup = "GMA" });
        Assert.True(made.Ok, made.Message);
        var id = made.Id!.Value;
        Assert.NotEqual(7, id);
        Assert.Equal(1, Count("COMPANY", $"ID = {id} AND COMPANY = N'Tan & Sons' AND COMPANYGROUPID = {FakeOldTables.MyCRM().StaticId("CompanyGroup", "GMA")} AND CREATEDBY = 1"));
        Assert.Equal("GMA", d.Store.Get<Company>(id)!.CompanyGroup);

        // A new territory of Sam's, with its name kept here, takes Tan & Sons and company 7 (out of territory 1203).
        var territory = d.Territories.Save(admin, 0, new Territories.TerritoryInput { Name = "North", Number = 2001, Owner = @"RSP\sam" });
        Assert.True(territory.Ok, territory.Message);
        var north = territory.Id!.Value;
        Assert.Equal(1, Count("TERRITORY", $"ID = {north} AND TERRITORY = 2001 AND OWNERID = 101 AND ISACTIVE = 1 AND ISTEMPORARY = 0"));
        Assert.Equal("North", d.Territories.Get(north)!.Name);
        Assert.True(d.Territories.AddCompanies(admin, north, [id, 7]).Ok);
        Assert.Equal(0, Count("COMPANYTERRITORY", "TERRITORYID = 1001 AND ISACTIVE = 1"));
        Assert.Equal(2, Count("COMPANYTERRITORY", $"TERRITORYID = {north} AND ISACTIVE = 1"));
        // MyCRM's project territory is left as it is.
        Assert.Equal(1, Count("COMPANYTERRITORY", "TERRITORYID = 1002 AND ISACTIVE = 1"));
        // Tan & Sons moved from nobody to Sam; company 7 stayed Sam's (no movement).
        Assert.Equal(2, Count("TERRITORYMOVEMENT"));
        Assert.Equal(new[] { id, 7L }.OrderBy(x => x), d.Territories.Companies(north, null, 0).Rows.Select(x => x.Id).OrderBy(x => x));

        // An address of company 7 deleted: the address inactive in MyCRM, its link (MyCRM has no mark) kept and marked here.
        var address = d.Companies.Addresses(7).Single();
        Assert.True(d.Companies.RemoveAddress(admin, 7, address.Id).Ok);
        Assert.Equal(1, Count("ADDRESS", $"ID = {address.Id} AND ISACTIVE = 0 AND PREISACTIVE = 1"));
        Assert.Empty(d.Companies.Addresses(7));

        // Ken made a CRM user of Mia's team, then taken away: inactive with no team in MyCRM, his former team kept here.
        Assert.True(d.Users.Save(admin, @"RSP\ken", new CrmUsers.ProfileInput { Role = CrmRoles.Sales, Teams = [1001] }).Ok);
        Assert.Equal(1, Count("CUSER", "USERID = 107 AND TEAMID = 1001 AND ISACTIVE = 1"));
        Assert.True(d.Users.RemoveUserAndTerritories(admin, @"RSP\ken").Ok);
        Assert.Equal(1, Count("CUSER", "USERID = 107 AND TEAMID IS NULL AND ISACTIVE = 0"));
        Assert.Equal(1001, d.Store.Find<CrmUser>(Query.Where("Login", @"RSP\ken")).Single().FormerTeamId);

        // The territory taken away: inactive in MyCRM, still listed here with its name among the taken away ones.
        Assert.True(d.Territories.Remove(admin, north).Ok);
        Assert.Equal(1, Count("TERRITORY", $"ID = {north} AND ISACTIVE = 0"));
        Assert.DoesNotContain(d.Territories.All(), t => t.Id == north);
        Assert.Equal("North", d.Territories.All(includeInactive: true).Single(t => t.Id == north).Name);

        // Nothing deleted anywhere; every value written is recorded with the one it replaced.
        Assert.Equal(rows["COMPANY"] + 1, Count("COMPANY"));
        Assert.Equal(rows["ADDRESS"], Count("ADDRESS"));
        Assert.Equal(rows["COMPANYADDRESSLINK"], Count("COMPANYADDRESSLINK"));
        Assert.Equal(rows["TERRITORY"] + 1, Count("TERRITORY"));
        Assert.Equal(rows["COMPANYTERRITORY"] + 2, Count("COMPANYTERRITORY"));
        Assert.Equal(rows["CUSER"] + 1, Count("CUSER"));
        var changes = world.Store.Find<MyCrmChange>();
        Assert.Contains(changes, x => x.TableName == "Company" && x.RowId == id && x.Column == "COMPANY" && x.Before == "" && x.After == "Tan & Sons" && x.ChangedBy == "Rob Root");
        Assert.Contains(changes, x => x.TableName == "CompanyTerritory" && x.RowId == 1001 && x.Column == "ISACTIVE" && x.Before == "1" && x.After == "0");
        Assert.Contains(changes, x => x.TableName == "Address" && x.RowId == address.Id && x.Column == "ISACTIVE" && x.Before == "1" && x.After == "0");
        // Saving what did not change writes nothing.
        var before = changes.Count;
        d.Store.Update(d.Store.Get<Company>(7)!);
        Assert.Equal(before, world.Store.Count<MyCrmChange>());
    }

    [Fact]
    public void The_company_summary_and_the_projects_download_read_MyCRMs_tables_in_bulk_and_write_nothing()
    {
        if (string.IsNullOrEmpty(TestDatabase.Server)) return;
        using var outSystems = new TestDatabase();
        FillMyCRM(outSystems);
        using var world = new TestWorld(crmUsers: false);
        var d = Connected(world, outSystems, null, writes: true);
        var admin = world.Actor(TestWorld.Admin);
        using var author = MyCrmStore.As(admin.ChangedBy);
        // Company 7 (Sam's territory 1203, billing address), a company nobody owns, and a project with both: Ken its caretaker.
        var tan = d.Companies.Save(admin, 0, new Companies.CompanyInput { Name = "Tan & Sons", Brn = "200012345K" }).Id!.Value;
        Assert.True(d.Users.Save(admin, @"RSP\ken", new CrmUsers.ProfileInput { Role = CrmRoles.Sales, Teams = [1001] }).Ok);
        var project = d.Projects.Create(admin, new Projects.ProjectInput { Name = "Cover", EndDate = world.Now.Date.AddDays(30).ToString("yyyy-MM-dd"), Companies = [tan, 7] }).Id!.Value;
        Assert.True(d.Projects.AddMember(admin, project, new Projects.MemberInput { Login = @"RSP\ken", Role = ProjectRoles.SalespersonCaretaker }).Ok);
        var written = world.Store.Count<MyCrmChange>();

        var (total, lines) = d.Reports.CompanySummary(admin, new Companies.ListFilter());
        Assert.Equal(2, total);
        var lim = lines.Single(l => l.Company.Id == 7);
        Assert.Equal(("Territory 1203", "Mia Manager", "654321", "2 Lim Street, 654321"), (lim.Territory, lim.Team, lim.PostalCode, lim.BillingAddress));
        Assert.Equal(("SME", "Lim Group", "Non-User"), (lim.CustomerType, lim.GroupName, lim.AccountType));
        Assert.EndsWith("(Cover)", lim.Caretakers);
        Assert.EndsWith("(Cover)", lines.Single(l => l.Company.Id == tan).Caretakers);
        Assert.Equal(new[] { tan }, d.Reports.CompanySummary(admin, new Companies.ListFilter(Show: Companies.NoSalesperson)).Lines.Select(l => l.Company.Id));
        Assert.Equal(new[] { 7L }, d.Reports.CompanySummary(admin, new Companies.ListFilter(PostalCode: "654321")).Lines.Select(l => l.Company.Id));
        Assert.Single(d.Reports.CompanySummary(admin, new Companies.ListFilter(), max: 1).Lines);

        // The project and a line per company; Ken reaches both through his temporary territory.
        var report = d.Reports.ProjectsReport(admin, "open", null);
        var p = report.Projects.Single(x => x.Project.Id == project);
        Assert.Equal(("Open", 2), (p.State, p.Companies));
        var companies = report.Companies.Where(c => c.Project.Id == project).ToList();
        Assert.Equal(new[] { tan, 7L }, companies.Select(c => c.CompanyId));
        Assert.All(companies, c => Assert.Equal("Yes", c.TemporaryAccess));
        Assert.Equal("Territory 1203", companies[1].Territory);
        Assert.Single(d.Reports.ProjectsReport(admin, "open", null, maxCompanies: 1).Companies);
        // Both downloads only read.
        Assert.Equal(written, world.Store.Count<MyCrmChange>());
        Assert.Equal(200, new FilesApi(d).Download(FilesApi.ProjectsReport, new Dictionary<string, string> { ["show"] = "all" }, TestWorld.Admin).Status);
        Assert.Equal(200, new FilesApi(d).Download(FilesApi.CompanySummary, new Dictionary<string, string>(), TestWorld.Admin).Status);
    }

    [Fact]
    public void Projects_and_activities_are_written_in_MyCRMs_tables_with_their_temporary_territories_never_deleting()
    {
        if (string.IsNullOrEmpty(TestDatabase.Server)) return;
        using var outSystems = new TestDatabase();
        FillMyCRM(outSystems);
        var old = FakeOldTables.MyCRM();
        using var world = new TestWorld(crmUsers: false);
        var d = Connected(world, outSystems, null, writes: true);
        int Count(string table, string where = "1 = 1") => Convert.ToInt32(outSystems.Scalar($"SELECT COUNT(*) FROM dbo.[OSUSR_ab1_{table}] WHERE {where}"));
        var tables = new[] { "TERRITORY", "COMPANYTERRITORY", "PROJECT", "PROJECTCOMPANY", "PROJECTMEMBERS", "PROJECTLINK", "COMPANYACTIVITY", "ACTIVITYTYPELINK",
            "ACTIVITYPARTICIPANTS", "COMPANYACTIVITYCONTACTLINK", "PROSPECT" };
        var rows = tables.ToDictionary(t => t, t => Count(t));
        var admin = world.Actor(TestWorld.Admin);
        using var author = MyCrmStore.As(admin.ChangedBy);
        Assert.All(d.Databases.Test(ExternalNames.OutSystems), l => Assert.True(l.Ok, l.Line));

        // A company nobody owns, and a project with it and company 7 (Sam's): Ken, a member, gets both through one temporary territory.
        var tan = d.Companies.Save(admin, 0, new Companies.CompanyInput { Name = "Tan & Sons", Brn = "200012345K" }).Id!.Value;
        Assert.True(d.Users.Save(admin, @"RSP\ken", new CrmUsers.ProfileInput { Role = CrmRoles.Sales, Teams = [1001] }).Ok);
        var made = d.Projects.Create(admin, new Projects.ProjectInput { Name = "Cover", EndDate = world.Now.Date.AddDays(30).ToString("yyyy-MM-dd"), Companies = [tan, 7] });
        Assert.True(made.Ok, made.Message);
        var project = made.Id!.Value;
        Assert.Equal(1, Count("PROJECT", $"ID = {project} AND PROJECTNAME = N'Cover' AND OWNERID = 1 AND ISACTIVE = 1 AND ISCLOSED = 0 AND PROJECTSTATUSID = {old.StaticId("ProjectStatus", "Conception And Initiation")}"));
        Assert.Equal(2, Count("PROJECTCOMPANY", $"PROJECTID = {project} AND ISACTIVE = 1"));
        // The Projects list counts a project's companies and names the first ones in one grouped query in MyCRM.
        var listed = d.Projects.List(admin, "open", null, 0).Rows.Single(r => r.Project.Id == project);
        Assert.Equal(2, listed.CompanyCount);
        Assert.Equal(new[] { "Tan & Sons" }, listed.Companies.Take(1));
        Assert.Equal(1, d.Projects.List(admin, "open", "tan & s", 0).Total);
        Assert.Equal(1, Count("PROJECTMEMBERS", $"PROJECTID = {project} AND USERID = 1 AND PROJECTROLEID = {old.StaticId("ProjectRoles", "Project Manager")} AND TERRITORYID IS NULL"));
        var added = d.Projects.AddMember(admin, project, new Projects.MemberInput { Login = @"RSP\ken", Role = ProjectRoles.SalespersonCaretaker });
        Assert.True(added.Ok, added.Message);
        var territory = Convert.ToInt64(outSystems.Scalar($"SELECT TERRITORYID FROM dbo.[OSUSR_ab1_PROJECTMEMBERS] WHERE ID = {added.Id}"));
        Assert.Equal(1, Count("TERRITORY", $"ID = {territory} AND ISTEMPORARY = 1 AND ISACTIVE = 1 AND ISOWNER = 0 AND OWNERID = 107 AND TERRITORY = 107{tan}"));
        Assert.Equal(2, Count("COMPANYTERRITORY", $"TERRITORYID = {territory} AND ISACTIVE = 1 AND CREATEDBY = 1"));
        Assert.True(d.Access.CanOpen(new ApiUser(@"RSP\ken", "Ken Kwan").ToActor(d.People), d.Companies.Get(tan)!));
        // The temporary rows are not MyCRMRecords' territories: the territory pages read only the permanent ones.
        Assert.Equal(new[] { 1001L }, d.Store.Find<Territory>().Select(t => t.Id));
        Assert.Equal(new[] { 7L }, d.Store.Find<CompanyTerritory>().Select(l => l.CompanyId));
        Assert.DoesNotContain(d.Territories.All(includeInactive: true), t => t.Id == territory);
        Assert.Equal(new[] { 1002L, territory }, d.Store.Find<TemporaryTerritory>(Query.Where("Owner", @"RSP\ken").And("IsActive", true)).Select(t => t.Id).OrderBy(i => i));

        // An activity with company 7: MyCRM's static records, its time as 1900-01-01 with the time, its type and participant.
        var saved = d.Activities.Save(admin, 0, new Activities.ActivityInput
        {
            CompanyId = 7, Action = ActivityActions.FaceToFace, Remarks = "Review", StartDate = "2026-10-20", StartTime = "09:15", Types = ["FollowUp", "Negotiation"],
            Projects = [project],
        });
        Assert.True(saved.Ok, saved.Message);
        var activity = saved.Id!.Value;
        Assert.Equal(1, Count("COMPANYACTIVITY", $"ID = {activity} AND COMPANYID = 7 AND OWNERID = 1 AND ISACTIVE = 1 AND STARTTIME = '1900-01-01 09:15' AND ENDTIME = '1900-01-01 10:15' " +
            $"AND VISITDATE = '2026-10-20' AND COMPANYACTIVITYACTIONID = {old.StaticId("CompanyActivityAction", "Face to Face")}"));
        Assert.Equal(2, Count("ACTIVITYTYPELINK", $"COMPANYACTIVITYID = {activity}"));
        Assert.Equal(1, Count("ACTIVITYPARTICIPANTS", $"COMPANYEVENTID = {activity} AND USERID = 1 AND ISACTIVE = 1"));
        Assert.Equal(1, Count("PROJECTLINK", $"COMPANYACTIVITYID = {activity} AND PROJECTID = {project} AND ISACTIVE = 1"));
        Assert.Equal("09:15", d.Activities.Read(admin, activity)!.Activity.StartTime);
        // The Activities page finds it in MyCRM's tables (owner or participant, by date), counts and pages in SQL.
        var mineListed = d.Activities.List(admin, new Activities.Filter("all", Owner: "me"));
        Assert.Contains(mineListed.Rows, r => r.Activity.Id == activity);
        Assert.Equal(mineListed.Total, mineListed.Rows.Count);
        Assert.Empty(d.Activities.List(admin, new Activities.Filter("all", Owner: "me", Skip: 50)).Rows);
        Assert.Contains(d.Activities.List(admin, new Activities.Filter("upcoming", Owner: "me")).Rows, r => r.Activity.Id == activity);
        Assert.DoesNotContain(d.Activities.List(admin, new Activities.Filter("past", Owner: "me")).Rows, r => r.Activity.Id == activity);
        // A type taken off keeps its row, without its activity; the activity deleted is inactive.
        Assert.True(d.Activities.Save(admin, activity, new Activities.ActivityInput
            { CompanyId = 7, Action = ActivityActions.FaceToFace, Remarks = "Review", StartDate = "2026-10-20", StartTime = "09:15", Types = ["FollowUp"] }).Ok);
        Assert.Equal(1, Count("ACTIVITYTYPELINK", $"COMPANYACTIVITYID = {activity}"));
        Assert.Equal(rows["ACTIVITYTYPELINK"] + 2, Count("ACTIVITYTYPELINK"));
        Assert.Equal(1, Count("PROJECTLINK", $"COMPANYACTIVITYID = {activity} AND ISACTIVE = 0"));
        Assert.True(d.Activities.Delete(admin, activity).Ok);
        Assert.Equal(1, Count("COMPANYACTIVITY", $"ID = {activity} AND ISACTIVE = 0"));

        // Closed: the temporary territory and its links are inactive.
        Assert.True(d.Projects.Close(admin, project).Ok);
        Assert.Equal(1, Count("PROJECT", $"ID = {project} AND ISCLOSED = 1"));
        Assert.Equal(1, Count("TERRITORY", $"ID = {territory} AND ISACTIVE = 0"));
        Assert.Equal(0, Count("COMPANYTERRITORY", $"TERRITORYID = {territory} AND ISACTIVE = 1"));
        Assert.False(d.Access.CanOpen(new ApiUser(@"RSP\ken", "Ken Kwan").ToActor(d.People), d.Companies.Get(tan)!));

        // A prospect is only read.
        outSystems.Execute($"INSERT INTO dbo.[OSUSR_ab1_PROSPECT] (REMARKS, ISACTIVE, COMPANYID, STAGEID, PROSPECTTYPE, OWNERID, DATECREATED, ESTIMATEDORDERDATE, ISCLOSED, ISCANCELLED) " +
            $"VALUES (N'6 MFPs', 1, 7, {old.StaticId("Stages", "Negotiation")}, {old.StaticId("ProspectType", "R2R")}, 1, '2026-09-01', '2026-11-15', 0, 0)");
        var prospect = d.Activities.Prospects(7).Single();
        // The Prospects page finds, filters, counts and pages them in SQL (owner; remarks or company name by UNION).
        string[] Listed(Activities.ProspectFilter f) => d.Activities.ProspectList(admin, f).Rows.Select(r => r.Prospect.Remarks).ToArray();
        Assert.Equal(new[] { "6 MFPs" }, Listed(new Activities.ProspectFilter(Owner: "me")));
        Assert.Equal(new[] { "6 MFPs" }, Listed(new Activities.ProspectFilter(Q: "lim pte", Owner: "me", Status: "all")));
        Assert.Equal(new[] { "6 MFPs" }, Listed(new Activities.ProspectFilter(Q: "mfps", Owner: "me", Stage: "Negotiation", Type: "R2R")));
        Assert.Equal(new[] { "6 MFPs" }, Listed(new Activities.ProspectFilter(Owner: "me", From: new DateTime(2026, 11, 15), To: new DateTime(2026, 11, 15))));
        Assert.Empty(Listed(new Activities.ProspectFilter(Owner: "me", Status: "closed")));
        Assert.Empty(Listed(new Activities.ProspectFilter(Owner: "me", Stage: "Prove")));
        Assert.Empty(Listed(new Activities.ProspectFilter(Q: "nothing like it", Owner: "me")));
        var listedProspects = d.Activities.ProspectList(admin, new Activities.ProspectFilter(Owner: "me", Skip: 50));
        Assert.Empty(listedProspects.Rows);
        Assert.Equal(1, listedProspects.Total);
        Assert.Equal("Lim Pte Ltd", d.Activities.ProspectList(admin, new Activities.ProspectFilter(Owner: "me")).Rows.Single().Company);
        Assert.Equal("Negotiation", prospect.Stage);
        Assert.Throws<MyCrmRefusedException>(() => d.Store.Update(prospect));
        Assert.Throws<MyCrmRefusedException>(() => d.Store.Insert(new Prospect { CompanyId = 7 }));

        // Nothing was deleted in MyCRM; every value written is recorded with the one it replaced.
        foreach (var t in tables) Assert.True(Count(t) >= rows[t], t + " lost rows");
        var changes = world.Store.Find<MyCrmChange>();
        Assert.Contains(changes, c => c.TableName == "Territory" && c.RowId == territory && c.Column == "ISACTIVE" && c.Before == "1" && c.After == "0");
        // The territory's company links, taken away in one statement, are each recorded too.
        Assert.Equal(2, changes.Count(c => c.TableName == "CompanyTerritory" && c.Column == "ISACTIVE" && c.Before == "1" && c.After == "0"));
        Assert.Contains(changes, c => c.TableName == "ActivityTypeLink" && c.Column == "COMPANYACTIVITYID" && c.Before == activity.ToString() && c.After == "");
        Assert.Contains(changes, c => c.TableName == "Project" && c.RowId == project && c.Column == "ISCLOSED" && c.After == "1");
        Assert.Contains(d.Projects.History(project), h => h.What == "Closed the project");
    }

    [Fact]
    public void Customer_numbers_are_read_from_AdvanceConnector_by_their_real_column_names()
    {
        if (string.IsNullOrEmpty(TestDatabase.Server)) return;
        using var outSystems = new TestDatabase();
        FillMyCRM(outSystems);
        using var advance = new TestDatabase();
        advance.Execute(FakeOldTables.AdvanceSql());
        using var world = new TestWorld(crmUsers: false);
        var d = Connected(world, outSystems, advance, writes: true);
        var tan = d.Store.Insert(new Company { Name = "Tan & Sons", Brn = "200012345K", IsActive = true });
        var lim = d.Store.Get<Company>(7)!;
        lim.CustomerNo = "OLD";
        d.Store.Update(lim);
        var shortBrn = d.Store.Insert(new Company { Name = "Short", Brn = "ABC", IsActive = true, CustomerNo = "KEPT" });
        var run = d.Scheduler.Run(Jobs.CustomerNumbers, "Test");
        Assert.True(run.Outcome == "Done", run.Summary);
        Assert.Equal(2, run.Processed);
        Assert.Equal("C0002", d.Store.Get<Company>(tan.Id)!.CustomerNo);
        Assert.Equal("C0001 #Multiple", outSystems.Scalar("SELECT CUSTOMERNO FROM dbo.[OSUSR_ab1_COMPANY] WHERE ID = 7"));
        Assert.Equal("KEPT", d.Store.Get<Company>(shortBrn.Id)!.CustomerNo);
        Assert.Contains(world.Store.Find<MyCrmChange>(), x => x.RowId == 7 && x.Column == "CUSTOMERNO" && x.Before == "OLD" && x.ChangedBy == "Test");
        Assert.Equal(0, d.Scheduler.Run(Jobs.CustomerNumbers, "Test").Processed);
        Assert.All(d.Databases.Test(ExternalNames.AdvanceConnector), l => Assert.True(l.Ok, l.Line));
        Assert.All(d.Databases.Test(ExternalNames.OutSystems), l => Assert.True(l.Ok, l.Line));
        world.SettingsFile.Change(s => s.Connections.AdvanceTables.CustomerSupp = "dbo.NoSuchTable");
        Assert.Contains(d.Databases.Test(ExternalNames.AdvanceConnector), l => !l.Ok && l.Line.Contains("NoSuchTable"));
    }

    [Fact]
    public void The_cleanup_migration_keeps_what_MyCRM_has_no_column_for_and_drops_the_copies_only_when_nothing_is_lost()
    {
        if (string.IsNullOrEmpty(TestDatabase.Server)) return;
        using var database = new TestDatabase();
        using var world = new TestWorld(crmUsers: false);
        var storage = Storage(world.Folder);
        foreach (var m in DataMigrations.All.TakeWhile(m => m.Id != DataMigrations.MyCrmTablesDirectly))
            storage.RunMigration(database.Settings, m, "Test");
        // As the import and the sync left them: MyCRM's Ids kept on each record.
        database.Execute("""
            INSERT INTO dbo.CrUser ([Login], [DisplayName], [Email], [LastSeenAt], [IsActive], [RemovedOn], [RemovedBy]) VALUES (N'RSP\sam', N'Sam Sales', N'sam@example.com', NULL, 1, NULL, N'');
            INSERT INTO dbo.CrTeam ([Name], [ShortName], [IsActive], [OutSystemsId]) VALUES (N'Mia Manager', N'', 1, 1001);
            INSERT INTO dbo.CrCrmUser ([Login], [Role], [TeamId], [IsActive], [CreatedBy], [CreatedAt], [UpdatedBy], [UpdatedAt], [Designation], [PhoneNumber], [SalesmanCode], [CId],
              [ProfileId], [TerritoryCode], [FormerTeamId], [RemovedOn], [RemovedBy], [OutSystemsId])
              VALUES (N'RSP\ken', N'', NULL, 0, N'', NULL, N'', NULL, N'', N'', N'', 0, NULL, N'', 1, '2026-10-18 10:00', N'Sara Admin', 2001);
            INSERT INTO dbo.CrTerritory ([Name], [Number], [Owner], [IsActive], [IsOwner], [CreatedOn], [CreatedBy], [RemovedOn], [RemovedBy], [OutSystemsId])
              VALUES (N'North', 2001, N'RSP\sam', 1, 1, '2026-10-18 09:00', N'RSP\sara', NULL, N'', 3001), (N'', 1203, N'RSP\sam', 1, 1, NULL, N'', NULL, N'', 3002);
            INSERT INTO dbo.CrSyncLink ([Entity], [LocalId], [OldKey], [LocalHash], [OldHash], [RemovedHere], [RemovedThere], [Pending], [SyncedOn], [Unpaired])
              VALUES (N'Territory', 1, N'3001', N'', N'', 0, 0, 0, '2026-10-18 09:05', 0);
            """);
        var migration = DataMigrations.Find(DataMigrations.MyCrmTablesDirectly)!;
        void Refused()
        {
            Assert.ThrowsAny<Exception>(() => storage.RunMigration(database.Settings, migration, "Test"));
            Assert.Equal(2, Convert.ToInt32(database.Scalar("SELECT COUNT(*) FROM dbo.CrTerritory")));
        }

        // An import still recorded as running: nothing dropped.
        database.Execute("INSERT INTO dbo.CrJobRun ([Job], [StartedAt], [FinishedAt], [Outcome], [By], [Processed], [Summary], [Watermark]) VALUES (N'outsystems-import', '2026-10-18 08:00', NULL, N'Running', N'', 0, N'', NULL);");
        Refused();
        database.Execute("UPDATE dbo.CrJobRun SET [Outcome] = N'Stopped';");
        // A record that is only here (never synced): nothing dropped.
        database.Execute("INSERT INTO dbo.CrCompanyContactLink ([CompanyId], [ContactId], [CId], [IsDefault], [OutSystemsId]) VALUES (1, 1, 0, 1, NULL);");
        Refused();
        database.Execute("UPDATE dbo.CrCompanyContactLink SET [OutSystemsId] = 4001;");
        // A write to MyCRM the sync never confirmed: nothing dropped.
        database.Execute("UPDATE dbo.CrSyncLink SET [Pending] = 1;");
        Refused();
        database.Execute("UPDATE dbo.CrSyncLink SET [Pending] = 0;");

        storage.RunMigration(database.Settings, migration, "Test");
        foreach (var table in new[] { "CrTeam", "CrCrmUser", "CrCompany", "CrTerritory", "CrCompanyTerritory", "CrSyncLink", "CrSyncRun", "CrSyncChange" })
            Assert.Equal(0, Convert.ToInt32(database.Scalar($"SELECT CASE WHEN OBJECT_ID(N'dbo.{table}') IS NULL THEN 0 ELSE 1 END")));
        var store = storage.SqlServer(database.Settings);
        // The territory's name and who made it, and Ken's former team (MyCRM's Id of it), kept by MyCRM's Ids.
        var notes = store.Find<RecordNote>();
        Assert.Equal(2, notes.Count);
        var north = notes.Single(n => n.Entity == "Territory");
        Assert.Equal((3001L, "North", @"RSP\sara"), (north.RecordId, north.Name, north.CreatedBy));
        var ken = notes.Single(n => n.Entity == "CUser");
        Assert.Equal((2001L, 1001L, "Sara Admin"), (ken.RecordId, ken.FormerTeamId!.Value, ken.RemovedBy));
        Assert.Equal(1, store.Count<AppUser>());
        store.Insert(new MyCrmChange { ChangedOn = world.Now, TableName = "Company", RowId = 7, Column = "COMPANY", Before = "A", After = "B" });
        Assert.Equal("B", store.Find<MyCrmChange>().Single().After);
        Assert.Empty(storage.RunPendingMigrations(database.Settings, "Test"));
    }
}
