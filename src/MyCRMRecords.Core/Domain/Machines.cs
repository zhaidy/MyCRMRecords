using MyCRMRecords.Data;

namespace MyCRMRecords.Domain;

/// <summary>Who sees which machines: every one, or those of some BRNs and customers' names, and how they reach each.</summary>
public sealed class MachineScope
{
    /// <summary>The roles that open every company see every machine.</summary>
    public bool All { get; init; }
    /// <summary>BRN (trimmed, case ignored) → how the person reaches it: a territory's name, "Project" or "Temporary".</summary>
    public Dictionary<string, string> Brns { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Customers' names the person reaches by MyMachines' temporary access.</summary>
    public HashSet<string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The BRN → MyCRM company the person may open (for the link to the company).</summary>
    public Dictionary<string, long> Companies { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Sees(MachineRow machine) =>
        All || (machine.Brn.Length > 0 && Brns.ContainsKey(machine.Brn.Trim())) || (machine.Customer.Length > 0 && Names.Contains(machine.Customer.Trim()));

    /// <summary>How the person reaches the machine: "All companies", a territory's name, "Project" or "Temporary".</summary>
    public string Via(MachineRow machine) =>
        All ? Machines.ViaAll
        : machine.Brn.Length > 0 && Brns.TryGetValue(machine.Brn.Trim(), out var via) ? via
        : Machines.ViaTemporary;
}

/// <summary>A machine on the pages: Advance's row, how the person reaches it, and the MyCRM company it belongs to (when they may open it).</summary>
public sealed record MachineListRow(MachineRow Machine, string Via, long? CompanyId);

/// <summary>
/// The Machines pages: Advance's machines (read only, <see cref="IAdvanceMachines"/>), seen by MyCRMRecords' company rule
/// (<see cref="Access"/>: a company's territories, MyCRM's projects, the roles that open every company) by the company's
/// BRN, plus MyMachines' temporary access (a TempAccess row of the person with the customer's BRN or name). The same rule
/// for the list, a company's Machines tab and a machine's page. The Machine access page (Sales Admins) lists the
/// temporary access and gives or removes it only when switched on (<see cref="IMyMachinesAccess"/>).
/// </summary>
public sealed class Machines(IAdvanceMachines advance, IMyMachinesAccess temporary, Access access, IRecordStore store, Func<DateTime> clock)
{
    public const int PageSize = 50;
    /// <summary>At least this many characters are searched for (the roles that see every machine see none until they search).</summary>
    public const int SearchFrom = 3;
    public const string ViaAll = "All companies";
    public const string ViaTemporary = "Temporary";

    public IAdvanceMachines Advance => advance;
    public IMyMachinesAccess Temporary => temporary;

    /// <summary>Whether the person keeps MyMachines' temporary access (the Machine access page): Sales Admins and administrators.</summary>
    public static bool MayManageAccess(Actor actor) => actor.IsSalesAdmin;

    private static string Key(string? text) => (text ?? "").Trim();

    /// <summary>The person's own rows of MyMachines' temporary access (by login or email); none when it cannot be read.</summary>
    public IReadOnlyList<TempAccessRow> TemporaryOf(Actor actor)
    {
        if (!temporary.Available) return Array.Empty<TempAccessRow>();
        return temporary.List().Where(r => actor.IsLogin(r.Login) || actor.Is(r.Email)).ToList();
    }

    /// <summary>What the person sees, and how.</summary>
    public MachineScope ScopeOf(Actor actor)
    {
        if (actor.SeesAllCompanies) return new MachineScope { All = true };
        var scope = new MachineScope();
        var reasons = access.CompanyReasons(actor);
        foreach (var chunk in reasons.Keys.Select((v, i) => (v, i)).GroupBy(x => x.i / 1000, x => (object)x.v))
            foreach (var company in store.Find<Company>(Query.All().In("Id", chunk)).OrderBy(c => c.Id))
            {
                var brn = Key(company.Brn);
                // A BRN of 3 characters or fewer is no BRN (as the customer numbers from Advance).
                if (brn.Length <= 3) continue;
                if (!scope.Brns.ContainsKey(brn)) scope.Brns[brn] = reasons[company.Id];
                if (!scope.Companies.ContainsKey(brn)) scope.Companies[brn] = company.Id;
            }
        foreach (var row in TemporaryOf(actor))
        {
            if (Key(row.Brn).Length > 0 && !scope.Brns.ContainsKey(Key(row.Brn))) scope.Brns[Key(row.Brn)] = ViaTemporary;
            if (Key(row.CustomerName).Length > 0) scope.Names.Add(Key(row.CustomerName));
        }
        return scope;
    }

    private static DateTime Today(DateTime now) => now.Date;

    /// <summary>
    /// A page of the machines the person sees matching the search. The roles that see every machine see none until they
    /// type at least <see cref="SearchFrom"/> characters; the others see all of theirs.
    /// </summary>
    public (int Total, IReadOnlyList<MachineListRow> Rows, bool NeedsSearch) List(Actor actor, MachineSearch search)
    {
        var scope = ScopeOf(actor);
        search.Keyword = Key(search.Keyword);
        if (search.Keyword.Length < SearchFrom)
        {
            if (scope.All) return (0, Array.Empty<MachineListRow>(), true);
            search.Keyword = "";
        }
        Limit(search, scope);
        search.Today = Today(clock());
        search.Skip = Math.Max(0, search.Skip);
        search.Take = PageSize;
        var page = advance.Search(search);
        return (page.Total, Rows(scope, page.Rows), false);
    }

    private static void Limit(MachineSearch search, MachineScope scope)
    {
        search.Brns = scope.All ? null : scope.Brns.Keys.ToList();
        search.Names = scope.All ? Array.Empty<string>() : scope.Names.ToList();
    }

    /// <summary>The rows with how the person reaches each and its MyCRM company (one they may open).</summary>
    private IReadOnlyList<MachineListRow> Rows(MachineScope scope, IReadOnlyList<MachineRow> rows)
    {
        var companies = new Dictionary<string, long>(scope.Companies, StringComparer.OrdinalIgnoreCase);
        if (scope.All)
        {
            var brns = rows.Select(r => Key(r.Brn)).Where(b => b.Length > 3).Distinct(StringComparer.OrdinalIgnoreCase).Cast<object>().ToList();
            if (brns.Count > 0)
                foreach (var company in store.Find<Company>(Query.Where("IsActive", true).In("Brn", brns)).OrderBy(c => c.Id))
                    if (!companies.ContainsKey(Key(company.Brn))) companies[Key(company.Brn)] = company.Id;
        }
        return rows.Select(r => new MachineListRow(r, scope.Via(r), companies.TryGetValue(Key(r.Brn), out var id) ? id : null)).ToList();
    }

    /// <summary>Whether the person may open the MyCRM company, or reaches its BRN or name by temporary access.</summary>
    public bool MaySeeCompany(Actor actor, Company company)
    {
        if (actor.SeesAllCompanies || access.CanOpen(actor, company)) return true;
        return TemporaryOf(actor).Any(r => (Key(r.Brn).Length > 0 && Strings.SameText(r.Brn, company.Brn)) || (Key(r.CustomerName).Length > 0 && Strings.SameText(r.CustomerName, company.Name)));
    }

    /// <summary>
    /// A company's Machines tab (MyCRM's rule): Advance's machines with the company's BRN, by default those whose contract or
    /// warranty ends after today (<paramref name="all"/>: every one). Null when the person may not see them.
    /// </summary>
    public (int Total, IReadOnlyList<MachineListRow> Rows, int ActiveCount)? ForCompany(Actor actor, long companyId, bool all)
    {
        if (store.Get<Company>(companyId) is not { } company || !MaySeeCompany(actor, company)) return null;
        var brn = Key(company.Brn);
        if (brn.Length <= 3) return (0, Array.Empty<MachineListRow>(), 0);
        var today = Today(clock());
        var page = advance.Search(new MachineSearch { Brn = brn, Coverage = all ? "" : Coverages.Active, Today = today, Take = 500 });
        var active = all ? page.Rows.Count(r => r.Active) : page.Total;
        var scope = ScopeOf(actor);
        return (page.Total, page.Rows.Select(r => new MachineListRow(r, scope.All ? ViaAll : scope.Brns.TryGetValue(brn, out var via) ? via : ViaTemporary, companyId)).ToList(), active);
    }

    /// <summary>A machine's page, with the last 12 months of meters, service calls and toner, and a T&amp;M machine's last contract.</summary>
    public sealed record Detail(MachineDetail Machine, string Via, long? CompanyId, IReadOnlyList<MeterReading> Meters, IReadOnlyList<ServiceCall> Services,
        IReadOnlyList<TonerOrder> Toner, LastContract? LastContract);

    /// <summary>The first day of the month <paramref name="months"/> − 1 months before this one: the last <paramref name="months"/> months.</summary>
    public static DateTime From(DateTime today, int months) => new DateTime(today.Year, today.Month, 1).AddMonths(-(Math.Max(1, months) - 1));

    /// <summary>A machine the person sees (null: not there; false: theirs to see no more).</summary>
    public (Detail? Detail, bool Allowed) Get(Actor actor, string serial, string item, int months = 12)
    {
        serial = Key(serial);
        if (serial.Length == 0) return (null, true);
        var today = Today(clock());
        if (advance.Get(serial, Key(item), today) is not { } machine) return (null, true);
        var scope = ScopeOf(actor);
        if (!scope.Sees(machine.Machine)) return (null, false);
        var m = machine.Machine;
        var from = From(today, months);
        var row = Rows(scope, [m])[0];
        return (new Detail(machine, row.Via, row.CompanyId,
            advance.Meters(m.Serial, m.ContractNo, from),
            advance.Services(m.Serial, m.CustomerNo, m.ContractNo, from),
            advance.Toner(m.Serial, m.ContractNo, from),
            m.Coverage == Coverages.TimeAndMaterials || m.LastContract.Length > 0 ? advance.LastContract(m.Serial, m.Item) : null), true);
    }

    // ---------------------------------------------------------------- the Machine access page

    /// <summary>Gives the person temporary access to a company's machines (its name and BRN), as MyMachines did.</summary>
    public OperationResult Give(Actor actor, string login, long companyId)
    {
        if (!MayManageAccess(actor)) return OperationResult.Failure(ResultCodes.Forbidden, "Only Sales Admins give temporary access to machines.");
        if (!temporary.CanWrite) return OperationResult.Failure(ResultCodes.Conflict, TempAccessRules.WritesOff);
        var person = store.FindFirst<AppUser>(Query.Where("Login", Key(login)));
        if (person is null) return OperationResult.Failure(ResultCodes.Validation, "Choose the person.");
        if (store.Get<Company>(companyId) is not { } company || !company.IsActive) return OperationResult.Failure(ResultCodes.Validation, "Choose the company.");
        if (Key(company.Brn).Length <= 3) return OperationResult.Failure(ResultCodes.Validation, $"{company.Name} has no BRN: its machines are found by BRN.");
        if (temporary.List().Any(r => (Strings.SameText(r.Login, person.Login) || (person.Email.Length > 0 && Strings.SameText(r.Email, person.Email)))
                                       && Strings.SameText(r.Brn, company.Brn)))
            return OperationResult.Failure(ResultCodes.Validation, $"{person.DisplayName} already has temporary access to {company.Name}.");
        try
        {
            temporary.Give(person.Login, person.Email, company.Name, Key(company.Brn), actor.ChangedBy);
        }
        catch (MachineAccessRefusedException ex)
        {
            return OperationResult.Failure(ResultCodes.Validation, ex.Message);
        }
        return OperationResult.Success($"{person.DisplayName} sees the machines of {company.Name} from now on.");
    }

    /// <summary>Takes temporary access away: the row is marked in MyMachines, never deleted.</summary>
    public OperationResult Remove(Actor actor, long id)
    {
        if (!MayManageAccess(actor)) return OperationResult.Failure(ResultCodes.Forbidden, "Only Sales Admins remove temporary access to machines.");
        if (!temporary.CanWrite) return OperationResult.Failure(ResultCodes.Conflict, TempAccessRules.WritesOff);
        var row = temporary.List().FirstOrDefault(r => r.Id == id);
        if (row is null) return OperationResult.Failure(ResultCodes.NotFound, "That temporary access is not there any more.");
        try
        {
            temporary.Remove(id, actor.ChangedBy);
        }
        catch (MachineAccessRefusedException ex)
        {
            return OperationResult.Failure(ResultCodes.Validation, ex.Message);
        }
        return OperationResult.Success($"{(row.UserName.Length > 0 ? row.UserName : row.Login)} no longer has temporary access to {row.CustomerName}.");
    }
}
