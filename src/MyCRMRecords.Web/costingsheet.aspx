<%@ Page Language="C#" AutoEventWireup="true" Inherits="MyCRMRecords.Web.AppPage" %>
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1">
  <title>Costing sheet · MyCRMRecords</title>
  <%= Stylesheet() %>
</head>
<body>
  <%= TopBar("costingsheets.aspx", "a-costing-sheet") %>
  <main id="costing-sheet-page">
    <div class="loading" id="loading">Loading…</div>
    <div id="page" hidden></div>
  </main>
  <%= Scripts("costingsheet.js") %>
</body>
</html>
