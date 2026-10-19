using System.Web;
using MyCRMRecords.Api;
using MyCRMRecords.Web.Auth;

namespace MyCRMRecords.Web;

/// <summary>
/// Serves <c>file.ashx/&lt;kind&gt;</c>: GET downloads a feedback's attachment or the statistics report (Excel); POST
/// uploads a feedback's attachment (<c>attachment?feedback=&amp;kind=</c>) as multipart form data with an
/// <c>X-MyCRMRecords</c> header. Browsers cannot add that header cross-site without a CORS preflight, which is never
/// granted, so other sites cannot upload.
/// </summary>
public sealed class FileHandler : IHttpHandler
{
    public bool IsReusable => true;

    public void ProcessRequest(HttpContext context)
    {
        Services.EnsureStarted();
        var request = context.Request;
        var response = context.Response;
        response.Cache.SetCacheability(HttpCacheability.NoCache);
        response.Cache.SetNoStore();
        response.TrySkipIisCustomErrors = true;
        response.SuppressFormsAuthenticationRedirect = true;
        var signedIn = SignIn.Current(context);
        if (signedIn is null)
        {
            response.StatusCode = 401;
            response.ContentType = "text/plain";
            response.Write("Please sign in again.");
            return;
        }
        var user = Admins.Resolve(context, signedIn);
        var kind = (request.PathInfo ?? "").Trim('/');
        var query = request.QueryString.AllKeys.Where(k => k is not null)
            .ToDictionary(k => k!, k => request.QueryString[k] ?? "", StringComparer.OrdinalIgnoreCase);

        if (request.HttpMethod == "GET")
        {
            var file = Services.FilesApi.Download(kind, query, user);
            if (file.Status != 200)
            {
                response.StatusCode = file.Status;
                response.ContentType = "text/plain";
                response.Write(file.Error);
                return;
            }
            response.ContentType = file.ContentType;
            response.AppendHeader("Content-Disposition", "attachment; filename=\"" + file.FileName.Replace("\"", "") + "\"; filename*=UTF-8''" + Uri.EscapeDataString(file.FileName));
            response.BinaryWrite(file.Content);
            return;
        }

        response.ContentType = "application/json";
        if (request.HttpMethod != "POST" || string.IsNullOrEmpty(request.Headers["X-MyCRMRecords"]))
        {
            ApiHandler.Write(context, ApiResponse.Fail(400, "BAD_REQUEST", "Upload the file from the page."));
            return;
        }
        var posted = request.Files.Count > 0 ? request.Files[0] : null;
        ApiResponse result;
        try
        {
            byte[] content;
            using (var memory = new MemoryStream())
            {
                posted?.InputStream.CopyTo(memory);
                content = memory.ToArray();
            }
            result = Services.FilesApi.Upload(kind, query, posted?.FileName ?? "", posted?.ContentType ?? "", content, user);
        }
        catch (Exception ex)
        {
            result = ApiResponse.Fail(500, "SERVER_ERROR", "Something went wrong: " + ex.Message);
        }
        var (ok, message) = ApiHandler.Outcome(result);
        var feedback = query.TryGetValue("feedback", out var f) ? f : "";
        var action = kind switch
        {
            FilesApi.Attachment => $"Attach a file to feedback {feedback}",
            _ => "Upload " + kind,
        };
        Services.Record(context, user, action, "file.ashx/" + kind, ok, message, posted is null ? "" : $"{posted.FileName} ({posted.ContentLength} bytes)", status: result.Status);
        ApiHandler.Write(context, result);
    }
}
