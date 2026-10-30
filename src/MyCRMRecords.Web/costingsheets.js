(() => {
  'use strict';
  // costingsheets.aspx: MyOrders' costing sheets (its CostingSheets screen), read only. The person sees those MyOrders let them
  // see (every one for the roles that see all; else their own and their MyCRM team's), 50 at a time, newest first. The
  // search and filters apply as they are typed or chosen (MyOrders waited for Search), are kept for next time, and the
  // list downloads as an Excel workbook. Each row opens the costing sheet's page or its print. Laid out as the approved
  // design: glass filter capsules above a one-line list (the dates behind Dates); on a phone the Costing sheets / Request
  // forms switch, a card per costing sheet, the status in a sheet and the other filters in another.
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
  const dateCapsule = (name, label) => `<label class="filter-capsule cs-date"><span>${esc(label)}</span><input type="date" id="${name}" value="${esc(filter[name])}"></label>`;
  // Specialist: one capsule for Has Specialist (Any, Yes, No) and the specialist (a person); choosing one clears the other.
  const specialistValue = () => filter.specialist ? 'p' + filter.specialist : filter.hasSpecialist ? 'has' + filter.hasSpecialist : '';
  const specialistOptions = () => `<option value="">Any</option><option value="hasYes"${filter.hasSpecialist === 'Yes' && !filter.specialist ? ' selected' : ''}>Has specialist</option>
    <option value="hasNo"${filter.hasSpecialist === 'No' && !filter.specialist ? ' selected' : ''}>No specialist</option>
    ${(choices.specialists || []).length ? `<optgroup label="Specialist">${choices.specialists.map(c => `<option value="p${c.id}"${String(c.id) === String(filter.specialist) ? ' selected' : ''}>${esc(c.label)}</option>`).join('')}</optgroup>` : ''}`;
  const setSpecialist = value => {
    filter.specialist = value.startsWith('p') ? value.slice(1) : '';
    filter.hasSpecialist = value.startsWith('has') ? value.slice(3) : '';
  };
  const dateKeys = ['createdFrom', 'createdTo', 'approvedFrom', 'approvedTo', 'invoiceFrom', 'invoiceTo'];
  const statusTone = status => /invoiced/i.test(status) && !/pending/i.test(status) ? 'done' : /recall|reject|cancel/i.test(status) ? 'warn' : '';

  function shell() {
    const seesAll = session && session.can.seesAll;
    page.innerHTML = `
      <div class="page-heading orders-heading"><div class="orders-title"><h1>Costing sheets</h1><p id="count">&nbsp;</p></div>
        <div class="heading-actions" id="actions"></div></div>
      <div class="tabs orders-kinds" role="group" aria-label="Orders"><a class="tab" href="costingsheets.aspx" aria-current="page" aria-pressed="true">Costing sheets</a><a class="tab" href="requestforms.aspx" aria-pressed="false">Request forms</a></div>
      <div class="orders-filters" role="search" aria-label="Filter costing sheets">
        <div class="orders-filter-row">
          <label class="search-box"><span data-icon="search"></span><input id="q" type="search" placeholder="Number, status, reference, company, invoice or order no" value="${esc(filter.q)}" aria-label="Search the costing sheets"></label>
          <label class="filter-capsule orders-wide"><span>Status</span><select id="status">${options(choices.statuses, filter.status)}</select></label>
          <label class="filter-capsule orders-wide"><span>Sales rep</span><select id="salesRep">${options(choices.salesReps, filter.salesRep, 'Everyone')}</select></label>
          <label class="filter-capsule orders-wide"><span>Specialist</span><select id="specialist">${specialistOptions()}</select></label>
          <button type="button" class="filter-capsule orders-wide" id="more" aria-expanded="${moreOpen}" aria-controls="dates"><span data-icon="clock"></span>Dates</button>
          <button type="button" class="text-button orders-wide" id="reset">Reset</button>
          <button type="button" class="icon-button orders-filter-button" id="open-filters" aria-haspopup="dialog" aria-label="Filters"><span data-icon="settings"></span><span class="filter-dot" id="filter-dot" hidden></span></button>
        </div>
        <button type="button" class="filter-capsule orders-status-button" id="open-status" aria-haspopup="dialog"><span>Status:</span><b id="status-label"></b><span data-icon="arrow-right" class="orders-caret"></span></button>
        <div class="orders-filter-row orders-dates orders-wide" id="dates"${moreOpen ? '' : ' hidden'}>
          ${dateCapsule('createdFrom', 'From Creation Date')}${dateCapsule('createdTo', 'To Creation Date')}
          ${dateCapsule('approvedFrom', 'From Head Approval Date')}${dateCapsule('approvedTo', 'To Head Approval Date')}
          ${dateCapsule('invoiceFrom', 'From Invoice Date')}${dateCapsule('invoiceTo', 'To Invoice Date')}
        </div>
        ${company.id ? `<p class="notice orders-company" id="company-only">Only the costing sheets of <b>${esc(company.name || 'company ' + company.id)}</b>. <button type="button" class="text-button" id="every-company">Show every company</button></p>` : ''}
      </div>
      <section class="orders-list" aria-label="Costing sheet list"><div id="rows"></div><div id="cards" class="file-cards"></div><div class="pager" id="pager" hidden></div></section>`;
    drawIcons(page);
    page.dataset.seesAll = seesAll ? '1' : '';
    const later = () => { clearTimeout(timer); filter.skip = 0; timer = setTimeout(load, 400); };
    $('#q').addEventListener('input', event => { filter.q = event.target.value; later(); });
    ['status', 'salesRep', ...dateKeys].forEach(name =>
      $('#' + name).addEventListener('change', event => { filter[name] = event.target.value; filter.skip = 0; load(); }));
    $('#specialist').addEventListener('change', event => { setSpecialist(event.target.value); filter.skip = 0; load(); });
    $('#more').addEventListener('click', () => { moreOpen = !moreOpen; $('#dates').hidden = !moreOpen; $('#more').setAttribute('aria-expanded', moreOpen); });
    $('#every-company')?.addEventListener('click', () => { everyCompany(); filter.skip = 0; page.innerHTML = ''; load(); });
    $('#reset').addEventListener('click', reset);
    $('#open-filters').addEventListener('click', filterSheet);
    $('#open-status').addEventListener('click', statusSheet);
  }

  function reset() { everyCompany(); Object.assign(filter, blank, {skip: 0}); moreOpen = false; page.innerHTML = ''; load(); }

  function actions() {
    const signIn = session && session.can.actAs && !session.impersonating;
    $('#actions').innerHTML = `<div class="action-capsule" role="group" aria-label="Costing sheet actions">
      ${signIn ? '<button type="button" class="capsule-button" id="act-as" title="Sign in as…"><span data-icon="users"></span><span class="action-label">Sign in as…</span></button><span class="capsule-divider" aria-hidden="true"></span>' : ''}
      <a class="capsule-button" id="download" href="file.ashx/costingsheets?${esc(query().toString())}" title="Download"><span data-icon="excel"></span><span class="action-label">Download</span></a></div>`;
    drawIcons($('#actions'));
    $('#act-as')?.addEventListener('click', actAs);
  }

  // One line per costing sheet; the headings sort as before (Number, Company, Sales rep, Created, Achieved, Invoice, OPO).
  const columns = [['id', 'Number', 'c-no'], ['', 'Reference', 'c-ref'], ['company', 'Company', 'c-company'], ['salesman', 'Sales rep', 'c-rep'], ['', 'Status', 'c-status'],
    ['created', 'Created', 'c-date'], ['', 'Updated', 'c-date c-updated'], ['achieved', 'Achieved', 'num c-money'], ['invoice', 'Invoice', 'c-invoice'], ['', 'Invoiced', 'c-date'],
    ['opo', 'OPO', 'c-opo'], ['', '', 'c-open']];

  function heading([key, label, cls]) {
    if (!key) return `<th class="${cls}">${label ? esc(label) : '<span class="sr-only">Open</span>'}</th>`;
    const sorted = filter.sort === key;
    return `<th class="${cls}"${sorted ? ` aria-sort="${filter.desc === '1' ? 'descending' : 'ascending'}"` : ''}><button type="button" data-sort="${key}">${esc(label)}</button></th>`;
  }

  function render() {
    actions();
    const filtered = !!filter.company || Object.keys(blank).some(k => k !== 'sort' && k !== 'desc' && filter[k]);
    const seesAll = session && session.can.seesAll;
    const sortNote = filter.sort === 'id' && filter.desc === '1' ? 'newest first' : `by ${(columns.find(c => c[0] === filter.sort) || ['', 'number'])[1].toLowerCase()}`;
    $('#count').innerHTML = `<b>${data.total.toLocaleString('en-SG')} costing sheet${data.total === 1 ? '' : 's'}</b>${filtered ? ' match' : ''}<span class="orders-note"> · ${esc(sortNote)} · ${seesAll ? 'every costing sheet in MyOrders' : "yours and your team's"} · read only, from MyOrders</span>`;
    const status = (choices.statuses || []).find(c => String(c.id) === String(filter.status));
    $('#status-label').textContent = status ? status.label : 'All';
    const others = filter.salesRep || filter.specialist || filter.hasSpecialist || dateKeys.some(k => filter[k]);
    $('#filter-dot').hidden = !others;
    $('#open-filters').setAttribute('aria-label', others ? 'Filters (on)' : 'Filters');
    const pill = r => `<span class="orders-status ${statusTone(r.status)}" title="${esc(r.status)}">${esc(r.status)}</span>`;
    const printLink = r => r.canPrint ? `<a class="icon-button small" href="print.aspx?id=${r.id}" target="_blank" rel="noopener" title="Print costing sheet ${r.id}" aria-label="Print costing sheet ${r.id}"><span data-icon="printer"></span></a>` : '';
    const empty = `<div class="empty"><h3>No costing sheets to show</h3><p class="muted">${filtered ? 'No costing sheet matches the search and filters. Try another number or status, or press Reset.' : 'There are no costing sheets of yours or your team.'}</p></div>`;
    $('#rows').innerHTML = data.rows.length ? `<table class="data orders-table cs-table"><thead><tr>${columns.map(heading).join('')}</tr></thead><tbody>
      ${data.rows.map(r => `<tr data-href="costingsheet.aspx?id=${r.id}">
        <td class="c-no"><a href="costingsheet.aspx?id=${r.id}"># ${r.id}</a></td>
        <td class="c-ref" title="${esc(r.reference)}">${r.reference ? esc(r.reference) : '<span class="muted">—</span>'}</td>
        <td class="c-company" title="${esc(r.company)}${r.contact ? ' · ' + esc(r.contact) : ''}"><b>${esc(r.company)}</b>${r.contact ? `<span class="muted"> · ${esc(r.contact)}</span>` : ''}</td>
        <td class="c-rep" title="${esc(r.salesman)}">${esc(r.salesman)}</td>
        <td class="c-status">${pill(r)}</td>
        <td class="c-date">${esc(date(r.createdAt))}</td>
        <td class="c-date c-updated" title="${r.printedAt ? 'Printed ' + esc(date(r.printedAt)) : ''}">${r.updatedAt ? esc(date(r.updatedAt)) : '<span class="muted">—</span>'}${r.printedAt ? '<span class="cs-printed" aria-label="printed"> · printed ' + esc(date(r.printedAt)) + '</span>' : ''}</td>
        <td class="num c-money">${money(r.totalAchievedPrice)}</td>
        <td class="c-invoice" title="${esc([r.invoiceNo, r.orderNo ? 'Order ' + r.orderNo : ''].filter(Boolean).join(' · '))}">${r.invoiceNo ? esc(r.invoiceNo) + (r.orderNo ? `<span class="muted"> · Order ${esc(r.orderNo)}</span>` : '') : r.orderNo ? `<span class="muted">Order ${esc(r.orderNo)}</span>` : '<span class="muted">—</span>'}</td>
        <td class="c-date">${r.minInvoiceDate ? esc(date(r.minInvoiceDate)) : '<span class="muted">—</span>'}</td>
        <td class="c-opo">${r.isOpoChecked ? '<span class="yes">Yes</span>' : '<span class="muted">No</span>'}</td>
        <td class="c-open">${printLink(r)}<a class="icon-button small" href="costingsheet.aspx?id=${r.id}" title="Open costing sheet ${r.id}" aria-label="Open costing sheet ${r.id}"><span data-icon="arrow-right"></span></a></td></tr>`).join('')}
      </tbody></table>`
      : empty;
    // On a phone: a card per costing sheet.
    $('#cards').innerHTML = data.rows.map(r => `<a class="file-card orders-card" href="costingsheet.aspx?id=${r.id}">
        <span class="file-card-body"><span class="file-card-top"><b># ${r.id}${r.reference ? `<span class="muted"> · ${esc(r.reference)}</span>` : ''}</b><span class="orders-money">$${money(r.totalAchievedPrice)}</span></span>
          <span class="file-card-line strong">${esc(r.company)}</span>
          <span class="orders-card-foot">${pill(r)}<span class="file-card-line">${esc(date(r.updatedAt || r.createdAt))}</span><span class="file-card-line orders-card-right">${r.invoiceNo ? esc(r.invoiceNo) : 'No invoice yet'}</span></span>
          <span class="file-card-line">${esc([r.salesman, r.orderNo ? 'Order ' + r.orderNo : '', r.isOpoChecked ? 'OPO' : ''].filter(Boolean).join(' · '))}</span></span>
        <span data-icon="arrow-right" class="section-chevron"></span></a>`).join('') || empty;
    drawIcons($('#rows'));
    drawIcons($('#cards'));
    pager($('#pager'), data.total, data.skip, data.take);
  }

  /** The phone's status filter: a sheet listing the statuses. */
  function statusSheet() {
    const sheet = document.createElement('dialog');
    sheet.className = 'companies-sheet';
    sheet.setAttribute('aria-labelledby', 'status-sheet-title');
    sheet.innerHTML = `<form method="dialog" class="sheet-form"><span class="sheet-grabber" aria-hidden="true"></span>
      <div class="sheet-head"><button type="button" class="icon-button" value="cancel" aria-label="Close status list"><span data-icon="close"></span></button>
        <h2 id="status-sheet-title">Costing Sheet Status</h2><span class="orders-sheet-spacer" aria-hidden="true"></span></div>
      <div class="sheet-group orders-sheet-list" role="radiogroup" aria-labelledby="status-sheet-title">${[{id: '', label: 'All'}].concat(choices.statuses || []).map(c =>
        `<button type="button" role="radio" class="orders-sheet-row" data-status="${esc(c.id)}" aria-checked="${String(c.id) === String(filter.status)}"><span>${esc(c.label)}</span><span data-icon="check" class="orders-check"></span></button>`).join('')}</div></form>`;
    document.body.append(sheet);
    drawIcons(sheet);
    $('button[value=cancel]', sheet).addEventListener('click', () => sheet.close());
    sheet.addEventListener('close', () => sheet.remove());
    sheet.addEventListener('click', event => {
      if (event.target === sheet) { sheet.close(); return; }
      const pick = event.target.closest('[data-status]');
      if (!pick) return;
      filter.status = pick.dataset.status;
      filter.skip = 0;
      sheet.close();
      page.innerHTML = '';
      load();
    });
    sheet.showModal();
  }

  /** The phone's other filters: sales rep, specialist and the dates, in a sheet, applied together. */
  function filterSheet() {
    const sheet = document.createElement('dialog');
    sheet.className = 'companies-sheet';
    sheet.setAttribute('aria-labelledby', 'filter-title');
    const dateRow = (name, label) => `<label><span>${esc(label)}</span><input type="date" data-sheet="${name}" value="${esc(filter[name])}"></label>`;
    sheet.innerHTML = `<form method="dialog" class="sheet-form"><span class="sheet-grabber" aria-hidden="true"></span>
      <div class="sheet-head"><button type="button" class="icon-button" value="cancel" aria-label="Close filters"><span data-icon="close"></span></button>
        <h2 id="filter-title">Filter costing sheets</h2><button type="submit" class="button primary">Apply</button></div>
      <div class="sheet-group">
        <label><span>Sales rep</span><select data-sheet="salesRep">${options(choices.salesReps, filter.salesRep, 'Everyone')}</select></label>
        <label><span>Specialist</span><select id="sheet-specialist">${specialistOptions()}</select></label></div>
      <div class="sheet-group">${dateRow('createdFrom', 'From Creation Date')}${dateRow('createdTo', 'To Creation Date')}${dateRow('approvedFrom', 'From Head Approval')}${dateRow('approvedTo', 'To Head Approval')}
        ${dateRow('invoiceFrom', 'From Invoice Date')}${dateRow('invoiceTo', 'To Invoice Date')}</div>
      <button type="button" class="button secondary" id="sheet-reset">Reset filters</button></form>`;
    document.body.append(sheet);
    drawIcons(sheet);
    $('button[value=cancel]', sheet).addEventListener('click', () => sheet.close());
    sheet.addEventListener('close', () => sheet.remove());
    sheet.addEventListener('click', event => { if (event.target === sheet) sheet.close(); });
    $('form', sheet).addEventListener('submit', () => {
      $$('[data-sheet]', sheet).forEach(input => { filter[input.dataset.sheet] = input.value; });
      setSpecialist($('#sheet-specialist', sheet).value);
      moreOpen = dateKeys.some(k => filter[k]);
      filter.skip = 0;
      page.innerHTML = '';
      load();
    });
    $('#sheet-reset', sheet).addEventListener('click', () => { sheet.close(); reset(); });
    sheet.showModal();
  }

  /** Sign in as someone else (administrators): MyCRMRecords then shows what that person sees; it is recorded with both names. */
  async function actAs() {
    const answer = await api('GET', 'orders/people');
    if (!answer.result.ok) { toast(answer.result.message, true); return; }
    const dialog = document.createElement('dialog');
    dialog.className = 'act-panel dlg';
    dialog.innerHTML = `<form method="dialog" class="act-panel-in">${window.WH.dialogHead('Sign in as someone else', {closeLabel: 'Cancel'})}
      <div class="act-panel-body"><p class="dlg-text">MyCRMRecords shows what that person sees until you choose Return in the bar at the top. Signing in as them is recorded.</p>
      <label class="field"><span>Find</span><input id="act-q" type="search" placeholder="Name, login, email or MyCRM role" autocomplete="off"></label>
      <div class="people-list dlg-card" id="act-people" style="max-height:320px;overflow:auto;padding:0"></div></div></form>`;
    document.body.append(dialog);
    drawIcons(dialog);
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
    if (move && data) { filter.skip = Math.max(0, data.skip + Number(move.dataset.page) * data.take); load(); window.scrollTo({top: 0}); return; }
    // The row opens its costing sheet (its number is the link for the keyboard).
    const row = event.target.closest('tr[data-href]');
    if (!row || event.target.closest('a, button, input, label, select')) return;
    if (window.getSelection && String(window.getSelection())) return;
    if (event.ctrlKey || event.metaKey) window.open(row.dataset.href, '_blank'); else location.href = row.dataset.href;
  });

  (async () => {
    const [answer, filters] = await Promise.all([api('GET', 'orders/session'), api('GET', 'costingsheets/filters')]);
    session = answer.result.ok ? {can: answer.can, impersonating: !!$('.impersonation-bar:not(.warning-bar)')} : null;
    choices = filters.result.ok ? filters : {statuses: [], salesReps: [], specialists: []};
    load();
  })();
})();
