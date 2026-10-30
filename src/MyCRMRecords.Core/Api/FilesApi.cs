using MyCRMRecords.Data;
using System.Globalization;
using MyCRMRecords.Documents;
using MyCRMRecords.Domain;

namespace MyCRMRecords.Api;

/// <summary>A file to send: its name, type and bytes, and whether the browser shows it rather than saves it; or why it cannot be sent.</summary>
public sealed record FileAnswer(int Status, string FileName, string ContentType, byte[] Content, string Error = "", bool Inline = false)
{
    public const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static FileAnswer Fail(int status, string error) => new(status, "", "", Array.Empty<byte>(), error);
}

/// <summary>
/// The files of file.ashx: the Excel samples to fill in, downloaded; the number of employees (BRN, NoOfEmployees) and a
/// territory's list of companies (Company, BRN) uploaded as Excel workbooks; and, read only from MyOrders, a costing
/// sheet's or request form's attachment and the costing sheets and request forms lists as Excel workbooks; and, read only
/// from MyFiles, a file's PDF (files/{id}, shown in the page with ?view=1) or several as a zip (files/zip?ids=1,2); and, read
/// only from Advance, a company's Usage &amp; history as an Excel workbook (usage?company=).
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
    /// <summary>files/{id} (?view=1 to show it) and files/zip?ids=1,2: MyFiles' PDFs.</summary>
    public const string MyFilesPrefix = "files/";
    public const string MyFilesZip = "files/zip";
    /// <summary>activities?&lt;the Activities page's filters&gt;: MyCRM's Download (CompanyActivity.xls), every activity shown.</summary>
    public const string ActivitiesList = "activities";
    /// <summary>dormant: the Dormant opportunities page's Download Excel (all): everything in the person's scope, whatever the filters.</summary>
    public const string DormantList = "dormant";
    /// <summary>usage?company=&amp;months=N (or from=yyyy-MM&amp;to=yyyy-MM): a company's Usage &amp; history page as an Excel workbook.</summary>
    public const string Usage = "usage";

    /// <summary>
    /// A company's Usage &amp; history (read only from Advance) as an Excel workbook: the copies per month of the range
    /// chosen with the same month a year before, and the copies per year. Only for who may see the company's machines.
    /// </summary>
    private FileAnswer UsageFile(IReadOnlyDictionary<string, string> query, ApiUser user)
    {
        if (!w.Machines.Advance.Available) return FileAnswer.Fail(503, "Advance's machines cannot be read yet: an administrator has to set up the connection to Advance first.");
        var actor = user.ToActor(w.People);
        var companyId = Id(query, "company");
        try
        {
            var (from, to) = MachinesApi.Span(query, w.Clock().Date);
            // The year before each month too, to compare.
            if (w.Machines.CompanyCopies(actor, companyId, from.AddMonths(-12), to) is not { } copies || w.Machines.CompanyYears(actor, companyId, Machines.MaxYears) is not { } years)
                return FileAnswer.Fail(403, "You don't have permissions to view this company's machines: it is not in your or your team's territories.");
            var byMonth = copies.Months.ToDictionary(m => m.Year * 100 + m.Month);
            static object? Change(long now, long before) => before == 0 ? null : Math.Round((decimal)(now - before) / before * 100m, 1);
            var months = copies.Months.Where(m => new DateTime(m.Year, m.Month, 1) >= from).Select(m =>
            {
                var before = byMonth.TryGetValue((m.Year - 1) * 100 + m.Month, out var b) ? b : new MonthCopies(m.Year - 1, m.Month, 0, 0);
                return new object?[]
                {
                    new DateTime(m.Year, m.Month, 1).ToString("MMM yyyy", CultureInfo.InvariantCulture),
                    (decimal)m.BlackWhite, (decimal)before.BlackWhite, Change(m.BlackWhite, before.BlackWhite),
                    (decimal)m.Colour, (decimal)before.Colour, Change(m.Colour, before.Colour),
                };
            }).ToList();
            var name = new string(copies.Company.Name.Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-').ToArray()).Trim();
            return new FileAnswer(200, $"Usage-{(name.Length > 0 ? name : companyId.ToString(CultureInfo.InvariantCulture))}-{w.Clock():yyyyMMdd}.xlsx", FileAnswer.XlsxType, Xlsx.Create(
            [
                new Xlsx.Sheet("Copies per month",
                    [new Xlsx.ColumnSpec("Month", 12), new Xlsx.ColumnSpec("Black & white", 16, "number"), new Xlsx.ColumnSpec("B&W year before", 16, "number"),
                     new Xlsx.ColumnSpec("B&W change %", 14, "number"), new Xlsx.ColumnSpec("Colour", 14, "number"), new Xlsx.ColumnSpec("Colour year before", 18, "number"),
                     new Xlsx.ColumnSpec("Colour change %", 16, "number")], months),
                new Xlsx.Sheet("Copies per year",
                    [new Xlsx.ColumnSpec("Year", 8), new Xlsx.ColumnSpec("Machines billed", 16, "number"), new Xlsx.ColumnSpec("Black & white", 16, "number"),
                     new Xlsx.ColumnSpec("Colour", 14, "number")],
                    years.Years.OrderByDescending(y => y.Year).Select(y => new object?[] { y.Year.ToString(CultureInfo.InvariantCulture), (decimal)y.Machines, (decimal)y.BlackWhite, (decimal)y.Colour })),
            ]));
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or TimeoutException or InvalidOperationException)
        {
            return FileAnswer.Fail(503, "Advance's machines could not be read: " + ex.Message);
        }
    }

    /// <summary>The Activities page's activities as an Excel workbook, as the list shows them (up to <see cref="Activities.MaxRows"/>).</summary>
    private FileAnswer ActivitiesFile(IReadOnlyDictionary<string, string> query, ApiUser user)
    {
        var actor = user.ToActor(w.People);
        try
        {
            var rows = w.Activities.ForDownload(actor, ProjectsApi.Filter(query));
            static DateTime? D(DateTime? d) => d is { } x && x.Year > 1900 ? x : null;
            return new FileAnswer(200, "CompanyActivity.xlsx", FileAnswer.XlsxType, Xlsx.Create("Activities",
                [new Xlsx.ColumnSpec("Remarks", 50), new Xlsx.ColumnSpec("Company", 40), new Xlsx.ColumnSpec("Action", 20), new Xlsx.ColumnSpec("Activity Type", 30),
                 new Xlsx.ColumnSpec("Start Date", 12, "date"), new Xlsx.ColumnSpec("End Date", 12, "date"), new Xlsx.ColumnSpec("Start Time", 10), new Xlsx.ColumnSpec("End Time", 10),
                 new Xlsx.ColumnSpec("Owner", 24), new Xlsx.ColumnSpec("Contacts", 30), new Xlsx.ColumnSpec("Status", 14)],
                rows.Select(r => new object?[] { r.Activity.Remarks, r.Company, ActivityActions.List.Label(r.Activity.Action), string.Join(", ", r.Types),
                    D(r.Activity.VisitDate), D(r.Activity.ToDate), r.Activity.StartTime, r.Activity.EndTime, r.OwnerName, string.Join(", ", r.Contacts), r.Status })));
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or TimeoutException or InvalidOperationException)
        {
            return FileAnswer.Fail(503, "MyCRM's activities could not be read: " + ex.Message);
        }
    }

    /// <summary>
    /// Everything the Dormant opportunities page can show for the person (their territories, or their team's), whatever filter
    /// the page has: both lists on their own sheets with every column, the activity history and the decision made.
    /// </summary>
    private FileAnswer DormantFile(ApiUser user)
    {
        var actor = user.ToActor(w.People);
        try
        {
            var (prospects, visited) = w.Dormant.ForDownload(actor);
            static DateTime? D(DateTime? d) => d is { } x && x.Year > 1900 ? x : null;
            static string History(Dormant.Row r) => string.Join("\n", r.History.Select(h => (D(h.Date) is { } d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "no date") + "  " + h.Label));
            static string State(Dormant.Row r) => r.State switch
            {
                DormantDecisions.FollowedUp => "Followed up",
                DormantDecisions.NoOpportunity => "No opportunity",
                DormantDecisions.Snoozed => "Snoozed",
                _ => "To follow up",
            };
            var common = new[]
            {
                new Xlsx.ColumnSpec("Company", 40), new Xlsx.ColumnSpec("BRN", 18), new Xlsx.ColumnSpec("Territory", 24), new Xlsx.ColumnSpec("Salesperson", 24),
                new Xlsx.ColumnSpec("Priority", 10), new Xlsx.ColumnSpec("Why it's here", 40), new Xlsx.ColumnSpec("Furthest activity", 24),
            };
            var tail = new[]
            {
                new Xlsx.ColumnSpec("Last activity", 12, "date"), new Xlsx.ColumnSpec("Activities", 10, "number"), new Xlsx.ColumnSpec("Activity history", 60),
                new Xlsx.ColumnSpec("Decision", 16), new Xlsx.ColumnSpec("Reason", 24), new Xlsx.ColumnSpec("Snoozed until", 12, "date"),
                new Xlsx.ColumnSpec("Decided by", 24), new Xlsx.ColumnSpec("Decided on", 12, "date"),
            };
            var prospectColumns = common.Concat(new[]
            {
                new Xlsx.ColumnSpec("Status", 18), new Xlsx.ColumnSpec("Type", 18), new Xlsx.ColumnSpec("Value", 14, "number"), new Xlsx.ColumnSpec("Closed on", 12, "date"),
            }).Concat(tail).ToList();
            var visitedColumns = common.Concat(tail).ToList();
            return new FileAnswer(200, "DormantOpportunities.xlsx", FileAnswer.XlsxType, Xlsx.Create(
            [
                new Xlsx.Sheet("Prospects", prospectColumns, prospects.Select(r => new object?[]
                {
                    r.Company, r.Brn, r.Territory, r.Owner, r.Priority, r.Why, r.Furthest, r.Status, r.ProspectType, r.Value, D(r.ClosedOn),
                    D(r.LastActivity), r.Activities, History(r), State(r), DormantReasons.List.Label(r.Reason), D(r.SnoozeUntil), r.DecidedBy, D(r.DecidedAt),
                })),
                new Xlsx.Sheet("Visited, no prospect", visitedColumns, visited.Select(r => new object?[]
                {
                    r.Company, r.Brn, r.Territory, r.Owner, r.Priority, r.Why, r.Furthest,
                    D(r.LastActivity), r.Activities, History(r), State(r), DormantReasons.List.Label(r.Reason), D(r.SnoozeUntil), r.DecidedBy, D(r.DecidedAt),
                })),
            ]));
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or TimeoutException or InvalidOperationException)
        {
            return FileAnswer.Fail(503, "MyCRM's tables could not be read: " + ex.Message);
        }
    }

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
        if (kind == ActivitiesList) return ActivitiesFile(query, user);
        if (kind == DormantList) return DormantFile(user);
        if (kind == Usage) return UsageFile(query, user);
        if (kind.StartsWith(MyFilesPrefix, StringComparison.Ordinal)) return MyFilesFile(kind, query, user);
        return Orders(kind, query, user) ?? FileAnswer.Fail(404, "Not found.");
    }

    /// <summary>
    /// A file of MyFiles (files/{id}, shown in the page with ?view=1), or several as one zip (files/zip?ids=1,2): only
    /// files the person may see (MyFiles.MaySee), checked before anything is read.
    /// </summary>
    private FileAnswer MyFilesFile(string kind, IReadOnlyDictionary<string, string> query, ApiUser user)
    {
        if (!w.MyFilesData.Available) return FileAnswer.Fail(503, "MyFiles' tables cannot be read yet: an administrator has to set up the connection first.");
        var actor = user.ToActor(w.People);
        try
        {
            if (kind == MyFilesZip)
            {
                var ids = Q(query, "ids").Split(',')
                    .Select(t => long.TryParse(t.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var one) ? one : 0)
                    .Where(id => id > 0).Distinct().ToList();
                if (ids.Count == 0) return FileAnswer.Fail(404, "Tick the files to download.");
                if (ids.Count > MyFiles.ZipMax) return FileAnswer.Fail(400, $"At most {MyFiles.ZipMax} files are downloaded at a time.");
                var files = ids.Select(w.MyFiles.Get).Where(f => f is not null).Select(f => f!).ToList();
                if (files.Count == 0) return FileAnswer.Fail(404, "The files are not there.");
                var refused = files.Count(f => !w.MyFiles.MaySee(actor, f));
                if (refused > 0)
                    return FileAnswer.Fail(403, refused == 1
                        ? "One of the files belongs to a company you may not open: nothing was downloaded."
                        : $"{refused} of the files belong to companies you may not open: nothing was downloaded.");
                if (files.Count == 1)
                    return w.MyFiles.Bytes(files[0]) is { } single
                        ? new FileAnswer(200, MyFiles.NameOf(files[0]), ContentType(MyFiles.NameOf(files[0])), single)
                        : FileAnswer.Fail(404, "MyFiles has no PDF for this file.");
                var (name, zip) = w.MyFiles.Zip(files);
                return new FileAnswer(200, name, "application/zip", zip);
            }
            var fileId = long.TryParse(kind.Substring(MyFilesPrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
            if (w.MyFiles.Get(fileId) is not { } file) return FileAnswer.Fail(404, "The file is not there.");
            if (!w.MyFiles.MaySee(actor, file)) return FileAnswer.Fail(403, "This file belongs to a company you may not open: it is not in your or your team's territories.");
            if (w.MyFiles.Bytes(file) is not { } bytes) return FileAnswer.Fail(404, "MyFiles has no PDF for this file.");
            var view = Q(query, "view") == "1";
            var fileName = MyFiles.NameOf(file);
            return new FileAnswer(200, fileName, view ? "application/pdf" : ContentType(fileName), bytes, Inline: view);
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or TimeoutException or InvalidOperationException)
        {
            return FileAnswer.Fail(503, "MyFiles' tables could not be read: " + ex.Message);
        }
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
