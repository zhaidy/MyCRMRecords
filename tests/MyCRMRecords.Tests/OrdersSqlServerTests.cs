namespace MyCRMRecords.Tests;

/// <summary>
/// The costing sheets and request forms against a real SQL Server: MyOrders' and MyCRM's tables in an OutSystems database
/// read as the in-memory fake reads them. Run when MYCRMRECORDS_TEST_SQLSERVER is set (CI sets it to LocalDB).
/// </summary>
public sealed class OrdersSqlServerTests
{
    /// <summary>
    /// The OutSystems database as MyOrders and MyCRM keep it: the platform's catalog (OSSYS_ESPACE, OSSYS_ENTITY, with an
    /// inactive older module and an older entity row to skip), OutSystems' users (Cara's username her email address), and a
    /// table per entity with generated names (OSUSR_ab1_COSTINGSHEET) holding the fake's rows, file columns as varbinary.
    /// </summary>
    public static string OutSystemsSql(FakeMyOrders data)
    {
        var sql = new System.Text.StringBuilder();
        sql.AppendLine("CREATE TABLE dbo.OSSYS_ESPACE ([ID] int NOT NULL PRIMARY KEY, [NAME] nvarchar(200) NOT NULL, [IS_ACTIVE] bit NOT NULL);");
        sql.AppendLine("CREATE TABLE dbo.OSSYS_ENTITY ([ID] int NOT NULL PRIMARY KEY, [ESPACE_ID] int NOT NULL, [NAME] nvarchar(200) NOT NULL, [PHYSICAL_TABLE_NAME] nvarchar(200) NOT NULL, [IS_ACTIVE] bit NOT NULL);");
        sql.AppendLine("CREATE TABLE dbo.OSSYS_USER ([ID] int NOT NULL PRIMARY KEY, [USERNAME] nvarchar(250) NOT NULL, [NAME] nvarchar(250) NOT NULL, [EMAIL] nvarchar(250) NOT NULL, [IS_ACTIVE] bit NOT NULL);");
        sql.AppendLine("INSERT INTO dbo.OSSYS_ESPACE VALUES (1, N'MyOrders', 0), (2, N'MyOrders', 1), (3, N'MyCRM', 1), (4, N'MyFiles', 1), (5, N'MyMachines', 1);");
        foreach (var u in data.UserRows)
        {
            // OutSystems keeps Cara as her email address (bug 29): MyCRMRecords finds her by it.
            var username = u.Login == @"RSP\cara.tan" ? @"RSP\cara.tan@ricoh.sg" : u.Login;
            sql.AppendLine($"INSERT INTO dbo.OSSYS_USER VALUES ({u.Id}, N'{username.Replace("'", "''")}', N'{u.Name.Replace("'", "''")}', N'{u.Email}', 1);");
        }
        var entityId = 100;
        // What the costing sheets read, and MyCRM's entities as MyCRMRecords maps them (the connection test checks both).
        var entities = Entities.Checked.Select(c => (c.Entity, c.Columns)).ToList();
        foreach (var (name, columns) in MyCrmMap.Checked)
        {
            var at = entities.FindIndex(x => x.Entity.Module == OsModule.MyCRM && x.Entity.Name == name);
            if (at < 0) entities.Add((new OsEntity(OsModule.MyCRM, name), columns));
            else entities[at] = (entities[at].Entity, entities[at].Columns.Concat(columns).ToArray());
        }
        foreach (var check in entities)
        {
            var e = check.Entity;
            var physical = (e.Module == OsModule.MyOrders ? "OSUSR_ab1_" : e.Module == OsModule.MyFiles ? "OSUSR_mf1_"
                : e.Module == OsModule.MyMachines ? "OSUSR_mm1_" : "OSUSR_824_") + e.Name.ToUpperInvariant();
            var espace = e.Module == OsModule.MyOrders ? 2 : e.Module == OsModule.MyFiles ? 4 : e.Module == OsModule.MyMachines ? 5 : 3;
            // An older row of the same entity, deleted from the module, is not the table.
            sql.AppendLine($"INSERT INTO dbo.OSSYS_ENTITY VALUES ({entityId++}, {espace}, N'{e.Name}', N'{physical}_OLD', 0);");
            sql.AppendLine($"INSERT INTO dbo.OSSYS_ENTITY VALUES ({entityId++}, {espace}, N'{e.Name}', N'{physical}', 1);");
            var rows = data.Tables.TryGetValue(e, out var found) ? found : new List<Dictionary<string, object?>>();
            var columns = check.Columns.Concat(rows.SelectMany(r => r.Keys)).Select(c => c.ToUpperInvariant()).Distinct().ToList();
            string TypeOf(string column)
            {
                var value = rows.Select(r => r.Get(column)).FirstOrDefault(v => v is not null);
                return value switch
                {
                    long => "bigint",
                    int => "int",
                    decimal => "decimal(37,8)",
                    bool => "bit",
                    DateTime => "datetime",
                    byte[] => "varbinary(max)",
                    string => "nvarchar(2000)",
                    _ => column == "ID" || column.EndsWith("ID", StringComparison.Ordinal) ? "bigint"
                        : column == "BINARY" ? "varbinary(max)"
                        : column.StartsWith("IS", StringComparison.Ordinal) ? "bit"
                        : "nvarchar(2000)",
                };
            }
            // MyMachines' TempAccess is added to (the Machine access page): its Id is an identity, as OutSystems makes them.
            var identity = e == Entities.TempAccess;
            sql.AppendLine($"CREATE TABLE dbo.[{physical}] ({string.Join(", ", columns.Select(c => $"[{c}] {TypeOf(c)} {(c == "ID" ? (identity ? "IDENTITY(1,1) " : "") + "NOT NULL PRIMARY KEY" : "NULL")}"))});");
            if (identity && rows.Count > 0) sql.AppendLine($"SET IDENTITY_INSERT dbo.[{physical}] ON;");
            foreach (var row in rows)
                sql.AppendLine($"INSERT INTO dbo.[{physical}] ({string.Join(", ", row.Keys.Select(k => "[" + k.ToUpperInvariant() + "]"))}) VALUES ({string.Join(", ", row.Values.Select(Literal))});");
            if (identity && rows.Count > 0) sql.AppendLine($"SET IDENTITY_INSERT dbo.[{physical}] OFF;");
        }
        return sql.ToString();
    }

    private static string Literal(object? value) => value switch
    {
        null => "NULL",
        bool b => b ? "1" : "0",
        DateTime d => "'" + d.ToString("yyyy-MM-ddTHH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture) + "'",
        decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture),
        long or int => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!,
        byte[] bytes => "0x" + (bytes.Length == 0 ? "" : BitConverter.ToString(bytes).Replace("-", "")),
        _ => "N'" + Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!.Replace("'", "''") + "'",
    };

    /// <summary>The world's MyOrders and MyCRM in a real OutSystems database, and MyCRMRecords reading it there.</summary>
    private static (CrmApp App, SqlMyOrdersData Data) InOutSystems(OrdersWorld world, TestDatabase outSystems)
    {
        outSystems.Execute(OutSystemsSql(world.Data));
        world.SettingsFile.Change(s =>
        {
            s.Connections.OutSystems.Configured = true;
            s.Connections.OutSystems.SqlServer = outSystems.Settings;
        });
        var databases = new ExternalDatabases(world.SettingsFile.Load, TestDatabase.Connect);
        var data = new SqlMyOrdersData(databases, () => world.SettingsFile.Load().Connections, "RSP");
        return (CrmFactory.Create(world.Store, world.SettingsFile, TestDatabase.Connect, Path.Combine(world.Folder, "scheduled-tasks.json"), () => world.Now,
            new FakeEmail(), world.Store, orders: data, domain: "RSP"), data);
    }

    [Fact]
    public void MyOrders_tables_are_read_where_MyOrders_keeps_them_as_the_tests_read_them()
    {
        if (string.IsNullOrEmpty(TestDatabase.Server)) return;
        using var world = new OrdersWorld();
        var a = world.Sheet(world.SamId, world.Acme, "Invoiced", created: new DateTime(2025, 3, 4, 10, 0, 0), achieved: 1200m, invoiceNo: "INV_77%",
            mds: world.DanId, headApproved: new DateTime(2025, 3, 10), invoiceDate: new DateTime(2025, 4, 1));
        var b = world.Sheet(world.KenId, world.Zen, "Draft", created: new DateTime(2026, 1, 5, 9, 0, 0), achieved: 300.5m);
        var c = world.Sheet(world.CaraId, world.Dbs, "Pending Head", created: new DateTime(2026, 2, 1, 9, 0, 0), pp: world.DanId, achieved: 77m);
        world.Sheet(world.SamId, world.Acme, "Draft", active: false);
        var item = world.Data.Add(Entities.CostingSheetItem, new { COSTINGSHEETID = a, ISACTIVE = true, PRODUCTNAME = "IM C3000", PRODUCTTYPE = "Configuration", EDPCODE = "418283", QUANTITY = 2, UNITPRICE = 4000m, TOTALSTP = 8000m, ACHIEVEDPRICE = 7000m, ISFSA = true, MONTHLYMONTHS = 60 });
        world.Data.Add(Entities.CostingSheetSubItem, new { COSTINGSHEETITEMID = item, ISACTIVE = true, PRODUCTNAME = "Tray", EDPCODE = "T-1", QUANTITY = 1, UNITPRICE = 250m });
        world.Data.Add(Entities.Variable, new { COSTINGSHEETID = a, VARIABLENAMEID = world.VariableName["R2R Trade In"], COST = 200m, TRADEINITEM = "Old MFP", MANUFACTURER = "Ricoh", MODEL = "MP", SERIALNO = "S-1", REMARKS = "" });
        var file = world.Data.Add(Entities.CostingSheetAttachment, new { COSTINGSHEETID = a, BINARY = new byte[] { 1, 2, 3, 4, 5 }, FILENAME = "PO.pdf", REMARKS = "", UPLOADEDBY = world.SamId, UPLOADEDON = new DateTime(2025, 3, 5, 8, 0, 0), ISCOMPLEX = false });

        using var outSystems = new TestDatabase();
        var (app, data) = InOutSystems(world, outSystems);

        // Rows, files as their length; a file read whole only when asked for.
        Assert.Equal(5L, data.Rows(Entities.CostingSheetAttachment, "COSTINGSHEETID", new[] { a }).Single().Long("BINARY"));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, data.File(Entities.CostingSheetAttachment, "BINARY", file));
        Assert.Null(data.File(Entities.CostingSheetAttachment, "BINARY", 999999));
        Assert.Equal(world.Data.All(Entities.CostingSheetStatus).Select(r => r.Text("LABEL")), data.All(Entities.CostingSheetStatus).Select(r => r.Text("LABEL")));
        Assert.Equal(@"RSP\cara.tan", data.Users().Single(u => u.Id == world.CaraId).Login);
        Assert.Equal(world.Data.People().Salesmen.OrderBy(i => i), data.People().Salesmen.OrderBy(i => i));
        Assert.Equal(world.Data.People().Specialists.OrderBy(i => i), data.People().Specialists.OrderBy(i => i));

        // The list's search answers as the fake does, with LIKE's wildcards taken literally.
        var searches = new[]
        {
            new CostingSheetSearch(), new CostingSheetSearch { Keyword = "inv_77%" }, new CostingSheetSearch { Keyword = "_77" }, new CostingSheetSearch { Keyword = "dbs" },
            new CostingSheetSearch { Keyword = "pending" }, new CostingSheetSearch { StatusId = world.Status["Draft"] }, new CostingSheetSearch { SalesRepIds = new[] { world.SamId } },
            new CostingSheetSearch { SpecialistIds = new[] { world.DanId } }, new CostingSheetSearch { HasSpecialist = "Yes" }, new CostingSheetSearch { HasSpecialist = "No" },
            new CostingSheetSearch { CreatedFrom = new DateTime(2026, 1, 5) }, new CostingSheetSearch { CreatedTo = new DateTime(2026, 1, 5) },
            new CostingSheetSearch { HeadApprovedFrom = new DateTime(2025, 3, 10), HeadApprovedTo = new DateTime(2025, 3, 10) },
            new CostingSheetSearch { InvoiceFrom = new DateTime(2025, 1, 1) }, new CostingSheetSearch { Sort = "achieved", Descending = false },
            new CostingSheetSearch { Sort = "company", Descending = false }, new CostingSheetSearch { SalesmanIds = new[] { world.SamId, world.CaraId } },
            new CostingSheetSearch { SalesmanIds = Array.Empty<long>() }, new CostingSheetSearch { SalesmanIds = new[] { world.KenId, world.CaraId }, CompanyContains = "dbs" },
            new CostingSheetSearch { Skip = 1, Take = 1 }, new CostingSheetSearch { CompanyId = world.Acme }, new CostingSheetSearch { CompanyId = world.Zen, SalesmanIds = new[] { world.SamId } },
        };
        foreach (var search in searches)
            Assert.Equal(MyCRMRecords.Json.Serialize(world.Data.Search(search)), MyCRMRecords.Json.Serialize(data.Search(search)));
        Assert.Equal(new[] { c, b, a }, data.Search(new CostingSheetSearch()).Rows.Select(r => r.Id));

        // A costing sheet's page and who sees it, the same over the OutSystems database as in memory.
        foreach (var user in new[] { OrdersWorld.Sam, OrdersWorld.Hana, OrdersWorld.Cara, OrdersWorld.Sid })
        {
            var actor = user.ToActor(app.People, app.OrdersAccess);
            Assert.Equal(world.Actor(user).Orders.Role, actor.Orders.Role);
            Assert.Equal(world.Actor(user).Orders.People.OrderBy(i => i), actor.Orders.People.OrderBy(i => i));
            foreach (var id in new[] { a, b, c })
                Assert.Equal(MyCRMRecords.Json.Serialize(world.D.CostingSheets.Get(world.Actor(user), id)), MyCRMRecords.Json.Serialize(app.CostingSheets.Get(actor, id)));
        }
        Assert.Equal(1200m, app.CostingSheets.Get(OrdersWorld.Admin.ToActor(app.People, app.OrdersAccess), a)!.TotalAchievedPrice);
    }

    [Fact]
    public void Request_forms_are_read_where_MyOrders_keeps_them_as_the_tests_read_them()
    {
        if (string.IsNullOrEmpty(TestDatabase.Server)) return;
        using var world = new OrdersWorld();
        var sheet = world.Sheet(world.SamId, world.Acme, "Invoiced");
        world.Data.Add(Entities.CostingSheetItem, new { COSTINGSHEETID = sheet, ISACTIVE = true, PRODUCTNAME = "IM C3000", EDPCODE = "418283", QUANTITY = 2 });
        var a = world.Form(world.SamId, world.Acme, "Pending Allocation", created: new DateTime(2026, 1, 5, 9, 0, 0), costingSheet: sheet, mds: world.DanId,
            printed: new DateTime(2026, 1, 9, 8, 0, 0));
        var b = world.Form(world.KenId, world.Zen, "Draft", "Collection", created: new DateTime(2026, 3, 1, 9, 0, 0));
        var c = world.Form(world.CaraId, world.Dbs, "Backordered", created: new DateTime(2026, 2, 1, 9, 0, 0), printed: new DateTime(2026, 2, 2), recalled: new DateTime(2026, 2, 3));
        world.Form(world.SamId, world.Acme, active: false);
        var item = world.Data.Add(Entities.RequestFormItem, new { REQUESTFORMID = a, ISACTIVE = true, PRODUCTNAME = "IM C3000", PRODUCTTYPE = "Mainframe", EDPCODE = "418283",
            QUANTITY = 1, UNITPRICE = 4000m, TOTALSTP = 4000m, ADDRESSID = world.AcmeAddress, CONTACTID = world.AcmeContact, DATE = new DateTime(2026, 1, 20), ISFSA = true,
            NOLIFT = false, REMARKS = "", MODEL = "M", SERIAL = "S" });
        world.Data.Add(Entities.RequestFormSubItem, new { REQUESTFORMITEMID = item, ISACTIVE = true, PRODUCTNAME = "Tray", EDPCODE = "T-1", QUANTITY = 1, UNITPRICE = 250m });
        world.Data.Add(Entities.RequestFormItem, new { REQUESTFORMID = b, ISACTIVE = true, PRODUCTNAME = "", ASSETTYPEID = world.AssetType["Hardware"],
            COLLECTIONTYPEID = world.CollectionType["Exchange"], MODEL = "MP", SERIAL = "X", QUANTITY = 1, DATE = new DateTime(2026, 3, 9), HASFINISHER = true });
        world.Data.Add(Entities.RequestFormAttachment, new { REQUESTFORMID = a, BINARY = new byte[] { 9, 9 }, FILENAME = "DO.pdf", REMARKS = "", UPLOADEDBY = world.SamId,
            UPLOADEDON = new DateTime(2026, 1, 6), ISCOMPLEX = false });
        var print = world.Data.Add(Entities.DeliveryFormPrint, new { REQUESTFORMID = a, ADDRESSID = world.AcmeAddress, CONTACTID = world.AcmeContact, DATE = new DateTime(2026, 1, 20),
            NOLIFT = false, REMARKS = "" });
        world.Data.Add(Entities.DeliveryFormPrintItem, new { DELIVERYFORMPRINTID = print, REQUESTFORMID = a, REQUESTFORMITEMID = item, ITEMCODE = "418283", NAME = "IM C3000",
            SERIALNO = "S", ALLOCATEDQUANTITY = 1, QUANTITY = 1, MODEL = "M", PRODUCTTYPE = "Mainframe", PAGE = 1, ISFSA = true });

        using var outSystems = new TestDatabase();
        var (app, data) = InOutSystems(world, outSystems);
        Assert.Equal(world.Data.RequestFormSalesmen().OrderBy(i => i), data.RequestFormSalesmen().OrderBy(i => i));
        var searches = new[]
        {
            new RequestFormSearch(), new RequestFormSearch { Keyword = "dbs" }, new RequestFormSearch { Keyword = "pending" }, new RequestFormSearch { Keyword = "50%" },
            new RequestFormSearch { StatusId = world.FormStatus["Draft"] }, new RequestFormSearch { TypeId = world.FormType["Collection"] },
            new RequestFormSearch { SalesRepIds = new[] { world.SamId } }, new RequestFormSearch { CreatedFrom = new DateTime(2026, 2, 1) },
            new RequestFormSearch { CreatedTo = new DateTime(2026, 2, 1) }, new RequestFormSearch { Sort = "id", Descending = false },
            new RequestFormSearch { Sort = "company", Descending = false }, new RequestFormSearch { Sort = "pendingprint" },
            new RequestFormSearch { SalesmanIds = new[] { world.KenId } }, new RequestFormSearch { SalesmanIds = Array.Empty<long>(), SpecialistOf = new[] { world.DanId } },
            new RequestFormSearch { SalesmanIds = Array.Empty<long>(), AnyWithSpecialist = true }, new RequestFormSearch { SalesmanIds = new[] { world.KenId, world.CaraId }, CompanyContains = "dbs" },
            new RequestFormSearch { Skip = 1, Take = 1 }, new RequestFormSearch { CompanyId = world.Dbs }, new RequestFormSearch { CompanyId = world.Zen, SalesmanIds = new[] { world.SamId } },
        };
        foreach (var search in searches)
            Assert.Equal(MyCRMRecords.Json.Serialize(world.Data.SearchRequestForms(search)), MyCRMRecords.Json.Serialize(data.SearchRequestForms(search)));
        Assert.Equal(new[] { b, c, a }, data.SearchRequestForms(new RequestFormSearch()).Rows.Select(r => r.Id));
        Assert.True(data.SearchRequestForms(new RequestFormSearch()).Rows.Single(r => r.Id == c).IsPendingPrint);

        // A request form's page and print, and who sees them, the same over the OutSystems database as in memory.
        foreach (var user in new[] { OrdersWorld.Sam, OrdersWorld.Hana, OrdersWorld.Dan, OrdersWorld.Sid })
        {
            var actor = user.ToActor(app.People, app.OrdersAccess);
            foreach (var id in new[] { a, b, c })
            {
                Assert.Equal(MyCRMRecords.Json.Serialize(world.D.RequestForms.Get(world.Actor(user), id)), MyCRMRecords.Json.Serialize(app.RequestForms.Get(actor, id)));
                Assert.Equal(MyCRMRecords.Json.Serialize(world.D.RequestForms.Print(world.Actor(user), id)), MyCRMRecords.Json.Serialize(app.RequestForms.Print(actor, id)));
            }
            Assert.Equal(MyCRMRecords.Json.Serialize(world.D.RequestForms.List(world.Actor(user), new RequestFormSearch())),
                MyCRMRecords.Json.Serialize(app.RequestForms.List(actor, new RequestFormSearch())));
        }
        Assert.Single(app.RequestForms.Print(OrdersWorld.Hana.ToActor(app.People, app.OrdersAccess), a)!.Value.Print.Deliveries);
    }

    [Fact]
    public void MyOrders_and_MyCRM_tables_pass_the_connection_test_and_a_missing_one_shows()
    {
        if (string.IsNullOrEmpty(TestDatabase.Server)) return;
        using var world = new OrdersWorld();
        world.Sheet(world.SamId, world.Acme);
        using var outSystems = new TestDatabase();
        var (app, _) = InOutSystems(world, outSystems);
        var test = app.Databases.Test(ExternalNames.OutSystems);
        Assert.All(test, l => Assert.True(l.Ok, l.Line));
        // Connected, each of MyCRM's and MyOrders' entities, and the users.
        Assert.Equal(MyCrmMap.Checked.Count + Entities.Checked.Count + 2, test.Count);
        world.SettingsFile.Change(s => s.Connections.OrdersModule = "MyOrdersOld");
        Assert.Contains(app.Databases.Test(ExternalNames.OutSystems), l => !l.Ok && l.Line.Contains("MyOrdersOld"));
    }

    [Fact]
    public void MyFiles_files_are_read_where_MyFile_keeps_them_as_the_tests_read_them()
    {
        if (string.IsNullOrEmpty(TestDatabase.Server)) return;
        using var world = new OrdersWorld();
        var files = world.Files;
        var pdf = new byte[] { 37, 80, 68, 70, 1, 2 };
        var a = files.Add("INV_77%.pdf", "CostingSheet", world.Acme, "Acme Pte Ltd", "199912345K", invoice: "INV_77%", uploaded: new DateTime(2026, 10, 5, 10, 0, 0),
            by: world.SamId, pdf: pdf);
        // Uploaded at the same time: the newer Id first.
        files.Add("Zen DO.pdf", "DO", world.Zen, "Zen Trading", "", doNumber: "DO-5", uploaded: new DateTime(2026, 10, 5, 10, 0, 0));
        files.Add("walk-in.pdf", "Others", null, "Walk-in", "", contract: "C-9", uploaded: new DateTime(2025, 1, 2));
        files.Add("gone.pdf", "FSA", world.Acme, "Acme Pte Ltd", "", active: false);
        files.TypeId("Leasing");

        using var outSystems = new TestDatabase();
        var (app, _) = InOutSystems(world, outSystems);
        var sql = app.MyFilesData;
        Assert.IsType<SqlMyFilesData>(sql);
        // More companies than an IN list or parameters could hold.
        var many = Enumerable.Range(1, 3000).Select(i => 100000L + i).Append(world.Acme).ToList();
        var searches = new[]
        {
            new FileSearch(), new FileSearch { Keyword = "inv_77%" }, new FileSearch { Keyword = "%" }, new FileSearch { Keyword = "199912345" },
            new FileSearch { Keyword = "c-9" }, new FileSearch { FileType = "DO" }, new FileSearch { FileType = "Leasing" }, new FileSearch { FileType = "costing sheet" },
            new FileSearch { CompanyId = world.Zen }, new FileSearch { UploadedFrom = new DateTime(2026, 1, 1) }, new FileSearch { Companies = new long[0] },
            new FileSearch { Companies = many }, new FileSearch { Skip = 1, Take = 1 },
        };
        foreach (var search in searches)
            Assert.Equal(MyCRMRecords.Json.Serialize(files.Search(search)), MyCRMRecords.Json.Serialize(sql.Search(search)));
        Assert.Equal(3, sql.Search(new FileSearch()).Total);
        Assert.Equal(2, sql.Search(new FileSearch { Companies = many }).Total);
        foreach (var id in files.Tables.Tables[Entities.File].Select(r => r.Long("ID")))
            Assert.Equal(MyCRMRecords.Json.Serialize(files.Get(id)), MyCRMRecords.Json.Serialize(sql.Get(id)));
        Assert.Equal("Sam Sales", sql.Get(a)!.UploadedBy);
        Assert.Equal(6L, sql.Get(a)!.Size);
        Assert.Equal(pdf, sql.Bytes(a));
        Assert.Null(sql.Bytes(999999));
        Assert.Null(sql.Get(999999));
    }

    [Fact]
    public void Advance_machines_are_read_by_their_real_column_names_as_the_tests_read_them()
    {
        if (string.IsNullOrEmpty(TestDatabase.Server)) return;
        var fake = new FakeAdvanceMachines();
        MachinesTests.Fill(fake);
        using var advance = new TestDatabase();
        // The tables with their real names ([serial-no], [SO-status], [Meter_Type]…), not MyMachines' (serial_no…).
        advance.Execute(fake.AdvanceSql());
        var settings = new CrmSettings();
        settings.Connections.AdvanceConnector = new ExternalDatabase { Configured = true, SqlServer = advance.Settings };
        var databases = new ExternalDatabases(() => settings, TestDatabase.Connect);
        var sql = new SqlAdvanceMachines(databases, () => settings.Connections);
        var today = new DateTime(2026, 10, 17);
        string J(object? value) => MyCRMRecords.Json.Serialize(value);
        // More BRNs than an IN list or parameters could hold.
        var many = Enumerable.Range(1, 3000).Select(i => "B" + i.ToString("D6")).Append("L2000XYZ").ToList();
        var searches = new List<MachineSearch>
        {
            new(), new() { Keyword = "r000" }, new() { Keyword = "%" }, new() { Keyword = "zen tr" }, new() { Keyword = "T1000" },
            new() { Brn = "T1000ABC" }, new() { Brn = "T1000ABC", Coverage = Coverages.Active }, new() { Coverage = Coverages.Active },
            new() { Finance = FinanceTypes.NoneKey }, new() { Finance = "SALCAS" }, new() { Brns = many }, new() { Brns = new string[0] },
            new() { Brns = new[] { "T1000ABC" }, Names = new[] { "Zen Trading" } }, new() { Brns = new string[0], Names = new[] { "LIM PTE LTD" } },
            new() { Skip = 2, Take = 3 }, new() { LastServiced = true }, new() { Keyword = "3100R", LastServiced = true, Skip = 0, Take = 2 },
        };
        searches.AddRange(Coverages.All.Select(c => new MachineSearch { Coverage = c.Code }));
        foreach (var search in searches)
        {
            search.Today = today;
            Assert.Equal(J(fake.Search(search)), J(sql.Search(search)));
        }
        Assert.Equal(8, sql.Search(new MachineSearch { Today = today }).Total);
        Assert.Equal(new DateTime(2026, 10, 1), sql.Search(new MachineSearch { Today = today, LastServiced = true, Keyword = "3100R0001" }).Rows.Single().LastServiced);
        var from = Machines.From(today, 12);
        foreach (var row in fake.Search(new MachineSearch { Today = today, Take = 500 }).Rows)
        {
            Assert.Equal(J(fake.Get(row.Serial, row.Item, today)), J(sql.Get(row.Serial, row.Item, today)));
            Assert.Equal(J(fake.Get(row.Serial, "", today)), J(sql.Get(row.Serial, "", today)));
            Assert.Equal(J(fake.Meters(row.Serial, row.ContractNo, from)), J(sql.Meters(row.Serial, row.ContractNo, from)));
            Assert.Equal(J(fake.Meters(row.Serial, "", from)), J(sql.Meters(row.Serial, "", from)));
            Assert.Equal(J(fake.Services(row.Serial, row.CustomerNo, row.ContractNo, from)), J(sql.Services(row.Serial, row.CustomerNo, row.ContractNo, from)));
            Assert.Equal(J(fake.Toner(row.Serial, row.ContractNo, from)), J(sql.Toner(row.Serial, row.ContractNo, from)));
            Assert.Equal(J(fake.LastContract(row.Serial, row.Item)), J(sql.LastContract(row.Serial, row.Item)));
        }
        // A company's copies per month: the machines' invoices added up, each machine's of its contract.
        var listed = fake.Search(new MachineSearch { Today = today, Take = 500 }).Rows.Select(r => new MeterMachine(r.Serial, r.ContractNo)).ToList();
        Assert.Equal(J(fake.MonthlyCopies(listed, from)), J(sql.MonthlyCopies(listed, from)));
        var tan = listed.Where(m => m.Serial.StartsWith("3100R", StringComparison.Ordinal)).ToList();
        Assert.Equal(J(fake.MonthlyCopies(tan, from)), J(sql.MonthlyCopies(tan, from)));
        Assert.Equal(5000L, sql.MonthlyCopies(tan, from).Single(m => m.Month == 9).BlackWhite);
        Assert.Empty(sql.MonthlyCopies(new[] { new MeterMachine("NOPE", "") }, from));
        // A company's copies per year, with how many of its machines were billed each year.
        var years = new DateTime(2024, 1, 1);
        Assert.Equal(J(fake.YearlyCopies(listed, years)), J(sql.YearlyCopies(listed, years)));
        Assert.Equal(J(fake.YearlyCopies(tan, years)), J(sql.YearlyCopies(tan, years)));
        Assert.Equal(new[] { 2025, 2026 }, sql.YearlyCopies(tan, years).Select(y => y.Year));
        Assert.Equal(2, sql.YearlyCopies(tan, years).Single(y => y.Year == 2026).Machines);
        Assert.Empty(sql.YearlyCopies(new[] { new MeterMachine("NOPE", "") }, years));
        Assert.Null(sql.Get("NOPE", "", today));
        Assert.Equal(41882L, sql.Services("3100R0001", "C1", "CT-1", from).Single().BlackWhite);
        Assert.Equal("Mei", sql.Get("3100R0002", "", today)!.Contact);
        Assert.Equal(2, sql.LastContract("3100R0003", "418283")!.Rates.Count);
        // The connection test finds every table and column by MyMachines' names; a wrong table name shows.
        settings.Connections.AdvanceTables.CustomerSupp = "dbo.daily_mif";
        var lines = databases.Test(ExternalNames.AdvanceConnector);
        Assert.Contains(lines, l => !l.Ok && l.Line.Contains("Business_reg"));
        Assert.All(lines.Where(l => !l.Line.StartsWith("dbo.daily_mif: found, but", StringComparison.Ordinal)), l => Assert.True(l.Ok, l.Line));
        settings.Connections.AdvanceTables.DailyTm = "dbo.NoSuchTable";
        Assert.Contains(databases.Test(ExternalNames.AdvanceConnector), l => !l.Ok && l.Line.Contains("NoSuchTable"));
    }

    [Fact]
    public void MyMachines_temporary_access_is_changed_only_when_switched_on_recorded_and_never_deleted()
    {
        if (string.IsNullOrEmpty(TestDatabase.Server)) return;
        using var world = new OrdersWorld();
        var fake = new FakeMyMachinesAccess(world.Data);
        fake.Add(OrdersWorld.Sam.Login, OrdersWorld.Sam.DisplayName, OrdersWorld.Sam.Email, "Acme Pte Ltd", "199912345K", new DateTime(2026, 10, 1, 9, 0, 0));
        // Taken away before: marked, not listed.
        fake.Add(OrdersWorld.Ken.Login, OrdersWorld.Ken.DisplayName, OrdersWorld.Ken.Email, "X:Zen Trading", "X:Z1", new DateTime(2026, 9, 1, 9, 0, 0));
        using var outSystems = new TestDatabase();
        var (app, _) = InOutSystems(world, outSystems);
        var sql = app.MachineAccess;
        Assert.IsType<SqlMyMachinesAccess>(sql);
        Assert.Equal(MyCRMRecords.Json.Serialize(fake.List()), MyCRMRecords.Json.Serialize(sql.List()));
        Assert.Equal(OrdersWorld.Sam.Login, sql.List().Single().Login);
        // Switched off: nothing is written.
        Assert.Throws<MachineAccessRefusedException>(() => sql.Give(OrdersWorld.Ken.Login, OrdersWorld.Ken.Email, "Zen Trading", "Z9000QRS", "Sara Admin"));
        Assert.Throws<MachineAccessRefusedException>(() => sql.Remove(sql.List().Single().Id, "Sara Admin"));
        Assert.Equal(2, Convert.ToInt32(outSystems.Scalar("SELECT COUNT(*) FROM dbo.[OSUSR_mm1_TEMPACCESS]")));
        Assert.Equal(0, world.Store.Count<MyCrmChange>());
        world.SettingsFile.Change(s => s.MyMachines.WritesEnabled = true);
        var id = sql.Give(OrdersWorld.Ken.Login, OrdersWorld.Ken.Email, "Zen Trading", "Z9000QRS", "Sara Admin");
        Assert.Contains(sql.List(), r => r.Id == id && r.Brn == "Z9000QRS" && r.CustomerName == "Zen Trading" && r.Login == OrdersWorld.Ken.Login);
        var added = world.Store.Find<MyCrmChange>(Query.Where("RowId", id));
        Assert.All(added, c => Assert.Equal(TempAccessRules.ChangeTable, c.TableName));
        Assert.Contains(added, c => c.Column == "BRN" && c.Before == "" && c.After == "Z9000QRS" && c.ChangedBy == "Sara Admin");
        Assert.Contains(added, c => c.Column == "USERID" && c.After.Length > 0);
        // Removed: the row stays, marked, with every value it replaced recorded.
        sql.Remove(id, "Sara Admin");
        Assert.DoesNotContain(sql.List(), r => r.Id == id);
        Assert.Equal(3, Convert.ToInt32(outSystems.Scalar("SELECT COUNT(*) FROM dbo.[OSUSR_mm1_TEMPACCESS]")));
        Assert.Equal("X:Z9000QRS", outSystems.Scalar($"SELECT BRN FROM dbo.[OSUSR_mm1_TEMPACCESS] WHERE ID = {id}"));
        Assert.Equal("X:Zen Trading", outSystems.Scalar($"SELECT CUSTOMERNAME FROM dbo.[OSUSR_mm1_TEMPACCESS] WHERE ID = {id}"));
        Assert.Contains(world.Store.Find<MyCrmChange>(Query.Where("RowId", id)), c => c.Column == "BRN" && c.Before == "Z9000QRS" && c.After == "X:Z9000QRS");
        Assert.Throws<MachineAccessRefusedException>(() => sql.Remove(id, "Sara Admin"));
        Assert.Throws<MachineAccessRefusedException>(() => sql.Give(@"RSP
obody", "", "Zen Trading", "Z9000QRS", "Sara Admin"));
    }

    /// <summary>MyOrders retired: its module and entities are inactive in the catalog, but OutSystems keeps the tables.</summary>
    [Fact]
    public void A_retired_MyOrders_module_is_still_read()
    {
        if (string.IsNullOrEmpty(TestDatabase.Server)) return;
        using var world = new OrdersWorld();
        var id = world.Sheet(world.SamId, world.Acme, "Invoiced", achieved: 5m);
        using var outSystems = new TestDatabase();
        outSystems.Execute(OutSystemsSql(world.Data));
        outSystems.Execute("UPDATE dbo.OSSYS_ESPACE SET IS_ACTIVE = 0 WHERE NAME = N'MyOrders'; UPDATE dbo.OSSYS_ENTITY SET IS_ACTIVE = 0 WHERE ESPACE_ID IN (1, 2);");
        world.SettingsFile.Change(s =>
        {
            s.Connections.OutSystems.Configured = true;
            s.Connections.OutSystems.SqlServer = outSystems.Settings;
        });
        var data = new SqlMyOrdersData(new ExternalDatabases(world.SettingsFile.Load, TestDatabase.Connect), () => world.SettingsFile.Load().Connections, "RSP");
        Assert.Equal(new[] { id }, data.Search(new CostingSheetSearch()).Rows.Select(r => r.Id));
    }
}
