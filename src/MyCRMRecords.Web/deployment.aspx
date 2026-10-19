<%@ Page Language="C#" AutoEventWireup="true" Inherits="MyCRMRecords.Web.AdminPage" %>
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1">
  <title>Deployment · MyCRMRecords</title>
  <%= Stylesheet() %>
</head>
<body>
  <%= TopBar("deployment.aspx", "deployment") %>
  <main class="admin-main" id="deployment-page" data-login="<%: Login %>">
    <div class="loading" id="loading">Loading…</div>
    <div id="page" hidden></div>
  </main>
  <%= Scripts("deployment.js") %>
</body>
</html>
