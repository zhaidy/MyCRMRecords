(() => {
  'use strict';
  // requestforms.aspx: MyOrders' request forms (its RequestForms screen, with what its specialists' list showed), read
  // only. The person sees those MyOrders let them see (every one for the roles that see all; else their own and their
  // MyCRM team's, and the ones they are the specialist of), 50 at a time, newest first. The search and filters apply as
  // they are typed or chosen (MyOrders waited for Search), are kept for next time, and the list downloads as MyOrders'
  // Excel. Each row opens the request form's page or its print. Laid out as the approved design, as the costing sheets:
  // glass filter capsules (the type as a switch) above a one-line list; on a phone the Costing sheets / Request forms
  // switch, a card per request form, the status in a sheet and the other filters in another.
  const {$, $$, esc, api, drawIcons, pager, dateTime, date} = window.WH;
  const page = $('#page');
  const blank = {q: '', status: '', type: '', salesRep: '', createdFrom: '', createdTo: '', sort: 'created', desc: '1'};
  const saved = (() => { try { return JSON.parse(localStorage.getItem('request-forms-filter') || '{}'); } catch { return {}; } })();
  const filter = {...blank, ...Object.fromEntries(Object.entries(saved).filter(([k]) => k in blank)), skip: 0};
  // From a company's page (company=<id>&name=<name>): only that company's request forms, not kept for next time.
  const linked = new URLSearchParams(location.search);
  const company = {id: /^\d+$/.test(linked.get('company') || '') ? linked.get('company') : '', name: linked.get('name') || ''};
  // It starts with no other filter, and the filters kept for next time are left as they were.
  if (company.id) Object.assign(filter, blank, {company: company.id});
  const everyCompany = () => { delete filter.company; company.id = ''; history.replaceState(null, '', location.pathname); };
  let data = null, choices = null, session = null, timer = 0, datesOpen = !!(filter.createdFrom || filter.createdTo);

  const query = () => new URLSearchParams(Object.entries(filter).filter(([k, v]) => v !== '' && !(k === 'skip' && v === 0)).map(([k, v]) => [k, String(v)]));

  async function load() {
    if (!filter.company) try { localStorage.setItem('request-forms-filter', JSON.stringify({...filter, skip: undefined})); } catch { /* not kept */ }
    const answer = await api('GET', 'requestforms?' + query());
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
  const statusTone = status => /invoiced/i.test(status) && !/pending/i.test(status) ? 'done' : /recall|reject|cancel/i.test(status) ? 'warn' : /backorder/i.test(status) ? 'other' : '';
  // The type: a switch (All and each type) when there are a few, else a list.
  const types = () => choices.types || [];
  const typeControl = () => types().length <= 3
    ? `<div class="tabs orders-segments orders-wide" role="group" aria-label="Request Form Type">${[{id: '', label: 'All'}].concat(types()).map(t =>
      `<button type="button" class="tab" data-type="${esc(t.id)}" aria-pressed="${String(String(t.id) === String(filter.type))}">${esc(t.label)}</button>`).join('')}</div>`
    : `<label class="filter-capsule orders-wide"><span>Type</span><select id="type">${options(types(), filter.type)}</select></label>`;

  function shell() {
    page.innerHTML = `
      <div class="page-heading orders-heading"><div class="orders-title"><h1>Request forms</h1><p id="count">&nbsp;</p></div>
        <div class="heading-actions" id="actions"></div></div>
      <div class="tabs orders-kinds" role="group" aria-label="Orders"><a class="tab" href="costingsheets.aspx" aria-pressed="false">Costing sheets</a><a class="tab" href="requestforms.aspx" aria-current="page" aria-pressed="true">Request forms</a></div>
      <div class="orders-filters" role="search" aria-label="Filter request forms">
        <div class="orders-filter-row">
          <label class="search-box"><span data-icon="search"></span><input id="q" type="search" placeholder="Number, status, reference or company" value="${esc(filter.q)}" aria-label="Search the request forms"></label>
          ${typeControl()}
          <label class="filter-capsule orders-wide"><span>Status</span><select id="status">${options(choices.statuses, filter.status)}</select></label>
          <label class="filter-capsule orders-wide"><span>Sales rep</span><select id="salesRep">${options(choices.salesReps, filter.salesRep, 'Everyone')}</select></label>
          <button type="button" class="filter-capsule orders-wide" id="more" aria-expanded="${datesOpen}" aria-controls="dates"><span data-icon="clock"></span>Created</button>
          <button type="button" class="text-button orders-wide" id="reset">Reset</button>
          <button type="button" class="icon-button orders-filter-button" id="open-filters" aria-haspopup="dialog" aria-label="Filters"><span data-icon="settings"></span><span class="filter-dot" id="filter-dot" hidden></span></button>
        </div>
        <button type="button" class="filter-capsule orders-status-button" id="open-status" aria-haspopup="dialog"><span>Status:</span><b id="status-label"></b><span data-icon="arrow-right" class="orders-caret"></span></button>
        <div class="orders-filter-row orders-dates orders-wide" id="dates"${datesOpen ? '' : ' hidden'}>${dateCapsule('createdFrom', 'From Creation Date')}${dateCapsule('createdTo', 'To Creation Date')}</div>
        ${company.id ? `<p class="notice orders-company" id="company-only">Only the request forms of <b>${esc(company.name || 'company ' + company.id)}</b>. <button type="button" class="text-button" id="every-company">Show every company</button></p>` : ''}
      </div>
      <section class="orders-list" aria-label="Request form list"><div id="rows"></div><div id="cards" class="file-cards"></div><div class="pager" id="pager" hidden></div></section>`;
    drawIcons(page);
    const later = () => { clearTimeout(timer); filter.skip = 0; timer = setTimeout(load, 400); };
    $('#q').addEventListener('input', event => { filter.q = event.target.value; later(); });
    ['status', 'type', 'salesRep', 'createdFrom', 'createdTo'].forEach(name =>
      $('#' + name)?.addEventListener('change', event => { filter[name] = event.target.value; filter.skip = 0; load(); }));
    $$('[data-type]', page).forEach(b => b.addEventListener('click', () => { filter.type = b.dataset.type; filter.skip = 0; page.innerHTML = ''; load(); }));
    $('#more').addEventListener('click', () => { datesOpen = !datesOpen; $('#dates').hidden = !datesOpen; $('#more').setAttribute('aria-expanded', datesOpen); });
    $('#every-company')?.addEventListener('click', () => { everyCompany(); filter.skip = 0; page.innerHTML = ''; load(); });
    $('#reset').addEventListener('click', reset);
    $('#open-filters').addEventListener('click', filterSheet);
    $('#open-status').addEventListener('click', statusSheet);
  }

  function reset() { everyCompany(); Object.assign(filter, blank, {skip: 0}); datesOpen = false; page.innerHTML = ''; load(); }

  function actions() {
    $('#actions').innerHTML = `<div class="action-capsule" role="group" aria-label="Request form actions"><a class="capsule-button" id="download" href="file.ashx/requestforms?${esc(query().toString())}" title="Download">
      <span data-icon="excel"></span><span class="action-label">Download</span></a></div>`;
    drawIcons($('#actions'));
  }

  // One line per request form; the headings sort as before (Request form, Company, Sales rep, Created, Status).
  const columns = [['id', 'Request form', 'c-rf'], ['', 'Type', 'c-type'], ['company', 'Company', 'c-company'], ['salesman', 'Sales rep', 'c-rep'], ['status', 'Status', 'c-rfstatus'],
    ['', 'Delivery / collection', 'c-when'], ['created', 'Created', 'c-date'], ['', 'Printed', 'c-date c-updated'], ['', '', 'c-open']];

  function heading([key, label, cls]) {
    if (!key) return `<th class="${cls}">${label ? esc(label) : '<span class="sr-only">Open</span>'}</th>`;
    const sorted = filter.sort === key;
    return `<th class="${cls}"${sorted ? ` aria-sort="${filter.desc === '1' ? 'descending' : 'ascending'}"` : ''}><button type="button" data-sort="${key}">${esc(label)}</button></th>`;
  }

  function render() {
    actions();
    const filtered = !!filter.company || Object.keys(blank).some(k => k !== 'sort' && k !== 'desc' && filter[k]);
    const seesAll = session && session.can.seesAll;
    const sortNote = filter.sort === 'created' && filter.desc === '1' ? 'newest first' : `by ${(columns.find(c => c[0] === filter.sort) || ['', 'number'])[1].toLowerCase()}`;
    $('#count').innerHTML = `<b>${data.total.toLocaleString('en-SG')} request form${data.total === 1 ? '' : 's'}</b>${filtered ? ' match' : ''}<span class="orders-note"> · ${esc(sortNote)} · ${seesAll ? 'every request form in MyOrders' : "yours, your team's and those you're the specialist of"} · read only, from MyOrders</span>`;
    const status = (choices.statuses || []).find(c => String(c.id) === String(filter.status));
    $('#status-label').textContent = status ? status.label : 'All';
    const others = filter.type || filter.salesRep || filter.createdFrom || filter.createdTo;
    $('#filter-dot').hidden = !others;
    $('#open-filters').setAttribute('aria-label', others ? 'Filters (on)' : 'Filters');
    const whenOf = r => r.dates.length ? `${r.type === 'Collection' ? 'Collection' : 'Delivery'} ${r.dates.map(d => date(d)).join(', ')}` : '';
    const pills = r => `<span class="orders-status ${statusTone(r.status)}" title="${esc(r.status)}">${esc(r.status)}</span>${r.isPendingPrint ? '<span class="orders-status warn rf-pending">Pending print</span>' : ''}`;
    const facts = r => [r.updatedAt ? 'Updated ' + date(r.updatedAt) : '', r.recallAt ? 'Recalled ' + date(r.recallAt) : '', r.printedAt ? 'Printed ' + dateTime(r.printedAt) : '', r.isOpoChecked ? 'OPO checked' : ''].filter(Boolean).join(' · ');
    const empty = `<div class="empty"><h3>No request forms to show</h3><p class="muted">${filtered ? 'No request form matches the search and filters. Try another number or type, or press Reset.' : 'There are no request forms of yours or your team.'}</p></div>`;
    $('#rows').innerHTML = data.rows.length ? `<table class="data orders-table rf-table"><thead><tr>${columns.map(heading).join('')}</tr></thead><tbody>
      ${data.rows.map(r => `<tr data-href="requestform.aspx?id=${r.id}">
        <td class="c-rf" title="${r.reference ? 'Ref # ' + esc(r.reference) : ''}"><a href="requestform.aspx?id=${r.id}"># ${r.id}</a>${r.reference ? `<span class="muted"> · ${esc(r.reference)}</span>` : ''}</td>
        <td class="c-type">${esc(r.type)}</td>
        <td class="c-company" title="${esc(r.company)}${r.contact ? ' · ' + esc(r.contact) : ''}"><b>${esc(r.company)}</b>${r.contact ? `<span class="muted"> · ${esc(r.contact)}</span>` : ''}</td>
        <td class="c-rep" title="${esc(r.salesman)}">${esc(r.salesman)}</td>
        <td class="c-rfstatus" title="${esc(facts(r))}">${pills(r)}</td>
        <td class="c-when" title="${esc(whenOf(r))}">${esc(whenOf(r)) || '<span class="muted">—</span>'}</td>
        <td class="c-date" title="${esc(facts(r))}">${esc(date(r.createdAt))}</td>
        <td class="c-date c-updated" title="${r.printedAt ? esc(dateTime(r.printedAt)) : ''}">${r.printedAt ? esc(date(r.printedAt)) : '<span class="muted">—</span>'}</td>
        <td class="c-open">${r.canPrint ? `<a class="icon-button small" href="requestformprint.aspx?id=${r.id}" target="_blank" rel="noopener" title="Print request form ${r.id}" aria-label="Print request form ${r.id}"><span data-icon="printer"></span></a>` : ''}<a class="icon-button small" href="requestform.aspx?id=${r.id}" title="Open request form ${r.id}" aria-label="Open request form ${r.id}"><span data-icon="arrow-right"></span></a></td></tr>`).join('')}
      </tbody></table>`
      : empty;
    $('#cards').innerHTML = data.rows.map(r => `<a class="file-card orders-card" href="requestform.aspx?id=${r.id}">
        <span class="file-card-body"><span class="file-card-top"><b># ${r.id}<span class="muted"> · ${esc(r.type)}</span></b></span>
          ${r.reference ? `<span class="file-card-line">${esc(r.reference)}</span>` : ''}
          <span class="file-card-line strong">${esc(r.company)}</span>
          <span class="orders-card-foot">${pills(r)}<span class="file-card-line orders-card-right">${esc(whenOf(r) || 'Created ' + date(r.createdAt))}</span></span>
          ${facts(r) || r.salesman ? `<span class="file-card-line">${esc([r.salesman, facts(r)].filter(Boolean).join(' · '))}</span>` : ''}</span>
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
        <h2 id="status-sheet-title">Request Form Status</h2><span class="orders-sheet-spacer" aria-hidden="true"></span></div>
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

  /** The phone's other filters: type, sales rep and the creation dates, in a sheet, applied together. */
  function filterSheet() {
    const sheet = document.createElement('dialog');
    sheet.className = 'companies-sheet';
    sheet.setAttribute('aria-labelledby', 'filter-title');
    sheet.innerHTML = `<form method="dialog" class="sheet-form"><span class="sheet-grabber" aria-hidden="true"></span>
      <div class="sheet-head"><button type="button" class="icon-button" value="cancel" aria-label="Close filters"><span data-icon="close"></span></button>
        <h2 id="filter-title">Filter request forms</h2><button type="submit" class="button primary">Apply</button></div>
      <div class="sheet-group">
        <label><span>Type</span><select data-sheet="type">${options(types(), filter.type)}</select></label>
        <label><span>Sales rep</span><select data-sheet="salesRep">${options(choices.salesReps, filter.salesRep, 'Everyone')}</select></label></div>
      <div class="sheet-group"><label><span>From Creation Date</span><input type="date" data-sheet="createdFrom" value="${esc(filter.createdFrom)}"></label>
        <label><span>To Creation Date</span><input type="date" data-sheet="createdTo" value="${esc(filter.createdTo)}"></label></div>
      <button type="button" class="button secondary" id="sheet-reset">Reset filters</button></form>`;
    document.body.append(sheet);
    drawIcons(sheet);
    $('button[value=cancel]', sheet).addEventListener('click', () => sheet.close());
    sheet.addEventListener('close', () => sheet.remove());
    sheet.addEventListener('click', event => { if (event.target === sheet) sheet.close(); });
    $('form', sheet).addEventListener('submit', () => {
      $$('[data-sheet]', sheet).forEach(input => { filter[input.dataset.sheet] = input.value; });
      datesOpen = !!(filter.createdFrom || filter.createdTo);
      filter.skip = 0;
      page.innerHTML = '';
      load();
    });
    $('#sheet-reset', sheet).addEventListener('click', () => { sheet.close(); reset(); });
    sheet.showModal();
  }

  page.addEventListener('click', event => {
    const sort = event.target.closest('[data-sort]');
    if (sort) {
      const key = sort.dataset.sort;
      filter.desc = filter.sort === key ? (filter.desc === '1' ? '0' : '1') : (['id', 'created', 'printed'].includes(key) ? '1' : '0');
      filter.sort = key;
      filter.skip = 0;
      load();
      return;
    }
    const move = event.target.closest('[data-page]');
    if (move && data) { filter.skip = Math.max(0, data.skip + Number(move.dataset.page) * data.take); load(); window.scrollTo({top: 0}); return; }
    // The row opens its request form (its number is the link for the keyboard).
    const row = event.target.closest('tr[data-href]');
    if (!row || event.target.closest('a, button, input, label, select')) return;
    if (window.getSelection && String(window.getSelection())) return;
    if (event.ctrlKey || event.metaKey) window.open(row.dataset.href, '_blank'); else location.href = row.dataset.href;
  });

  (async () => {
    const [answer, filters] = await Promise.all([api('GET', 'orders/session'), api('GET', 'requestforms/filters')]);
    session = answer.result.ok ? {can: answer.can} : null;
    choices = filters.result.ok ? filters : {statuses: [], types: [], salesReps: []};
    load();
  })();
})();
