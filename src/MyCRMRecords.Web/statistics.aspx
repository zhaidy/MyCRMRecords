<%@ Page Language="C#" AutoEventWireup="true" Inherits="MyCRMRecords.Web.AppPage" %>
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1">
  <title>Statistics · MyCRMRecords</title>
  <%= Stylesheet() %>
</head>
<body>
  <%= TopBar("statistics.aspx", "statistics") %>
  <main id="statistics-page">
    <div class="loading" id="loading">Loading…</div>
    <div id="page" hidden></div>
  </main>
  <%= Scripts("statistics.js") %>
</body>
</html>
