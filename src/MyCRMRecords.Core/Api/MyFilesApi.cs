using System.Globalization;
using MyCRMRecords.Data;
using MyCRMRecords.Domain;

namespace MyCRMRecords.Api;

/// <summary>
/// The Files page's API (api.ashx/files...), as the signed-in person: MyFiles' files they may see, searched and paged,
/// and a company's (files?company=). Read only: MyCRMRecords changes nothing in MyFiles. A file's PDF is file.ashx/files/{id}
/// (<see cref="FilesApi"/>).
/// </summary>
public sealed class MyFilesApi(CrmApp w)
{
    public static bool Handles(string path) => path.Trim('/').Split('/')[0] == "files";

    public ApiResponse Handle(string method, string path, string body, IReadOnlyDictionary<string, string> query, ApiUser user)
    {
        var parts = path.Trim('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (method != "GET" || parts.Length != 1) return ApiResponse.NotFound();
        var actor = user.ToActor(w.People);
        if (!w.MyFilesData.Available)
            return ApiResponse.Fail(503, "NOT_CONNECTED", actor.IsAdmin
                ? "MyFiles' tables cannot be read yet: set up the OutSystems database on the admin page (Settings, Connections tab)."
                : "The files cannot be read now: an administrator has to set up the connection to MyFiles first.");
        try
        {
            var search = Search(query);
            if (search.CompanyId is { } company && !w.MyFiles.MayOpenCompany(actor, company))
                return ApiResponse.Forbidden("You don't have permissions to view this company's files: it is not in your or your team's territories.");
            var choices = w.MyFiles.TerritoryChoices(actor);
            // A territory: one of the person's choices (another lists nothing).
            long? territory = long.TryParse(query.TryGetValue("territory", out var chosen) ? chosen : "", NumberStyles.Integer, CultureInfo.InvariantCulture, out var tid) && tid > 0
                ? (choices.Any(c => c.Id == tid) ? tid : -1) : null;
            var page = w.MyFiles.List(actor, search, territory);
            return ApiResponse.Of(new
            {
                total = page.Total, skip = search.Skip, take = search.Take, rows = page.Rows,
                types = MyFilesTypes.All.Select(t => new { label = t.Label, name = t.Name }),
                territories = choices.Select(c => new { c.Id, title = Territories.Title(c) }),
                limited = MyFiles.Limited(actor),
            });
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or TimeoutException or InvalidOperationException)
        {
            return ApiResponse.Fail(503, "UNAVAILABLE", "MyFiles' tables could not be read: " + ex.Message);
        }
    }

    /// <summary>The list's search from the address: q, type (a FileType label or its plain words), company, since (yyyy-MM-dd), skip.</summary>
    public static FileSearch Search(IReadOnlyDictionary<string, string> query)
    {
        string Q(string name) => query.TryGetValue(name, out var v) ? (v ?? "").Trim() : "";
        return new FileSearch
        {
            Keyword = Strings.Shorten(Q("q"), 200),
            FileType = MyFilesTypes.LabelOf(Q("type")),
            CompanyId = long.TryParse(Q("company"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var company) && company > 0 ? company : null,
            UploadedFrom = DateTime.TryParseExact(Q("since"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var since) ? since : null,
            Skip = int.TryParse(Q("skip"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var skip) ? Math.Max(0, skip) : 0,
            Take = MyFiles.PageSize,
        };
    }
}
