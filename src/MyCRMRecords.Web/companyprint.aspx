<%@ Page Language="C#" AutoEventWireup="true" Inherits="MyCRMRecords.Web.AppPage" %>
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
  <title>Company summary · MyCRMRecords</title>
  <link rel="stylesheet" href="<%: MyCRMRecords.Web.Assets.Url("companyprint.css") %>">
</head>
<body>
  <div class="print-bar" id="print-bar">
    <a href="./">Companies</a>
    <a id="back" href="./">Back to the company</a>
    <span class="grow"></span>
    <button type="button" class="primary" id="print">Print / Save as PDF</button>
  </div>
  <div id="doc"><p class="loading">Loading…</p></div>
  <script src="<%: MyCRMRecords.Web.Assets.Url("common.js") %>"></script>
  <script src="<%: MyCRMRecords.Web.Assets.Url("companyprint.js") %>"></script>
</body>
</html>
