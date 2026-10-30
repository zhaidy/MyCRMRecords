using System.Data.Common;
using System.Globalization;
using System.Text;

namespace MyCRMRecords.Data;

/// <summary>
/// MyFiles' file types (its static entity FileType), in the order the Files page offers them, with the plain words the
/// page shows. Matched by label (spaces, punctuation and case ignored), never by Id: the Ids are the database's.
/// </summary>
public static class MyFilesTypes
{
    public static readonly IReadOnlyList<(string Label, string Name)> All =
    [
        ("CostingSheet", "Costing sheet"),
        ("RequestForm", "Request form"),
        ("FSA", "FSA"),
        ("Leasing", "Leasing"),
        ("DO", "DO"),
        ("Others", "Others"),
    ];

    public static string Key(string? label) => new string((label ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    /// <summary>MyFiles' label of a type chosen on the page ("costing sheet" or "CostingSheet"), or "" when there is none.</summary>
    public static string LabelOf(string? text)
    {
        var key = Key(text);
        if (key.Length == 0) return "";
        foreach (var (label, name) in All)
            if (Key(label) == key || Key(name) == key) return label;
        return "";
    }

    /// <summary>The words the page shows for a label ("CostingSheet" → "Costing sheet"); an unknown label as it is.</summary>
    public static string NameOf(string? label)
    {
        var key = Key(label);
        foreach (var (l, name) in All)
            if (Key(l) == key) return name;
        return (label ?? "").Trim();
    }
}

/// <summary>What the Files page asks for: its search and filters, whose files, a page.</summary>
public sealed class FileSearch
{
    /// <summary>Part of the file's name, one of its numbers, its company's name or BRN.</summary>
    public string Keyword { get; set; } = "";
    /// <summary>MyFiles' FileType label (<see cref="MyFilesTypes"/>); "" for every type.</summary>
    public string FileType { get; set; } = "";
    /// <summary>One MyCRM company's files (a company's page links to them); null for any.</summary>
    public long? CompanyId { get; set; }
    /// <summary>Uploaded on or after this day; null for any time.</summary>
    public DateTime? UploadedFrom { get; set; }

    /// <summary>
    /// The MyCRM companies whose files the person sees, with the files of no company; null for every file. Set by the
    /// rules (<see cref="Domain.MyFiles"/>), never by the page.
    /// </summary>
    public IReadOnlyCollection<long>? Companies { get; set; }

    public int Skip { get; set; }
    public int Take { get; set; } = 50;
}

/// <summary>One of a file's numbers: its kind (Invoice, DO, Contract…) and the number as MyFiles keeps it.</summary>
public sealed record FileNumber(string Kind, string Number);

/// <summary>A file of MyFiles as the Files page shows it: never its bytes, only their size.</summary>
public sealed record FileRow
{
    public long Id { get; init; }
    public string FileName { get; init; } = "";
    /// <summary>MyFiles' FileType label (CostingSheet…), "" when none.</summary>
    public string TypeLabel { get; init; } = "";
    /// <summary>The type in plain words (Costing sheet…).</summary>
    public string Type { get; init; } = "";
    /// <summary>The MyCRM company (MyCRM's Company Id), null when the file has none.</summary>
    public long? CompanyId { get; init; }
    /// <summary>The company's name and BRN as MyFiles copied them on the file when it was uploaded.</summary>
    public string Company { get; init; } = "";
    public string Brn { get; init; } = "";
    /// <summary>The numbers the file has, in MyFiles' order (invoice first).</summary>
    public IReadOnlyList<FileNumber> Numbers { get; init; } = Array.Empty<FileNumber>();
    public DateTime? UploadedAt { get; init; }
    /// <summary>The OutSystems user who uploaded it, by name; "" when unknown.</summary>
    public string UploadedBy { get; init; } = "";
    /// <summary>The PDF's size in bytes (DATALENGTH: the file is not read).</summary>
    public long Size { get; init; }
    public bool IsActive { get; init; }
}

public sealed record FilePage(int Total, IReadOnlyList<FileRow> Rows);

/// <summary>
/// MyFiles' files, read only where MyFile keeps them (MyFiles' tables in the OutSystems database): MyCRMRecords never
/// writes to them (there is no method that could). A list reads each file's size, never its bytes, which only
/// <see cref="Bytes"/> reads, for one file's download.
/// </summary>
public interface IMyFilesData
{
    /// <summary>Whether the OutSystems database is set up on the Connections tab.</summary>
    bool Available { get; }
    /// <summary>A page of active files matching the search, newest first, and how many match.</summary>
    FilePage Search(FileSearch search);
    /// <summary>A file (active or not), or null.</summary>
    FileRow? Get(long id);
    /// <summary>The PDF of a file, or null when the file or its PDF is not there.</summary>
    byte[]? Bytes(long id);
}

/// <summary>
/// <see cref="IMyFilesData"/> over MyFiles' tables in the OutSystems database (the <c>MyFiles module</c> setting), as
/// MyFile reads them: the entity File (a row per PDF, FILEBINARY its bytes) and the static FileType. Their columns are
/// the attribute names.
/// </summary>
public sealed class SqlMyFilesData(ExternalDatabases databases, Func<ConnectionSettings> connections) : IMyFilesData
{
    /// <summary>A file's number columns, in MyFiles' order, with the kind the page names them by.</summary>
    public static readonly IReadOnlyList<(string Column, string Kind)> NumberColumns =
    [
        ("INVOICENUMBER", "Invoice"), ("DONUMBER", "DO"), ("CONTRACTNUMBER", "Contract"), ("SERVICENUMBER", "Service"),
        ("REFERENCENUMBER", "Reference"), ("PPSNUMBER", "PPS"), ("RMANUMBER", "RMA"), ("PSDREFNUMBER", "PSD ref"), ("MSDREFNUMBER", "MSD ref"),
    ];

    /// <summary>The columns the search looks in: the file's name, its company's name and BRN, and every number.</summary>
    public static readonly IReadOnlyList<string> SearchColumns = new[] { "FILENAME", "COMPANYNAME", "BRN" }.Concat(NumberColumns.Select(n => n.Column)).ToList();

    public bool Available => databases.IsConfigured(ExternalNames.OutSystems);

    private string TableOf(DbConnection connection, OsEntity entity) =>
        databases.EntityTable(connection, Entities.ModuleName(connections(), entity.Module), entity.Name, Entities.ReadOnly(entity.Module));

    private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>MyFiles' FileType: the database's Id → label.</summary>
    private Dictionary<long, string> Types(DbConnection connection) =>
        Sql.Rows(connection, null, $"SELECT [ID], [LABEL] FROM {TableOf(connection, Entities.FileType)}")
            .ToDictionary(r => r.Long("ID"), r => r.Text("LABEL"));

    /// <summary>What a row is read with: the file's columns, its size (DATALENGTH, which does not read the file) and the uploader's name.</summary>
    private static string Select() =>
        "f.[ID], f.[FILENAME], " + string.Join(", ", NumberColumns.Select(n => $"f.[{n.Column}]")) +
        ", f.[COMPANYID], f.[COMPANYNAME], f.[BRN], f.[CREATEDAT], f.[ISACTIVE], f.[FILETYPEID], " +
        "CAST(ISNULL(DATALENGTH(f.[FILEBINARY]), 0) AS bigint) AS [SIZE], u.[NAME] AS [UPLOADEDBY]";

    private string From(DbConnection connection) => $" FROM {TableOf(connection, Entities.File)} f LEFT JOIN dbo.OSSYS_USER u ON u.[ID] = f.[CREATEDBY]";

    public FilePage Search(FileSearch search) => databases.Use(ExternalNames.OutSystems, connection =>
    {
        var types = Types(connection);
        var where = new List<string> { "f.[ISACTIVE] = 1" };
        var parameters = new List<(string, object?)>();
        string P(object value)
        {
            var name = "@p" + parameters.Count.ToString(CultureInfo.InvariantCulture);
            parameters.Add((name, value));
            return name;
        }
        var keyword = (search.Keyword ?? "").Trim();
        if (keyword.Length > 0)
        {
            var like = P("%" + Sql.EscapeLike(keyword) + "%");
            where.Add("(" + string.Join(" OR ", SearchColumns.Select(c => $"f.[{c}] LIKE {like} ESCAPE '\\'")) + ")");
        }
        var label = MyFilesTypes.Key(search.FileType);
        if (label.Length > 0)
        {
            var ids = types.Where(t => MyFilesTypes.Key(t.Value) == label).Select(t => N(t.Key)).ToList();
            where.Add(ids.Count == 0 ? "1 = 0" : $"f.[FILETYPEID] IN ({string.Join(", ", ids)})");
        }
        if (search.CompanyId is { } company) where.Add($"f.[COMPANYID] = {P(company)}");
        if (search.UploadedFrom is { } from) where.Add($"f.[CREATEDAT] >= {P(from.Date)}");
        if (search.Companies is { } companies)
        {
            // The person's companies, and the files of no company. The companies go as one XML text (a list can hold tens of
            // thousands, more than parameters or a statement's IN list should).
            var xml = new StringBuilder();
            foreach (var id in companies.Where(c => c > 0).Distinct()) xml.Append("<c>").Append(N(id)).Append("</c>");
            where.Add(xml.Length == 0
                ? "(f.[COMPANYID] IS NULL OR f.[COMPANYID] = 0)"
                : $"(f.[COMPANYID] IS NULL OR f.[COMPANYID] = 0 OR f.[COMPANYID] IN (SELECT n.c.value('.', 'bigint') FROM (SELECT CAST({P(xml.ToString())} AS xml) AS x) s CROSS APPLY s.x.nodes('/c') AS n(c)))");
        }
        var whereSql = " WHERE " + string.Join(" AND ", where);
        var args = parameters.ToArray();
        var total = Convert.ToInt32(Sql.Rows(connection, null, $"SELECT COUNT(*) AS [N] FROM {TableOf(connection, Entities.File)} f" + whereSql, args)[0]["N"],
            CultureInfo.InvariantCulture);
        var take = Math.Max(1, Math.Min(search.Take, 500));
        var rows = Sql.Rows(connection, null,
            $"SELECT {Select()}{From(connection)}{whereSql} ORDER BY f.[CREATEDAT] DESC, f.[ID] DESC " +
            $"OFFSET {N(Math.Max(0, search.Skip))} ROWS FETCH NEXT {N(take)} ROWS ONLY", args);
        return new FilePage(total, rows.Select(r => Row(r, types)).ToList());
    });

    public FileRow? Get(long id) => databases.Use(ExternalNames.OutSystems, connection =>
    {
        var rows = Sql.Rows(connection, null, $"SELECT {Select()}{From(connection)} WHERE f.[ID] = @id", ("@id", id));
        return rows.Count == 0 ? null : Row(rows[0], Types(connection));
    });

    public byte[]? Bytes(long id) => databases.Use(ExternalNames.OutSystems, connection =>
    {
        var rows = Sql.Rows(connection, null, $"SELECT [FILEBINARY] AS [FILE] FROM {TableOf(connection, Entities.File)} WHERE [ID] = @id", ("@id", id));
        return rows.Count == 1 && rows[0]["FILE"] is byte[] bytes && bytes.Length > 0 ? bytes : null;
    });

    /// <summary>A row of the query (or of the fake's join) as the page shows it; <paramref name="types"/> is FileType's Id → label.</summary>
    public static FileRow Row(IReadOnlyDictionary<string, object?> r, IReadOnlyDictionary<long, string> types)
    {
        var label = r.Id("FILETYPEID") is { } type && types.TryGetValue(type, out var found) ? found : "";
        return new FileRow
        {
            Id = r.Long("ID"),
            FileName = r.Text("FILENAME"),
            TypeLabel = label,
            Type = MyFilesTypes.NameOf(label),
            CompanyId = r.Id("COMPANYID"),
            Company = r.Text("COMPANYNAME"),
            Brn = r.Text("BRN"),
            Numbers = NumberColumns.Select(n => new FileNumber(n.Kind, r.Text(n.Column))).Where(n => n.Number.Length > 0).ToList(),
            UploadedAt = r.Date("CREATEDAT"),
            UploadedBy = r.Text("UPLOADEDBY"),
            Size = r.Long("SIZE"),
            IsActive = r.Bool("ISACTIVE"),
        };
    }
}
