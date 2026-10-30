<%@ Page Language="C#" AutoEventWireup="true" Inherits="MyCRMRecords.Web.AppPage" %>
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
  <title>Prospects · MyCRMRecords</title>
  <%= Stylesheet() %>
</head>
<body>
  <%= TopBar("prospects.aspx", "prospects") %>
  <main id="prospects-page">
    <div class="loading" id="loading">Loading…</div>
    <div id="page" hidden></div>
  </main>
  <%= Scripts("prospects.js") %>
</body>
</html>
