<%@ Page Language="C#" AutoEventWireup="true" Inherits="MyCRMRecords.Web.AppPage" %>
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
  <title>Machines · MyCRMRecords</title>
  <%= Stylesheet() %>
</head>
<body>
  <%= TopBar("machines.aspx", "machines") %>
  <main id="machines-page">
    <div class="loading" id="loading">Loading…</div>
    <div id="page" hidden></div>
  </main>
  <%= Scripts("machines.js") %>
</body>
</html>
