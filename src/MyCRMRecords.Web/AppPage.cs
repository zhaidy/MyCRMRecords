using System.Text;
using System.Web;
using System.Web.Security;
using System.Web.UI;
using MyCRMRecords.Api;
using MyCRMRecords.Domain;
using MyCRMRecords.Security;
using MyCRMRecords.Web.Auth;
using Roles = MyCRMRecords.Domain.Roles;

namespace MyCRMRecords.Web;

/// <summary>
/// Every page but signing in and out. Forms authentication only lets signed-in people reach them; each page checks the
/// person's roles (<see cref="Allowed"/>): their MyCRM role (from their CRM user records: Sales Admins keep the CRM users
/// and territories, MD the territories too) and MyCRMRecords' administrators, given on the User roles page. Every
/// signed-in person reaches the companies, as in MyCRM (each company is checked by MyCRM's rule). The pages load their
/// data from api.ashx.
/// </summary>
public class AppPage : Page
{
    protected ApiUser? Person { get; private set; }
    protected Actor? Actor { get; private set; }
    protected string Login => Person?.Login ?? "";
    protected string DisplayName => Person?.DisplayName ?? "";
    protected string Email => Person?.Email ?? "";
    /// <summary>Why the person's roles could not be read (the database cannot be reached), or null.</summary>
    protected string? DataProblem { get; private set; }

    protected bool IsAdmin => Actor?.IsAdmin == true;
    protected bool IsSalesAdmin => Actor?.IsSalesAdmin == true;
    protected bool CanManageTerritories => Actor?.CanManageTerritories == true;

    /// <summary>Whether the person may open this page; the page says who may, otherwise.</summary>
    protected virtual bool Allowed => true;

    /// <summary>Who may open this page, for the page shown to others.</summary>
    protected virtual string AllowedWho => "";

    protected virtual void Page_Load(object sender, EventArgs e)
    {
        Services.EnsureStarted();
        var user = SignIn.Current(Context);
        if (user is null)
        {
            FormsAuthentication.RedirectToLoginPage();
            Context.ApplicationInstance.CompleteRequest();
            return;
        }
        Person = Admins.Resolve(Context, user);
        try
        {
            var people = Services.App.People;
            // Notes who opened MyCRMRecords (the User roles page lists them), at most once an hour.
            if (user.Impersonator is null) people.Touch(user.Login, user.DisplayName, user.Email);
            Actor = Person.ToActor(people);
        }
        catch (Exception ex)
        {
            DataProblem = "The data cannot be read now: " + ex.Message;
            Actor = new Actor(Person.Login, Person.DisplayName, Person.Email, Person.IsListedAdmin ? new[] { Roles.Admin } : Array.Empty<string>());
        }
        if (!Allowed) Response.StatusCode = 403;
        Response.Cache.SetCacheability(HttpCacheability.NoCache);
    }

    protected bool SignedIn => Login.Length > 0;

    protected override void Render(HtmlTextWriter writer)
    {
        if (Response.IsRequestBeingRedirected) return;
        if (SignedIn && !Allowed) writer.Write(NoAccessPage());
        else base.Render(writer);
    }

    private string NoAccessPage() =>
        "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n  <meta charset=\"UTF-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1,viewport-fit=cover\">\n"
        + "  <title>No access · MyCRMRecords</title>\n"
        + $"  <link rel=\"stylesheet\" href=\"{HttpUtility.HtmlAttributeEncode(Assets.Url("app.css"))}\">{Brand.TabIcon}{Brand.AppLinks}\n</head>\n<body>\n"
        + TopBar("") + "\n  <div class=\"app-shell\"><main><div class=\"empty\"><h3>Not for your role</h3>"
        + $"<p>{HttpUtility.HtmlEncode(AllowedWho)} Your account is {HttpUtility.HtmlEncode(Login)}; a Sales Admin sets MyCRM roles on the CRM users page.</p>"
        + "<p><a class=\"button secondary small-btn\" href=\"guide.aspx\">Open the user guide</a></p></div></main></div>\n"
        + $"  <script src=\"{HttpUtility.HtmlAttributeEncode(Assets.Url("common.js"))}\"></script>\n</body>\n</html>\n";

    /// <summary>
    /// The top bar of every page: the brand, the menu (the pages the person's roles allow), the user guide button, the
    /// person and sign out; and, while someone is signed in as another person, the bar saying so.
    /// </summary>
    protected string TopBar(string current, string guideSection = "")
    {
        string Item(string page, string icon, string label) =>
            $"<a class=\"nav-item{(current == page ? " selected" : "")}\" href=\"{page}\"{(current == page ? " aria-current=\"page\"" : "")}><span data-icon=\"{icon}\"></span>{HttpUtility.HtmlEncode(label)}</a>";
        var html = new StringBuilder();
        html.Append("<header class=\"appbar\">")
            .Append("<button class=\"icon-button menu-button\" id=\"menu-button\" aria-label=\"Menu\" aria-controls=\"sidebar\" aria-expanded=\"false\"><span data-icon=\"menu\"></span></button>")
            .Append("<a class=\"brand\" href=\"./\" aria-label=\"MyCRMRecords home\">" + Brand.Mark + "<span>MyCRMRecords</span></a>")
            .Append("<nav class=\"sidebar\" id=\"sidebar\" aria-label=\"Main\">");
        html.Append(Item("./", "file", "Companies"));
        // MyCRM's projects and sales activities: everyone sees their own and their team's.
        html.Append(Item("projects.aspx", "briefcase", "Projects"));
        html.Append(Item("activities.aspx", "calendar", "Activities"));
        if (CanManageTerritories) html.Append(Item("territories.aspx", "matrix", "Territories"));
        if (IsSalesAdmin) html.Append(Item("people.aspx", "users", "CRM users"));
        // MyOrders' costing sheets and request forms, read only: everyone sees the ones MyOrders' rule lets them.
        html.Append(Item("costingsheets.aspx", "money", "Costing sheets"));
        html.Append(Item("requestforms.aspx", "truck", "Request forms"));
        // MyFiles' files (the PDFs MyFile keeps), read only: everyone sees those of the companies they may open.
        html.Append(Item("files.aspx", "folder", "Files"));
        // Advance's machines, read only: everyone sees those of the companies they may open and their temporary access.
        html.Append(Item("machines.aspx", "printer", "Machines"));
        if (IsAdmin)
        {
            var adminPages = new[] { "users.aspx", "admin.aspx", "deployment.aspx", "audit.aspx" };
            html.Append("<details class=\"nav-group\"><summary class=\"nav-item").Append(adminPages.Contains(current) ? " selected" : "")
                .Append("\"><span data-icon=\"key\"></span>Admin<span class=\"nav-caret\" aria-hidden=\"true\"></span></summary><div class=\"nav-menu\">")
                .Append(Item("users.aspx", "key", "User roles")).Append(Item("admin.aspx", "database", "Settings"))
                .Append(Item("deployment.aspx", "history", "Deployment")).Append(Item("audit.aspx", "audit", "Audit log"))
                .Append("</div></details>");
        }
        html.Append("</nav><div class=\"appbar-right\">")
            // Install app (common.js): on a computer, a QR code to open MyCRMRecords on a phone and install it there.
            .Append("<button type=\"button\" class=\"install-button\" id=\"install-button\" hidden><span data-icon=\"phone-download\"></span><span class=\"install-label\">Install app</span></button>")
            .Append($"<a class=\"help-button{(current == "guide.aspx" ? " selected" : "")}\" id=\"guide-button\" href=\"guide.aspx{(guideSection.Length > 0 ? "#" + guideSection : "")}\" title=\"User guide\" aria-label=\"User guide\"><span data-icon=\"help\"></span></a>")
            .Append($"<span class=\"avatar\" title=\"{HttpUtility.HtmlAttributeEncode(DisplayName)} ({HttpUtility.HtmlAttributeEncode(Login)})\">{HttpUtility.HtmlEncode(InitialsOf(DisplayName))}</span>")
            .Append($"<span class=\"appbar-name\">{HttpUtility.HtmlEncode(DisplayName)}</span>")
            .Append("<a class=\"sign-out\" href=\"logout.aspx\" title=\"Sign out\" aria-label=\"Sign out\"><span data-icon=\"logout\"></span></a>")
            .Append("</div></header><div class=\"menu-backdrop\" id=\"menu-backdrop\"></div>");
        // On a phone the menu is a bar at the bottom of the screen (app.css): Companies, Files, Machines, Orders (the costing
        // sheets and request forms) and More, which opens the rest of the menu (the links above) as a sheet.
        string Tab(string href, string icon, string label, bool selected) =>
            $"<a class=\"tab-item{(selected ? " selected" : "")}\" href=\"{href}\"{(selected ? " aria-current=\"page\"" : "")}><span data-icon=\"{icon}\"></span><span>{label}</span></a>";
        var onOrders = current == "costingsheets.aspx" || current == "requestforms.aspx";
        html.Append("<nav class=\"tabbar\" aria-label=\"App menu\">")
            .Append(Tab("./", "file", "Companies", current == "./"))
            .Append(Tab("files.aspx", "folder", "Files", current == "files.aspx"))
            .Append(Tab("machines.aspx", "printer", "Machines", current == "machines.aspx"))
            .Append(Tab("costingsheets.aspx", "money", "Orders", onOrders))
            .Append($"<button type=\"button\" class=\"tab-item{(current != "./" && current != "files.aspx" && current != "machines.aspx" && !onOrders && current.Length > 0 ? " selected" : "")}\" id=\"more-button\" aria-controls=\"sidebar\" aria-expanded=\"false\"><span data-icon=\"menu\"></span><span>More</span></button>")
            .Append("</nav>");
        if (Person?.Impersonator is { } admin)
            html.Append("<div class=\"impersonation-bar\" role=\"status\"><span>You are signed in as <strong>").Append(HttpUtility.HtmlEncode(DisplayName))
                .Append("</strong> (").Append(HttpUtility.HtmlEncode(Login)).Append("). Everything you do is recorded as ")
                .Append(HttpUtility.HtmlEncode(admin.DisplayName)).Append(" on behalf of ").Append(HttpUtility.HtmlEncode(DisplayName))
                .Append(".</span><a class=\"button secondary small-btn\" href=\"logout.aspx\">Return to ").Append(HttpUtility.HtmlEncode(admin.DisplayName)).Append("</a></div>");
        if (DataProblem is not null)
            html.Append("<div class=\"impersonation-bar warning-bar\" role=\"alert\"><span>").Append(HttpUtility.HtmlEncode(DataProblem)).Append("</span></div>");
        // Until an administrator lets it, MyCRMRecords only reads MyCRM's data: every page says so.
        if (SignedIn && current != "guide.aspx" && !Services.App.CanWrite)
            html.Append("<div class=\"impersonation-bar warning-bar\" role=\"status\"><span>").Append(HttpUtility.HtmlEncode(ReadOnlyNotice)).Append("</span></div>");
        return html.ToString();
    }

    public const string ReadOnlyNotice =
        "MyCRMRecords only shows MyCRM's data for now: saving is switched off until an administrator turns on \"Let MyCRMRecords change MyCRM's data\".";

    /// <summary>The scripts of every page, then the page's own.</summary>
    protected string Scripts(params string[] own) =>
        string.Concat(new[] { "common.js" }.Concat(own).Append("guide.js")
            .Select(script => $"<script src=\"{HttpUtility.HtmlAttributeEncode(Assets.Url(script))}\"></script>\n  "));

    protected string Stylesheet() => $"<link rel=\"stylesheet\" href=\"{HttpUtility.HtmlAttributeEncode(Assets.Url("app.css"))}\">" + Brand.TabIcon + Brand.AppLinks;

    /// <summary>"Jamie Lee" → "JL", "jsmith" → "JS".</summary>
    public static string InitialsOf(string name)
    {
        var words = name.Split(new[] { ' ', '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var initials = words.Length >= 2 ? $"{words[0][0]}{words[words.Length - 1][0]}" : name.Length >= 2 ? name.Substring(0, 2) : name;
        return initials.ToUpperInvariant();
    }
}

/// <summary>territories.aspx: Sales Admins (MyCRM role Admin or SalesAdmin), MD and administrators.</summary>
public class TerritoriesPage : AppPage
{
    protected override bool Allowed => CanManageTerritories;
    protected override string AllowedWho => "Territories are kept by Sales Admins (MyCRM role Admin or SalesAdmin) and MD.";
}

/// <summary>people.aspx: Sales Admins (MyCRM role Admin or SalesAdmin) and administrators (MyCRM's Users screens).</summary>
public class PeoplePage : AppPage
{
    protected override bool Allowed => IsSalesAdmin;
    protected override string AllowedWho => "The CRM users and teams are kept by Sales Admins (MyCRM role Admin or SalesAdmin).";
}

/// <summary>machineaccess.aspx: MyMachines' temporary access, kept by Sales Admins.</summary>
public class MachineAccessPage : AppPage
{
    protected override bool Allowed => IsSalesAdmin;
    protected override string AllowedWho => "Temporary access to machines is kept by Sales Admins (MyCRM role Admin or SalesAdmin).";
}

/// <summary>users.aspx, admin.aspx, deployment.aspx and audit.aspx: administrators only.</summary>
public class AdminPage : AppPage
{
    protected override bool Allowed => IsAdmin;
    protected override string AllowedWho => "This page is for MyCRMRecords's administrators.";
}

/// <summary>login.aspx: the same sign-in as ReportGenerator3 and MyAllocation (Active Directory username and password).</summary>
public class LoginPage : Page
{
    protected string Message { get; private set; } = "";
    protected string Username { get; private set; } = "";
    protected bool Remember { get; private set; }
    protected string Domain => SignIn.DomainName;

    protected void Page_Load(object sender, EventArgs e)
    {
        Response.Cache.SetCacheability(HttpCacheability.NoCache);
        if (Request.HttpMethod != "POST")
        {
            if (SignIn.Current(Context) is not null) Go(FormsAuthentication.DefaultUrl);
            return;
        }
        Username = Request.Form["username"] ?? "";
        Remember = Request.Form["remember"] == "on";
        var result = SignIn.Validate(Context, Username, Request.Form["password"]);
        if (result.User is null)
        {
            Message = result.Error ?? "Incorrect username or password.";
            Services.Record(Context, new ApiUser(Username.Trim(), Username.Trim()), "Could not sign in", "login.aspx", false, Message);
            return;
        }
        SignIn.IssueCookie(Response, result.User, Remember);
        Services.Record(Context, result.User, "Signed in", "login.aspx", true, Remember ? "Remember me was ticked: signed in for 7 days." : "");
        var returnUrl = Request.QueryString["ReturnUrl"];
        Go(ReturnUrls.IsLocal(returnUrl, Request.ApplicationPath ?? "/") ? returnUrl! : FormsAuthentication.DefaultUrl);
    }

    private void Go(string url)
    {
        Response.Redirect(url, false);
        Context.ApplicationInstance.CompleteRequest();
    }
}

/// <summary>logout.aspx: signs out; someone signed in as another person returns to their own account (and the Companies page).</summary>
public class LogoutPage : Page
{
    protected void Page_Load(object sender, EventArgs e)
    {
        Response.Cache.SetCacheability(HttpCacheability.NoCache);
        var user = SignIn.Current(Context);
        if (user?.Impersonator is { } admin)
        {
            Services.Record(Context, user, "Returned to their own account", "logout.aspx", true, "");
            SignIn.IssueCookie(Response, admin, remember: false);
            Response.Redirect("./", false);
            Context.ApplicationInstance.CompleteRequest();
            return;
        }
        if (user is not null) Services.Record(Context, user, "Signed out", "logout.aspx", true, "");
        FormsAuthentication.SignOut();
        Response.Redirect(FormsAuthentication.LoginUrl, false);
        Context.ApplicationInstance.CompleteRequest();
    }
}
