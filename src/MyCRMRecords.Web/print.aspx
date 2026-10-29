<%@ Page Language="C#" AutoEventWireup="true" Inherits="MyCRMRecords.Web.AppPage" %>
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1">
  <title>Costing sheet · MyCRMRecords</title>
  <link rel="stylesheet" href="<%: MyCRMRecords.Web.Assets.Url("print.css") %>">
</head>
<body>
  <div class="print-bar" id="print-bar">
    <a href="./">Costing sheets</a>
    <a id="back" href="./">Back to the costing sheet</a>
    <span class="grow"></span>
    <span class="layouts" role="group" aria-label="Layout">
      <button type="button" data-format="new" aria-pressed="true">New CS</button><button type="button" data-format="old" aria-pressed="false">Old CS</button>
    </span>
    <button type="button" class="primary" id="print">Print / Save as PDF</button>
  </div>
  <div id="doc"><p class="loading">Loading…</p></div>
  <script src="<%: MyCRMRecords.Web.Assets.Url("common.js") %>"></script>
  <script src="<%: MyCRMRecords.Web.Assets.Url("print.js") %>"></script>
</body>
</html>
