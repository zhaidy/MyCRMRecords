using System.Reflection;
using System.Text.RegularExpressions;

namespace MyCRMRecords.Tests;

public sealed class RecordTests
{
    [Fact]
    public void The_json_store_adds_finds_changes_and_deletes_records()
    {
        using var world = new TestWorld();
        var store = world.Store;
        var a = store.Insert(new Company { Name = "Alpha Logistics", Brn = "V-SO-1", Website = "Logistics", UpdatedDate = world.Now });
        var b = store.Insert(new Company { Name = "Beta IT", Brn = "WO123-7", Website = "IT", SalesRemarks = "Could not be reached", UpdatedDate = world.Now.AddMinutes(1) });
        var c = store.Insert(new Company { Name = "Gamma Logistics", Brn = "V-SO-2", Website = "Logistics", UpdatedDate = world.Now.AddMinutes(2) });
        Assert.True(a.Id > 0 && b.Id == a.Id + 1 && c.Id == b.Id + 1);

        Assert.Equal("WO123-7", store.Get<Company>(b.Id)!.Brn);
        Assert.Equal(new[] { c.Id, a.Id }, store.Find<Company>(Query.Where("Website", "logistics").Newest()).Select(m => m.Id));
        Assert.Equal(new[] { b.Id }, store.Find<Company>(Query.All().Search("could NOT", "SalesRemarks", "Brn")).Select(m => m.Id));
        Assert.Equal(2, store.Count<Company>(Query.All().Not("Website", "IT")));
        Assert.Equal(new[] { b.Id, c.Id }, store.Find<Company>(Query.All().AtLeast("UpdatedDate", world.Now.AddMinutes(1)).Sort("UpdatedDate")).Select(m => m.Id));
        Assert.Equal(new[] { a.Id }, store.Find<Company>(Query.All().Before("UpdatedDate", world.Now.AddMinutes(1))).Select(m => m.Id));
        Assert.Equal(new[] { a.Id, c.Id }, store.Find<Company>(Query.All().In("Id", new object[] { a.Id, c.Id })).Select(m => m.Id));
        Assert.Equal(new[] { b.Id }, store.Find<Company>(Query.All().Sort("Id").Page(1, 1)).Select(m => m.Id));
        Assert.Equal(new[] { a.Id, c.Id }, store.Find<Company>(Query.All().Contains("Brn", "-so-")).Select(m => m.Id));

        b.Website = "Logistics";
        b.NoOfEmployees = 15;
        b.Revenue = 1234.5m;
        store.Update(b);
        Assert.Equal("Logistics", new JsonRecordStore(store.Folder).Get<Company>(b.Id)!.Website);
        Assert.Equal(15, new JsonRecordStore(store.Folder).Get<Company>(b.Id)!.NoOfEmployees);
        Assert.Equal(1234.5m, new JsonRecordStore(store.Folder).Get<Company>(b.Id)!.Revenue);
        Assert.Equal(new DateTime(2026, 10, 17, 9, 31, 0), new JsonRecordStore(store.Folder).Get<Company>(b.Id)!.UpdatedDate);

        Assert.Equal(3, store.Delete<Company>(Query.Where("Website", "Logistics")));
        Assert.Equal(0, store.Count<Company>());
        Assert.True(store.Insert(new Company()).Id > c.Id);
    }

    /// <summary>
    /// SQL Server names constraints across the whole database, which MyCRMRecords may share with the other apps
    /// (MyCEV's dbo.CvMigrations has PK_CvMigrations): each primary key is named after its own table.
    /// </summary>
    [Fact]
    public void Every_primary_key_is_named_after_its_own_table()
    {
        var sql = (DataMigrations.CreateTableSql + "\n" + string.Join("\n", DataMigrations.All.Select(m => m.Sql))).Replace("\r\n", "\n");
        var tables = Regex.Matches(sql, @"CREATE TABLE dbo\.(\w+) \((.*?)\n\s*\);", RegexOptions.Singleline);
        Assert.True(tables.Count > 10);
        foreach (Match table in tables)
        {
            var key = Regex.Match(table.Groups[2].Value, @"CONSTRAINT (\w+) PRIMARY KEY");
            Assert.True(key.Success, table.Groups[1].Value + " has no named primary key.");
            Assert.Equal("PK_" + table.Groups[1].Value, key.Groups[1].Value);
            Assert.StartsWith("Cr", table.Groups[1].Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_batch_that_fails_keeps_nothing()
    {
        using var world = new TestWorld();
        Assert.Throws<InvalidOperationException>(() => world.Store.Batch(s =>
        {
            s.Insert(new JobRun { Job = "x", StartedAt = world.Now });
            throw new InvalidOperationException("stop");
        }));
        Assert.Equal(0, world.Store.Count<JobRun>());
        world.Store.Batch(s => { s.Insert(new JobRun { Job = "x" }); s.Insert(new JobRun { Job = "y" }); });
        Assert.Equal(2, world.Store.Count<JobRun>());
    }

    /// <summary>
    /// The tables as the migrations leave them, written as Table.CreateSql writes them: each CREATE TABLE with the columns
    /// later migrations ADD (in order) and the indexes CREATE INDEX makes.
    /// </summary>
    private static string Schema()
    {
        var sql = string.Join("\n", DataMigrations.All.Select(m => m.Sql)).Replace("\r\n", "\n");
        var tables = new List<(string Name, List<string> Columns, string Key, List<string> Indexes)>();
        foreach (Match create in Regex.Matches(sql, @"CREATE TABLE (\S+) \(\n(.*?)\n(    CONSTRAINT PK_\w+ PRIMARY KEY \(\[Id\]\))\n\);", RegexOptions.Singleline))
            tables.Add((create.Groups[1].Value, create.Groups[2].Value.Split('\n').ToList(), create.Groups[3].Value, []));
        foreach (Match line in Regex.Matches(sql, @"^(ALTER TABLE (\S+) ADD (\[\w+\] [^;]*?(?: NOT NULL| NULL))(?: CONSTRAINT \w+ DEFAULT [^;]+)?;|CREATE INDEX IX_\w+ ON (\S+) \(\[\w+\]\);)$", RegexOptions.Multiline))
        {
            if (line.Groups[2].Success) tables.Single(t => t.Name == line.Groups[2].Value).Columns.Add("    " + line.Groups[3].Value + ",");
            else tables.Single(t => t.Name == line.Groups[4].Value).Indexes.Add(line.Value);
        }
        return string.Join("\n", tables.Select(t => $"CREATE TABLE {t.Name} (\n{string.Join("\n", t.Columns)}\n{t.Key}\n);" + string.Concat(t.Indexes.Select(i => "\n" + i))));
    }

    [Fact]
    public void Every_table_is_created_by_the_migrations_exactly_as_it_is_mapped()
    {
        var sql = string.Join("\n", DataMigrations.All.Select(m => m.Sql)).Replace("\r\n", "\n");
        var schema = Schema();
        foreach (var type in Tables.All)
            Assert.Contains(Table.Of(type).CreateSql(), schema);
        // Every table the migrations make and keep is mapped (the copy of MyCRM's data was dropped again).
        Assert.Equal(Tables.All.Count, Regex.Matches(sql, "CREATE TABLE").Count - Regex.Matches(sql, "DROP TABLE").Count);
        Assert.Equal(DataMigrations.All.Count, DataMigrations.All.Select(m => m.Id).Distinct().Count());
        Assert.All(DataMigrations.All, m => Assert.Matches(@"^\d{4}-\d{2}-\d{2}-[a-z0-9-]+$", m.Id));
    }

    [Fact]
    public void A_role_taken_away_is_kept_marked_with_when_and_by_whom()
    {
        using var world = new TestWorld();
        var admin = TestWorld.Admin.ToActor(world.D.People);
        var login = TestWorld.Olga.Login;
        Assert.True(world.D.People.SetRoles(admin, login, "Olga Other", "olga@example.com", new[] { Roles.Admin }).Ok);
        Assert.Equal(new[] { Roles.Admin }, world.D.People.RolesOf(login));
        Assert.True(world.D.People.SetRoles(admin, login, null, null, Array.Empty<string>()).Ok);
        Assert.Empty(world.D.People.RolesOf(login));
        var row = world.Store.Find<UserRole>(Query.Where("Login", login)).Single();
        Assert.False(row.IsActive);
        Assert.Equal(world.Now, row.RemovedOn);
        Assert.Equal("Rob Root", row.RemovedBy);

        // Given again: a new role, the old one stays as it was.
        Assert.True(world.D.People.SetRoles(admin, login, null, null, new[] { Roles.Admin }).Ok);
        Assert.Equal(2, world.Store.Count<UserRole>(Query.Where("Login", login)));
        Assert.Contains(Roles.Admin, world.D.People.RolesOf(login));
        // Nobody takes their own Administrator role away.
        var olga = new ApiUser(login, "Olga Other").ToActor(world.D.People);
        Assert.Equal(ResultCodes.Forbidden, world.D.People.SetRoles(olga, login, null, null, Array.Empty<string>()).Code);
    }

    [Fact]
    public void Nothing_in_MyCRMRecords_deletes_a_record()
    {
        // Records are marked (IsActive = false, Unpaired, a schedule's time 00:00:00...), never deleted: only the stores themselves may delete.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "MyCRMRecords.Core"))) root = root.Parent;
        Assert.NotNull(root);
        var deletes = Directory.GetFiles(Path.Combine(root!.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f) is not ("Records.cs" or "Storage.cs") && f.IndexOf(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal) < 0)
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (File: Path.GetFileName(f), Line: i + 1, Text: line)))
            .Where(l => l.Text.Contains(".Delete<") && !l.Text.Contains("=> inner.Delete"))
            .Select(l => $"{l.File}:{l.Line}: {l.Text.Trim()}").ToList();
        Assert.True(deletes.Count == 0, "Mark records instead of deleting them:\n" + string.Join("\n", deletes));
    }

    [Fact]
    public void The_costing_sheets_and_request_forms_have_no_way_to_change_MyOrders_or_MyCRM()
    {
        // MyOrders' tables (and MyCRM's they name) are only read for them: IMyOrdersData has no method that writes, and its
        // SQL has no statement that could. MyCRM's own records are written by MyCrmStore only, behind its switch.
        Assert.Equal(new[] { "All", "File", "People", "RequestFormSalesmen", "Rows", "Search", "SearchRequestForms", "Users", "get_Available" },
            typeof(IMyOrdersData).GetMethods().Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "MyCRMRecords.Core"))) root = root.Parent;
        Assert.NotNull(root);
        var core = Path.Combine(root!.FullName, "src", "MyCRMRecords.Core");
        var source = string.Concat(new[] { "Data/MyOrdersData.cs", "Data/RequestFormData.cs", "Domain/CostingSheets.cs", "Domain/CostingSheetPrint.cs",
            "Domain/CostingSheetView.cs", "Domain/RequestForms.cs", "Domain/RequestFormView.cs", "Domain/OrdersAccess.cs", "Api/OrdersApi.cs" }
            .Select(f => File.ReadAllText(Path.Combine(core, f.Replace('/', Path.DirectorySeparatorChar)))));
        foreach (var word in new[] { "INSERT ", "UPDATE ", "DELETE ", "MERGE ", "BeginWrite", "Execute(", "MyCrmStore", ".Insert(", ".Update(", "Batch(" })
            Assert.DoesNotContain(word, source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_Files_page_has_no_way_to_change_MyFiles()
    {
        // MyFiles' files are only read: IMyFilesData has no method that writes, and its SQL and the rules have no statement that could.
        Assert.Equal(new[] { "Bytes", "Get", "Search", "get_Available" },
            typeof(IMyFilesData).GetMethods().Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "MyCRMRecords.Core"))) root = root.Parent;
        Assert.NotNull(root);
        var core = Path.Combine(root!.FullName, "src", "MyCRMRecords.Core");
        var source = string.Concat(new[] { "Data/MyFilesData.cs", "Domain/MyFiles.cs", "Api/MyFilesApi.cs" }
            .Select(f => File.ReadAllText(Path.Combine(core, f.Replace('/', Path.DirectorySeparatorChar)))));
        foreach (var word in new[] { "INSERT ", "UPDATE ", "DELETE ", "MERGE ", "BeginWrite", "Execute(", "MyCrmStore", ".Insert(", ".Update(", "Batch(" })
            Assert.DoesNotContain(word, source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_machines_have_no_way_to_change_Advance()
    {
        // Advance's machines are only read: IAdvanceMachines has no method that writes, and its SQL has no statement that could.
        Assert.Equal(new[] { "CustomerEmails", "DealerSold", "Get", "LastContract", "Meters", "MonthlyCopies", "Search", "Services", "Toner", "YearlyCopies", "get_Available" },
            typeof(IAdvanceMachines).GetMethods().Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "MyCRMRecords.Core"))) root = root.Parent;
        Assert.NotNull(root);
        var source = File.ReadAllText(Path.Combine(root!.FullName, "src", "MyCRMRecords.Core", "Data", "AdvanceMachines.cs"));
        foreach (var word in new[] { "INSERT ", "UPDATE ", "DELETE ", "MERGE ", "TRUNCATE ", "BeginTransaction", "Execute(", "MyCrmStore", ".Insert(", ".Update(", "Batch(" })
            Assert.DoesNotContain(word, source, StringComparison.OrdinalIgnoreCase);
        // MyMachines' temporary access is only added to and marked: nothing there deletes.
        var access = File.ReadAllText(Path.Combine(root.FullName, "src", "MyCRMRecords.Core", "Data", "MyMachinesAccess.cs"));
        Assert.DoesNotContain("DELETE ", access, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE ", access, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_property_of_MyCRMs_records_has_a_place_in_MyCRM_of_its_own()
    {
        foreach (var e in MyCrmMap.All)
        {
            var stored = e.Stored.Select(f => f.Column).ToList();
            Assert.True(stored.Count == stored.Distinct(StringComparer.OrdinalIgnoreCase).Count(), $"{e.Entity} maps two properties to one column: {string.Join(", ", stored)}");
            Assert.DoesNotContain("ID", stored);
            Assert.All(e.Fields.Where(f => f.Kind == CrmFieldKind.User), f => Assert.Equal(typeof(string), f.Type));
            Assert.All(e.Fields.Where(f => f.Kind == CrmFieldKind.Static), f => Assert.NotNull(f.List));
            Assert.Equal(e.Type.GetProperties().Count(p => p.CanWrite && p.Name != "Id"), e.Fields.Count);
        }
        Assert.Equal("CREATEDBY", MyCrmMap.Of<Company>()!.Field("CreatedBy")!.Column);
        Assert.Equal("UPDATEDBY", MyCrmMap.Of<Company>()!.Field("UpdatedBy")!.Column);
    }

    [Fact]
    public void Every_text_column_has_a_length()
    {
        foreach (var type in Tables.All)
            foreach (var property in type.GetProperties().Where(p => p.PropertyType == typeof(string)))
                Assert.True(property.GetCustomAttribute<LenAttribute>() is not null, $"{type.Name}.{property.Name} has no [Len].");
    }

    [Fact]
    public void Copying_between_stores_keeps_the_ids()
    {
        using var world = new TestWorld(crmUsers: false);
        world.Store.Insert(new JobRun { Job = "A" });
        world.Store.Insert(new JobRun { Job = "B" });
        world.Store.Delete<JobRun>(1);
        using var other = new TestWorld(crmUsers: false);
        other.Store.Delete<UserRole>(Query.All());
        other.Store.Delete<AppUser>(Query.All());
        var copied = DatabaseStorage.CopyAll(world.Store, other.Store);
        Assert.True(copied >= 2);
        Assert.Equal("B", other.Store.Get<JobRun>(2)!.Job);
        Assert.Null(other.Store.Get<JobRun>(1));
        Assert.Equal(3, other.Store.Insert(new JobRun { Job = "C" }).Id);
    }

    [Fact]
    public void Imported_records_keep_their_ids_and_new_ones_follow()
    {
        using var world = new TestWorld();
        world.Store.InsertKeepingIds(new[] { new JobRun { Id = 412, Job = "P0412" }, new JobRun { Id = 17, Job = "P0017" } });
        Assert.Equal("P0412", world.Store.Get<JobRun>(412)!.Job);
        Assert.Equal(413, world.Store.Insert(new JobRun()).Id);
    }

    [Fact]
    public void A_person_merged_into_another_is_not_offered()
    {
        using var world = new TestWorld();
        var olga = world.D.People.User(TestWorld.Olga.Login)!;
        Assert.True(olga.IsActive);
        Assert.Contains(world.D.People.Everyone(), u => u.Login == olga.Login);
        olga.IsActive = false;
        world.Store.Update(olga);
        Assert.DoesNotContain(world.D.People.Everyone(), u => u.Login == olga.Login);
    }
}

public sealed class SettingsTests
{
    [Fact]
    public void Secrets_are_kept_protected_and_never_shown()
    {
        using var world = new TestWorld(s =>
        {
            s.Connections.OutSystems.SqlServer.Password = "sql-pass";
            s.Email.Password = "mail-pass";
        });
        var text = File.ReadAllText(world.SettingsFile.Path);
        Assert.DoesNotContain("\"sql-pass\"", text);
        Assert.Contains("protected:sql-pass", text);
        Assert.Contains("protected:mail-pass", text);
        var loaded = world.SettingsFile.Load();
        Assert.Equal("mail-pass", loaded.Email.Password);
        var view = SettingsView.Of(loaded).ToString();
        foreach (var secret in new[] { "sql-pass", "mail-pass" }) Assert.DoesNotContain(secret, view);
        Assert.Contains("\"passwordSet\": true", view);
    }

    [Fact]
    public void Saving_a_section_with_blank_secrets_keeps_the_saved_ones()
    {
        using var world = new TestWorld(s => s.Email.Password = "mail-pass");
        var admin = new AdminApi(world.D, null!, demoTools: false);
        // Only POST changes anything: a HEAD (which another page can send without asking) or a PUT changes nothing.
        Assert.Equal(405, admin.Handle("HEAD", "admin/settings/email", "", new Dictionary<string, string>(), TestWorld.Admin).Status);
        Assert.Equal(405, admin.Handle("PUT", "admin/mycrm", "", new Dictionary<string, string>(), TestWorld.Admin).Status);
        Assert.Equal(new CrmSettings().Email.Server, world.SettingsFile.Load().Email.Server);
        Assert.False(world.SettingsFile.Load().MyCrm.WritesEnabled);
        Assert.Equal(200, admin.Handle("POST", "admin/settings/email", "{\"enabled\":true,\"server\":\"smtp.example.com\",\"port\":25,\"password\":\"\",\"from\":\"rspapps@ricoh.sg\",\"siteUrl\":\"https://apps.ricoh.sg/MyCRMRecords/\"}",
            new Dictionary<string, string>(), TestWorld.Admin).Status);
        var saved = world.SettingsFile.Load();
        Assert.Equal("mail-pass", saved.Email.Password);
        Assert.True(saved.Email.Enabled);
        Assert.Equal("smtp.example.com", saved.Email.Server);
    }

    [Fact]
    public void Everything_that_reaches_outside_starts_switched_off()
    {
        var s = new CrmSettings();
        Assert.False(s.Email.Enabled);
        Assert.False(s.Jobs.Enabled);
        Assert.False(s.MyCrm.WritesEnabled);
        Assert.False(s.MyMachines.WritesEnabled);
        Assert.All(ExternalNames.All, n => Assert.False(new ExternalDatabases(() => s, _ => throw new InvalidOperationException()).IsConfigured(n)));
    }
}

public sealed class SchedulerTests
{
    [Theory]
    [InlineData(5, "", null, "2026-10-17 09:30", true)]
    [InlineData(5, "", "2026-10-17 09:26", "2026-10-17 09:30", false)]
    [InlineData(0, "08:00", null, "2026-10-17 07:59", false)]
    [InlineData(0, "08:00", null, "2026-10-17 08:01", true)]
    [InlineData(0, "08:00", "2026-10-17 08:00", "2026-10-17 09:45", false)]
    public void A_task_is_due_every_few_minutes_or_once_a_day(int every, string dailyAt, string? last, string now, bool due)
    {
        static DateTime At(string text) => DateTime.ParseExact(text, "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(due, Scheduler.IsDue(new JobSchedule { EveryMinutes = every, DailyAt = dailyAt }, last is null ? null : At(last), At(now)));
    }

    [Fact]
    public void A_daily_task_runs_only_on_its_days()
    {
        // Monday to Friday: Saturday 17 October 2026 is not one of them; Monday 19 is.
        var schedule = new JobSchedule { Job = "x", Enabled = true, DailyAt = "09:15", Days = "1,2,3,4,5" };
        Assert.False(Scheduler.IsDue(schedule, null, new DateTime(2026, 10, 17, 9, 20, 0)));
        Assert.True(Scheduler.IsDue(schedule, null, new DateTime(2026, 10, 19, 9, 20, 0)));
        Assert.False(Scheduler.IsDue(schedule, null, new DateTime(2026, 10, 19, 9, 10, 0)));
    }
}

public sealed class UserGuideTests
{
    private static string Slug(string heading) =>
        Regex.Replace(heading.Trim().ToLowerInvariant(), @"[^\p{L}\p{M}\p{N}\p{Pc}\- ]", "").Replace(' ', '-');

    [Fact]
    public void The_pages_link_to_headings_of_the_guide()
    {
        var headings = UserGuide.Markdown.Split('\n').Where(l => l.StartsWith("#")).Select(l => Slug(l.TrimStart('#'))).ToHashSet();
        var pages = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Pages"), "*.aspx");
        Assert.NotEmpty(pages);
        var linked = 0;
        foreach (var page in pages)
            foreach (Match link in Regex.Matches(File.ReadAllText(page), @"TopBar\(""[^""]*"", ""([^""]+)""\)"))
            {
                linked++;
                Assert.True(headings.Contains(link.Groups[1].Value), $"{Path.GetFileName(page)} links to #{link.Groups[1].Value}, which the guide has no heading for.");
            }
        Assert.True(linked >= 6);
    }
}
