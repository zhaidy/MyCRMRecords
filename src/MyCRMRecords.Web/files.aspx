<%@ Page Language="C#" AutoEventWireup="true" Inherits="MyCRMRecords.Web.AppPage" %>
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
  <title>Files · MyCRMRecords</title>
  <%= Stylesheet() %>
</head>
<body>
  <%= TopBar("files.aspx", "files") %>
  <main id="files-page">
    <div class="loading" id="loading">Loading…</div>
    <div id="page" hidden></div>
  </main>
  <%= Scripts("files.js") %>
</body>
</html>
