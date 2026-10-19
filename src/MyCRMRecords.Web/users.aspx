<%@ Page Language="C#" AutoEventWireup="true" Inherits="MyCRMRecords.Web.AdminPage" %>
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1">
  <title>User roles · MyCRMRecords</title>
  <%= Stylesheet() %>
</head>
<body>
  <%= TopBar("users.aspx", "user-roles") %>
  <main id="users-page" data-login="<%: Login %>">
    <div class="loading" id="loading">Loading…</div>
    <div id="page" hidden></div>
  </main>
  <%= Scripts("users.js") %>
</body>
</html>
