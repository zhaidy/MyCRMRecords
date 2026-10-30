using System.Web;
using MyCRMRecords.Api;
using MyCRMRecords.Web.Auth;

namespace MyCRMRecords.Web;

/// <summary>
/// Serves <c>file.ashx/&lt;kind&gt;</c>: GET downloads an Excel sample to fill in (<c>sample?of=employees</c> or
/// <c>sample?of=territory</c>), a costing sheet's or request form's attachment, or a file of MyFiles (<c>files/{id}</c>,
/// shown in the page with <c>?view=1</c>; <c>files/zip?ids=</c> for several, recorded in the audit log); POST uploads an Excel workbook (<c>employees</c>, or <c>territory-list?territory=</c>)
/// as multipart form data with an
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
            response.AppendHeader("Content-Disposition", (file.Inline ? "inline" : "attachment") + "; filename=\"" + file.FileName.Replace("\"", "") + "\"; filename*=UTF-8''" + Uri.EscapeDataString(file.FileName));
            // A file of MyFiles downloaded (not shown in the page) is recorded, as MyFile records it.
            if (!file.Inline && kind.StartsWith(FilesApi.MyFilesPrefix, StringComparison.Ordinal))
                Services.Record(context, user, kind == FilesApi.MyFilesZip ? "Download files of MyFiles as a zip" : "Download a file of MyFiles", "file.ashx/" + kind, true,
                    file.FileName, request.Url.Query, method: "GET", target: kind == FilesApi.MyFilesZip ? (query.TryGetValue("ids", out var ids) ? ids : "") : kind.Substring(FilesApi.MyFilesPrefix.Length));
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
        var territory = query.TryGetValue("territory", out var t) ? t : "";
        var action = kind switch
        {
            FilesApi.Employees => "Upload the number of employees",
            FilesApi.TerritoryList => $"Find an Excel list of companies for territory {territory}",
            _ => "Upload " + kind,
        };
        Services.Record(context, user, action, "file.ashx/" + kind, ok, message, posted is null ? "" : $"{posted.FileName} ({posted.ContentLength} bytes)", status: result.Status);
        ApiHandler.Write(context, result);
    }
}
