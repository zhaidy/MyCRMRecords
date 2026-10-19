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
/// territory's list of companies (Company, BRN) uploaded as Excel workbooks.
/// </summary>
public sealed class FilesApi(CrmApp w)
{
    /// <summary>A sample workbook: sample?of=employees or sample?of=territory.</summary>
    public const string Sample = "sample";
    /// <summary>MyCRM's UploadNoOfEmployees: columns BRN and NoOfEmployees.</summary>
    public const string Employees = "employees";
    /// <summary>MyCRM's TerritoryUpdate import: columns Company and BRN; the companies found are shown, nothing is changed.</summary>
    public const string TerritoryList = "territory-list";

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
        return FileAnswer.Fail(404, "Not found.");
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
}
