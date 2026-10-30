<%@ Page Language="C#" AutoEventWireup="true" Inherits="MyCRMRecords.Web.AppPage" %>
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
  <title>Project · MyCRMRecords</title>
  <%= Stylesheet() %>
</head>
<body>
  <%= TopBar("projects.aspx", "a-project") %>
  <main id="project-page">
    <div class="loading" id="loading">Loading…</div>
    <div id="page" hidden></div>
  </main>
  <%= Scripts("activityform.js", "project.js") %>
</body>
</html>
