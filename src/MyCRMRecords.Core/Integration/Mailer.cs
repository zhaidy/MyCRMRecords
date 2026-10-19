using System.Net;
using System.Net.Mail;
using System.Text;
using MyCRMRecords.Data;
using MyCRMRecords.Domain;

namespace MyCRMRecords.Integration;

/// <summary>A file attached to an email.</summary>
public sealed record EmailAttachment(string Name, byte[] Content, string ContentType);

/// <summary>An email to send: for a record (kind and id), to and cc, subject and HTML body; from the settings' address unless <paramref name="From"/>.</summary>
public sealed record Email(string RequestKind, long RequestId, IReadOnlyList<string> To, IReadOnlyList<string> Cc, string Subject, string Html,
    IReadOnlyList<EmailAttachment>? Attachments = null, string? From = null);

/// <summary>Sends one HTML email.</summary>
public interface IEmailSender
{
    /// <summary>Throws when the server does not take it.</summary>
    void Send(EmailSettings settings, IReadOnlyList<string> to, IReadOnlyList<string> cc, string subject, string html, IReadOnlyList<EmailAttachment> attachments);
}

public sealed class SmtpEmailSender : IEmailSender
{
    public void Send(EmailSettings settings, IReadOnlyList<string> to, IReadOnlyList<string> cc, string subject, string html, IReadOnlyList<EmailAttachment> attachments)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(settings.From.Trim(), settings.FromName.Trim(), Encoding.UTF8),
            Subject = subject,
            SubjectEncoding = Encoding.UTF8,
            Body = html,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = true,
        };
        foreach (var address in to) message.To.Add(address);
        foreach (var address in cc) message.CC.Add(address);
        var streams = new List<MemoryStream>();
        try
        {
            foreach (var file in attachments)
            {
                var stream = new MemoryStream(file.Content);
                streams.Add(stream);
                message.Attachments.Add(new Attachment(stream, file.Name, file.ContentType));
            }
            using var client = new SmtpClient(settings.Server.Trim(), settings.Port)
            {
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = settings.UserName.Trim().Length > 0 ? new NetworkCredential(settings.UserName.Trim(), settings.Password) : null,
                Timeout = 30000,
            };
            client.Send(message);
        }
        finally
        {
            foreach (var stream in streams) stream.Dispose();
        }
    }
}

/// <summary>
/// Sends MyCRMRecords's emails, through the SMTP server on the admin page, and keeps each in
/// dbo.CrEmail: sent, or not sent and why (emails off, nobody to send to, the server refused). Never throws.
/// </summary>
public sealed class Mailer(IRecordStore store, Func<CrmSettings> settings, Func<DateTime> clock, IEmailSender? sender = null)
{
    private readonly IEmailSender _sender = sender ?? new SmtpEmailSender();

    public EmailMessage Send(Email email)
    {
        var s = settings().Email;
        if (Strings.IsEmail(email.From))
        {
            // An email may come from another address than the settings' (none of MyCRMRecords's do yet).
            s = Json.Clone(s);
            s.From = email.From!.Trim();
        }
        var to = email.To.Where(Strings.IsEmail).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var cc = email.Cc.Where(Strings.IsEmail).Where(c => !to.Contains(c, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var record = new EmailMessage
        {
            At = clock(), RequestKind = email.RequestKind, RequestId = email.RequestId,
            To = Strings.Shorten(string.Join(", ", to), 4000), Cc = Strings.Shorten(string.Join(", ", cc), 4000), Subject = Strings.Shorten(email.Subject, 500),
        };
        if (to.Count == 0) record.Error = "Nobody to send it to: no email address for the person (or role) it is for.";
        else if (!s.Enabled) record.Error = "Not sent: emails are switched off (Settings, Email tab).";
        else if (s.Server.Trim().Length == 0 || !Strings.IsEmail(s.From)) record.Error = "Not sent: the email settings are incomplete.";
        else
        {
            try
            {
                var html = email.Html;
                var redirect = s.RedirectTo.Trim();
                if (redirect.Length > 0)
                {
                    html = $"<p style=\"color:#8a5a00\">[Redirected: to {WebUtility.HtmlEncode(string.Join(", ", to))}; cc {WebUtility.HtmlEncode(string.Join(", ", cc))}]</p>" + html;
                    _sender.Send(s, [redirect], Array.Empty<string>(), email.Subject, html, email.Attachments ?? Array.Empty<EmailAttachment>());
                }
                else _sender.Send(s, to, cc, email.Subject, html, email.Attachments ?? Array.Empty<EmailAttachment>());
                record.Sent = true;
            }
            catch (Exception ex)
            {
                record.Error = Strings.Shorten("Could not send: " + ex.Message, 4000);
            }
        }
        try { store.Insert(record); } catch (Exception) { /* the email log is best effort */ }
        return record;
    }
}

/// <summary>Small HTML pieces for the emails' bodies.</summary>
public static class EmailHtml
{
    public static string E(string? text) => WebUtility.HtmlEncode(text ?? "");

    public static string Page(string heading, string body, string link, string linkText) =>
        "<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#18213d\">"
        + $"<h2 style=\"font-size:18px;margin:0 0 12px\">{E(heading)}</h2>{body}"
        + (link.Length > 0 ? $"<p style=\"margin-top:18px\"><a href=\"{E(link)}\" style=\"color:#3346c8;font-weight:bold\">{E(linkText)}</a></p>" : "")
        + "<p style=\"color:#5f6985;font-size:12px;margin-top:18px\">Sent by MyCRMRecords.</p></div>";

    public static string Facts(IEnumerable<(string Label, string? Value)> facts) =>
        "<table style=\"border-collapse:collapse;margin-bottom:12px\">" + string.Concat(facts.Where(f => (f.Value ?? "").Length > 0).Select(f =>
            $"<tr><td style=\"padding:3px 14px 3px 0;color:#5f6985\">{E(f.Label)}</td><td style=\"padding:3px 0;font-weight:600\">{E(f.Value)}</td></tr>")) + "</table>";

    public static string Table(IReadOnlyList<string> headings, IEnumerable<IReadOnlyList<string>> rows) =>
        "<table style=\"border-collapse:collapse;min-width:420px\"><tr>" + string.Concat(headings.Select(h =>
            $"<th style=\"text-align:left;padding:6px 10px;background:#e4e8fc;border:1px solid #d8deea\">{E(h)}</th>")) + "</tr>"
        + string.Concat(rows.Select(r => "<tr>" + string.Concat(r.Select(c => $"<td style=\"padding:6px 10px;border:1px solid #d8deea\">{E(c)}</td>")) + "</tr>"))
        + "</table>";
}
