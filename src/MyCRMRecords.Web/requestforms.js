(() => {
  'use strict';
  // requestforms.aspx: MyOrders' request forms (its RequestForms screen, with what its specialists' list showed), read
  // only. The person sees those MyOrders let them see (every one for the roles that see all; else their own and their
  // MyCRM team's, and the ones they are the specialist of), 50 at a time, newest first. The search and filters apply as
  // they are typed or chosen (MyOrders waited for Search), are kept for next time, and the list downloads as MyOrders'
  // Excel. Each row opens the request form's page or its print.
  const {$, esc, api, drawIcons, pager, dateTime, date} = window.WH;
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
  let data = null, choices = null, session = null, timer = 0;

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
  const dateField = (name, label) => `<label class="field"><span>${esc(label)}</span><input type="date" id="${name}" value="${esc(filter[name])}"></label>`;

  function shell() {
    const seesAll = session && session.can.seesAll;
    page.innerHTML = `
      <div class="page-heading"><div><h1>Request forms</h1><p>${seesAll ? 'Every request form in MyOrders.' : "Your request forms and your team's, as MyOrders shows them."} Read only: they are kept here for looking back.</p></div>
        <div class="heading-actions" id="actions"></div></div>
      <section class="card">
        <div class="filters">
          <label class="search-box"><span data-icon="search"></span><input id="q" type="search" placeholder="Number, status, reference or company" value="${esc(filter.q)}" aria-label="Search the request forms"></label>
          <label class="field"><span>Request Form Status</span><select id="status">${options(choices.statuses, filter.status)}</select></label>
          <label class="field"><span>Request Form Type</span><select id="type">${options(choices.types, filter.type)}</select></label>
          <label class="field"><span>Sales Rep</span><select id="salesRep" style="max-width:220px">${options(choices.salesReps, filter.salesRep)}</select></label>
          ${dateField('createdFrom', 'From Creation Date')}${dateField('createdTo', 'To Creation Date')}
          <button type="button" class="button secondary" id="reset">Reset</button>
        </div>
        ${company.id ? `<p class="notice" id="company-only" style="margin:10px 0 0">Only the request forms of <b>${esc(company.name || 'company ' + company.id)}</b>. <button type="button" class="text-button" id="every-company">Show every company</button></p>` : ''}
        <p class="muted small" id="count" style="margin:10px 0 0"></p>
        <div id="rows"></div><div class="pager" id="pager" hidden></div>
      </section>`;
    drawIcons(page);
    const later = () => { clearTimeout(timer); filter.skip = 0; timer = setTimeout(load, 400); };
    $('#q').addEventListener('input', event => { filter.q = event.target.value; later(); });
    ['status', 'type', 'salesRep', 'createdFrom', 'createdTo'].forEach(name =>
      $('#' + name).addEventListener('change', event => { filter[name] = event.target.value; filter.skip = 0; load(); }));
    $('#every-company')?.addEventListener('click', () => { everyCompany(); filter.skip = 0; page.innerHTML = ''; load(); });
    $('#reset').addEventListener('click', () => { everyCompany(); Object.assign(filter, blank, {skip: 0}); page.innerHTML = ''; load(); });
  }

  function actions() {
    $('#actions').innerHTML = `<a class="button secondary" id="download" href="file.ashx/requestforms?${esc(query().toString())}"><span data-icon="excel"></span>Download</a>`;
    drawIcons($('#actions'));
  }

  // The row's buttons first; secondary facts under the main one, so the list fits the page without scrolling sideways.
  const columns = [['id', 'Request form'], ['company', 'Company'], ['salesman', 'Salesman'], ['created', 'Dates'], ['status', 'Request Form Status']];

  function heading([key, label]) {
    const sorted = filter.sort === key;
    return `<th${sorted ? ` aria-sort="${filter.desc === '1' ? 'descending' : 'ascending'}"` : ''}><button type="button" data-sort="${key}">${esc(label)}</button></th>`;
  }

  function render() {
    actions();
    const filtered = !!filter.company || Object.keys(blank).some(k => k !== 'sort' && k !== 'desc' && filter[k]);
    $('#count').textContent = `${data.total} request form${data.total === 1 ? '' : 's'}${filtered ? ' match' : ''}.`;
    $('#rows').innerHTML = data.rows.length ? `<div class="table-wrap" style="margin-top:10px"><table class="data"><thead><tr><th></th>${columns.map(heading).join('')}</tr></thead><tbody>
      ${data.rows.map(r => `<tr>
        <td class="nowrap"><a class="icon-button small" href="requestform.aspx?id=${r.id}" title="Open request form ${r.id}" aria-label="Open request form ${r.id}"><span data-icon="arrow-right"></span></a>
          ${r.canPrint ? `<a class="icon-button small" href="requestformprint.aspx?id=${r.id}" target="_blank" rel="noopener" title="Print request form ${r.id}" aria-label="Print request form ${r.id}"><span data-icon="printer"></span></a>` : ''}</td>
        <td><a href="requestform.aspx?id=${r.id}" class="nowrap"><b># ${r.id}</b></a>${r.reference ? `<span class="sub nowrap">Ref # ${esc(r.reference)}</span>` : ''}<span class="sub">${esc(r.type)}</span></td>
        <td class="wide-text"><b>${esc(r.company)}</b>${r.contact ? `<span class="sub">${esc(r.contact)}</span>` : ''}</td>
        <td>${esc(r.salesman)}</td>
        <td class="small">Created ${esc(date(r.createdAt))}${r.updatedAt ? `<span class="sub">Updated ${esc(date(r.updatedAt))}</span>` : ''}${r.recallAt ? `<span class="sub">Recalled ${esc(date(r.recallAt))}</span>` : ''}
          ${r.dates.length ? `<span class="sub">${r.type === 'Collection' ? 'Collection' : 'Delivery'} ${r.dates.map(d => esc(date(d))).join(', ')}</span>` : ''}</td>
        <td>${esc(r.status)}${r.isPendingPrint ? '<span class="sub"><span class="pill warn">Pending print</span></span>' : ''}
          ${r.printedAt ? `<span class="sub">Printed ${esc(dateTime(r.printedAt))}</span>` : ''}${r.isOpoChecked ? '<span class="sub">OPO checked</span>' : ''}</td></tr>`).join('')}
      </tbody></table></div>`
      : `<div class="empty" style="margin-top:10px"><h3>No request forms to show</h3><p class="muted">${filtered ? 'No request form matches the search and filters.' : 'There are no request forms of yours or your team.'}</p></div>`;
    drawIcons($('#rows'));
    pager($('#pager'), data.total, data.skip, data.take);
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
    if (move && data) { filter.skip = Math.max(0, data.skip + Number(move.dataset.page) * data.take); load(); window.scrollTo({top: 0}); }
  });

  (async () => {
    const [answer, filters] = await Promise.all([api('GET', 'orders/session'), api('GET', 'requestforms/filters')]);
    session = answer.result.ok ? {can: answer.can} : null;
    choices = filters.result.ok ? filters : {statuses: [], types: [], salesReps: []};
    load();
  })();
})();
