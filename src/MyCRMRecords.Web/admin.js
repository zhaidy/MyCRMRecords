(() => {
  'use strict';
  // admin.aspx (administrators): where MyCRMRecords keeps its data and its data migrations; the OutSystems database
  // (MyCRM's tables) and AdvanceConnector (Advance's customers); email and the emails sent; the scheduled tasks;
  // whether MyCRMRecords may change MyCRM's data, and every value it changed there.
  // Passwords and keys are never shown: a blank one keeps the one saved.
  const {$, $$, esc, api, toast, ask, date, dateTime, dateInput, showErrors, drawIcons} = window.WH;
  const page = $('#page');
  const TABS = [['database', 'Database'], ['migrations', 'Data migrations'], ['connections', 'Connections'], ['email', 'Email'],
    ['jobs', 'Scheduled tasks'], ['mycrm', 'MyCRM'], ['speed', 'Speed']];
  let tab = TABS.some(([id]) => id === location.hash.slice(1)) ? location.hash.slice(1) : 'database';

  function shell() {
    $('#loading').hidden = true;
    page.hidden = false;
    page.innerHTML = `<div class="page-heading"><div><h1>Settings</h1><p>Everything MyCRMRecords sends or changes outside itself starts switched off.</p></div></div>
      <div class="page-tabs" role="tablist">${TABS.map(([id, label]) => `<button class="page-tab" role="tab" data-tab="${id}" aria-selected="${id === tab}">${esc(label)}</button>`).join('')}</div>
      <div id="tab" class="stack"><div class="loading">Loading…</div></div>`;
    show();
  }
  async function show() {
    $$('.page-tab').forEach(b => b.setAttribute('aria-selected', String(b.dataset.tab === tab)));
    history.replaceState(null, '', '#' + tab);
    $('#tab').innerHTML = '<div class="loading">Loading…</div>';
    await {database, migrations, connections, email, jobs, mycrm, speed}[tab]();
    drawIcons($('#tab'));
  }
  page.addEventListener('click', event => { const t = event.target.closest('[data-tab]'); if (t) { tab = t.dataset.tab; show(); } });

  // ---- form helpers: inputs named by their path in a settings section ("production.url") ----
  const get = (obj, path) => path.split('.').reduce((o, k) => (o == null ? undefined : o[k]), obj);
  function put(obj, path, value) {
    const keys = path.split('.');
    const last = keys.pop();
    keys.reduce((o, k) => (o[k] ??= {}), obj)[last] = value;
  }
  function field(section, path, label, {type = 'text', wide = false, hint = '', options = null, required = false} = {}) {
    const value = get(section, path);
    const span = `<span class="${required ? 'required' : ''}">${esc(label)}</span>`;
    const help = hint ? `<small class="muted">${esc(hint)}</small>` : '';
    if (type === 'check') return `<label class="check${wide ? ' wide' : ''}"><input type="checkbox" data-path="${path}" data-type="check"${value ? ' checked' : ''}>${esc(label)}</label>`;
    if (type === 'secret') {
      const isSet = get(section, path.replace(/([^.]+)$/, '$1Set'));
      return `<label class="field${wide ? ' wide' : ''}">${span}<input type="password" data-path="${path}" data-type="secret" data-field="${path}" autocomplete="new-password" placeholder="${isSet ? 'Saved; type to change it' : 'Not set'}">${help}<span class="field-error"></span></label>`;
    }
    if (options) return `<label class="field${wide ? ' wide' : ''}">${span}<select data-path="${path}" data-type="text">${options.map(([v, l]) => `<option value="${esc(v)}"${v === value ? ' selected' : ''}>${esc(l)}</option>`).join('')}</select>${help}</label>`;
    const shown = type === 'date' ? dateInput(value) : value ?? '';
    return `<label class="field${wide ? ' wide' : ''}">${span}<input type="${type === 'number' ? 'number' : type === 'date' ? 'date' : type === 'time' ? 'time' : 'text'}" data-path="${path}" data-type="${type}" value="${esc(shown)}" data-field="${path}">${help}<span class="field-error"></span></label>`;
  }
  function collect(root, section) {
    const copy = JSON.parse(JSON.stringify(section));
    $$('[data-path]', root).forEach(input => {
      const type = input.dataset.type;
      const value = type === 'check' ? input.checked : type === 'number' ? Number(input.value || 0) : type === 'date' ? (input.value || null) : input.value;
      put(copy, input.dataset.path, type === 'text' || type === 'secret' || type === 'time' ? String(value).trim() : value);
    });
    return copy;
  }
  function report(target, answer) {
    target.hidden = false;
    target.className = 'notice' + (answer.result.ok ? '' : ' error');
    target.textContent = answer.result.message;
  }
  async function saveSection(name, body, result) {
    const answer = await api('POST', 'admin/settings/' + name, body);
    if (result) report(result, answer); else toast(answer.result.message, !answer.result.ok);
    return answer;
  }
  const sqlFields = (s, path) => `<div class="form-grid two">
    ${field(s, path + '.server', 'Server', {required: true, hint: 'SQLSERVER01 or SQLSERVER01\\INSTANCE'})}
    ${field(s, path + '.database', 'Database', {required: true})}
    ${field(s, path + '.authentication', 'Authentication', {options: [['Windows', "Windows (the IIS application pool's account)"], ['SqlLogin', 'SQL Server login']]})}
    ${field(s, path + '.userName', 'Login name')}
    ${field(s, path + '.password', 'Password', {type: 'secret'})}
    <div class="field"><span>Connection</span>${field(s, path + '.encrypt', 'Encrypt', {type: 'check'})}${field(s, path + '.trustServerCertificate', 'Trust the server certificate', {type: 'check'})}</div></div>`;
  const loadSettings = () => api('GET', 'admin/settings');

  // ---- Database ----
  async function database() {
    const d = await api('GET', 'admin/database');
    const sql = {server: d.sqlServer.server, database: d.sqlServer.database, authentication: d.sqlServer.authentication, userName: d.sqlServer.userName,
      passwordSet: d.sqlServer.hasPassword, encrypt: d.sqlServer.encrypt, trustServerCertificate: d.sqlServer.trustServerCertificate};
    $('#tab').innerHTML = `
      <section class="card"><h2>Where MyCRMRecords keeps its data</h2>
        <p class="muted">Now: <b>${d.provider === 'SqlServer' ? 'SQL Server' : 'the JSON data files'}</b>${d.changedBy ? ` (set by ${esc(d.changedBy)}, ${esc(dateTime(d.changedAt))})` : ''}.
        ${d.provider === 'SqlServer' ? '' : `The files are in <code>${esc(d.dataFolder)}</code>.`}</p>
        ${d.error ? `<div class="notice error">${esc(d.error)}</div>` : ''}
        <form id="db-form" novalidate style="margin-top:14px">
          <label class="check"><input type="radio" name="provider" value="JsonFile"${d.provider !== 'SqlServer' ? ' checked' : ''}>JSON data files (development, and until SQL Server is set up)</label>
          <label class="check" style="margin:6px 0 14px"><input type="radio" name="provider" value="SqlServer"${d.provider === 'SqlServer' ? ' checked' : ''}>SQL Server</label>
          ${sqlFields({sql}, 'sql')}
          <label class="check" style="margin-top:14px"><input type="checkbox" name="copyData">Copy the data in use now into it, even when it already holds data</label>
          <div class="action-buttons"><button type="button" class="button secondary" id="db-test">Test connection</button><button type="submit" class="button primary">Save and switch</button></div>
          <div class="notice" id="db-result" hidden></div>
        </form>
        <p class="muted small">This is MyCRMRecords's own database, not MyCRM's (that one is on the Connections tab). Switching to a SQL Server database runs the data migrations it has not run (a new database gets every table; this needs CREATE TABLE permission once), and copies the data in use into it when it is empty. The password is encrypted for this server.</p>
      </section>
      ${d.tables.length ? `<section class="card"><h2>Tables</h2><div class="table-wrap"><table class="data"><thead><tr><th>Table</th><th class="num">Rows</th></tr></thead>
        <tbody>${d.tables.map(t => `<tr><td><code>${esc(t.name)}</code></td><td class="num">${t.rows < 0 ? 'missing' : t.rows}</td></tr>`).join('')}</tbody></table></div></section>` : ''}
`;
    const form = $('#db-form');
    const body = () => {
      const s = collect(form, {sql}).sql;
      return {provider: form.elements.provider.value, sqlServer: {...s, password: s.password}, copyData: form.elements.copyData.checked};
    };
    const errors = a => showErrors(form, (a.result.errors || []).map(e => ({...e, field: 'sql.' + e.field})));
    $('#db-test').addEventListener('click', async () => { const a = await api('POST', 'admin/database/test', body()); errors(a); report($('#db-result'), a); });
    form.addEventListener('submit', async event => {
      event.preventDefault();
      if (!await ask({title: 'Save and switch?', text: 'MyCRMRecords keeps its data in the chosen place from now on.', ok: 'Switch'})) return;
      const a = await api('POST', 'admin/database', body());
      errors(a);
      report($('#db-result'), a);
      if (a.result.ok) setTimeout(show, 1500);
    });
  }

  // ---- Data migrations ----
  async function migrations() {
    const d = await api('GET', 'admin/migrations');
    $('#tab').innerHTML = `<section class="card"><h2>Data migrations</h2>
      <p class="muted">Changes to MyCRMRecords's database ship as data migrations, run here; nobody pastes SQL into SQL Server Management Studio. Each runs in one transaction with its record, so a failure changes nothing.</p>
      ${d.sqlServer ? '' : '<div class="notice warn">The data is in the JSON files: migrations run on SQL Server.</div>'}
      ${d.error ? `<div class="notice error">${esc(d.error)}</div>` : ''}
      <div class="table-wrap" style="margin-top:14px"><table class="data"><thead><tr><th>Migration</th><th>Status</th><th></th></tr></thead><tbody>
      ${d.migrations.map(m => `<tr><td><b>${esc(m.title)}</b><span class="sub"><code>${esc(m.id)}</code> ${esc(m.description)}</span>
          <details><summary class="text-button">SQL</summary><pre class="log">${esc(m.sql)}</pre></details></td>
        <td>${m.applied ? `<span class="yes">Ran</span><span class="sub">${esc(m.appliedBy)}, ${esc(dateTime(m.appliedAtUtc))} UTC</span>` : '<span class="pill warn">Not run</span>'}</td>
        <td class="actions">${!m.applied && d.sqlServer ? `<button class="button primary small-btn" data-run="${esc(m.id)}">Run migration</button>` : ''}</td></tr>`).join('')}
      </tbody></table></div></section>`;
    $$('[data-run]').forEach(button => button.addEventListener('click', async () => {
      if (!await ask({title: 'Run this migration?', ok: 'Run migration'})) return;
      button.disabled = true;
      const a = await api('POST', 'admin/migrations/run', {name: button.dataset.run});
      toast(a.result.message, !a.result.ok);
      show();
    }));
  }

  // ---- Connections ----
  async function connections() {
    const {settings, databases} = await loadSettings();
    const c = settings.connections;
    const key = name => name.charAt(0).toLowerCase() + name.slice(1);
    const uses = {
      OutSystems: "MyCRM's Users, Territory and Company tables (CRM users and teams, companies with their addresses, contacts, phones and emails, permanent territories and their companies, territory movements) and its users: MyCRMRecords reads them where MyCRM keeps them, and changes them once Let MyCRMRecords change MyCRM's data is on (MyCRM tab: adding and changing rows, never deleting). MyCRM's temporary territories (projects, CSST) are read to decide who may open a company. MyOrders' costing sheets and request forms (with their items and attachments) and MyFiles' files (the Files page) are only read, never changed: a SELECT is all they need. MyMachines' temporary access (TempAccess) is read for who sees which machines, and changed only once Let MyCRMRecords change MyMachines' temporary access is on (MyCRM tab).",
      AdvanceConnector: "Advance's customers (Customer_Supp: Business_reg, Cust_no), read only: the scheduled task Customer numbers from Advance fills each company's customer number from them. And Advance's machines (daily_mif, serial, customer, meter_card, Service, SMeter_History, daily_TM), read only, for the Machines pages.",
    };
    $('#tab').innerHTML = `<form id="conn-form" novalidate class="stack">
      <p class="muted">The OutSystems database (MyCRM's tables, MyOrders' for the costing sheets and request forms, and MyFiles' for the Files page) and AdvanceConnector. Reading needs SELECT; changing MyCRM's data (MyCRM tab) also needs INSERT and UPDATE on MyCRM's tables, never DELETE. Test saves the tab, then checks every table and column MyCRMRecords uses.</p>
      ${databases.map(d => `<section class="card"><div class="card-heading"><h2>${esc(d.title)}</h2>${d.configured ? '<span class="pill">Set up</span>' : '<span class="pill warn">Not set up</span>'}</div>
        <p class="muted small">${esc(uses[d.name] || '')}</p>
        <div style="margin:12px 0">${field(c, key(d.name) + '.configured', 'Use this database', {type: 'check'})}</div>
        ${sqlFields(c, key(d.name) + '.sqlServer')}
        <div class="action-buttons"><button type="button" class="button secondary small-btn" data-test="${esc(d.name)}">Test</button></div><div data-test-result="${esc(d.name)}"></div></section>`).join('')}
      <section class="card"><details><summary class="text-button">Names in those databases</summary>
        <div class="form-grid two" style="margin-top:12px">
          ${field(c, 'crmModule', 'MyCRM module')}${field(c, 'ordersModule', 'MyOrders module')}${field(c, 'filesModule', 'MyFiles module')}${field(c, 'machinesModule', 'MyMachines module')}${field(c, 'advanceTables.customerSupp', 'Customers with their BRN (AdvanceConnector)')}
          ${field(c, 'advanceTables.dailyMif', 'Machines: daily_mif')}${field(c, 'advanceTables.serial', 'Machines: serial')}${field(c, 'advanceTables.customer', 'Machines: customer')}
          ${field(c, 'advanceTables.meterCard', 'Machines: meter_card')}${field(c, 'advanceTables.service', 'Machines: Service')}${field(c, 'advanceTables.sMeterHistory', 'Machines: SMeter_History')}
          ${field(c, 'advanceTables.dailyTm', 'Machines: daily_TM')}${field(c, 'advanceTables.customerMapping', 'Invoice / SOA emails: CustomerMapping')}${field(c, 'advanceTables.emailMaster', 'Invoice / SOA emails: Email_Master')}
        </div></details></section>
      <div class="action-buttons"><button type="submit" class="button primary">Save</button></div>
      <div class="notice" id="conn-result" hidden></div></form>`;
    const form = $('#conn-form');
    form.addEventListener('submit', async event => { event.preventDefault(); const a = await saveSection('connections', collect(form, c), $('#conn-result')); if (a.result.ok) setTimeout(show, 1200); });
    $$('[data-test]').forEach(button => button.addEventListener('click', async () => {
      const target = $(`[data-test-result="${button.dataset.test}"]`);
      // What is on the screen is saved first: the test uses the saved settings.
      target.innerHTML = '<p class="loading">Saving…</p>';
      const saved = await saveSection('connections', collect(form, c), $('#conn-result'));
      if (!saved.result.ok) { target.innerHTML = ''; return; }
      target.innerHTML = '<p class="loading">Testing…</p>';
      const a = await api('POST', 'admin/connections/test', {name: button.dataset.test});
      target.innerHTML = `<div class="notice${a.result.ok ? '' : ' error'}" style="margin-top:10px">${esc(a.result.message)}</div>
        ${a.lines ? `<ul class="check-lines">${a.lines.map(l => `<li class="${l.ok ? 'good' : 'bad'}">${l.ok ? '✓' : '✗'} ${esc(l.text)}</li>`).join('')}</ul>` : ''}`;
    }));
  }

  // ---- Email ----
  async function email() {
    const {settings} = await loadSettings();
    const e = settings.email;
    $('#tab').innerHTML = `<form id="email-form" novalidate class="stack"><section class="card"><h2>Email</h2>
      <p class="muted">MyCRM's Users, Territory and Company screens send no emails, so MyCRMRecords sends none of its own yet; these settings are for the test email and any later notice. Off, emails are only written to the list below.</p>
      <div class="switches" style="margin-top:12px">${field(e, 'enabled', 'Send emails', {type: 'check'})}</div>
      <div class="form-grid two" style="margin-top:12px">
        ${field(e, 'server', 'SMTP server')}${field(e, 'port', 'Port', {type: 'number'})}
        ${field(e, 'userName', 'User name (blank: none)')}${field(e, 'password', 'Password', {type: 'secret'})}
        ${field(e, 'from', 'From address')}${field(e, 'fromName', 'From name')}
        ${field(e, 'siteUrl', "MyCRMRecords's address, for the links")}${field(e, 'redirectTo', 'Send every email only to (for trying it out)')}
      </div>
      <div class="action-buttons"><input id="test-to" placeholder="Test address" style="max-width:260px"><button type="button" class="button secondary" id="email-test">Send a test email</button><span class="grow"></span><button type="submit" class="button primary">Save</button></div>
      <div class="notice" id="email-result" hidden></div></section></form>`;
    $('#email-form').addEventListener('submit', async event => { event.preventDefault(); await saveSection('email', collect($('#email-form'), e), $('#email-result')); });
    $('#email-test').addEventListener('click', async () => report($('#email-result'), await api('POST', 'admin/email/test', {name: $('#test-to').value.trim()})));
    $('#tab').insertAdjacentHTML('beforeend', `<section class="card"><h2>Emails</h2><p class="muted">Every email, newest first: sent, or not sent and why.</p>
      <div class="toolbar"><label class="search-box" style="flex:1"><span data-icon="search"></span><input id="emails-q" placeholder="Search address, subject or error"></label></div>
      <div id="emails"></div></section>`);
    let skip = 0, timer = 0;
    async function emails() {
      const c = await api('GET', 'admin/emails?' + new URLSearchParams({q: $('#emails-q').value, skip}));
      $('#emails').innerHTML = c.rows.length ? `<div class="table-wrap" style="margin-top:12px"><table class="data"><thead><tr><th>When</th><th>Subject</th><th>To</th><th>Sent</th></tr></thead><tbody>
        ${c.rows.map(r => `<tr><td class="nowrap small">${esc(dateTime(r.at))}</td><td>${esc(r.subject)}</td><td class="small">${esc(r.to)}${r.cc ? `<span class="sub">cc ${esc(r.cc)}</span>` : ''}</td>
          <td>${r.sent ? '<span class="yes">Sent</span>' : `<span class="no">Not sent</span><span class="sub">${esc(r.error)}</span>`}</td></tr>`).join('')}
        </tbody></table></div><p class="muted small">${c.total} emails${c.total > 50 ? `, showing ${skip + 1} to ${Math.min(skip + 50, c.total)}` : ''}.</p>
        ${c.total > 50 ? `<div class="toolbar"><button class="button secondary small-btn" id="emails-prev"${skip ? '' : ' disabled'}>Newer</button><button class="button secondary small-btn" id="emails-next"${skip + 50 < c.total ? '' : ' disabled'}>Older</button></div>` : ''}`
        : '<p class="muted">None yet.</p>';
      $('#emails-prev')?.addEventListener('click', () => { skip = Math.max(0, skip - 50); emails(); });
      $('#emails-next')?.addEventListener('click', () => { skip += 50; emails(); });
    }
    $('#emails-q').addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(() => { skip = 0; emails(); }, 300); });
    emails();
  }

  // ---- Scheduled tasks ----
  async function jobs() {
    const d = await api('GET', 'admin/jobs');
    if (!d.result.ok) { $('#tab').innerHTML = `<div class="notice error">${esc(d.result.message)}</div>`; return; }
    $('#tab').innerHTML = `<form id="jobs-form" class="stack"><section class="card"><h2>Scheduled tasks</h2>
      <p class="muted">Tasks MyCRMRecords runs by itself (MyCRM's timers that are kept). All start switched off. Daily times are Singapore time; days are numbers from 0 (Sunday) to 6, separated by commas (blank: every day).</p>
      <div class="switches" style="margin-top:12px"><label class="check"><input type="checkbox" id="jobs-enabled"${d.enabled ? ' checked' : ''}>Run the scheduled tasks</label></div></section>
      ${d.jobs.map(j => `<section class="card" data-job="${esc(j.job)}"><div class="card-heading"><h2>${esc(j.title)}</h2>
          <button type="button" class="button secondary small-btn" data-run-job="${esc(j.job)}"><span data-icon="refresh"></span>Run now</button></div>
        ${j.job === 'customer-numbers' ? '<p class="muted small">MyCRM\'s UpdateCustomerNo: each active company whose BRN has more than 3 characters gets the customer number of Advance\'s customers with that BRN (with " #Multiple" after it when several share it). Needs AdvanceConnector (Connections tab). Switch MyCRM\'s own timer off when this one runs.</p>' : ''}
        <div class="form-grid" style="margin-top:10px">
          <label class="check"><input type="checkbox" data-j="enabled"${j.schedule.enabled ? ' checked' : ''}>On</label>
          <label class="field"><span>Daily at (HH:mm)</span><input data-j="dailyAt" type="time" value="${esc(j.schedule.dailyAt || '')}"></label>
          <label class="field"><span>Or every (minutes)</span><input data-j="everyMinutes" type="number" min="0" value="${j.schedule.everyMinutes || 0}"></label>
          <label class="field"><span>Days</span><input data-j="days" value="${esc(j.schedule.days || '')}" placeholder="Every day"></label></div>
        <p class="muted small" style="margin:10px 0 0">${j.lastStart ? `Last started ${esc(dateTime(j.lastStart))}.` : 'Not run yet.'}</p>
        ${j.runs.length ? `<ul class="check-lines">${j.runs.map(r => `<li class="${r.outcome === 'Failed' ? 'bad' : r.outcome === 'Done' ? 'good' : ''}">${esc(dateTime(r.startedAt))} by ${esc(r.by)} · <b>${esc(r.outcome)}</b> · ${esc(r.summary)}</li>`).join('')}</ul>` : ''}</section>`).join('')}
      <div class="action-buttons"><button type="submit" class="button primary">Save</button></div><div class="notice" id="jobs-result" hidden></div></form>`;
    const form = $('#jobs-form');
    form.addEventListener('submit', async event => {
      event.preventDefault();
      const schedules = $$('[data-job]', form).map(card => ({job: card.dataset.job, enabled: $('[data-j=enabled]', card).checked,
        dailyAt: $('[data-j=dailyAt]', card).value, everyMinutes: Number($('[data-j=everyMinutes]', card).value || 0), days: $('[data-j=days]', card).value.trim()}));
      await saveSection('jobs', {enabled: $('#jobs-enabled').checked, schedules}, $('#jobs-result'));
    });
    $$('[data-run-job]', form).forEach(button => button.addEventListener('click', async () => {
      button.disabled = true;
      const a = await api('POST', 'admin/jobs/run', {name: button.dataset.runJob});
      toast(a.result.message, !a.result.ok);
      show();
    }));
  }

  // ---- MyCRM: whether MyCRMRecords may change its data, and what it changed ----
  // How long MyCRMRecords' answers take (since the website started), the slowest first: the aim is 0.3 s.
  async function speed() {
    const answer = await api('GET', 'admin/speed');
    if (!answer.result.ok) { $('#tab').innerHTML = `<div class="notice error">${esc(answer.result.message)}</div>`; return; }
    const ms = v => v >= 1000 ? (v / 1000).toFixed(1) + ' s' : v + ' ms';
    const slow = v => v > answer.aimMs ? ' class="speed-slow"' : '';
    $('#tab').innerHTML = `<section class="card">
        <div class="card-heading"><h2>Speed</h2><button type="button" class="button secondary small-btn" id="speed-clear">Start again</button></div>
        <p class="muted">How long each kind of request took since ${esc(dateTime(answer.since))} (the website's start or <strong>Start again</strong>), the slowest first. The aim is ${ms(answer.aimMs)}.
          <strong>Typical</strong>: half were faster; <strong>Slow</strong>: 9 in 10 were faster (of the last 50).</p>
        ${answer.lines.length ? `<div class="table-wrap"><table class="data"><thead><tr><th>Request</th><th class="num">Times</th><th class="num">Over ${ms(answer.aimMs)}</th><th class="num">Typical</th><th class="num">Slow</th><th class="num">Slowest</th><th>Last</th></tr></thead><tbody>
          ${answer.lines.map(l => `<tr><td><code>${esc(l.route)}</code></td><td class="num">${l.count}</td><td class="num">${l.overAim}</td><td class="num"${slow(l.typicalMs)}>${ms(l.typicalMs)}</td><td class="num"${slow(l.slowMs)}>${ms(l.slowMs)}</td><td class="num"${slow(l.maxMs)}>${ms(l.maxMs)}</td><td>${esc(dateTime(l.last))}</td></tr>`).join('')}
        </tbody></table></div>` : '<p class="muted">No requests yet.</p>'}
      </section>`;
    $('#speed-clear').addEventListener('click', async () => { const r = await api('POST', 'admin/speed/clear', {}); toast(r.result.message, !r.result.ok); speed(); });
  }

  async function mycrm() {
    const d = await api('GET', 'admin/mycrm');
    $('#tab').innerHTML = `<section class="card"><h2>MyCRM's data</h2>
      <p class="muted">MyCRMRecords reads and writes the Users, Territory and Company part of MyCRM in MyCRM's own tables (the OutSystems database, Connections tab): the CRM users and teams, the companies with their addresses, contacts, phones, emails, relationships and group names, the permanent territories with their companies, and the territory movements. It keeps no copy, so MyCRM and MyCRMRecords always show the same data. MyCRM's temporary territories (projects, CSST) are only read, for who may open which company.</p>
      ${d.configured ? '' : '<div class="notice warn">Set up the OutSystems database on the Connections tab first.</div>'}
      <form id="mycrm-form" style="margin-top:12px">
        <label class="check"><input type="checkbox" id="writes"${d.writesEnabled ? ' checked' : ''}>Let MyCRMRecords change MyCRM's data</label>
        <p class="muted small">Until this is ticked MyCRMRecords only reads: saving a company, address, contact, territory, CRM user or team is refused, and every page says so. When it is ticked, MyCRMRecords adds and changes rows of MyCRM's tables (the OutSystems database's login needs INSERT and UPDATE on them) and never deletes one: what is taken away is marked inactive, as MyCRMRecords always did. Every value written is listed below with the value it replaced.</p>
        <div class="action-buttons"><button class="button primary" type="submit">Save</button></div>
        <div class="notice" id="mycrm-result" hidden></div></form></section>
      <section class="card"><h2>MyMachines' temporary access</h2>
        <p class="muted">Who sees a company's machines: who may open the company, and the people given temporary access in MyMachines (its TempAccess table, in the OutSystems database). Sales Admins list it on the Machine access page.</p>
        <form id="mymachines-form" style="margin-top:12px">
          <label class="check"><input type="checkbox" id="machine-writes"${d.machineWritesEnabled ? ' checked' : ''}>Let MyCRMRecords change MyMachines' temporary access</label>
          <p class="muted small">Until this is ticked the Machine access page only lists it. When it is ticked, giving access adds a row to MyMachines' TempAccess, and removing it marks the row ("X:" before its customer name and BRN, so it matches no customer) instead of deleting it. The OutSystems database's login needs INSERT and UPDATE on that table. Every value written is listed below (table MyMachines.TempAccess).</p>
          <div class="action-buttons"><button class="button primary" type="submit">Save</button></div>
          <div class="notice" id="mymachines-result" hidden></div></form></section>
      <section class="card"><h2>Changes made in MyCRM</h2>
        <p class="muted">Every value MyCRMRecords wrote in MyCRM's tables (and in MyMachines' temporary access), newest first, with the value it replaced and who made the change.</p>
        <div class="toolbar"><label class="search-box" style="flex:1"><span data-icon="search"></span><input id="changes-q" placeholder="Search table, column, value or person"></label></div>
        <div id="changes"></div></section>`;
    $('#mycrm-form').addEventListener('submit', async event => {
      event.preventDefault();
      const writesEnabled = $('#writes').checked;
      if (writesEnabled && !d.writesEnabled && !await ask({title: "Let MyCRMRecords change MyCRM's data?", text: 'From now on, what people save in MyCRMRecords is written in MyCRM\'s tables (never deleting there), and listed on this tab.', ok: 'Turn on'})) return;
      const a = await api('POST', 'admin/mycrm', {writesEnabled});
      report($('#mycrm-result'), a);
      if (a.result.ok) setTimeout(() => location.reload(), 1200);
    });
    $('#mymachines-form').addEventListener('submit', async event => {
      event.preventDefault();
      const writesEnabled = $('#machine-writes').checked;
      if (writesEnabled && !d.machineWritesEnabled && !await ask({title: "Let MyCRMRecords change MyMachines' temporary access?", text: 'From now on, Sales Admins give and remove temporary access to machines in MyMachines\' TempAccess (never deleting there), and every value is listed on this tab.', ok: 'Turn on'})) return;
      const a = await api('POST', 'admin/mymachines', {writesEnabled});
      report($('#mymachines-result'), a);
      if (a.result.ok) d.machineWritesEnabled = writesEnabled;
    });
    let skip = 0, timer = 0;
    async function changes() {
      const c = await api('GET', 'admin/mycrm/changes?' + new URLSearchParams({q: $('#changes-q').value, skip}));
      $('#changes').innerHTML = c.rows && c.rows.length ? `<div class="table-wrap" style="margin-top:12px"><table class="data"><thead><tr><th>When</th><th>Row</th><th>Column</th><th>Was</th><th>Now</th></tr></thead><tbody>
        ${c.rows.map(r => `<tr><td class="nowrap small">${esc(dateTime(r.changedOn))}<span class="sub">${esc(r.changedBy)}</span></td><td>${esc(r.tableName)} #${r.rowId}</td><td><code>${esc(r.column)}</code></td><td class="small">${esc(r.before) || '<span class="muted">–</span>'}</td><td class="small">${esc(r.after) || '<span class="muted">–</span>'}</td></tr>`).join('')}
        </tbody></table></div><p class="muted small">${c.total} changes${c.total > 100 ? `, showing ${skip + 1} to ${Math.min(skip + 100, c.total)}` : ''}.</p>
        ${c.total > 100 ? `<div class="toolbar"><button class="button secondary small-btn" id="changes-prev"${skip ? '' : ' disabled'}>Newer</button><button class="button secondary small-btn" id="changes-next"${skip + 100 < c.total ? '' : ' disabled'}>Older</button></div>` : ''}`
        : '<p class="muted">None yet.</p>';
      $('#changes-prev')?.addEventListener('click', () => { skip = Math.max(0, skip - 100); changes(); });
      $('#changes-next')?.addEventListener('click', () => { skip += 100; changes(); });
    }
    $('#changes-q').addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(() => { skip = 0; changes(); }, 300); });
    changes();
  }

  shell();
})();
