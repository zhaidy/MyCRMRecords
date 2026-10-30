<%@ Page Language="C#" AutoEventWireup="true" Inherits="MyCRMRecords.Web.LoginPage" %>
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
  <title>Sign in · MyCRMRecords</title>
  <link rel="stylesheet" href="<%: MyCRMRecords.Web.Assets.Url("app.css") %>">
  <%= MyCRMRecords.Web.Brand.TabIcon %><%= MyCRMRecords.Web.Brand.AppLinks %>
</head>
<body class="login-body">
  <div class="login-main" role="main">
    <form class="login-card" method="post" autocomplete="on">
      <div class="brand"><%= MyCRMRecords.Web.Brand.Mark %><span>MyCRMRecords</span></div>
      <h2>Sign in</h2>
      <p>Use your Windows (domain) username and password, the same as Report Generator and MyAllocation.</p>
      <% if (Message.Length > 0) { %>
      <div class="login-error" role="alert"><%: Message %></div>
      <% } %>
      <label class="field"><span>Username</span><input name="username" value="<%: Username %>" placeholder="e.g. jsmith, <%: Domain %>\jsmith or your email" autocomplete="username" required autofocus maxlength="256"></label>
      <label class="field"><span>Password</span><input name="password" type="password" autocomplete="current-password" required></label>
      <label class="check"><input type="checkbox" name="remember" <%= Remember ? "checked" : "" %>>Remember me for 7 days</label>
      <button class="button primary login-submit" type="submit">Sign in</button>
    </form>
  </div>
</body>
</html>
