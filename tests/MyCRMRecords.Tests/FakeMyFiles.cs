namespace MyCRMRecords.Tests;

/// <summary>
/// MyFiles' tables in memory (in a <see cref="FakeMyOrders"/>, the OutSystems database of the tests), read the way
/// <see cref="SqlMyFilesData"/> reads them: the search worked out the same way, a file's size its PDF's length. The SQL
/// Server tests check the real one answers as this one does. Its FileType rows have Ids no list starts with (71, 74…).
/// </summary>
public sealed class FakeMyFiles(FakeMyOrders? tables = null) : IMyFilesData
{
    public FakeMyOrders Tables { get; } = tables ?? new FakeMyOrders();
    public bool Available { get; set; } = true;
    /// <summary>MyFiles' FileType: label → Id.</summary>
    public Dictionary<string, long> Type { get; } = new();

    private IEnumerable<Dictionary<string, object?>> Rows(OsEntity entity) =>
        Tables.Tables.TryGetValue(entity, out var rows) ? rows : Enumerable.Empty<Dictionary<string, object?>>();

    /// <summary>The FileType of a label, added the first time.</summary>
    public long TypeId(string label)
    {
        if (!Type.TryGetValue(label, out var id)) Type[label] = id = Tables.Add(Entities.FileType, new { ID = 71L + 3 * Type.Count, LABEL = label });
        return id;
    }

    /// <summary>Adds a file of MyFiles (active unless said) with its PDF, returning its Id.</summary>
    public long Add(string fileName, string type = "Others", long? company = null, string companyName = "", string brn = "", string invoice = "",
        string contract = "", string service = "", DateTime? uploaded = null, long? by = null, byte[]? pdf = null, bool active = true, string doNumber = "") =>
        Tables.Add(Entities.File, new
        {
            FILENAME = fileName, INVOICENUMBER = invoice, DONUMBER = doNumber, REFERENCENUMBER = "", PPSNUMBER = "", CONTRACTNUMBER = contract,
            SERVICENUMBER = service, RMANUMBER = "", PSDREFNUMBER = "", MSDREFNUMBER = "", COMPANYID = company, COMPANYNAME = companyName, BRN = brn,
            CREATEDBY = by, CREATEDAT = uploaded ?? new DateTime(2026, 10, 1, 9, 0, 0), ISACTIVE = active,
            FILEBINARY = pdf ?? System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 " + fileName), FILETYPEID = TypeId(type),
        });

    private Dictionary<long, string> Types() => Rows(Entities.FileType).ToDictionary(r => r.Long("ID"), r => r.Text("LABEL"));

    /// <summary>A row as the SQL implementation reads it: the PDF as its size, the uploader's name.</summary>
    private Dictionary<string, object?> AsRead(Dictionary<string, object?> row)
    {
        var read = new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase)
        {
            ["SIZE"] = row.Get("FILEBINARY") is byte[] bytes ? (long)bytes.Length : 0L,
            ["UPLOADEDBY"] = row.Id("CREATEDBY") is { } by ? Tables.UserRows.FirstOrDefault(u => u.Id == by)?.Name : null,
        };
        read.Remove("FILEBINARY");
        return read;
    }

    public FilePage Search(FileSearch s)
    {
        var types = Types();
        bool Has(Dictionary<string, object?> r, string column, string text) => r.Text(column).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        var keyword = (s.Keyword ?? "").Trim();
        var label = MyFilesTypes.Key(s.FileType);
        var matching = Rows(Entities.File).Where(r =>
                r.Bool("ISACTIVE")
                && (keyword.Length == 0 || SqlMyFilesData.SearchColumns.Any(c => Has(r, c, keyword)))
                && (label.Length == 0 || (r.Id("FILETYPEID") is { } t && types.TryGetValue(t, out var l) && MyFilesTypes.Key(l) == label))
                && (s.CompanyId is null || r.Id("COMPANYID") == s.CompanyId)
                && (s.UploadedFrom is null || (r.Date("CREATEDAT") is { } at && at >= s.UploadedFrom.Value.Date))
                && (s.Companies is null || (r.Id("COMPANYID") is not { } c2 ? s.NoCompanyToo : s.Companies.Contains(c2))))
            .OrderByDescending(r => r.Date("CREATEDAT")).ThenByDescending(r => r.Long("ID")).ToList();
        return new FilePage(matching.Count, matching.Skip(Math.Max(0, s.Skip)).Take(Math.Max(1, Math.Min(s.Take, 500))).Select(r => SqlMyFilesData.Row(AsRead(r), types)).ToList());
    }

    public FileRow? Get(long id) => Rows(Entities.File).FirstOrDefault(r => r.Long("ID") == id) is { } row ? SqlMyFilesData.Row(AsRead(row), Types()) : null;

    public byte[]? Bytes(long id) => Rows(Entities.File).FirstOrDefault(r => r.Long("ID") == id)?.Get("FILEBINARY") is byte[] { Length: > 0 } bytes ? bytes : null;
}
