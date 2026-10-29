using MyCRMRecords.Data;
using System.Globalization;
using MyCRMRecords.Documents;
using MyCRMRecords.Domain;

namespace MyCRMRecords.Api;

/// <summary>A file to send: its name, type and bytes; or why it cannot be sent.</summary>
public sealed record FileAnswer(int Status, string FileName, string ContentType, byte[] Content, string Error = "")
{
    public const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static FileAnswer Fail(int status, string error) => new(status, "", "", Array.Empty<byte>(), error);
}

/// <summary>
/// The files of file.ashx: the Excel samples to fill in, downloaded; the number of employees (BRN, NoOfEmployees) and a
/// territory's list of companies (Company, BRN) uploaded as Excel workbooks; and, read only from MyOrders, a costing
/// sheet's or request form's attachment and the costing sheets and request forms lists as Excel workbooks.
/// </summary>
public sealed class FilesApi(CrmApp w)
{
    /// <summary>A sample workbook: sample?of=employees or sample?of=territory.</summary>
    public const string Sample = "sample";
    /// <summary>MyCRM's UploadNoOfEmployees: columns BRN and NoOfEmployees.</summary>
    public const string Employees = "employees";
    /// <summary>MyCRM's TerritoryUpdate import: columns Company and BRN; the companies found are shown, nothing is changed.</summary>
    public const string TerritoryList = "territory-list";

    /// <summary>attachment?costingSheet=&lt;id&gt;&amp;id=&lt;attachment id&gt;</summary>
    public const string Attachment = "attachment";
    /// <summary>costingsheets?&lt;the list's filters&gt;: every costing sheet that matches (up to <see cref="MaxRows"/>).</summary>
    public const string List = "costingsheets";
    public const int MaxRows = 20000;
    /// <summary>requestform-attachment?requestForm=&lt;id&gt;&amp;id=&lt;attachment id&gt;</summary>
    public const string RequestFormAttachment = "requestform-attachment";
    /// <summary>requestforms?&lt;the list's filters&gt;: MyOrders' Download, a row per request form and delivery or collection date.</summary>
    public const string RequestFormList = "requestforms";

    private static string Q(IReadOnlyDictionary<string, string> query, string name) => query.TryGetValue(name, out var v) ? v ?? "" : "";

    private static long Id(IReadOnlyDictionary<string, string> query, string name) =>
        long.TryParse(Q(query, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;

    public FileAnswer Download(string kind, IReadOnlyDictionary<string, string> query, ApiUser user)
    {
        switch (kind)
        {
            case Sample when Q(query, "of") == Employees:
                return new FileAnswer(200, "NoOfEmployees.xlsx", FileAnswer.XlsxType,
                    Xlsx.Create("Sheet1", [new Xlsx.ColumnSpec("BRN", 20), new Xlsx.ColumnSpec("NoOfEmployees", 16, "number")], [new object?[] { "199912345K", 25 }]));
            case Sample when Q(query, "of") == "territory":
                return new FileAnswer(200, "Company.xlsx", FileAnswer.XlsxType,
                    Xlsx.Create("Sheet1", [new Xlsx.ColumnSpec("Company", 40), new Xlsx.ColumnSpec("BRN", 20)], [new object?[] { "Sample Holdings Pte Ltd", "199912345K" }]));
        }
        return Orders(kind, query, user) ?? FileAnswer.Fail(404, "Not found.");
    }

    /// <summary>
    /// The costing sheets' and request forms' files, read only from MyOrders as the signed-in person: an attachment, or a
    /// list as an Excel workbook (MyOrders' Download); null for any other kind.
    /// </summary>
    private FileAnswer? Orders(string kind, IReadOnlyDictionary<string, string> query, ApiUser user)
    {
        if (kind is not (Attachment or List or RequestFormAttachment or RequestFormList)) return null;
        if (!w.OrdersData.Available) return FileAnswer.Fail(503, "MyOrders' tables cannot be read yet: an administrator has to set up the connection first.");
        var actor = user.ToActor(w.People, w.OrdersAccess);
        switch (kind)
        {
            case Attachment:
            {
                var file = w.CostingSheets.Attachment(actor, Id(query, "costingSheet"), Id(query, "id"));
                return file is { } found
                    ? new FileAnswer(200, found.FileName, ContentType(found.FileName), found.Content)
                    : FileAnswer.Fail(404, "There is no such attachment, or it is not on a costing sheet you may see.");
            }
            case List:
            {
                var search = OrdersApi.Search(query);
                search.Skip = 0;
                search.Take = MaxRows;
                var page = w.CostingSheets.List(actor, search);
                var columns = new List<Xlsx.ColumnSpec>
                {
                    new("Id", 10, "number"), new("Salesman", 26), new("Reference", 18), new("Company", 40), new("Contact", 26), new("Created At", 18),
                    new("Updated At", 18), new("Printed At", 18), new("Head Approved At", 18), new("Invoice Date", 14), new("Achieved Price", 16, "money"),
                    new("Costing Sheet Status", 24), new("Invoice No", 18), new("Order No", 18), new("OPO Checked", 12),
                };
                var rows = page.Rows.Select(r => new object?[]
                {
                    (decimal)r.Id, r.Salesman, r.Reference, r.Company, r.Contact, r.CreatedAt, r.UpdatedAt, r.PrintedAt, r.HeadApprovedAt, r.MinInvoiceDate,
                    r.TotalAchievedPrice, r.Status, r.InvoiceNo, r.OrderNo, r.IsOpoChecked ? "Yes" : "No",
                });
                return new FileAnswer(200, $"CostingSheets-{w.Clock():yyyyMMdd-HHmm}.xlsx", FileAnswer.XlsxType, Xlsx.Create("Costing Sheets", columns, rows));
            }
            case RequestFormAttachment:
            {
                var file = w.RequestForms.Attachment(actor, Id(query, "requestForm"), Id(query, "id"));
                return file is { } found
                    ? new FileAnswer(200, found.FileName, ContentType(found.FileName), found.Content)
                    : FileAnswer.Fail(404, "There is no such attachment, or it is not on a request form you may see.");
            }
            case RequestFormList:
            {
                var search = OrdersApi.RequestFormQuery(query);
                search.Skip = 0;
                search.Take = MaxRows;
                var page = w.RequestForms.List(actor, search);
                // MyOrders' Download: its eight columns, a row for each of a request form's delivery or collection dates.
                var columns = new List<Xlsx.ColumnSpec>
                {
                    new("Request Form Id", 14, "number"), new("Type", 16), new("Status", 24), new("Company", 40), new("Delv / Collection Date", 18),
                    new("Salesperson", 26), new("Costing Sheet Id", 14, "number"), new("Created On", 18),
                };
                var rows = page.Rows.SelectMany(r => (r.Dates.Count == 0 ? new DateTime?[] { null } : r.Dates.Select(d => (DateTime?)d))
                    .Select(d => new object?[]
                    {
                        (decimal)r.Id, r.Type, r.Status, r.Company, d, r.Salesman, r.CostingSheetId is { } cs ? (decimal?)cs : null, r.CreatedAt,
                    }));
                return new FileAnswer(200, $"RequestForms-{w.Clock():yyyyMMdd-HHmm}.xlsx", FileAnswer.XlsxType, Xlsx.Create("Request Forms", columns, rows));
            }
        }
        return null;
    }

    /// <summary>A file uploaded to file.ashx: employees, or territory-list?territory=.</summary>
    public ApiResponse Upload(string kind, IReadOnlyDictionary<string, string> query, string fileName, string contentType, byte[] content, ApiUser user)
    {
        var actor = user.ToActor(w.People);
        switch (kind)
        {
            case Employees:
            {
                var rows = Rows(content);
                if (rows is null) return ApiResponse.Fail(400, ResultCodes.Validation, "Choose an Excel workbook (.xlsx) with the columns BRN and NoOfEmployees.");
                using var author = MyCrmStore.As(actor.ChangedBy);
                try
                {
                    return ApiResponse.Result(w.Companies.UploadEmployees(actor, rows.Select(r => (Value(r, "BRN"), Value(r, "NoOfEmployees"))).ToList()));
                }
                catch (MyCrmRefusedException ex)
                {
                    return ApiResponse.Fail(409, ResultCodes.Conflict, ex.Message);
                }
            }
            case TerritoryList:
            {
                if (!actor.CanManageTerritories) return ApiResponse.Forbidden("Territories are kept by Sales Admins.");
                var rows = Rows(content);
                if (rows is null) return ApiResponse.Fail(400, ResultCodes.Validation, "Choose an Excel workbook (.xlsx) with the columns Company and BRN.");
                var text = string.Join("\n", rows.Select(r => string.Join("\t", new[] { Value(r, "Company"), Value(r, "BRN") }.Where(v => v.Length > 0))));
                return ApiResponse.Of(new { matches = w.Territories.MatchList(Id(query, "territory"), text) });
            }
        }
        return ApiResponse.NotFound();
    }

    /// <summary>The rows of the first sheet by heading; null when the file is not an Excel workbook.</summary>
    private static List<Dictionary<string, string>>? Rows(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content);
            return Xlsx.Read(stream);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or System.Xml.XmlException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>A row's value under a heading (spaces, "_" and case ignored), "" when none.</summary>
    private static string Value(Dictionary<string, string> row, string heading)
    {
        string Key(string text) => new string(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var wanted = Key(heading);
        return (row.FirstOrDefault(p => Key(p.Key) == wanted).Value ?? "").Trim();
    }

    /// <summary>The values of the column headed <paramref name="heading"/> (spaces, "_" and case ignored); null when the file is not an Excel workbook.</summary>
    public static IReadOnlyList<string>? Column(byte[] content, string heading)
    {
        List<Dictionary<string, string>> rows;
        try
        {
            using var stream = new MemoryStream(content);
            rows = Xlsx.Read(stream);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or System.Xml.XmlException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
        string Key(string text) => new string(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var wanted = Key(heading);
        return rows.Select(r => r.FirstOrDefault(p => Key(p.Key) == wanted).Value ?? "").Select(v => v.Trim()).Where(v => v.Length > 0).ToList();
    }

    /// <summary>The content type of a file by its extension (the browser opens a PDF or a picture; anything else is saved).</summary>
    public static string ContentType(string fileName) => Path.GetExtension(fileName ?? "").ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".xlsx" => FileAnswer.XlsxType,
        ".xls" => "application/vnd.ms-excel",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".doc" => "application/msword",
        ".msg" => "application/vnd.ms-outlook",
        ".txt" => "text/plain",
        _ => "application/octet-stream",
    };
}
