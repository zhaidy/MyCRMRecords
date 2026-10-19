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

    [Fact]
    public void The_sql_store_answers_queries_as_the_json_store_does()
    {
        if (string.IsNullOrEmpty(TestDatabase.Server)) return;
        using var database = new TestDatabase();
        using var world = new TestWorld();
        var storage = Storage(world.Folder);
        storage.RunPendingMigrations(database.Settings, "Test");
        var sql = storage.SqlServer(database.Settings);
        foreach (var store in new IRecordStore[] { world.Store, sql })
        {
            store.Delete<Company>(Query.All());
            var a = store.Insert(new Company { UpdatedDate = world.Now, Website = "Logistics", Brn = "a_b", SalesRemarks = "100% done" });
            var b = store.Insert(new Company { UpdatedDate = world.Now.AddMinutes(1), Website = "IT", Brn = "axb", SalesRemarks = "Could not be reached" });
            var c = store.Insert(new Company { UpdatedDate = world.Now.AddMinutes(2), Website = "Logistics", Brn = "c" });
            Assert.Equal(new[] { c.Id, a.Id }, store.Find<Company>(Query.Where("Website", "logistics").Newest()).Select(m => m.Id));
            Assert.Equal(new[] { a.Id }, store.Find<Company>(Query.All().Search("a_b", "Brn")).Select(m => m.Id));
            Assert.Equal(new[] { a.Id }, store.Find<Company>(Query.All().Search("100%", "SalesRemarks")).Select(m => m.Id));
            Assert.Equal(2, store.Count<Company>(Query.All().Not("Website", "IT")));
            Assert.Equal(new[] { b.Id, c.Id }, store.Find<Company>(Query.All().AtLeast("UpdatedDate", world.Now.AddMinutes(1)).Sort("UpdatedDate")).Select(m => m.Id));
            Assert.Equal(new[] { a.Id, c.Id }, store.Find<Company>(Query.All().In("Id", new object[] { a.Id, c.Id })).Select(m => m.Id));
            // More values than SQL Server takes as parameters (2100), and text with quotes.
            var ids = Enumerable.Range(1, 3000).Select(i => (object)(i * 1000000L)).Append(a.Id).Append(c.Id).ToList();
            Assert.Equal(new[] { a.Id, c.Id }, store.Find<Company>(Query.All().In("Id", ids).Sort("Id")).Select(m => m.Id));
            var codes = Enumerable.Range(1, 2500).Select(i => (object)("x'" + i.ToString(System.Globalization.CultureInfo.InvariantCulture))).Append("a_b").Append("c").ToList();
            Assert.Equal(new[] { a.Id, c.Id }, store.Find<Company>(Query.All().In("Brn", codes).Sort("Id")).Select(m => m.Id));
            Assert.Equal(2, store.Count<Company>(Query.All().In("Id", ids)));
            Assert.Equal(new[] { b.Id }, store.Find<Company>(Query.All().Sort("Id").Page(1, 1)).Select(m => m.Id));
            // A nullable reference (long?) found in a list, and columns named like SQL words (Number, Owner).
            store.Insert(new Phone { ContactId = 42, Number = "1" });
            Assert.Single(store.Find<Phone>(Query.All().In("ContactId", new object[] { 42L, 43L })));
            store.Insert(new Territory { Number = 3, Owner = "o" });
            Assert.Equal(3, store.Find<Territory>(Query.All().Sort("Number", descending: true)).First().Number);
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
        SeedData.Add(world.Store, world.Now, world.Actor(TestWorld.Admin));
        var companies = world.Store.Count<Company>();
        Assert.Equal(2, companies);
        var storage = new DatabaseStorage(new DatabaseSettingsFile(Path.Combine(world.Folder, "database-settings.json"), new FakeProtector()), world.Store, TestDatabase.Connect);
        var d = CrmFactory.Create(storage, world.SettingsFile, TestDatabase.Connect, Path.Combine(world.Folder, "tasks.json"), () => world.Now);
        var admin = new AdminApi(d, storage, null!, _ => { }, demoTools: false);
        var answer = admin.Handle("POST", "admin/database", MyCRMRecords.Json.Serialize(new
        {
            provider = StorageProviders.SqlServer,
            sqlServer = new { server = database.Settings.Server, database = database.Settings.Database, authentication = "Windows", trustServerCertificate = true },
        }), new Dictionary<string, string>(), TestWorld.Admin);
        Assert.True(answer.Status == 200, answer.Json);
        Assert.NotNull(storage.CurrentSqlServer());
        Assert.Equal(companies, storage.Count<Company>());
        Assert.Equal((long)companies, Convert.ToInt64(database.Scalar("SELECT COUNT(*) FROM dbo.CrCompany")));
        var list = new CrmApi(d).Handle("GET", "companies", "", new Dictionary<string, string>(), TestWorld.Admin);
        Assert.True(list.Status == 200, list.Json);
        Assert.Contains(SeedData.SampleCompany, list.Json);
    }

    private static CrmApp Connected(TestWorld world, TestDatabase outSystems, TestDatabase? advance, bool sync)
    {
        world.SettingsFile.Change(s =>
        {
            s.Connections.OutSystems.Configured = true;
            s.Connections.OutSystems.SqlServer = outSystems.Settings;
            if (advance is not null) s.Connections.AdvanceConnector = new ExternalDatabase { Configured = true, SqlServer = advance.Settings };
            s.Sync.Enabled = sync;
        });
        return CrmFactory.Create(world.Store, world.SettingsFile, TestDatabase.Connect, Path.Combine(world.Folder, "tasks.json"), () => world.Now, world.Email);
    }

    /// <summary>The rows MyCRMRecords copies from <see cref="FillMyCRM"/>.</summary>
    private const int Copied = 13;

    /// <summary>
    /// MyCRM in OutSystems: Mia's team with Sam's CUser (Sales), the group Lim Group, company 7 (created by Sara, 102) with its
    /// address, primary contact, phone and email, Sam's permanent territory 1203 with company 7 and a recent movement; and
    /// rows that are not MyCRMRecords': Ken's temporary territory of a project with company 7, an inactive territory and an
    /// inactive link.
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
    public void MyCRM_data_is_imported_with_its_people_and_only_the_permanent_territories()
    {
        if (string.IsNullOrEmpty(TestDatabase.Server)) return;
        using var outSystems = new TestDatabase();
        FillMyCRM(outSystems);
        using var world = new TestWorld(crmUsers: false);
        world.Store.Delete<UserRole>(Query.All());
        world.Store.Delete<AppUser>(Query.All());
        var d = Connected(world, outSystems, null, sync: false);
        var preview = d.Import.Preview();
        Assert.Equal(1, preview.Found["Companies"]);
        // Read through CrmTables: of the three territories and three links, the active permanent one and its active link.
        Assert.Equal(1, preview.Found["Territories (active, permanent)"]);
        Assert.Equal(1, preview.Found["Companies of territories"]);
        var counts = d.Import.Import("Test", _ => { });
        Assert.Equal(1, counts.Copied["Companies"]);
        Assert.Equal(1, counts.Copied["CRM user records"]);
        Assert.Equal(1, counts.Copied["Territories (active, permanent)"]);
        var c = d.Store.Get<Company>(7)!;
        Assert.Equal("Lim Pte Ltd", c.Name);
        Assert.Equal("SME", c.CompanyGroup);
        Assert.Equal("NonUser", c.CompAccountType);
        // MyCRM's users are RSP\name@ricoh.sg: the logins are RSP\name.
        Assert.Equal(@"RSP\sara", c.CreatedBy);
        Assert.Equal(1000.25m, c.Revenue);
        Assert.Equal("Lim Group", d.Store.Get<CompanyGroupName>(c.CompanyGroupNameId!.Value)!.GroupName);
        var sam = d.Store.Find<CrmUser>().Single();
        Assert.Equal(@"RSP\sam", sam.Login);
        Assert.Equal(CrmRoles.Sales, sam.Role);
        Assert.Equal(@"RSP\sam", d.Store.Find<Territory>().Single().Owner);
        Assert.Equal(1203, d.Store.Find<Territory>().Single().Number);
        Assert.Equal(AddressTypes.Billing, d.Store.Find<Address>().Single().AddressType);
        Assert.Equal("Mobile", d.Store.Find<Phone>().Single().PhoneType);
        Assert.Contains(d.People.Everyone(), u => u.Login == @"RSP\olga" && u.DisplayName == "Olga Other");

        // Again: nothing twice.
        var again = d.Import.Import("Test", _ => { });
        Assert.Equal(0, again.Copied["Companies"]);
        Assert.Equal(1, d.Store.Count<Company>());

        // Then the sync pairs everything without changing anything, either side.
        world.SettingsFile.Change(s => s.Sync.Enabled = true);
        var outcome = d.Sync.Run("test");
        Assert.True(outcome.Ok, outcome.Message + "\n" + string.Join("\n", outcome.Details));
        Assert.True(outcome.ToOldApp + outcome.ToHere == 0, string.Join("\n", outcome.Details));
    }

    [Fact]
    public void The_sync_writes_MyCRM_tables_and_never_deletes_there()
    {
        if (string.IsNullOrEmpty(TestDatabase.Server)) return;
        using var outSystems = new TestDatabase();
        FillMyCRM(outSystems);
        using var world = new TestWorld(crmUsers: false);
        var d = Connected(world, outSystems, null, sync: true);
        SyncOutcome Sync()
        {
            var outcome = d.Sync.Run("test", allowLarge: true);
            Assert.True(outcome.Ok, outcome.Message + "\n" + string.Join("\n", outcome.Details));
            return outcome;
        }
        Assert.Equal(Copied, Sync().ToHere);
        var settled = Sync();
        Assert.True(settled.ToHere + settled.ToOldApp == 0, string.Join("\n", settled.Details));
        // Ken reaches company 7 through MyCRM's temporary territory of his project (read from MyCRM, never copied).
        var lim = d.Store.Get<Company>(7)!;
        Assert.True(d.Access.CanOpen(new ApiUser(@"RSP\ken", "Ken Kwan").ToActor(d.People), lim));
        Assert.False(d.Access.CanOpen(new ApiUser(@"RSP\olga", "Olga Other").ToActor(d.People), lim));

        // A new company takes MyCRM's number; a new territory of Sam's takes it and company 7 (out of territory 1203); an
        // address of company 7 is deleted.
        var admin = world.Actor(TestWorld.Admin);
        var made = d.Companies.Save(admin, 0, new Companies.CompanyInput { Name = "Tan & Sons", Brn = "200012345K", CompanyGroup = "GMA" });
        Assert.True(made.Ok, made.Message);
        var id = made.Id!.Value;
        Assert.NotEqual(7, id);
        Assert.Equal(1, Convert.ToInt32(outSystems.Scalar($"SELECT COUNT(*) FROM dbo.[OSUSR_ab1_COMPANY] WHERE ID = {id} AND COMPANY = N'Tan & Sons'")));
        var territory = d.Territories.Save(admin, 0, new Territories.TerritoryInput { Name = "North", Number = 2001, Owner = @"RSP\sam" });
        Assert.True(territory.Ok, territory.Message);
        Assert.True(d.Territories.AddCompanies(admin, territory.Id!.Value, [id, 7]).Ok);
        Assert.True(d.Companies.RemoveAddress(admin, 7, d.Store.Find<Address>().Single().Id).Ok);
        Sync();
        Assert.Equal(1, Convert.ToInt32(outSystems.Scalar("SELECT COUNT(*) FROM dbo.[OSUSR_ab1_TERRITORY] WHERE TERRITORY = 2001 AND OWNERID = 101 AND ISACTIVE = 1 AND ISTEMPORARY = 0")));
        Assert.Equal(5, Convert.ToInt32(outSystems.Scalar("SELECT COUNT(*) FROM dbo.[OSUSR_ab1_COMPANYTERRITORY]")));
        Assert.Equal(0, Convert.ToInt32(outSystems.Scalar("SELECT COUNT(*) FROM dbo.[OSUSR_ab1_COMPANYTERRITORY] WHERE TERRITORYID = 1001 AND ISACTIVE = 1")));
        // MyCRM's project territory is left as it is.
        Assert.Equal(1, Convert.ToInt32(outSystems.Scalar("SELECT COUNT(*) FROM dbo.[OSUSR_ab1_COMPANYTERRITORY] WHERE TERRITORYID = 1002 AND ISACTIVE = 1")));
        Assert.Equal(1, Convert.ToInt32(outSystems.Scalar("SELECT COUNT(*) FROM dbo.[OSUSR_ab1_ADDRESS] WHERE ISACTIVE = 0 AND PREISACTIVE = 1")));
        // Tan & Sons moved from nobody to Sam; company 7 stayed Sam's (no movement).
        Assert.Equal(2, Convert.ToInt32(outSystems.Scalar("SELECT COUNT(*) FROM dbo.[OSUSR_ab1_TERRITORYMOVEMENT]")));
        Assert.NotEmpty(d.Store.Find<SyncChange>());

        var again = Sync();
        Assert.True(again.ToOldApp + again.ToHere == 0, string.Join("\n", again.Details));
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
        var d = Connected(world, outSystems, advance, sync: false);
        var tan = world.Store.Insert(new Company { Name = "Tan & Sons", Brn = "200012345K", IsActive = true });
        var lim = world.Store.Insert(new Company { Name = "Lim", Brn = "199912345K", IsActive = true, CustomerNo = "OLD" });
        var shortBrn = world.Store.Insert(new Company { Name = "Short", Brn = "ABC", IsActive = true, CustomerNo = "KEPT" });
        var run = d.Scheduler.Run(Jobs.CustomerNumbers, "Test");
        Assert.True(run.Outcome == "Done", run.Summary);
        Assert.Equal(2, run.Processed);
        Assert.Equal("C0002", world.Store.Get<Company>(tan.Id)!.CustomerNo);
        Assert.Equal("C0001 #Multiple", world.Store.Get<Company>(lim.Id)!.CustomerNo);
        Assert.Equal("KEPT", world.Store.Get<Company>(shortBrn.Id)!.CustomerNo);
        Assert.Equal(0, d.Scheduler.Run(Jobs.CustomerNumbers, "Test").Processed);
        Assert.All(d.Databases.Test(ExternalNames.AdvanceConnector), l => Assert.True(l.Ok, l.Line));
        Assert.All(d.Databases.Test(ExternalNames.OutSystems), l => Assert.True(l.Ok, l.Line));
        world.SettingsFile.Change(s => s.Connections.AdvanceTables.CustomerSupp = "dbo.NoSuchTable");
        Assert.Contains(d.Databases.Test(ExternalNames.AdvanceConnector), l => !l.Ok && l.Line.Contains("NoSuchTable"));
    }
}
