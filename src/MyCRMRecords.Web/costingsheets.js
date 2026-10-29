(() => {
  'use strict';
  // costingsheets.aspx: MyOrders' costing sheets (its CostingSheets screen), read only. The person sees those MyOrders let them
  // see (every one for the roles that see all; else their own and their MyCRM team's), 50 at a time, newest first. The
  // search and filters apply as they are typed or chosen (MyOrders waited for Search), are kept for next time, and the
  // list downloads as an Excel workbook. Each row opens the costing sheet's page or its print.
  const {$, $$, esc, api, toast, drawIcons, pager, dateTime, date} = window.WH;
  const page = $('#page');
  const money = value => Number(value || 0).toLocaleString('en-SG', {minimumFractionDigits: 2, maximumFractionDigits: 2});
  const blank = {q: '', status: '', salesRep: '', specialist: '', hasSpecialist: '', createdFrom: '', createdTo: '', approvedFrom: '', approvedTo: '',
    invoiceFrom: '', invoiceTo: '', sort: 'id', desc: '1'};
  const saved = (() => { try { return JSON.parse(localStorage.getItem('costing-sheets-filter') || '{}'); } catch { return {}; } })();
  const filter = {...blank, ...Object.fromEntries(Object.entries(saved).filter(([k]) => k in blank)), skip: 0};
  // From a company's page (company=<id>&name=<name>): only that company's costing sheets, not kept for next time.
  const linked = new URLSearchParams(location.search);
  const company = {id: /^\d+$/.test(linked.get('company') || '') ? linked.get('company') : '', name: linked.get('name') || ''};
  // It starts with no other filter, and the filters kept for next time are left as they were.
  if (company.id) Object.assign(filter, blank, {company: company.id});
  const everyCompany = () => { delete filter.company; company.id = ''; history.replaceState(null, '', location.pathname); };
  let data = null, choices = null, session = null, timer = 0, moreOpen = ['createdFrom', 'createdTo', 'approvedFrom', 'approvedTo', 'invoiceFrom', 'invoiceTo'].some(k => filter[k]);

  const query = () => new URLSearchParams(Object.entries(filter).filter(([k, v]) => v !== '' && !(k === 'skip' && v === 0)).map(([k, v]) => [k, String(v)]));

  async function load() {
    if (!filter.company) try { localStorage.setItem('costing-sheets-filter', JSON.stringify({...filter, skip: undefined})); } catch { /* not kept */ }
    const answer = await api('GET', 'costingsheets?' + query());
    $('#loading').hidden = true;
    page.hidden = false;
    if (!$('#rows')) shell();
    if (!answer.result.ok) { $('#rows').innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3></div>`; $('#count').textContent = ''; $('#pager').hidden = true; return; }
    data = answer;
    render();
  }

  const options = (list, current, all = 'All') =>
    `<option value="">${esc(all)}</option>${(list || []).map(c => `<option value="${c.id}"${String(c.id) === String(current) ? ' selected' : ''}>${esc(c.label)}</option>`).join('')}`;
  const dateField = (name, label) => `<label class="field"><span>${esc(label)}</span><input type="date" id="${name}" value="${esc(filter[name])}"></label>`;

  function shell() {
    const seesAll = session && session.can.seesAll;
    page.innerHTML = `
      <div class="page-heading"><div><h1>Costing sheets</h1><p>${seesAll ? 'Every costing sheet in MyOrders.' : "Your costing sheets and your team's, as MyOrders shows them."} Read only: they are kept here for looking back.</p></div>
        <div class="heading-actions" id="actions"></div></div>
      <section class="card">
        <div class="filters">
          <label class="search-box"><span data-icon="search"></span><input id="q" type="search" placeholder="Number, status, reference, company, invoice or order no" value="${esc(filter.q)}" aria-label="Search the costing sheets"></label>
          <label class="field"><span>Costing Sheet Status</span><select id="status">${options(choices.statuses, filter.status)}</select></label>
          <label class="field"><span>Sales Rep</span><select id="salesRep" style="max-width:220px">${options(choices.salesReps, filter.salesRep)}</select></label>
          <label class="field"><span>Specialist</span><select id="specialist" style="max-width:220px">${options(choices.specialists, filter.specialist)}</select></label>
          <label class="field"><span>Has Specialist</span><select id="hasSpecialist">
            <option value="">-</option><option value="Yes"${filter.hasSpecialist === 'Yes' ? ' selected' : ''}>Yes</option><option value="No"${filter.hasSpecialist === 'No' ? ' selected' : ''}>No</option></select></label>
          <button type="button" class="button secondary" id="more" aria-expanded="${moreOpen}" aria-controls="dates"><span data-icon="clock"></span>Dates</button>
          <button type="button" class="button secondary" id="reset">Reset</button>
        </div>
        <div class="filters" id="dates"${moreOpen ? '' : ' hidden'} style="margin-top:10px">
          ${dateField('createdFrom', 'From Creation Date')}${dateField('createdTo', 'To Creation Date')}
          ${dateField('approvedFrom', 'From Head Approval Date')}${dateField('approvedTo', 'To Head Approval Date')}
          ${dateField('invoiceFrom', 'From Invoice Date')}${dateField('invoiceTo', 'To Invoice Date')}
        </div>
        ${company.id ? `<p class="notice" id="company-only" style="margin:10px 0 0">Only the costing sheets of <b>${esc(company.name || 'company ' + company.id)}</b>. <button type="button" class="text-button" id="every-company">Show every company</button></p>` : ''}
        <p class="muted small" id="count" style="margin:10px 0 0"></p>
        <div id="rows"></div><div class="pager" id="pager" hidden></div>
      </section>`;
    drawIcons(page);
    const later = () => { clearTimeout(timer); filter.skip = 0; timer = setTimeout(load, 400); };
    $('#q').addEventListener('input', event => { filter.q = event.target.value; later(); });
    ['status', 'salesRep', 'specialist', 'hasSpecialist', 'createdFrom', 'createdTo', 'approvedFrom', 'approvedTo', 'invoiceFrom', 'invoiceTo'].forEach(name =>
      $('#' + name).addEventListener('change', event => { filter[name] = event.target.value; filter.skip = 0; load(); }));
    $('#more').addEventListener('click', () => { moreOpen = !moreOpen; $('#dates').hidden = !moreOpen; $('#more').setAttribute('aria-expanded', moreOpen); });
    $('#every-company')?.addEventListener('click', () => { everyCompany(); filter.skip = 0; page.innerHTML = ''; load(); });
    $('#reset').addEventListener('click', () => { everyCompany(); Object.assign(filter, blank, {skip: 0}); moreOpen = false; page.innerHTML = ''; load(); });
  }

  function actions() {
    $('#actions').innerHTML = `
      ${session && session.can.actAs && !session.impersonating ? '<button class="button secondary" id="act-as"><span data-icon="users"></span>Sign in as…</button>' : ''}
      <a class="button secondary" id="download" href="file.ashx/costingsheets?${esc(query().toString())}"><span data-icon="excel"></span>Download</a>`;
    drawIcons($('#actions'));
    $('#act-as')?.addEventListener('click', actAs);
  }

  // The row's buttons first; secondary facts under the main one, so the list fits the page without scrolling sideways.
  const columns = [['id', 'Costing sheet'], ['company', 'Company'], ['salesman', 'Salesman'], ['created', 'Dates'], ['achieved', 'Achieved Price'], ['invoice', 'Invoice'],
    ['opo', 'OPO']];

  function heading([key, label]) {
    const sorted = filter.sort === key;
    return `<th${sorted ? ` aria-sort="${filter.desc === '1' ? 'descending' : 'ascending'}"` : ''}${key === 'achieved' ? ' class="num"' : ''}><button type="button" data-sort="${key}">${esc(label)}</button></th>`;
  }

  function render() {
    actions();
    const filtered = !!filter.company || Object.keys(blank).some(k => k !== 'sort' && k !== 'desc' && filter[k]);
    $('#count').textContent = `${data.total} costing sheet${data.total === 1 ? '' : 's'}${filtered ? ' match' : ''}.`;
    $('#rows').innerHTML = data.rows.length ? `<div class="table-wrap" style="margin-top:10px"><table class="data"><thead><tr><th></th>${columns.map(heading).join('')}</tr></thead><tbody>
      ${data.rows.map(r => `<tr>
        <td class="nowrap"><a class="icon-button small" href="costingsheet.aspx?id=${r.id}" title="Open costing sheet ${r.id}" aria-label="Open costing sheet ${r.id}"><span data-icon="arrow-right"></span></a>
          ${r.canPrint ? `<a class="icon-button small" href="print.aspx?id=${r.id}" target="_blank" rel="noopener" title="Print costing sheet ${r.id}" aria-label="Print costing sheet ${r.id}"><span data-icon="printer"></span></a>` : ''}</td>
        <td><a href="costingsheet.aspx?id=${r.id}" class="nowrap"><b># ${r.id}</b></a>${r.reference ? `<span class="sub nowrap">Ref # ${esc(r.reference)}</span>` : ''}<span class="sub">${esc(r.status)}</span></td>
        <td class="wide-text"><b>${esc(r.company)}</b>${r.contact ? `<span class="sub">${esc(r.contact)}</span>` : ''}</td>
        <td>${esc(r.salesman)}</td>
        <td class="small">Created ${esc(date(r.createdAt))}${r.updatedAt ? `<span class="sub">Updated ${esc(date(r.updatedAt))}</span>` : ''}${r.printedAt ? `<span class="sub">Printed ${esc(date(r.printedAt))}</span>` : ''}</td>
        <td class="num nowrap">${money(r.totalAchievedPrice)}</td>
        <td class="nowrap">${esc(r.invoiceNo) || '<span class="muted">-</span>'}${r.minInvoiceDate ? `<span class="sub">${esc(date(r.minInvoiceDate))}</span>` : ''}${r.orderNo ? `<span class="sub">Order ${esc(r.orderNo)}</span>` : ''}</td>
        <td class="center">${r.isOpoChecked ? '<span class="yes">Yes</span>' : '<span class="muted">No</span>'}</td></tr>`).join('')}
      </tbody></table></div>`
      : `<div class="empty" style="margin-top:10px"><h3>No costing sheets to show</h3><p class="muted">${filtered ? 'No costing sheet matches the search and filters.' : 'There are no costing sheets of yours or your team.'}</p></div>`;
    drawIcons($('#rows'));
    pager($('#pager'), data.total, data.skip, data.take);
  }

  /** Sign in as someone else (administrators): MyCRMRecords then shows what that person sees; it is recorded with both names. */
  async function actAs() {
    const answer = await api('GET', 'orders/people');
    if (!answer.result.ok) { toast(answer.result.message, true); return; }
    const dialog = document.createElement('dialog');
    dialog.innerHTML = `<form method="dialog" class="modal"><h2>Sign in as someone else</h2>
      <p class="muted">MyCRMRecords shows what that person sees until you choose Return in the bar at the top. Signing in as them is recorded.</p>
      <label class="field"><span>Find</span><input id="act-q" type="search" placeholder="Name, login, email or MyCRM role" autocomplete="off"></label>
      <div class="people-list" id="act-people" style="max-height:320px;overflow:auto;margin-top:10px"></div>
      <div class="modal-footer"><button type="button" class="button secondary" value="cancel">Cancel</button></div></form>`;
    document.body.append(dialog);
    const list = $('#act-people', dialog);
    const draw = () => {
      const q = $('#act-q', dialog).value.trim().toLowerCase();
      const people = answer.people.filter(p => !q || `${p.name} ${p.login} ${p.email} ${p.role}`.toLowerCase().includes(q)).slice(0, 60);
      list.innerHTML = people.length ? people.map(p => `<button type="button" class="person" data-login="${esc(p.login)}" style="display:block;width:100%;text-align:left;padding:8px 10px;border:0;background:none;border-bottom:1px solid var(--border-soft)">
        <b>${esc(p.name)}</b>${p.role ? ` <span class="tag">${esc(p.role)}</span>` : ''}<span class="sub muted small" style="display:block">${[p.login, p.email].filter(Boolean).map(esc).join(' · ')}</span></button>`).join('')
        : `<p class="muted">${q ? 'Nobody found. Everyone MyCRM has a user for is here, and every salesperson and specialist of a costing sheet.' : 'Nobody to sign in as: MyOrders cannot be read just now.'}</p>`;
    };
    draw();
    $('#act-q', dialog).addEventListener('input', draw);
    list.addEventListener('click', async event => {
      const person = event.target.closest('[data-login]');
      if (!person) return;
      const result = await api('POST', 'users/impersonate', {login: person.dataset.login});
      toast(result.result.message, !result.result.ok);
      if (result.result.ok) location.href = 'costingsheets.aspx';
    });
    $('button[value=cancel]', dialog).addEventListener('click', () => dialog.close());
    dialog.addEventListener('close', () => dialog.remove());
    dialog.showModal();
    $('#act-q', dialog).focus();
  }

  page.addEventListener('click', event => {
    const sort = event.target.closest('[data-sort]');
    if (sort) {
      const key = sort.dataset.sort;
      filter.desc = filter.sort === key ? (filter.desc === '1' ? '0' : '1') : (['id', 'created', 'achieved'].includes(key) ? '1' : '0');
      filter.sort = key;
      filter.skip = 0;
      load();
      return;
    }
    const move = event.target.closest('[data-page]');
    if (move && data) { filter.skip = Math.max(0, data.skip + Number(move.dataset.page) * data.take); load(); window.scrollTo({top: 0}); }
  });

  (async () => {
    const [answer, filters] = await Promise.all([api('GET', 'orders/session'), api('GET', 'costingsheets/filters')]);
    session = answer.result.ok ? {can: answer.can, impersonating: !!$('.impersonation-bar:not(.warning-bar)')} : null;
    choices = filters.result.ok ? filters : {statuses: [], salesReps: [], specialists: []};
    load();
  })();
})();
