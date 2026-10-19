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
/// The files of file.ashx: a feedback's attachment and the statistics report (Excel), downloaded; a feedback's
/// attachment uploaded.
/// </summary>
public sealed class FilesApi(CrmApp w)
{
    public const string Attachment = "attachment";
    public const string Report = "report";

    private static string Q(IReadOnlyDictionary<string, string> query, string name) => query.TryGetValue(name, out var v) ? v ?? "" : "";

    private static long Id(IReadOnlyDictionary<string, string> query, string name) =>
        long.TryParse(Q(query, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;

    /// <summary>The type to send a file with, from its name (MyCRM kept none).</summary>
    public static string ContentTypeOf(string fileName) => Path.GetExtension(fileName ?? "").ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".txt" => "text/plain",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xls" => "application/vnd.ms-excel",
        ".xlsx" => FileAnswer.XlsxType,
        ".msg" => "application/vnd.ms-outlook",
        _ => "application/octet-stream",
    };

    public FileAnswer Download(string kind, IReadOnlyDictionary<string, string> query, ApiUser user)
    {
        try
        {
            switch (kind)
            {
                case Attachment:
                {
                    if (w.Feedbacks.File(Id(query, "id")) is not { } file) return FileAnswer.Fail(404, "That file is not there.");
                    return new FileAnswer(200, file.FileName, ContentTypeOf(file.FileName), file.Content);
                }
                case Report:
                {
                    var months = CrmApi.Months(Q(query, "months"));
                    if (months.Count == 0) return FileAnswer.Fail(400, "Please select month");
                    var (name, bytes) = w.Statistics.ReportFile(months);
                    return new FileAnswer(200, name, FileAnswer.XlsxType, bytes);
                }
            }
            return FileAnswer.Fail(404, "Not found.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Data.Common.DbException or TimeoutException)
        {
            return FileAnswer.Fail(503, ex.Message);
        }
    }

    /// <summary>A file uploaded to file.ashx: a feedback's attachment (attachment?feedback=&amp;kind=CD or CS).</summary>
    public ApiResponse Upload(string kind, IReadOnlyDictionary<string, string> query, string fileName, string contentType, byte[] content, ApiUser user)
    {
        var actor = user.ToActor(w.People);
        return kind switch
        {
            Attachment => ApiResponse.Result(w.Feedbacks.Attach(actor, Id(query, "feedback"), Q(query, "kind"), fileName, content)),
            _ => ApiResponse.NotFound(),
        };
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
