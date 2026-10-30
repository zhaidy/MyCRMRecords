<%@ Page Language="C#" AutoEventWireup="true" Inherits="MyCRMRecords.Web.TerritoriesPage" %>
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
  <title>Territories · MyCRMRecords</title>
  <%= Stylesheet() %>
</head>
<body>
  <%= TopBar("territories.aspx", "territories") %>
  <main id="territories-page">
    <div class="loading" id="loading">Loading…</div>
    <div id="page" hidden></div>
  </main>
  <%= Scripts("crm.js", "territories.js") %>
</body>
</html>
