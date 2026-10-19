<%@ Page Language="C#" AutoEventWireup="true" Inherits="MyCRMRecords.Web.AppPage" %>
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1">
  <title>User guide · MyCRMRecords</title>
  <%= Stylesheet() %>
</head>
<body>
  <%= TopBar("guide.aspx") %>
  <main class="guide-main">
    <div class="page-heading"><div><h1>User guide</h1><p class="guide-lead">How to use MyCRMRecords, page by page. The ? button at the top right of every page opens this guide beside the page.</p></div></div>
    <div class="guide-layout" id="guide-page">
      <aside class="guide-side" aria-label="Contents and search">
        <div class="search-box"><span data-icon="search"></span><input id="guide-search" type="search" placeholder="Search the guide…" aria-label="Search the guide" autocomplete="off"><kbd>/</kbd></div>
        <p class="guide-search-status" id="guide-search-status" aria-live="polite" hidden></p>
        <details class="guide-contents" id="guide-contents" open><summary>Contents</summary><nav class="guide-toc" id="guide-toc" aria-label="Contents"></nav></details>
      </aside>
      <article class="guide card" id="guide-body"><p class="guide-loading">Loading the guide…</p></article>
    </div>
    <%-- The guide itself (docs/user-guide.md, built into MyCRMRecords.Core.dll); guide.js shows it, here and in the panel of the other pages. --%>
    <pre id="guide-source" hidden><%: MyCRMRecords.UserGuide.Markdown %></pre>
  </main>
  <%= Scripts() %>
</body>
</html>
