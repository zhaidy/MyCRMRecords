namespace MyCRMRecords;

public static class Strings
{
    /// <summary>The text cut to <paramref name="length"/> characters (the last one an ellipsis) when longer.</summary>
    public static string Shorten(string? text, int length)
    {
        text ??= "";
        return text.Length <= length ? text : text.Substring(0, length - 1) + "…";
    }

    /// <summary>OutSystems' Substr: <paramref name="length"/> characters from <paramref name="start"/>, or fewer.</summary>
    public static string Part(string? text, int start, int length)
    {
        text ??= "";
        if (start >= text.Length) return "";
        return text.Substring(start, Math.Min(length, text.Length - start));
    }

    public static bool SameText(string? a, string? b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>A plausible email address (one @, a dot after it, no spaces).</summary>
    public static bool IsEmail(string? text)
    {
        var t = (text ?? "").Trim();
        var at = t.IndexOf('@');
        return at > 0 && at == t.LastIndexOf('@') && t.IndexOf('.', at) > at + 1 && !t.Any(char.IsWhiteSpace) && t.Length <= 254;
    }

    /// <summary>The addresses in a list separated by commas or semicolons.</summary>
    public static IReadOnlyList<string> Emails(string? list) =>
        (list ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim()).Where(IsEmail)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
