namespace MyCRMRecords.Data;

/// <summary>
/// OutSystems' usernames as MyCRMRecords' logins. MyCRMRecords no longer imports MyCRM's data (it reads and writes MyCRM's
/// tables where they are); the name stays because the shared OldTables.cs uses <see cref="LoginOf"/>.
/// </summary>
public static class OutSystemsImport
{
    /// <summary>"jsmith", "RSP\jsmith", "jsmith@ricoh.sg" or "RSP\jsmith@ricoh.sg" (MyCRM's users) → "RSP\jsmith".</summary>
    public static string LoginOf(string username, string domain)
    {
        var name = username.Trim();
        if (name.Length == 0) return "";
        var slash = name.IndexOf('\\');
        if (slash >= 0)
        {
            // DOMAIN\name@mail (OutSystems keeps some users so): DOMAIN\name, whatever the domain.
            var account = name.Substring(slash + 1);
            var sign = account.IndexOf('@');
            var prefix = string.Equals(name.Substring(0, slash), domain, StringComparison.OrdinalIgnoreCase) ? domain : name.Substring(0, slash);
            return prefix + "\\" + (sign > 0 ? account.Substring(0, sign) : account);
        }
        var at = name.IndexOf('@');
        if (at > 0) name = name.Substring(0, at);
        return domain + "\\" + name;
    }
}
