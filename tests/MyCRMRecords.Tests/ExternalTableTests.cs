namespace MyCRMRecords.Tests;

/// <summary>Columns of the other databases found by the names the OutSystems apps gave them (Customer_Name → [Customer Name]).</summary>
public sealed class ExternalTableTests
{
    [Fact]
    public void A_column_is_found_by_its_name_or_by_its_letters_and_digits()
    {
        var columns = new[] { "OrderNo", "Customer Name", "Serial-no", "transferstatus.label", "Created On", "CreatedOn" };
        Assert.Equal("OrderNo", ExternalTable.Find(columns, "orderno"));
        Assert.Equal("Customer Name", ExternalTable.Find(columns, "Customer_Name"));
        Assert.Equal("Serial-no", ExternalTable.Find(columns, "Serial_no"));
        Assert.Equal("transferstatus.label", ExternalTable.Find(columns, "transferstatus__label"));
        // Two columns with the same letters: only the exact name is sure.
        Assert.Equal("CreatedOn", ExternalTable.Find(columns, "CreatedOn"));
        Assert.Null(ExternalTable.Find(columns, "Created_On"));
        Assert.Null(ExternalTable.Find(columns, "Lead_Number"));
    }

    [Theory]
    [InlineData("dbo.OWSProject", "[dbo].[OWSProject]")]
    [InlineData("Advance.dbo.public_holiday", "[Advance].[dbo].[public_holiday]")]
    [InlineData("dbo.[OWS_Customer.ContactPerson]", "[dbo].[OWS_Customer.ContactPerson]")]
    [InlineData("[dbo].[service-line]", "[dbo].[service-line]")]
    [InlineData("dbo.[odd]]name]", "[dbo].[odd]]name]")]
    [InlineData(" dbo . My Table ", "[dbo].[My Table]")]
    public void A_table_name_keeps_the_dots_written_in_brackets(string name, string sql) => Assert.Equal(sql, Sql.Name(name));

    [Fact]
    public void A_dotted_table_name_that_is_not_found_says_how_to_write_it()
    {
        Assert.EndsWith("dbo.[OWS_Customer.ContactPerson]", ExternalDatabases.DottedNameHint("OWS_Customer.ContactPerson"));
        Assert.EndsWith("dbo.[OWS_Customer.ContactPerson]", ExternalDatabases.DottedNameHint("dbo.OWS_Customer.ContactPerson"));
        Assert.Equal("", ExternalDatabases.DottedNameHint("dbo.[OWS_Customer.ContactPerson]"));
        Assert.Equal("", ExternalDatabases.DottedNameHint("OWSProject"));
    }

    [Fact]
    public void SQL_for_the_other_databases_names_columns_through_ExternalTable()
    {
        // [Customer_Name] in SQL for AdvanceConnector is the OutSystems name, which may not be
        // the column's: write t["Customer_Name"] (ExternalTable.Of) instead. Aliases (AS [Customer_Name]) are fine.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "MyCRMRecords.Core"))) root = root.Parent;
        Assert.NotNull(root);
        var typed = new System.Text.RegularExpressions.Regex(@"(?<!AS )\[[A-Za-z0-9]*[a-z][A-Za-z0-9]*_[A-Za-z0-9_]*\]");
        var found = Directory.GetFiles(Path.Combine(root!.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => f.IndexOf(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal) < 0)
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(f), @"ExternalNames\.(DataLake|Rspsis|AdvanceConnector)"))
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (File: Path.GetFileName(f), Line: i + 1, Text: line)))
            .Where(l => typed.IsMatch(l.Text))
            .Select(l => $"{l.File}:{l.Line}: {l.Text.Trim()}").ToList();
        Assert.True(found.Count == 0, "Write these columns as table[\"Name\"] (ExternalTable):\n" + string.Join("\n", found));
    }

    [Fact]
    public void A_value_is_read_by_its_column_name_or_its_OutSystems_name()
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Customer Name"] = "Tan", ["Serial-no"] = "E1", ["Ship_Date"] = null, ["shipdate"] = "2026-10-17",
        };
        object? Value(string column) => ExternalTable.TryGetValue(row, column, out var value) ? value : "(none)";
        Assert.Equal("Tan", Value("Customer_Name"));
        Assert.Equal("E1", Value("SERIAL_NO"));
        // The exact name first: a view with both Ship_Date and shipdate gives each its own value.
        Assert.Equal("2026-10-17", Value("shipdate"));
        Assert.Null(Value("Ship_Date"));
        Assert.Equal("(none)", Value("ShipDate_"));
        Assert.Equal("(none)", Value("Sales_Person"));
    }

    [Fact]
    public void MyCRM_tables_in_OutSystems_and_AdvanceConnector_pass_the_connection_test()
    {
        if (string.IsNullOrEmpty(TestDatabase.Server)) return;
        using var outSystems = new TestDatabase();
        outSystems.Execute(FakeOldTables.MyCRM().OutSystemsSql("MyCRM"));
        using var advance = new TestDatabase();
        advance.Execute(FakeOldTables.AdvanceSql());
        var settings = new CrmSettings();
        settings.Connections.OutSystems = new ExternalDatabase { Configured = true, SqlServer = outSystems.Settings };
        settings.Connections.AdvanceConnector = new ExternalDatabase { Configured = true, SqlServer = advance.Settings };
        var databases = new ExternalDatabases(() => settings, TestDatabase.Connect);
        var test = databases.Test(ExternalNames.OutSystems);
        Assert.All(test, l => Assert.True(l.Ok, l.Line));
        // Connected, each MyCRM entity (the static ones too), and the users.
        Assert.Equal(OldTableColumns.Checked.Count + 2, test.Count);
        // AdvanceConnector's holidays are found under MyCRM's names ([Date] for date).
        var adv = databases.Test(ExternalNames.AdvanceConnector);
        Assert.All(adv, l => Assert.True(l.Ok, l.Line));
        Assert.Equal(2, adv.Count);
        // A wrong table name shows.
        settings.Connections.AdvanceTables.PublicHolidays = "dbo.NoSuchTable";
        Assert.Contains(databases.Test(ExternalNames.AdvanceConnector), l => !l.Ok && l.Line.Contains("NoSuchTable"));
    }
}
