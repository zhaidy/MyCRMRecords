<%@ Page Language="C#" AutoEventWireup="true" Inherits="MyCRMRecords.Web.ListsPage" %>
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1">
  <title>Lists · MyCRMRecords</title>
  <%= Stylesheet() %>
</head>
<body>
  <%= TopBar("lists.aspx", "lists") %>
  <main id="lists-page">
    <div class="loading" id="loading">Loading…</div>
    <div id="page" hidden></div>
  </main>
  <%= Scripts("lists.js") %>
</body>
</html>
