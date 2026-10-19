<%@ Page Language="C#" AutoEventWireup="true" Inherits="MyCRMRecords.Web.AdminPage" %>
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1">
  <title>Audit log · MyCRMRecords</title>
  <%= Stylesheet() %>
</head>
<body>
  <%= TopBar("audit.aspx", "audit-log") %>
  <main id="audit-page" data-login="<%: Login %>">
    <div class="loading" id="loading">Loading…</div>
    <div id="page" hidden></div>
  </main>
  <%= Scripts("audit.js") %>
</body>
</html>
