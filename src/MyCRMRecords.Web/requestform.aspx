<%@ Page Language="C#" AutoEventWireup="true" Inherits="MyCRMRecords.Web.AppPage" %>
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
  <title>Request form · MyCRMRecords</title>
  <%= Stylesheet() %>
</head>
<body>
  <%= TopBar("requestforms.aspx", "a-request-form") %>
  <main id="request-form-page">
    <div class="loading" id="loading">Loading…</div>
    <div id="page" hidden></div>
  </main>
  <%= Scripts("requestform.js") %>
</body>
</html>
