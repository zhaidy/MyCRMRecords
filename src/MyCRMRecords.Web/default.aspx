<%@ Page Language="C#" AutoEventWireup="true" Inherits="MyCRMRecords.Web.AppPage" %>
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1">
  <title>Feedbacks · MyCRMRecords</title>
  <%= Stylesheet() %>
</head>
<body>
  <%= TopBar("./", "feedbacks") %>
  <main id="feedbacks-page">
    <div class="loading" id="loading">Loading…</div>
    <div id="page" hidden></div>
  </main>
  <%= Scripts("feedbacks.js") %>
</body>
</html>
