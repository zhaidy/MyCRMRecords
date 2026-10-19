using System.Globalization;
using MyCRMRecords.Data;
using MyCRMRecords.Domain;

namespace MyCRMRecords.Api;

/// <summary>
/// The API behind MyCRMRecords's pages (api.ashx/...), as the signed-in person: the feedbacks with their tasks and files, the
/// lists (divisions, categories, mediums, KPI dates) for Feedback Admins, the statistics, and the people to choose from.
/// Every change is checked again in the domain.
/// </summary>
public sealed class CrmApi(CrmApp w, Func<string, IReadOnlyList<DirectoryPerson>>? findInDirectory = null)
{
    public sealed class CompleteInput { public bool Completed { get; set; } }

    public sealed class PersonInput
    {
        public string? Login { get; set; }
        public string? DisplayName { get; set; }
        public string? Email { get; set; }
    }

    public ApiResponse Handle(string method, string path, string body, IReadOnlyDictionary<string, string> query, ApiUser user)
    {
        var parts = path.Trim('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return ApiResponse.NotFound();
        var get = method == "GET";
        var post = method == "POST";
        var actor = user.ToActor(w.People);
        string Q(string name) => query.TryGetValue(name, out var v) ? v ?? "" : "";
        long Id(int index) => parts.Length > index && long.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;
        try
        {
            switch (parts[0])
            {
                case "session" when get:
                    return ApiResponse.Of(new
                    {
                        user = new { login = actor.Login, name = actor.DisplayName, email = actor.Email, roles = actor.Roles, changedBy = actor.ChangedBy },
                        can = Can(actor),
                        roles = Roles.All,
                    });

                case "lists" when get:
                    return Lists();

                // ---- People to choose from ----
                case "people" when get && parts.Length == 1:
                    return ApiResponse.Of(new { people = w.People.Everyone().Select(u => new { u.Login, name = u.DisplayName, u.Email }) });
                case "people" when get && parts.Length == 2 && parts[1] == "search":
                {
                    var q = Q("q").Trim();
                    if (q.Length < 2 || findInDirectory is null) return ApiResponse.Of(new { people = Array.Empty<DirectoryPerson>() });
                    try { return ApiResponse.Of(new { people = findInDirectory(q) }); }
                    catch (Exception ex) { return ApiResponse.Fail(503, "DIRECTORY_UNAVAILABLE", "Active Directory could not be searched: " + ex.Message); }
                }
                case "people" when post && parts.Length == 1:
                {
                    var input = Input.Read<PersonInput>(body);
                    var login = (input.Login ?? "").Trim();
                    if (login.Length == 0 || !Strings.IsEmail(input.Email)) return ApiResponse.Fail(400, ResultCodes.Validation, "Choose someone with an email address.");
                    var person = w.People.Remember(login, input.DisplayName ?? "", input.Email ?? "");
                    return ApiResponse.Ok($"{person.DisplayName} can be chosen now.", new { person = new { person.Login, name = person.DisplayName, person.Email } });
                }

                // ---- Feedbacks (every signed-in person, as in MyCRM) ----
                case "feedbacks" when get && parts.Length == 1:
                    return ListFeedbacks(actor, new Feedbacks.ListFilter(Q("q"), Q("status"), Q("division")));
                case "feedbacks" when get && parts.Length == 2:
                    return FeedbackDetail(actor, Id(1));
                case "feedbacks" when post && parts.Length == 2:
                    return ApiResponse.Result(w.Feedbacks.Save(actor, Id(1), Input.Read<Feedbacks.FeedbackInput>(body)));
                case "feedbacks" when post && parts.Length == 3 && parts[2] == "delete":
                    return ApiResponse.Result(w.Feedbacks.Delete(actor, Id(1)));
                case "feedbacks" when post && parts.Length == 3 && parts[2] == "close":
                    return ApiResponse.Result(w.Feedbacks.CloseCase(actor, Id(1)));
                case "feedbacks" when post && parts.Length == 4 && parts[2] == "tasks":
                    return ApiResponse.Result(w.Feedbacks.SaveTask(actor, Id(1), Id(3), Input.Read<Feedbacks.TaskInput>(body)));
                case "tasks" when post && parts.Length == 3 && parts[2] == "complete":
                    return ApiResponse.Result(w.Feedbacks.CompleteTask(actor, Id(1), Input.Read<CompleteInput>(body).Completed));
                case "tasks" when post && parts.Length == 3 && parts[2] == "remove":
                    return ApiResponse.Result(w.Feedbacks.RemoveTask(actor, Id(1)));
                case "files" when post && parts.Length == 3 && parts[2] == "remove":
                    return ApiResponse.Result(w.Feedbacks.RemoveFile(actor, Id(1)));

                // ---- The lists (Feedback Admins: MyCRM's Admin screen) ----
                case "setup" when !actor.IsFeedbackAdmin:
                    return ApiResponse.Forbidden("The lists are kept by Feedback Admins.");
                case "setup" when get && parts.Length == 1:
                    return SetupLists();
                case "setup" when get && parts.Length == 2 && parts[1] == "kpi":
                    return KpiRows(Q("q"), int.TryParse(Q("skip"), out var skip) ? Math.Max(0, skip) : 0);
                case "setup" when post && parts.Length == 3 && parts[1] == "divisions":
                    return ApiResponse.Result(w.Setup.SaveDivision(actor, Id(2), Input.Read<Setup.DivisionInput>(body)));
                case "setup" when post && parts.Length == 4 && parts[1] == "divisions" && parts[3] == "remove":
                    return ApiResponse.Result(w.Setup.RemoveDivision(actor, Id(2)));
                case "setup" when post && parts.Length == 3 && parts[1] == "categories":
                    return ApiResponse.Result(w.Setup.SaveCategory(actor, Id(2), Input.Read<Setup.ChoiceInput>(body)));
                case "setup" when post && parts.Length == 4 && parts[1] == "categories" && parts[3] == "remove":
                    return ApiResponse.Result(w.Setup.RemoveCategory(actor, Id(2)));
                case "setup" when post && parts.Length == 3 && parts[1] == "mediums":
                    return ApiResponse.Result(w.Setup.SaveMedium(actor, Id(2), Input.Read<Setup.ChoiceInput>(body)));
                case "setup" when post && parts.Length == 4 && parts[1] == "mediums" && parts[3] == "remove":
                    return ApiResponse.Result(w.Setup.RemoveMedium(actor, Id(2)));
                case "setup" when post && parts.Length == 3 && parts[1] == "kpi":
                    return ApiResponse.Result(w.Setup.SaveKpiDates(actor, Id(2), Input.Read<Setup.KpiInput>(body)));

                // ---- Statistics (everyone, as in MyCRM) ----
                case "statistics" when get && parts.Length == 1:
                    return StatisticsOf(int.TryParse(Q("year"), out var year) ? year : FinancialYears.Of(w.Clock()));
                case "statistics" when get && parts.Length == 2 && parts[1] == "report":
                {
                    var rows = w.Statistics.Report(Months(Q("months")));
                    return ApiResponse.Of(new { count = rows.Count, rows });
                }
            }
            return ApiResponse.NotFound();
        }
        catch (BadInputException ex)
        {
            return ApiResponse.BadRequest(ex.Message);
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or TimeoutException or InvalidOperationException)
        {
            return ApiResponse.Fail(503, "UNAVAILABLE", ex.Message.StartsWith("The ", StringComparison.Ordinal) ? ex.Message : "A database could not be read: " + ex.Message);
        }
    }

    public static object Can(Actor actor) => new
    {
        admin = actor.IsAdmin, feedbackAdmin = actor.IsFeedbackAdmin, be = actor.IsBe, hr = actor.IsHr,
        // MyCRM's Admin could act as anyone (its user switch): Sign in as, recorded in the audit log.
        actAs = actor.IsFeedbackAdmin,
    };

    /// <summary>"2026-10-01,2026-11-01" → those months (the report's choice).</summary>
    public static IReadOnlyList<DateTime> Months(string text) =>
        (text ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => DateTime.TryParseExact(t.Trim(), new[] { "yyyy-MM-dd", "yyyy-MM" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? new DateTime(d.Year, d.Month, 1) : (DateTime?)null)
            .Where(d => d is not null).Select(d => d!.Value).Distinct().OrderBy(d => d).ToList();

    private static object Choices(ChoiceList list) => list.All.Select(c => new { code = c.Code, label = c.Label });

    private ApiResponse Lists() => ApiResponse.Of(new
    {
        groups = Choices(FeedbackGroups.List),
        statuses = Choices(FeedbackStatuses.List),
        types = Choices(FeedbackTypes.List),
        typeHelp = FeedbackTypes.Help,
        satisfaction = Choices(SatisfactionLevels.List),
        taskTypes = Choices(TaskTypes.List),
        hrAnswers = HrAnswers.All,
        criteria = EssChoices.Criteria.Select(c => new { c.Label, c.Value }),
        ricohWay = EssChoices.RicohWayValues.Select(c => new { c.Label, c.Value }),
        categories = w.Feedbacks.Categories().Select(c => new { c.Id, c.Label, c.IsActive }),
        mediums = w.Feedbacks.Mediums().Select(c => new { c.Id, c.Label, c.IsActive }),
        divisions = w.Feedbacks.Divisions().Select(d => new { d.Id, d.Name, d.PersonInCharge, personName = w.People.NameOf(d.PersonInCharge) }),
        people = w.People.Everyone().Select(u => new { u.Login, name = u.DisplayName, u.Email }),
        directory = findInDirectory is not null,
    });

    private string Name(string login) => login.Length == 0 ? "" : w.People.NameOf(login);

    private object Person(string login) => new { login, name = Name(login) };

    private ApiResponse ListFeedbacks(Actor actor, Feedbacks.ListFilter filter)
    {
        var list = w.Feedbacks.List(actor, filter);
        var all = w.Feedbacks.List(actor, new Feedbacks.ListFilter());
        var divisions = w.Feedbacks.DivisionNames();
        return ApiResponse.Of(new
        {
            rows = list.Rows.Select(f => new
            {
                f.Id, group = FeedbackGroups.List.Label(f.FeedbackGroup), f.CompanyName, f.ContactName, f.Status, statusLabel = FeedbackStatuses.List.Label(f.Status),
                createdBy = f.CreatedByUserName.Length > 0 ? f.CreatedByUserName : Name(f.CreatedBy), f.CreatedOn, inCharge = Name(f.PersonResponsible),
                division = Feedbacks.DivisionOf(f, divisions), f.DateReceived, type = FeedbackTypes.List.Label(f.FeedbackType),
            }),
            counts = list.Counts,
            all = list.All,
            divisions = w.Feedbacks.DivisionChoices(all.Rows),
            statuses = Choices(FeedbackStatuses.List),
            can = new { create = true, seesAll = actor.IsFeedbackAdmin },
        });
    }

    private ApiResponse FeedbackDetail(Actor actor, long id)
    {
        var f = id == 0 ? new Feedback { CreatedBy = actor.Login, CreatedByUserName = actor.DisplayName } : w.Feedbacks.Get(id);
        if (f is null) return ApiResponse.NotFound();
        var rights = w.Feedbacks.Rights(actor, f);
        var sla = id == 0 ? null : w.Feedbacks.SlaExpectedCompletion(f);
        var tasks = id == 0 ? new List<FeedbackTask>() : w.Feedbacks.Tasks(f.Id).ToList();
        var divisions = w.Feedbacks.DivisionNames();
        return ApiResponse.Of(new
        {
            feedback = new
            {
                f.Id, f.FeedbackGroup, groupLabel = FeedbackGroups.List.Label(f.FeedbackGroup), f.Status, statusLabel = FeedbackStatuses.List.Label(f.Status),
                f.DivisionId, division = Feedbacks.DivisionOf(f, divisions), personResponsible = Person(f.PersonResponsible), f.CompanyName, f.CompanyAddress, f.PostalCode,
                f.CompanyPhone, f.CompanyFax, f.CompanyEmail, f.ContactName, f.MediumId, medium = f.MediumId is { } m ? w.Store.Get<FeedbackMedium>(m)?.Label ?? "" : "",
                f.DateReceived, timeReceived = TimesOfDay.Short(f.TimeReceived), f.FeedbackType, typeLabel = FeedbackTypes.List.Label(f.FeedbackType), f.CategoryId,
                category = f.CategoryId is { } c ? w.Store.Get<FeedbackCategory>(c)?.Label ?? "" : "", f.Description, cc = Strings.Emails(f.Cc),
                f.MightCauseCustomerComplaint, f.CreatorHasSolution, f.SolutionDetail, f.ExpectedOutcome,
                suggestionMeetCriteria = EssChoices.Parse(EssChoices.Criteria, f.SuggestionMeetCriteria), ricohWayValue = EssChoices.Parse(EssChoices.RicohWayValues, f.RicohWayValue),
                f.HrAccepted, f.HrRemarks, f.HrUpdatedOn, hrUpdatedBy = Name(f.HrUpdatedBy), f.IsAcknowledged, f.AcknowledgedDate,
                acknowledgedTime = TimesOfDay.Short(f.AcknowledgedTime), signOff = Name(f.SignOff), f.FirstLookAtFeedback, f.RootCause,
                staffResponsible = Name(f.StaffResponsibleForSolution), f.SatisfactionLevel, satisfactionLabel = SatisfactionLevels.List.Label(f.SatisfactionLevel),
                f.CreatedOn, createdBy = Person(f.CreatedBy), createdByName = f.CreatedByUserName.Length > 0 ? f.CreatedByUserName : Name(f.CreatedBy), f.UpdatedOn,
                updatedBy = f.UpdatedByUserName, f.AcknowledgedHours, f.StartResolutionDays, f.CloseCaseDays, f.StartedResolutionOn, f.CompletedResolutionOn, f.IsCompleted,
                f.CorrectiveAction, f.PreventiveAction,
            },
            rights,
            kpi = new { acknowledgeTarget = Domain.Statistics.AcknowledgeTarget, startTarget = Domain.Statistics.StartTarget, closeTarget = Domain.Statistics.CloseTarget },
            sla,
            tasks = tasks.Select(t => new
            {
                t.Id, t.TaskType, t.ActionPlan, t.Remarks, personInCharge = Person(t.PersonInCharge), t.DueDate, t.IsCompleted, t.CreatedOn, createdBy = Name(t.CreatedBy),
                mine = actor.IsLogin(t.PersonInCharge),
                canEdit = rights.NotClosed && (rights.InCharge || actor.IsLogin(t.PersonInCharge)),
                canRemove = rights.NotClosed && rights.InCharge,
            }),
            files = id == 0 ? new List<object>() : w.Feedbacks.Files(f.Id).Select(x => (object)new { x.Id, x.FileName, x.Kind, size = x.Content.Length, x.UploadedOn, uploadedBy = Name(x.UploadedBy) }).ToList(),
            holidays = w.Holidays.Available,
        });
    }

    private ApiResponse SetupLists() => ApiResponse.Of(new
    {
        divisions = w.Feedbacks.Divisions().Select(d => new { d.Id, d.Name, d.PersonInCharge, personName = w.People.NameOf(d.PersonInCharge) }),
        categories = w.Feedbacks.Categories().Select(c => new { c.Id, c.Label, c.SortOrder, c.IsActive }),
        mediums = w.Feedbacks.Mediums().Select(c => new { c.Id, c.Label, c.SortOrder, c.IsActive }),
        people = w.People.Everyone().Select(u => new { u.Login, name = u.DisplayName, u.Email }),
    });

    /// <summary>MyCRM's BackOffice: every active feedback, newest first, with its KPI dates (50 at a time).</summary>
    private ApiResponse KpiRows(string q, int skip)
    {
        var search = (q ?? "").Trim().TrimStart('#');
        var all = w.Store.Find<Feedback>(Query.Where("IsActive", true))
            .Where(f => search.Length == 0 || f.Id.ToString(CultureInfo.InvariantCulture) == search || f.CompanyName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderByDescending(f => f.Id).ToList();
        return ApiResponse.Of(new
        {
            total = all.Count, skip, take = 50,
            rows = all.Skip(skip).Take(50).Select(f => new
            {
                f.Id, f.CompanyName, statusLabel = FeedbackStatuses.List.Label(f.Status), f.CreatedOn, f.DateReceived, timeReceived = TimesOfDay.Short(f.TimeReceived),
                f.AcknowledgedDate, acknowledgedTime = TimesOfDay.Short(f.AcknowledgedTime), f.AcknowledgedHours, f.StartedResolutionOn, f.StartResolutionDays,
                f.IsCompleted, f.CompletedResolutionOn, f.CloseCaseDays,
            }),
        });
    }

    private ApiResponse StatisticsOf(int year)
    {
        var figures = w.Statistics.Year(year);
        var divisions = w.Feedbacks.DivisionNames();
        return ApiResponse.Of(new
        {
            year, label = FinancialYears.Label(year),
            years = w.Statistics.Years().Select(y => new { year = y, label = FinancialYears.Label(y) }),
            targets = new { acknowledge = Domain.Statistics.AcknowledgeTarget, start = Domain.Statistics.StartTarget, close = Domain.Statistics.CloseTarget, within = 95 },
            months = figures.Months.Select(m => new
            {
                month = m.Month, label = m.Month.ToString("MMM yyyy", CultureInfo.InvariantCulture), ids = m.Ids, acknowledgedHours = m.AcknowledgedHours,
                startResolutionDays = m.StartResolutionDays, closeCaseDays = m.CloseCaseDays, within = m.WithinTarget, over = m.OverTarget,
            }),
            categories = figures.Categories.Select(s => new { label = s.Label, ids = s.Ids }),
            divisions = figures.Divisions.Select(s => new { label = s.Label, ids = s.Ids }),
            feedbacks = figures.Feedbacks.Select(f => new
            {
                f.Id, f.CompanyName, statusLabel = FeedbackStatuses.List.Label(f.Status), inCharge = Name(f.PersonResponsible), division = Feedbacks.DivisionOf(f, divisions),
                f.DateReceived, f.AcknowledgedHours, f.StartResolutionDays, f.CloseCaseDays,
            }),
            reportMonths = FinancialYears.Months(year).Select(m => new { month = m.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), label = m.ToString("MMMM yyyy", CultureInfo.InvariantCulture) }),
            thisMonth = new DateTime(w.Clock().Year, w.Clock().Month, 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        });
    }
}
