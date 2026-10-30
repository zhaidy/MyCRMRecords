<%@ Page Language="C#" AutoEventWireup="true" Inherits="MyCRMRecords.Web.AppPage" %>
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
  <title>Company · MyCRMRecords</title>
  <%= Stylesheet() %>
</head>
<body>
  <%= TopBar("./", "a-company") %>
  <main id="company-page">
    <div class="loading" id="loading">Loading…</div>
    <div id="page" hidden></div>
  </main>
  <%= Scripts("crm.js", "activityform.js", "company.js") %>
</body>
</html>
