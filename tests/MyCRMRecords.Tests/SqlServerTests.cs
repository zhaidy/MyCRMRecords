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
            store.Delete<Feedback>(Query.All());
            var a = store.Insert(new Feedback { UpdatedOn = world.Now, ContactName = "Logistics", PostalCode = "a_b", Description = "100% done" });
            var b = store.Insert(new Feedback { UpdatedOn = world.Now.AddMinutes(1), ContactName = "IT", PostalCode = "axb", Description = "Could not be reached" });
            var c = store.Insert(new Feedback { UpdatedOn = world.Now.AddMinutes(2), ContactName = "Logistics", PostalCode = "c" });
            Assert.Equal(new[] { c.Id, a.Id }, store.Find<Feedback>(Query.Where("ContactName", "logistics").Newest()).Select(m => m.Id));
            Assert.Equal(new[] { a.Id }, store.Find<Feedback>(Query.All().Search("a_b", "PostalCode")).Select(m => m.Id));
            Assert.Equal(new[] { a.Id }, store.Find<Feedback>(Query.All().Search("100%", "Description")).Select(m => m.Id));
            Assert.Equal(2, store.Count<Feedback>(Query.All().Not("ContactName", "IT")));
            Assert.Equal(new[] { b.Id, c.Id }, store.Find<Feedback>(Query.All().AtLeast("UpdatedOn", world.Now.AddMinutes(1)).Sort("UpdatedOn")).Select(m => m.Id));
            Assert.Equal(new[] { a.Id, c.Id }, store.Find<Feedback>(Query.All().In("Id", new object[] { a.Id, c.Id })).Select(m => m.Id));
            // More values than SQL Server takes as parameters (2100), and text with quotes.
            var ids = Enumerable.Range(1, 3000).Select(i => (object)(i * 1000000L)).Append(a.Id).Append(c.Id).ToList();
            Assert.Equal(new[] { a.Id, c.Id }, store.Find<Feedback>(Query.All().In("Id", ids).Sort("Id")).Select(m => m.Id));
            var codes = Enumerable.Range(1, 2500).Select(i => (object)("x'" + i.ToString(System.Globalization.CultureInfo.InvariantCulture))).Append("a_b").Append("c").ToList();
            Assert.Equal(new[] { a.Id, c.Id }, store.Find<Feedback>(Query.All().In("PostalCode", codes).Sort("Id")).Select(m => m.Id));
            Assert.Equal(2, store.Count<Feedback>(Query.All().In("Id", ids)));
            Assert.Equal(new[] { b.Id }, store.Find<Feedback>(Query.All().Sort("Id").Page(1, 1)).Select(m => m.Id));
            // Columns named like SQL words (SortOrder, Kind) and text without limit.
            store.Insert(new FeedbackCategory { Label = "Order", SortOrder = 3 });
            Assert.Equal(3, store.Find<FeedbackCategory>(Query.All().Sort("SortOrder", descending: true)).First().SortOrder);
            Assert.Throws<InvalidOperationException>(() => store.Batch(s =>
            {
                s.Insert(new Feedback { ContactName = "Lost" });
                throw new InvalidOperationException("stop");
            }));
            Assert.Equal(0, store.Count<Feedback>(Query.Where("ContactName", "Lost")));
            // A file of a few megabytes is kept as it is.
            var bytes = Enumerable.Range(0, 3_000_000).Select(i => (byte)(i % 251)).ToArray();
            var file = store.Insert(new FeedbackFile { FeedbackId = 1, FileName = "big.bin", Content = bytes });
            Assert.Equal(bytes, store.Get<FeedbackFile>(file.Id)!.Content);
        }
    }

    [Fact]
    public void Switching_to_sql_server_copies_the_data_in()
    {
        if (string.IsNullOrEmpty(TestDatabase.Server)) return;
        using var database = new TestDatabase();
        using var world = new TestWorld();
        SeedData.Add(world.Store, world.Now, world.Actor(TestWorld.FeedbackAdmin));
        var feedbacks = world.Store.Count<Feedback>();
        Assert.Equal(3, feedbacks);
        var storage = new DatabaseStorage(new DatabaseSettingsFile(Path.Combine(world.Folder, "database-settings.json"), new FakeProtector()), world.Store, TestDatabase.Connect);
        var d = CrmFactory.Create(storage, world.SettingsFile, TestDatabase.Connect, Path.Combine(world.Folder, "tasks.json"), () => world.Now, holidays: world.Holidays);
        var admin = new AdminApi(d, storage, null!, _ => { }, demoTools: false);
        var answer = admin.Handle("POST", "admin/database", MyCRMRecords.Json.Serialize(new
        {
            provider = StorageProviders.SqlServer,
            sqlServer = new { server = database.Settings.Server, database = database.Settings.Database, authentication = "Windows", trustServerCertificate = true },
        }), new Dictionary<string, string>(), TestWorld.Admin);
        Assert.True(answer.Status == 200, answer.Json);
        Assert.NotNull(storage.CurrentSqlServer());
        Assert.Equal(feedbacks, storage.Count<Feedback>());
        Assert.Equal((long)feedbacks, Convert.ToInt64(database.Scalar("SELECT COUNT(*) FROM dbo.CrFeedback")));
        var list = new CrmApi(d).Handle("GET", "feedbacks", "", new Dictionary<string, string>(), TestWorld.FeedbackAdmin);
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

    /// <summary>
    /// MyCRM in OutSystems: a division, category and medium; feedback 7 (created by Fay, user 102, in charge Sam, 101) with a
    /// task for Ben (104) and a file; a task of a feedback never saved; and the roles (Fay Admin, Ben BE, Hana HR).
    /// </summary>
    private static void FillMyCRM(TestDatabase outSystems)
    {
        outSystems.Execute(FakeOldTables.MyCRM().OutSystemsSql("MyCRM"));
        outSystems.Execute("""
            INSERT INTO dbo.[OSUSR_ab1_FEEDBACKCATEGORY] (LABEL, [ORDER], ISACTIVE) VALUES (N'Service', 1, 1);
            INSERT INTO dbo.[OSUSR_ab1_FEEDBACKMEDIUM] (LABEL, [ORDER], ISACTIVE) VALUES (N'Phone', 1, 1);
            INSERT INTO dbo.[OSUSR_ab1_FEEDBACKDIVISION] (DIVISION, PERSONINCHARGE, ISACTIVE) VALUES (N'Logistics', 101, 1);
            SET IDENTITY_INSERT dbo.[OSUSR_ab1_FEEDBACK] ON;
            INSERT INTO dbo.[OSUSR_ab1_FEEDBACK] (ID, FEEDBACKGROUPID, PERSONRESPONSIBLE, FEEDBACKSTATUSID, COMPANYNAME, CONTACTNAME, COMPANYEMAIL, FEEDBACKMEDIUMID,
              DATERECEIVED, TIMERECEIVED, FEEDBACKTYPEID, FEEDBACKCATEGORYID, FEEDBACKDESCRIPTION, ISACKNOWLEDGED, ACKNOWLEDGEDDATE, ACKNOWLEDGEDTIME, SIGNOFF,
              CREATEDON, CREATEDBY, CREATEDBYUSERNAME, ISACTIVE, SESSIONID, HRACCEPTED, FEEDBACKDIVISIONID, ACKNOWLEGEDHOURS)
              VALUES (7, 11, 101, 23, N'Tan & Sons', N'Mr Tan', 'tan@tan.example', 1001, '2026-09-01', '1900-01-01 09:15:00', 33, 1001, N'Late invoice', 1,
              '2026-09-01', '1900-01-01 11:00:00', 101, '2026-09-01 09:20', 102, N'Fay Admin', 1, '2026-09-01 09:19:00', '-', 1001, 1);
            SET IDENTITY_INSERT dbo.[OSUSR_ab1_FEEDBACK] OFF;
            INSERT INTO dbo.[OSUSR_ab1_TASK] (FEEDBACKID, ACTIONPLAN, PERSONINCHARGE, DUEDATE, CREATEDON, CREATEDBY, SESSIONID, TASKTYPEID)
              VALUES (7, N'Resend the invoice', 104, '2026-09-05', '2026-09-02 10:00', 101, '2026-09-01 09:19:00', 61),
                     (NULL, N'Never saved', 101, '2026-09-05', '2026-09-02 10:00', 101, '2026-08-01 08:00:00', 61);
            INSERT INTO dbo.[OSUSR_ab1_FEEDBACKATTACHMENT] (FEEDBACKID, FILENAME, FILEBINARY, TYPE, UPLOADEDBY, UPLOADEDON, SESSIONID)
              VALUES (7, N'letter.pdf', 0x01020304, 'CD', 102, '2026-09-01 09:30', '2026-09-01 09:19:00');
            CREATE TABLE dbo.OSSYS_ROLE ([ID] int NOT NULL, [NAME] nvarchar(100) NOT NULL, [ESPACE_ID] int NOT NULL);
            CREATE TABLE dbo.OSSYS_USER_ROLE ([USER_ID] int NOT NULL, [ROLE_ID] int NOT NULL);
            INSERT INTO dbo.OSSYS_ROLE VALUES (1, 'Admin', 1), (2, 'BE', 1), (3, 'HR', 1);
            INSERT INTO dbo.OSSYS_USER_ROLE VALUES (102, 1), (104, 2), (103, 3);
            """);
    }

    [Fact]
    public void MyCRM_data_is_imported_with_its_files_people_and_roles()
    {
        if (string.IsNullOrEmpty(TestDatabase.Server)) return;
        using var outSystems = new TestDatabase();
        FillMyCRM(outSystems);
        using var world = new TestWorld();
        world.Store.Delete<UserRole>(Query.All());
        world.Store.Delete<AppUser>(Query.All());
        var d = Connected(world, outSystems, null, sync: false);
        Assert.Equal(1, d.Import.Preview().Found["Feedbacks"]);
        // "Read MyCRM's data" counts the attachments without reading or hashing them.
        Assert.Equal(1, d.Import.Preview().Found["Attachments"]);
        Assert.Equal(0, ((CrmTables)d.Sync.Tables!).FilesHashed);
        var counts = d.Import.Import("Test", _ => { });
        Assert.Equal(1, counts.Copied["Feedbacks"]);
        Assert.Equal(1, counts.Copied["Tasks"]);
        Assert.Equal(1, counts.Copied["Attachments"]);
        Assert.Equal(1, counts.Copied["Divisions"]);
        var f = d.Store.Get<Feedback>(7)!;
        Assert.Equal(FeedbackGroups.CustomerComplaint, f.FeedbackGroup);
        Assert.Equal(FeedbackStatuses.Acknowledged, f.Status);
        Assert.Equal(FeedbackTypes.Normal, f.FeedbackType);
        // MyCRM's users are RSP\name@ricoh.sg: the logins are RSP\name.
        Assert.Equal(@"RSP\sam", f.PersonResponsible);
        Assert.Equal(@"RSP\fay", f.CreatedBy);
        Assert.Equal("09:15:00", f.TimeReceived);
        Assert.Equal("Logistics", Feedbacks.DivisionOf(f, d.Feedbacks.DivisionNames()));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, d.Store.Find<FeedbackFile>().Single().Content);
        Assert.Equal(@"RSP\ben", d.Store.Find<FeedbackTask>().Single().PersonInCharge);
        Assert.Equal(new[] { Roles.FeedbackAdmin }, d.People.RolesOf(@"RSP\fay"));
        Assert.Equal(new[] { Roles.Be }, d.People.RolesOf(@"RSP\ben"));
        Assert.Equal(new[] { Roles.Hr }, d.People.RolesOf(@"RSP\hana"));
        Assert.Contains(d.People.Everyone(), u => u.Login == @"RSP\olga" && u.DisplayName == "Olga Other");

        // Again: nothing twice.
        var again = d.Import.Import("Test", _ => { });
        Assert.Equal(0, again.Copied["Feedbacks"]);
        Assert.Equal(1, d.Store.Count<Feedback>());

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
        using var advance = new TestDatabase();
        advance.Execute(FakeOldTables.AdvanceSql());
        using var world = new TestWorld();
        var d = Connected(world, outSystems, advance, sync: true);
        SyncOutcome Sync()
        {
            var outcome = d.Sync.Run("test", allowLarge: true);
            Assert.True(outcome.Ok, outcome.Message + "\n" + string.Join("\n", outcome.Details));
            return outcome;
        }
        Assert.Equal(6, Sync().ToHere);
        // The file came with its bytes, read once: the sync's rows give its fingerprint.
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, d.Store.Find<FeedbackFile>().Single().Content);
        // A file kept here is not hashed again by SQL Server: the rows give its length, and the one kept here is taken.
        var myCms = (CrmTables)d.Sync.Tables!;
        var hashed = myCms.FilesHashed;
        var settled = Sync();
        Assert.True(settled.ToHere + settled.ToOldApp == 0, string.Join("\n", settled.Details));
        Assert.Equal(hashed, myCms.FilesHashed);

        // A new feedback takes MyCRM's number, its task and file go with it; a task and a file removed are kept there, marked.
        var olga = world.Actor(TestWorld.Olga);
        var input = new Feedbacks.FeedbackInput
        {
            FeedbackGroup = FeedbackGroups.CustomerComplaint, DivisionId = d.Store.Find<FeedbackDivision>().Single().Id, PersonResponsible = TestWorld.Sam.Login,
            CompanyName = "Lim", ContactName = "Ms Lim", CompanyEmail = "lim@lim.example", MediumId = d.Store.Find<FeedbackMedium>().Single().Id,
            DateReceived = new DateTime(2026, 10, 15), TimeReceived = "10:00", FeedbackType = FeedbackTypes.Normal, CategoryId = d.Store.Find<FeedbackCategory>().Single().Id,
            Description = "Noisy machine",
        };
        var made = d.Feedbacks.Save(olga, 0, input);
        Assert.True(made.Ok, made.Message);
        var id = made.Id!.Value;
        Assert.NotEqual(7, id);
        var sam = world.Actor(TestWorld.Sam);
        Assert.True(d.Feedbacks.RemoveTask(sam, d.Feedbacks.Tasks(7).Single().Id).Ok);
        Assert.True(d.Feedbacks.RemoveFile(world.Actor(TestWorld.FeedbackAdmin), d.Feedbacks.Files(7).Single().Id).Ok);
        Sync();
        Assert.Equal(1, Convert.ToInt32(outSystems.Scalar($"SELECT COUNT(*) FROM dbo.[OSUSR_ab1_FEEDBACK] WHERE ID = {id} AND COMPANYNAME = 'Lim' AND PERSONRESPONSIBLE = 101 AND FEEDBACKSTATUSID = 22")));
        Assert.Equal(2, Convert.ToInt32(outSystems.Scalar("SELECT COUNT(*) FROM dbo.[OSUSR_ab1_TASK]")));
        Assert.Equal(1, Convert.ToInt32(outSystems.Scalar("SELECT COUNT(*) FROM dbo.[OSUSR_ab1_TASK] WHERE FEEDBACKID IS NULL AND SESSIONID LIKE 'X:7:%'")));
        Assert.Equal("(removed) letter.pdf", outSystems.Scalar("SELECT FILENAME FROM dbo.[OSUSR_ab1_FEEDBACKATTACHMENT]"));
        Assert.NotEmpty(d.Store.Find<SyncChange>());

        var again = Sync();
        Assert.True(again.ToOldApp + again.ToHere == 0, string.Join("\n", again.Details));
    }

    [Fact]
    public void AdvanceConnector_holidays_are_read_by_their_real_column_names()
    {
        if (string.IsNullOrEmpty(TestDatabase.Server)) return;
        using var outSystems = new TestDatabase();
        FillMyCRM(outSystems);
        using var advance = new TestDatabase();
        advance.Execute(FakeOldTables.AdvanceSql());
        using var world = new TestWorld();
        var d = Connected(world, outSystems, advance, sync: false);
        Assert.True(d.Holidays.Available);
        Assert.Equal(new[] { new DateTime(2026, 10, 16), new DateTime(2026, 12, 25) }, d.Holidays.Dates().OrderBy(x => x));
        Assert.All(d.Databases.Test(ExternalNames.AdvanceConnector), l => Assert.True(l.Ok, l.Line));
        Assert.All(d.Databases.Test(ExternalNames.OutSystems), l => Assert.True(l.Ok, l.Line));
        world.SettingsFile.Change(s => s.Connections.AdvanceTables.PublicHolidays = "dbo.NoSuchTable");
        Assert.Contains(d.Databases.Test(ExternalNames.AdvanceConnector), l => !l.Ok && l.Line.Contains("NoSuchTable"));
    }
}
