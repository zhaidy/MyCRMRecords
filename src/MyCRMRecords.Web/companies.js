(() => {
  'use strict';
  // default.aspx: the companies (MyCRM's Companies): those the person may open (MyCRM's rule: their and their team's
  // territories, or every company for the roles that see all), searched by name or BRN, customer type, group name and
  // postal code, 50 at a time by name. The first page shows at once (MyCRM showed nothing before Search). Sales Admins
  // add companies; MyCRM Admins upload the number of employees; administrators may sign in as someone else.
  const {$, esc, api, toast, drawIcons, pager} = window.WH;
  const {companyForm, uploadDialog} = window.CRM;
  const page = $('#page');
  const saved = (() => { try { return JSON.parse(localStorage.getItem('companies-filter') || '{}'); } catch { return {}; } })();
  const filter = {q: saved.q || '', customerType: saved.customerType || '', groupName: saved.groupName || '', postalCode: saved.postalCode || '', skip: 0};
  let data = null, lists = null, session = null, timer = 0;

  async function load() {
    try { localStorage.setItem('companies-filter', JSON.stringify({...filter, skip: undefined})); } catch { /* not kept */ }
    const answer = await api('GET', 'companies?' + new URLSearchParams(filter));
    $('#loading').hidden = true;
    page.hidden = false;
    if (!$('#rows')) shell();
    if (!answer.result.ok) { $('#rows').innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3></div>`; $('#count').textContent = ''; return; }
    data = answer;
    render();
  }

  function shell() {
    const groups = lists ? lists.companyGroups : [];
    const names = lists ? lists.groupNames : [];
    page.innerHTML = `
      <div class="page-heading"><div><h1>Companies</h1><p>${session && session.can.seesAll ? 'Every active company.' : "The active companies of your and your team's territories (and of MyCRM's projects you are in)."}</p></div>
        <div class="heading-actions" id="actions"></div></div>
      <section class="card">
        <div class="filters">
          <label class="search-box"><span data-icon="search"></span><input id="q" type="search" placeholder="Company name or BRN" value="${esc(filter.q)}" aria-label="Company name or BRN"></label>
          <label class="field"><span>Customer Type</span><select id="customerType"><option value="">All</option>${groups.map(g => `<option value="${esc(g.code)}"${g.code === filter.customerType ? ' selected' : ''}>${esc(g.label)}</option>`).join('')}</select></label>
          <label class="field"><span>Group Name</span><select id="groupName" style="max-width:240px"><option value="">All</option>${names.map(g => `<option value="${g.id}"${String(g.id) === String(filter.groupName) ? ' selected' : ''}>${esc(g.groupName)}</option>`).join('')}</select></label>
          <label class="field"><span>Postal Code</span><input id="postalCode" maxlength="50" value="${esc(filter.postalCode)}" style="max-width:130px"></label>
          <button type="button" class="button secondary" id="reset">Reset</button>
        </div>
        <p class="muted small" id="count" style="margin:10px 0 0"></p>
        <div id="rows"></div><div class="pager" id="pager" hidden></div>
      </section>`;
    drawIcons(page);
    const later = () => { clearTimeout(timer); filter.skip = 0; timer = setTimeout(load, 400); };
    $('#q').addEventListener('input', event => { filter.q = event.target.value; later(); });
    $('#postalCode').addEventListener('input', event => { filter.postalCode = event.target.value; later(); });
    $('#customerType').addEventListener('change', event => { filter.customerType = event.target.value; filter.skip = 0; load(); });
    $('#groupName').addEventListener('change', event => { filter.groupName = event.target.value; filter.skip = 0; load(); });
    $('#reset').addEventListener('click', () => { Object.assign(filter, {q: '', customerType: '', groupName: '', postalCode: '', skip: 0}); page.innerHTML = ''; load(); });
  }

  function actions() {
    const can = data.can;
    $('#actions').innerHTML = `
      ${session && session.can.actAs && !session.impersonating ? '<button class="button secondary" id="act-as"><span data-icon="users"></span>Sign in as…</button>' : ''}
      ${can.uploadEmployees ? '<button class="button secondary" id="employees"><span data-icon="upload"></span>Upload number of employees</button>' : ''}
      ${can.create ? '<button class="button primary" id="new-company"><span data-icon="plus"></span>New company</button>' : ''}`;
    drawIcons($('#actions'));
    $('#act-as')?.addEventListener('click', actAs);
    $('#new-company')?.addEventListener('click', async () => {
      const id = await companyForm(lists, null);
      if (id) location.href = 'company.aspx?id=' + id;
    });
    $('#employees')?.addEventListener('click', async () => {
      const answer = await uploadDialog('Upload number of employees',
        '<p class="muted">An Excel workbook with the columns <b>BRN</b> and <b>NoOfEmployees</b>. The first active company with each BRN gets the number; rows with no such company are skipped.</p>',
        'employees', 'employees');
      if (answer) toast(answer.result ? answer.result.message : 'Uploaded.', answer.result && !answer.result.ok);
    });
  }

  function render() {
    actions();
    const filtered = filter.q || filter.customerType || filter.groupName || filter.postalCode;
    $('#count').textContent = `${data.total} compan${data.total === 1 ? 'y' : 'ies'}${filtered ? ' match' : ''}.`;
    $('#rows').innerHTML = data.rows.length ? `<div class="table-wrap" style="margin-top:10px"><table class="data"><thead><tr>
        <th></th><th>Company</th><th>Postal Code</th><th>Salesperson</th><th>Created By</th></tr></thead><tbody>
      ${data.rows.map(r => `<tr>
        <td class="nowrap"><a class="button secondary small-btn" href="company.aspx?id=${r.id}" title="Open ${esc(r.name)}"><span data-icon="arrow-right"></span>Open</a></td>
        <td class="wide-text"><a href="company.aspx?id=${r.id}"><b>${esc(r.name)}</b></a>${r.brn ? `<span class="sub">BRN ${esc(r.brn)}</span>` : ''}</td>
        <td class="nowrap">${esc(r.postalCode)}</td>
        <td>${r.salesperson ? esc(r.salesperson) : '<span class="muted">None</span>'}</td>
        <td class="small">${esc(r.createdBy)}</td></tr>`).join('')}
      </tbody></table></div>`
      : `<div class="empty" style="margin-top:10px"><h3>Nothing to show</h3><p class="muted">${filtered ? 'No company matches the search and filters.' : "No company is in your or your team's territories."}</p></div>`;
    drawIcons($('#rows'));
    pager($('#pager'), data.total, data.skip, data.take);
  }

  /** Sign in as someone else (administrators): MyCRMRecords then works as that person; everything is recorded with both names. */
  async function actAs() {
    const answer = await api('GET', 'people');
    if (!answer.result.ok) { toast(answer.result.message, true); return; }
    const dialog = document.createElement('dialog');
    dialog.innerHTML = `<form method="dialog" class="modal"><h2>Sign in as someone else</h2>
      <p class="muted">MyCRMRecords works as that person until you choose Return in the bar at the top. Everything you do is recorded as you on behalf of them.</p>
      <label class="field"><span>Find</span><input id="act-q" type="search" placeholder="Name, login or email" autocomplete="off"></label>
      <div class="people-list" id="act-people" style="max-height:320px;overflow:auto;margin-top:10px"></div>
      <div class="modal-footer"><button type="button" class="button secondary" value="cancel">Cancel</button></div></form>`;
    document.body.append(dialog);
    const list = $('#act-people', dialog);
    const draw = () => {
      const q = $('#act-q', dialog).value.trim().toLowerCase();
      const people = answer.people.filter(p => !q || `${p.name} ${p.login} ${p.email}`.toLowerCase().includes(q)).slice(0, 60);
      list.innerHTML = people.length ? people.map(p => `<button type="button" class="person" data-login="${esc(p.login)}" style="display:block;width:100%;text-align:left;padding:8px 10px;border:0;background:none;border-bottom:1px solid var(--border-soft)">
        <b>${esc(p.name)}</b><span class="sub muted small" style="display:block">${esc(p.login)} · ${esc(p.email)}</span></button>`).join('') : '<p class="muted">Nobody found.</p>';
    };
    draw();
    $('#act-q', dialog).addEventListener('input', draw);
    list.addEventListener('click', async event => {
      const person = event.target.closest('[data-login]');
      if (!person) return;
      const result = await api('POST', 'users/impersonate', {login: person.dataset.login});
      toast(result.result.message, !result.result.ok);
      if (result.result.ok) location.href = './';
    });
    $('button[value=cancel]', dialog).addEventListener('click', () => dialog.close());
    dialog.addEventListener('close', () => dialog.remove());
    dialog.showModal();
    $('#act-q', dialog).focus();
  }

  page.addEventListener('click', event => {
    const move = event.target.closest('[data-page]');
    if (move && data) { filter.skip = Math.max(0, data.skip + Number(move.dataset.page) * data.take); load(); window.scrollTo({top: 0}); }
  });

  try {
    const flash = sessionStorage.getItem('flash');
    if (flash) { sessionStorage.removeItem('flash'); setTimeout(() => toast(flash), 300); }
  } catch { /* nothing to show */ }
  (async () => {
    const [answer, listsAnswer] = await Promise.all([api('GET', 'session'), api('GET', 'lists')]);
    session = answer.result.ok ? {can: answer.can, impersonating: !!$('.impersonation-bar:not(.warning-bar)')} : null;
    lists = listsAnswer.result.ok ? listsAnswer : {companyGroups: [], groupNames: [], advanceCustomerTypes: [], compAccountTypes: [], compOwnerships: []};
    load();
  })();
})();
