<%@ Page Language="C#" AutoEventWireup="true" Inherits="MyCRMRecords.Web.PeoplePage" %>
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
  <title>CRM users · MyCRMRecords</title>
  <%= Stylesheet() %>
</head>
<body>
  <%= TopBar("people.aspx", "crm-users") %>
  <main id="people-page">
    <div class="loading" id="loading">Loading…</div>
    <div id="page" hidden></div>
  </main>
  <%= Scripts("crm.js", "people.js") %>
</body>
</html>
