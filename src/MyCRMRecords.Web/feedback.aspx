<%@ Page Language="C#" AutoEventWireup="true" Inherits="MyCRMRecords.Web.AppPage" %>
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1">
  <title>Feedback · MyCRMRecords</title>
  <%= Stylesheet() %>
</head>
<body>
  <%= TopBar("./", "a-feedback") %>
  <main id="feedback-page">
    <div class="loading" id="loading">Loading…</div>
    <div id="page" hidden></div>
  </main>
  <%= Scripts("feedback.js") %>
</body>
</html>
