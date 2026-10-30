(() => {
  'use strict';
  // company.aspx?id=: a company (MyCRM's CompanyDetails) on one page: its details and salesperson, the number of
  // employees and sales remarks (anyone who opens it), its addresses and contacts (every role but Marketing), its
  // parent, siblings and subsidiaries, its territory movements (hidden from Sales and Telemarketers) and more details.
  // The Overview adds the copies per month on its machines (Advance) and tiles counting what the other tabs hold.
  // The company form is for Sales Admins. Every change is made in a dialog or in place, and risky ones ask first.
  const {$, $$, esc, api, toast, ask, date, dateTime, number, drawIcons, param, showErrors, crumbs} = window.WH;
  const {options, dialog, refused, text, companyForm} = window.CRM;
  const page = $('#page');
  const id = Number(param('id') || 0);
  let d = null, lists = null, addressType = '';

  async function load() {
    const [answer, listsAnswer] = await Promise.all([api('GET', 'companies/' + id), lists ? Promise.resolve(null) : api('GET', 'lists')]);
    if (listsAnswer) lists = listsAnswer;
    $('#loading').hidden = true;
    page.hidden = false;
    if (!answer.result.ok || !lists.result.ok) {
      const message = !answer.result.ok ? (answer.result.code === 'NOT_FOUND' ? 'That company is not there.' : answer.result.message) : lists.result.message;
      page.innerHTML = `<div class="empty"><h3>${esc(message)}</h3><p><a class="button secondary" href="./">Back to the companies</a></p></div>`;
      return;
    }
    d = answer;
    render();
  }


  /**
   * The page's sections, as tabs that switch in place (the chosen one is kept in the address, #contacts, so coming back
   * from a costing sheet opens the same tab). On a phone the Overview is the company's details and a list of the
   * sections, More details first (the rest of the Overview: #more), each opening on the whole screen with a way back
   * (app.css: .section-list, .section-back, .more-details).
   */
  const tabIds = ['overview', 'contacts', 'activities', 'prospects', 'projects', 'files', 'machines', 'costing', 'requests', 'relations'];
  const hashTab = location.hash.slice(1);
  let tab = tabIds.includes(hashTab) || hashTab === 'more' ? hashTab : 'overview';
  let orderCounts = {activities: '', prospects: '', projects: '', files: '', machines: '', costing: '', requests: ''};
  function sections() {
    return [
      {id: 'overview', label: 'Overview', count: '', color: '#0a58f0'},
      {id: 'contacts', label: 'Contacts', count: d.contacts.length, color: '#0a58f0'},
      {id: 'activities', label: 'Activities', count: orderCounts.activities, color: '#8b5cf6'},
      {id: 'prospects', label: 'Prospects', count: orderCounts.prospects, color: '#e59a0b'},
      {id: 'projects', label: 'Projects', count: orderCounts.projects, color: '#f2675a'},
      {id: 'files', label: 'Files', count: orderCounts.files, color: '#7c3aed'},
      {id: 'machines', label: 'Machines', count: orderCounts.machines, color: '#0d9488'},
      {id: 'costing', label: 'Costing sheets', count: orderCounts.costing, color: '#64748b'},
      {id: 'requests', label: 'Request forms', count: orderCounts.requests, color: '#64748b'},
      {id: 'relations', label: 'Relationships & history', count: '', color: '#0d9488'}
    ];
  }
  function showTab(next) {
    const more = next === 'more';
    tab = tabIds.includes(next) || more ? next : 'overview';
    const panelTab = more ? 'overview' : tab;
    $$('.company-tabs .tab', page).forEach(b => { b.setAttribute('aria-selected', String(b.dataset.tab === panelTab)); b.tabIndex = b.dataset.tab === panelTab ? 0 : -1; });
    $$('.tab-panel', page).forEach(panel => { panel.hidden = panel.id !== 'panel-' + panelTab; });
    page.classList.toggle('in-section', tab !== 'overview');
    page.classList.toggle('more-details', more);
    history.replaceState(null, '', tab === 'overview' ? location.pathname + location.search : '#' + tab);
  }
  function setCount(id, count) {
    orderCounts[id] = count;
    const label = $(`#tab-${id}`, page);
    if (label && !$('.count', label)) label.insertAdjacentHTML('beforeend', ` <span class="count">${count}</span>`);
    const row = $(`.section-row[data-tab="${id}"] .section-count`, page);
    if (row) row.textContent = count;
    const stat = $(`.stat-tile[data-tab="${id}"] .stat-n`, page);
    if (stat) stat.textContent = number(count);
  }

  /** A row of the company's details: its label, value and, smaller, what goes with it. */
  const detail = (label, value, sub = '') => `<div class="ov-fact"><dt>${esc(label)}</dt><dd>${value || '<span class="muted">-</span>'}${sub ? `<span class="ov-sub">${sub}</span>` : ''}</dd></div>`;

  function render() {
    const c = d.company, can = d.can;
    document.title = c.name + ' · MyCRMRecords';
    const territory = d.owner ? (can.territories ? `<a href="territories.aspx?id=${d.owner.territoryId}">${esc(d.owner.territory)}</a>` : esc(d.owner.territory)) : '';
    const info = [d.owner ? 'Territory ' + esc(d.owner.territory) : '', d.owner ? esc(d.owner.name || 'Vacant') : 'No salesperson',
      c.brn ? 'BRN ' + esc(c.brn) : 'No BRN', c.customerNo ? 'Customer code ' + esc(c.customerNo) : ''].filter(Boolean).join(' · ');
    const primary = d.contacts.filter(p => p.isDefault);
    const stats = [
      {id: 'machines', label: 'Machines', sub: 'contract or warranty running', color: 'var(--teal)'},
      {id: 'files', label: 'Files', sub: 'in MyFiles', color: '#7c3aed'},
      {id: 'contacts', label: 'Contacts', sub: primary.length ? 'primary: ' + primary[0].name : 'no primary contact', color: 'var(--accent)'},
      {id: 'costing', label: 'Costing sheets', sub: 'from MyOrders', color: 'var(--coral)'}
    ];
    const ownerName = d.owner ? d.owner.name || 'Vacant' : '';
    page.innerHTML = `
      <div class="page-heading company-heading"><div class="company-title">${crumbs(c.name, [['Companies', './']])}
          <h1>${esc(c.name)} ${c.isActive ? '' : '<span class="pill warn">Inactive</span>'}</h1>
          <p class="company-info">${info}</p></div>
        <div class="heading-actions"><div class="action-capsule" role="group" aria-label="Company actions">
          <a class="capsule-button" href="companyprint.aspx?id=${id}" title="Print summary"><span data-icon="printer"></span><span class="action-label">Print summary</span></a>
          ${can.edit ? '<span class="capsule-divider" aria-hidden="true"></span><button type="button" class="capsule-button" id="edit" title="Edit company"><span data-icon="edit"></span><span class="action-label">Edit company</span></button>' : ''}
        </div><button type="button" class="button primary" id="new-activity" title="New activity"><span data-icon="plus"></span><span class="action-label">New activity</span></button></div></div>
      <div class="tabs company-tabs" role="tablist" aria-label="Company sections">${sections().map(s => `<button type="button" class="tab" role="tab" id="tab-${s.id}" aria-controls="panel-${s.id}" data-tab="${s.id}">${esc(s.label)}${s.count !== '' ? ` <span class="count">${s.count}</span>` : ''}</button>`).join('')}</div>
      <button type="button" class="section-back" id="section-back"><span data-icon="arrow-left"></span>${esc(c.name)}</button>
      <div class="tab-panel overview" role="tabpanel" id="panel-overview" aria-labelledby="tab-overview">
        <div class="ov-main">
          <section class="card ov-details" aria-labelledby="ov-details"><div class="card-heading"><h2 id="ov-details">Company details</h2>${can.edit ? '<span class="muted small ov-hint">Change with Edit company</span>' : ''}</div>
            <dl class="ov-facts">
              ${detail('BRN', esc(c.brn))}${detail('A/C Type', esc(c.compAccountTypeLabel))}${detail('Customer Code', esc(c.customerNo))}
              ${detail('Salesperson', d.owner ? esc(ownerName) : '<span class="muted">No salesperson</span>', territory)}${detail('Customer Type', esc(c.companyGroupLabel))}${detail('Company Group', esc(c.groupName))}
              ${detail('Advance Customer Type', esc(c.advanceCustomerTypeLabel))}${detail('Industry', esc(c.industry))}${detail('Website', esc(c.website))}
              ${detail('Is Channel Partner', c.isChannelPartner ? 'Yes' : 'No')}${detail('Created', esc(date(c.createdDate)), c.createdBy && c.createdBy.name ? 'by ' + esc(c.createdBy.name) : '')}${detail('Last Updated', c.updatedDate ? esc(date(c.updatedDate)) : '', c.updatedDate && c.updatedBy && c.updatedBy.name ? 'by ' + esc(c.updatedBy.name) : '')}
            </dl></section>
          <section class="card ov-more-part ov-copies" aria-labelledby="ov-copies" id="copies"></section>
          <section class="card ov-more-part" id="addresses"></section>
        </div>
        <aside class="ov-side">
          <div class="stat-tiles">${stats.map(s => `<button type="button" class="stat-tile" data-tab="${s.id}" aria-label="${esc(s.label)}: open the tab">
            <span class="stat-label" style="color:${s.color}">${esc(s.label)}</span><span class="stat-n">${s.id === 'contacts' ? number(d.contacts.length) : '…'}</span><span class="stat-sub">${esc(s.sub)}</span></button>`).join('')}</div>
          <section class="card ov-more-part ov-owner" aria-labelledby="ov-owner"><h2 id="ov-owner">Salesperson</h2>
            ${d.owner ? `<div class="owner-row"><span class="owner-avatar" aria-hidden="true">${esc(((/[A-Za-z0-9]/.exec(ownerName) || ['?'])[0]).toUpperCase())}</span>
              <span class="owner-text"><span class="muted small">Owner</span><b>${esc(ownerName)}</b><span class="muted small">Territory ${territory}</span></span></div>`
              : '<p class="muted small">No salesperson: the company is in no territory.</p>'}
            ${c.isCaretake ? '<p class="muted small" style="margin:8px 0 0">This company is taken care of by someone else too: see Caretake Remarks under More company details.</p>' : ''}</section>
          <section class="card ov-more-part ov-sales" aria-labelledby="ov-sales"><h2 id="ov-sales">Sales notes</h2>
            <div class="ov-employees"><div class="fact-label">No of Employees</div><div id="employees"></div></div>
            <label class="field"><span class="fact-label">Sales Remarks</span><textarea id="sales-remarks" rows="3" maxlength="50000" data-field="salesRemarks">${esc(c.salesRemarks)}</textarea><span class="field-error"></span></label>
            <div class="action-buttons"><button type="button" class="button primary small-btn" id="save-remarks"><span data-icon="check"></span>Save remarks</button></div></section>
          <section class="card ov-more-part" aria-labelledby="ov-more"><h2 id="ov-more">More company details</h2><dl class="ov-facts ov-facts-list">
            ${detail('Remarks', c.remarks ? `<span style="white-space:pre-wrap">${esc(c.remarks)}</span>` : '')}
            ${detail('Caretake Remarks', d.caretakes.map(r => `<div style="white-space:pre-wrap">${esc(r)}</div>`).join(''))}
            ${detail('Phone', esc(c.phone))}${detail('Fax', esc(c.fax))}${detail('Inc. Cert. No.', esc(c.incCertNo))}${detail('Paid Up Capital', c.paidUpCapital ? number(c.paidUpCapital) : '')}
            ${detail('MIF Potential', c.mifPotential ? number(c.mifPotential) : '')}${detail('Revenue', c.revenue ? number(c.revenue) : '')}${detail('Comp Ownership', esc(c.compOwnershipLabel))}${detail('Is Caretake', c.isCaretake ? 'Yes' : 'No')}
            ${detail('Is Created By MSD', c.isCreatedByMsd ? 'Yes' : 'No')}${detail('Customer Type (BDG)', esc(c.customerType))}${detail('Credit Terms', esc(c.creditTerms))}${detail('Old CRM Id', c.crmCompanyId ? String(c.crmCompanyId) : '')}
          </dl></section>
        </aside>
        <nav class="section-list" aria-label="Company sections">${[{id: 'more', label: 'More details', count: '', color: '#8e8e93'}].concat(sections().filter(s => s.id !== 'overview')).map(s => `<button type="button" class="section-row" data-tab="${s.id}">
          <span class="section-dot" style="--dot:${s.color}" aria-hidden="true">${esc(s.label.charAt(0))}</span><span class="section-name">${esc(s.label)}</span>
          <span class="section-count">${s.count}</span><span data-icon="arrow-right" class="section-chevron"></span></button>`).join('')}</nav>
      </div>
      <div class="stack tab-panel" role="tabpanel" id="panel-contacts" aria-labelledby="tab-contacts"><section class="card" id="contacts"></section></div>
      <div class="stack tab-panel" role="tabpanel" id="panel-activities" aria-labelledby="tab-activities"><section class="card" id="activities"></section></div>
      <div class="stack tab-panel" role="tabpanel" id="panel-prospects" aria-labelledby="tab-prospects"><section class="card" id="prospects"></section></div>
      <div class="stack tab-panel" role="tabpanel" id="panel-projects" aria-labelledby="tab-projects"><section class="card" id="projects"></section></div>
      <div class="stack tab-panel" role="tabpanel" id="panel-files" aria-labelledby="tab-files"><section class="card" id="files"></section></div>
      <div class="stack tab-panel" role="tabpanel" id="panel-machines" aria-labelledby="tab-machines"><section class="card" id="machines"></section></div>
      <div class="stack tab-panel" role="tabpanel" id="panel-costing" aria-labelledby="tab-costing"><section class="card" id="costing"></section></div>
      <div class="stack tab-panel" role="tabpanel" id="panel-requests" aria-labelledby="tab-requests"><section class="card" id="requests"></section></div>
      <div class="stack tab-panel" role="tabpanel" id="panel-relations" aria-labelledby="tab-relations"><section class="card" id="relationships"></section>
        ${d.movements ? `<section class="card"><h2>Territory Movement History</h2>${movements()}</section>` : ''}</div>`;
    employees(false);
    addresses();
    contacts();
    relationships();
    orders(c);
    files(c);
    machines(false);
    copies();
    activities();
    prospects();
    projectsTab();
    drawIcons(page);
    $('#new-activity').addEventListener('click', () => window.ACT.create({companyId: id, companyName: c.name}, () => { showTab('activities'); activities(); }));
    showTab(tab);
    $('#edit')?.addEventListener('click', async () => { if (await companyForm(lists, c)) load(); });
    $('#save-remarks').addEventListener('click', async () => {
      const answer = await api('POST', `companies/${id}/remarks`, {salesRemarks: $('#sales-remarks').value});
      toast(answer.result.message, !answer.result.ok);
      showErrors(page, answer.result.errors || []);
    });
  }

  /**
   * The company's activities in MyCRM (MyCRM's Company Activities): those of the person's list and of the company's
   * salespeople, by when and action; each opens the activity panel (activityform.js), where its owner changes it.
   */
  const actFilter = {when: 'all', action: ''};
  async function activities() {
    const box = $('#activities');
    if (!box.innerHTML) box.innerHTML = '<h2>Activities</h2><p class="muted small">Loading…</p>';
    const answer = await api('GET', 'activities?' + new URLSearchParams({company: String(id), when: actFilter.when, action: actFilter.action}));
    if (!answer.result.ok) { box.innerHTML = `<h2>Activities</h2><p class="muted small">${esc(answer.result.message)}</p>`; return; }
    if (!answer.canCreate) $('#new-activity')?.remove();
    const rows = answer.rows;
    box.innerHTML = `<div class="co-tab-head"><div class="tabs" role="group" aria-label="When">${[['upcoming', 'Upcoming'], ['past', 'Past'], ['all', 'All']].map(([v, l]) =>
        `<button type="button" class="tab" data-act-when="${v}" aria-pressed="${actFilter.when === v}">${l}</button>`).join('')}</div>
      <label><span class="sr-only">Activity Action</span><select data-act-action><option value="">Activity Action: All</option>${answer.actions.map(a =>
        `<option value="${esc(a.code)}"${actFilter.action === a.code ? ' selected' : ''}>${esc(a.label)}</option>`).join('')}</select></label>
      <span class="muted small">${answer.total} activit${answer.total === 1 ? 'y' : 'ies'}</span></div>
      ${rows.length ? `<div class="table-wrap"><table class="data"><thead><tr><th>Date · time</th><th>Action</th><th>Activity type</th><th>Remarks</th><th>Attendees</th><th>Owner</th><th>Status</th></tr></thead><tbody>
        ${rows.map(r => `<tr><td class="nowrap">${esc(date(r.date))} <span class="muted small">${esc(r.start)}${r.end ? '–' + esc(r.end) : ''}</span></td><td>${esc(r.action)}</td><td>${esc(r.types.join(', '))}</td>
          <td><button type="button" class="act-remarks" data-activity="${r.id}">${esc(r.remarks || r.action)}</button></td><td>${esc(r.contacts.join(', '))}</td><td>${esc(r.owner)}</td>
          <td>${window.ACT.statusTag(r.status)}</td></tr>`).join('')}</tbody></table></div>`
        : '<p class="muted">No activities here yet. Use New activity to log one.</p>'}
      <p class="muted small" style="margin:10px 0 0">Activities are saved in MyCRM. Only the owner can change an activity; others can open it to read.</p>`;
    setCount('activities', answer.total);
    drawIcons(box);
    $$('[data-act-when]', box).forEach(b => b.addEventListener('click', () => { actFilter.when = b.dataset.actWhen; activities(); }));
    $('[data-act-action]', box).addEventListener('change', event => { actFilter.action = event.target.value; activities(); });
    $$('[data-activity]', box).forEach(b => b.addEventListener('click', () => window.ACT.open(Number(b.dataset.activity), activities)));
  }

  /** The company's prospects in MyCRM: only read here, with the chosen one's details; they are changed in MyCRM. */
  let prospectId = 0;
  async function prospects() {
    const box = $('#prospects');
    const answer = await api('GET', 'prospects?' + new URLSearchParams({company: String(id)}));
    if (!answer.result.ok) { box.innerHTML = `<h2>Prospects</h2><p class="muted small">${esc(answer.result.message)}</p>`; return; }
    const rows = answer.rows, chosen = rows.find(p => p.id === prospectId);
    const link = `${esc(answer.myCrm)}CompanyDetails.aspx?CompanyId=${id}`;
    const sum = p => [p.sumUpMonth, p.sumUpYear].filter(Boolean).join(' ');
    box.innerHTML = `<div class="co-tab-head"><h2 style="margin:0">Prospects</h2><span class="muted small">View only here: prospects are created and changed in MyCRM.</span>
        <a class="button secondary small-btn" href="${link}" target="_blank" rel="noopener"><span data-icon="arrow-up-right"></span>Open in MyCRM</a></div>
      <div class="co-prospects${chosen ? '' : ' closed'}"><div>${rows.length ? `<div class="table-wrap"><table class="data"><thead><tr><th>Remarks · prospect type</th><th>Stage</th><th class="num">Forecast</th><th>Sum up</th><th>Owner</th><th>Created · updated</th></tr></thead><tbody>
        ${rows.map(p => `<tr class="co-prospect-row${p.id === prospectId ? ' chosen' : ''}" data-prospect="${p.id}" tabindex="0"><td><b>${esc(p.remarks)}</b> <span class="muted">${esc(p.type)}</span></td><td>${esc(p.stage)}</td>
          <td class="num">${number(p.forecast)}</td><td class="muted">${esc(sum(p))}</td><td>${esc(p.owner)}</td><td class="muted nowrap">${esc(date(p.created))}${p.updated ? ' · ' + esc(date(p.updated)) : ''}</td></tr>`).join('')}</tbody></table></div>`
        : '<p class="muted">This company has no prospects in MyCRM.</p>'}</div>
      ${chosen ? `<aside class="card co-prospect" aria-label="Prospect"><div class="modal-head"><h2>${esc(chosen.remarks)}</h2><button type="button" class="icon-button small" data-prospect-close aria-label="Close prospect details"><span data-icon="close"></span></button></div>
        <dl class="act-facts">${[['Stage', chosen.stage], ['Status', chosen.status], ['Prospect type', chosen.type], ['Forecast', number(chosen.forecast)], ['Total price', number(chosen.totalPrice)],
          ['Estimated order', date(chosen.estimatedOrder)], ['Date closed', date(chosen.closed)], ['Owner', chosen.owner], ['Contact', chosen.contact], ['Date created', date(chosen.created)],
          ['Updated', date(chosen.updated) + (chosen.updatedBy ? ' by ' + chosen.updatedBy : '')], ['Sum up', sum(chosen)]]
          .map(([l, v]) => `<div><dt>${esc(l)}</dt><dd>${esc(v) || '<span class="muted">–</span>'}</dd></div>`).join('')}</dl>
        <p class="muted small">Read only. Changes made in MyCRM show here straight away.</p></aside>` : ''}</div>`;
    setCount('prospects', rows.length);
    drawIcons(box);
    $$('[data-prospect]', box).forEach(r => r.addEventListener('click', () => { prospectId = Number(r.dataset.prospect); prospects(); }));
    $('[data-prospect-close]', box)?.addEventListener('click', () => { prospectId = 0; prospects(); });
  }

  /** The projects that include the company (MyCRM's projects), with their members and roles. */
  async function projectsTab() {
    const box = $('#projects');
    const answer = await api('GET', 'projects?' + new URLSearchParams({company: String(id)}));
    if (!answer.result.ok) { box.innerHTML = `<h2>Projects</h2><p class="muted small">${esc(answer.result.message)}</p>`; return; }
    const rows = answer.rows, open = rows.filter(r => !r.closed).length;
    box.innerHTML = `<div class="co-tab-head"><span><b>${rows.length} project${rows.length === 1 ? '' : 's'}</b> include this company · ${open} open</span>
        ${answer.canCreate ? '<a class="button secondary small-btn" href="projects.aspx"><span data-icon="plus"></span>Create a project</a>' : ''}</div>
      ${rows.length ? `<div class="table-wrap"><table class="data"><thead><tr><th>Project</th><th>Status</th><th>Owner</th><th>Members · role</th><th>End date</th><th>Closed</th></tr></thead><tbody>
        ${rows.map(p => `<tr><td>${p.canOpen ? `<a href="project.aspx?id=${p.id}"><b>${esc(p.name)}</b></a>` : `<b>${esc(p.name)}</b>`}</td><td>${esc(p.status)}</td><td>${esc(p.owner)}</td>
          <td class="muted">${esc(p.members.map(m => m.name + ' ' + m.role).join(' · '))}</td><td class="nowrap">${esc(date(p.endDate))}</td><td>${p.closed ? 'Closed' : 'Open'}</td></tr>`).join('')}</tbody></table></div>`
        : '<p class="muted">This company is in no project.</p>'}
      <p class="muted small" style="margin:10px 0 0"><b>Caretake works through projects.</b> Add a salesperson to a project with the role Salesperson (Caretaker) and they can see and work on this company until the project's end date: a temporary territory gives them access, taken away when the project closes or ends.</p>`;
    setCount('projects', rows.length);
    drawIcons(box);
  }

  /**
   * Copies per month (Overview): the black and colour copies billed on all the company's machines in Advance over the
   * last 12 months, from machines/copies (read only), as pairs of bars.
   */
  async function copies() {
    const box = $('#copies');
    const heading = (sub = '') => `<div class="card-heading"><h2 id="ov-copies">Copies per month${sub ? ` <span class="muted ov-sub-title">· ${sub}</span>` : ''}</h2>
      <span class="legend copies-legend"><span><i style="background:var(--teal)"></i>Black</span><span><i style="background:var(--coral)"></i>Colour</span></span></div>`;
    box.innerHTML = heading() + '<p class="muted small">Loading…</p>';
    const answer = await api('GET', 'machines/copies?' + new URLSearchParams({company: String(id)}));
    if (!answer.result.ok) { box.innerHTML = heading() + `<p class="muted small">${esc(answer.result.message)}</p>`; return; }
    if (!answer.machines) { box.innerHTML = heading() + `<p class="muted small">${d.company.brn ? 'Advance has no machines with this BRN.' : 'The company has no BRN: its machines are found by BRN.'}</p>`; return; }
    const months = answer.months;
    const name = m => ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'][m.month - 1];
    const top = Math.max(1, ...months.map(m => Math.max(m.blackWhite, m.colour)));
    const pct = v => v > 0 ? Math.max(2, Math.round(v / top * 100)) : 0;
    const total = (k) => months.reduce((sum, m) => sum + m[k], 0);
    const first = months[0], last = months[months.length - 1];
    const label = `Copies per month, ${name(first)} ${first.year} to ${name(last)} ${last.year}: ${number(total('blackWhite'))} black and ${number(total('colour'))} colour in all`;
    box.innerHTML = heading(`all ${answer.machines} machine${answer.machines === 1 ? '' : 's'}`) + `
      <div class="copies-chart" role="img" aria-label="${esc(label)}">${months.map(m => `<div class="copies-month" title="${esc(`${name(m)} ${m.year}: ${number(m.blackWhite)} black, ${number(m.colour)} colour`)}">
        <div class="copies-bars"><span class="bar-black" style="height:${pct(m.blackWhite)}%"></span><span class="bar-colour" style="height:${pct(m.colour)}%"></span></div>
        <span class="copies-label">${esc(name(m))}</span></div>`).join('')}</div>
      <p class="muted small copies-note">Billed copies from Advance, each machine's of its contract. Last 12 months: <b>${number(total('blackWhite'))}</b> black, <b>${number(total('colour'))}</b> colour.
        <button type="button" class="text-button" data-tab="machines">Machines</button> · <a href="usage.aspx?company=${id}">Usage &amp; history ›</a></p>`;
  }

  /**
   * The company's costing sheets and request forms from MyOrders (read only), each on its tab: the newest of those the
   * person may see, each opening its page, and a link to the whole list of this company's.
   */
  async function orders(c) {
    const costing = $('#costing'), requests = $('#requests');
    costing.innerHTML = '<h2>Costing sheets</h2><p class="muted small">Loading…</p>';
    requests.innerHTML = '<h2>Request forms</h2><p class="muted small">Loading…</p>';
    const listOf = kind => api('GET', `${kind}?` + new URLSearchParams({company: String(id)}));
    const [sheets, forms] = await Promise.all([listOf('costingsheets'), listOf('requestforms')]);
    const all = (href, total, what) => total > 5
      ? `<p class="small" style="margin:8px 0 0"><a href="${href}?${new URLSearchParams({company: String(id), name: c.name})}">See all ${total} ${what}</a></p>` : '';
    const none = what => `<p class="muted small" style="margin:6px 0 0">No ${what} of this company that you may see.</p>`;
    const note = '<p class="muted small">From MyOrders, read only: the newest of those you may see.</p>';
    const sheetRows = sheets.result.ok ? sheets.rows.slice(0, 5) : [];
    const formRows = forms.result.ok ? forms.rows.slice(0, 5) : [];
    costing.innerHTML = `<h2>Costing sheets</h2>${note}
      ${!sheets.result.ok ? `<p class="muted small">${esc(sheets.result.message)}</p>` : sheetRows.length ? `<div class="table-wrap"><table class="data"><thead><tr><th>Costing sheet</th><th>Salesman</th><th>Created</th><th class="num">Achieved Price</th></tr></thead><tbody>
        ${sheetRows.map(r => `<tr><td><a href="costingsheet.aspx?id=${r.id}" class="nowrap"><b># ${r.id}</b></a><span class="sub">${esc(r.status)}</span></td>
          <td>${esc(r.salesman)}</td><td class="nowrap">${esc(date(r.createdAt))}</td>
          <td class="num nowrap">${Number(r.totalAchievedPrice || 0).toLocaleString('en-SG', {minimumFractionDigits: 2, maximumFractionDigits: 2})}</td></tr>`).join('')}
        </tbody></table></div>${all('costingsheets.aspx', sheets.total, 'costing sheets')}` : none('costing sheets')}`;
    requests.innerHTML = `<h2>Request forms</h2>${note}
      ${!forms.result.ok ? `<p class="muted small">${esc(forms.result.message)}</p>` : formRows.length ? `<div class="table-wrap"><table class="data"><thead><tr><th>Request form</th><th>Salesman</th><th>Created</th></tr></thead><tbody>
        ${formRows.map(r => `<tr><td><a href="requestform.aspx?id=${r.id}" class="nowrap"><b># ${r.id}</b></a><span class="sub">${esc(r.type)} · ${esc(r.status)}</span></td>
          <td>${esc(r.salesman)}</td><td class="nowrap">${esc(date(r.createdAt))}</td></tr>`).join('')}
        </tbody></table></div>${all('requestforms.aspx', forms.total, 'request forms')}` : none('request forms')}`;
    if (sheets.result.ok) setCount('costing', sheets.total);
    if (forms.result.ok) setCount('requests', forms.total);
    drawIcons(costing);
    drawIcons(requests);
  }

  /**
   * The company's files from MyFiles (read only), on its tab: the newest ten, each opening its PDF, and a link to the
   * Files page with all of this company's.
   */
  async function files(c) {
    const box = $('#files');
    box.innerHTML = '<h2>Files</h2><p class="muted small">Loading…</p>';
    const answer = await api('GET', 'files?' + new URLSearchParams({company: String(id)}));
    const rows = answer.result.ok ? answer.rows.slice(0, 10) : [];
    const size = bytes => bytes >= 1048576 ? (bytes / 1048576).toFixed(1) + ' MB' : bytes >= 1024 ? Math.round(bytes / 1024) + ' KB' : bytes + ' B';
    box.innerHTML = `<h2>Files</h2><p class="muted small">From MyFiles, read only: the newest first.</p>
      ${!answer.result.ok ? `<p class="muted small">${esc(answer.result.message)}</p>` : rows.length ? `<div class="table-wrap"><table class="data"><thead><tr><th>File</th><th>Type</th><th>Number</th><th>Uploaded</th><th class="num">Size</th></tr></thead><tbody>
        ${rows.map(r => `<tr><td class="wide-text"><a href="file.ashx/files/${r.id}?view=1" target="_blank" rel="noopener"><b>${esc(r.fileName || 'File ' + r.id)}</b></a></td>
          <td class="nowrap">${esc(r.type)}</td><td>${esc(r.numbers.length ? r.numbers[0].kind + ' ' + r.numbers[0].number : '')}</td>
          <td class="nowrap">${esc(date(r.uploadedAt))}</td><td class="num">${esc(size(r.size))}</td></tr>`).join('')}
        </tbody></table></div>${answer.total > 10 ? `<p class="small" style="margin:8px 0 0"><a href="files.aspx?${new URLSearchParams({company: String(id), name: c.name})}">See all ${answer.total} files</a></p>` : ''}`
        : '<p class="muted small" style="margin:6px 0 0">No files of this company in MyFiles.</p>'}`;
    if (answer.result.ok) setCount('files', answer.total);
    drawIcons(box);
  }

  /**
   * The company's machines from Advance (read only, MyCRM's Machines tab): by its BRN, those whose contract or warranty
   * ends after today (Active), or every one (All). Past dates are dimmed.
   */
  async function machines(all) {
    const box = $('#machines');
    const {coverageTag, machineUrl} = window.WH;
    box.innerHTML = '<h2>Machines</h2><p class="muted small">Loading…</p>';
    const answer = await api('GET', 'machines?' + new URLSearchParams(all ? {company: String(id), all: '1'} : {company: String(id)}));
    const today = new Date().toISOString().slice(0, 10);
    const day = v => v ? `<span class="${v.slice(0, 10) < today ? 'past' : ''}">${esc(date(v))}</span>` : '<span class="muted">–</span>';
    const rows = answer.result.ok ? answer.rows : [];
    const active = answer.result.ok ? answer.active : 0;
    box.innerHTML = `<div class="card-heading machines-heading"><h2>Machines</h2>
        ${answer.result.ok ? `<div class="tabs machine-switch" role="group" aria-label="Machines shown">
          <button type="button" class="tab" data-machines="active" aria-pressed="${String(!all)}">Active${!all ? ' · ' + answer.total : ''}</button>
          <button type="button" class="tab" data-machines="all" aria-pressed="${String(all)}">All${all ? ' · ' + answer.total : ''}</button></div>` : ''}</div>
      <p class="muted small">${all ? 'Every machine of this BRN in Advance, with time and materials, ended and no contract.' : 'Contract or warranty still running (from Advance, by BRN).'}
        <a href="usage.aspx?company=${id}">Usage &amp; history ›</a> · <a href="companymachines.aspx?company=${id}">Grouped by coverage ›</a></p>
      ${!answer.result.ok ? `<p class="muted small">${esc(answer.result.message)}</p>` : rows.length ? `<div class="file-cards machine-cards">${rows.map(r => `<a class="file-card" href="${machineUrl(r)}">
          <span class="file-card-body"><span class="file-card-top"><b>${esc(r.serial)} <span class="muted small">${esc(r.model)}</span></b>${coverageTag(r)}</span>
            <span class="file-card-line">${esc([r.contractNo, r.contractEnd ? 'contract ends ' + date(r.contractEnd) : '', r.warrantyEnd ? 'warranty ends ' + date(r.warrantyEnd) : ''].filter(Boolean).join(' · '))}</span>
            <span class="file-card-line">${esc(r.finance || 'No finance type')}</span></span>
          <span data-icon="arrow-right" class="section-chevron"></span></a>`).join('')}</div>
        <div class="table-wrap machines-wide"><table class="data company-machines"><thead><tr>
        <th>Serial</th><th>Model</th><th>Status</th><th>Contract</th><th>Contract start</th><th>Contract end</th><th>Warranty start</th><th>Warranty end</th><th>Finance type</th></tr></thead><tbody>
        ${rows.map(r => `<tr><td class="nowrap"><a href="${machineUrl(r)}"><b>${esc(r.serial)}</b></a></td><td class="nowrap">${esc(r.model)}</td><td>${coverageTag(r)}</td>
          <td class="nowrap">${r.contractNo ? `${esc(r.contractNo)} <span class="muted small">${esc(r.contractType)}</span>` : '<span class="muted">–</span>'}</td>
          <td class="nowrap">${day(r.contractStart)}</td><td class="nowrap">${day(r.contractEnd)}</td><td class="nowrap">${day(r.warrantyStart)}</td><td class="nowrap">${day(r.warrantyEnd)}</td>
          <td class="nowrap">${r.finance ? `${esc(r.finance)} <span class="muted small">${esc(r.financeCode)}</span>` : '<span class="muted">–</span>'}</td></tr>`).join('')}
        </tbody></table></div>`
        : `<p class="muted small" style="margin:6px 0 0">${all ? 'Advance has no machines with this BRN.' : 'No machine of this BRN has a contract or warranty running.'}</p>`}`;
    if (answer.result.ok) setCount('machines', all ? active : answer.total);
    $$('[data-machines]', box).forEach(b => b.addEventListener('click', () => machines(b.dataset.machines === 'all')));
    drawIcons(box);
  }

  /** The number of employees, shown with Edit, or an input with Save and Cancel (MyCRM's NoOfEmployees block). */
  function employees(editing) {
    const box = $('#employees');
    if (!editing) {
      box.innerHTML = `${number(d.company.noOfEmployees)} <button type="button" class="icon-button small" id="edit-employees" title="Change the number of employees" aria-label="Change the number of employees"><span data-icon="edit"></span></button>`;
      drawIcons(box);
      $('#edit-employees').addEventListener('click', () => employees(true));
      return;
    }
    box.innerHTML = `<span style="display:flex;gap:6px;align-items:center;flex-wrap:wrap"><input id="employees-input" type="number" min="0" value="${d.company.noOfEmployees}" style="max-width:110px" aria-label="No of Employees">
      <button type="button" class="button primary small-btn" id="employees-save">Save</button><button type="button" class="button secondary small-btn" id="employees-cancel">Cancel</button></span>`;
    $('#employees-input').focus();
    $('#employees-cancel').addEventListener('click', () => employees(false));
    $('#employees-save').addEventListener('click', async () => {
      const answer = await api('POST', `companies/${id}/employees`, {noOfEmployees: Number($('#employees-input').value || 0)});
      toast(answer.result.message, !answer.result.ok);
      if (answer.result.ok) { d.company.noOfEmployees = Number($('#employees-input').value || 0); employees(false); }
    });
  }

  // ---- Addresses ----
  function addresses() {
    const can = d.can.editContacts;
    const shown = d.addresses.filter(a => !addressType || a.type === addressType);
    $('#addresses').innerHTML = `<div class="card-heading"><h2>Addresses</h2><div class="row-first">
        <label class="field" style="min-width:150px"><span class="sr-only">Address type</span><select id="address-type">${options(lists.addressTypes, addressType, 'All types')}</select></label>
        ${can ? '<button type="button" class="button primary small-btn" data-address="0"><span data-icon="plus"></span>New address</button>' : ''}</div></div>
      ${shown.length ? `<div class="table-wrap"><table class="data"><thead><tr>${can ? '<th></th>' : ''}<th>Address</th><th>Type</th></tr></thead><tbody>
        ${shown.map(a => `<tr>${can ? `<td class="nowrap"><div class="row-first"><button type="button" class="button secondary small-btn" data-address="${a.id}"><span data-icon="edit"></span>Edit</button>
            <button type="button" class="icon-button small" data-delete-address="${a.id}" title="Delete the address" aria-label="Delete the address"><span data-icon="trash"></span></button></div></td>` : ''}
          <td class="wide-text">${esc(a.text)}${a.isDefault ? ' <span class="pill">Default Address</span>' : ''}${[a.city, a.stateProvince, a.country].filter(Boolean).length ? `<span class="sub">${esc([a.city, a.stateProvince, a.country].filter(Boolean).join(', '))}</span>` : ''}</td>
          <td>${can ? `<select class="cell-input" data-address-type="${a.id}" aria-label="Address type">${options(lists.addressTypes, a.type, 'Choose…')}</select>` : esc(a.typeLabel)}</td></tr>`).join('')}
        </tbody></table></div>` : `<p class="muted">${d.addresses.length ? 'No address of this type.' : 'No address.'}</p>`}`;
    drawIcons($('#addresses'));
    $('#address-type').addEventListener('change', event => { addressType = event.target.value; addresses(); });
  }

  function addressDialog(addressId) {
    const a = d.addresses.find(x => x.id === addressId) || {isDefault: false, type: '', postalCode: '', unitNumber: '', block: '', street: '', buildingName: '', city: '', stateProvince: '', country: 'Singapore'};
    const el = dialog(addressId ? 'Company Address' : 'New address', `<div class="form-grid two">
        <label class="check wide"><input type="checkbox" data-field="isDefault"${a.isDefault ? ' checked' : ''}>Is Default</label>
        <label class="field"><span class="required">Address Type</span><select data-field="addressType">${options(lists.addressTypes, a.type, 'Choose…')}</select><span class="field-error"></span></label>
        ${text('postalCode', 'Postal Code', a.postalCode, {max: 50})}
        ${text('block', 'Block', a.block, {required: true, max: 2000})}${text('street', 'Street', a.street, {required: true, max: 2000})}
        ${text('unitNumber', 'Unit Number', a.unitNumber, {max: 2000})}${text('buildingName', 'Building Name', a.buildingName, {max: 2000})}
        ${text('city', 'City', a.city, {max: 255})}${text('stateProvince', 'State / Province', a.stateProvince, {max: 255})}
        ${text('country', 'Country', a.country, {max: 255})}</div>`);
    $('form', el).addEventListener('submit', async event => {
      event.preventDefault();
      const v = name => $(`[data-field="${name}"]`, el);
      const body = {isDefault: v('isDefault').checked, addressType: v('addressType').value};
      for (const name of ['postalCode', 'block', 'street', 'unitNumber', 'buildingName', 'city', 'stateProvince', 'country']) body[name] = v(name).value;
      const answer = await api('POST', `companies/${id}/addresses/${addressId}`, body);
      if (!answer.result.ok) { refused(el, answer); return; }
      toast(answer.result.message);
      el.close();
      load();
    });
    $('[data-field=addressType]', el).focus();
  }

  // ---- Contacts ----
  function contacts() {
    const can = d.can.editContacts;
    $('#contacts').innerHTML = `<div class="card-heading"><h2>Contacts</h2>${can ? '<button type="button" class="button primary small-btn" data-contact="0"><span data-icon="plus"></span>Create Contact</button>' : ''}</div>
      ${d.contacts.length ? `<div class="table-wrap"><table class="data"><thead><tr>${can ? '<th></th>' : ''}<th>Contact</th><th>Phone</th><th>Email</th></tr></thead><tbody>
        ${d.contacts.map(p => `<tr>${can ? `<td class="nowrap"><div class="row-first"><button type="button" class="button secondary small-btn" data-contact="${p.id}"><span data-icon="edit"></span>Edit</button>
            <button type="button" class="icon-button small" data-delete-contact="${p.id}" data-name="${esc(p.name)}" title="Delete the contact" aria-label="Delete the contact"><span data-icon="trash"></span></button></div></td>` : ''}
          <td class="wide-text"><b>${esc(p.name)}</b>${p.isDefault ? ' <span class="pill">Primary Contact</span>' : ''}${p.isEqList ? ' <span class="pill">Eq. List</span>' : ''}${p.isLinkedIn ? ' <span class="pill">Sales Navigator</span>' : ''}
            ${p.designation || p.department ? `<span class="sub">${esc([p.designation, p.department].filter(Boolean).join(' · '))}</span>` : ''}</td>
          <td class="small">${p.phones.map(x => `<div class="nowrap">${x.typeLabel ? esc(x.typeLabel) + ': ' : ''}${esc(x.number)}</div>`).join('') || '<span class="muted">-</span>'}</td>
          <td class="small">${p.emails.map(x => `<div style="overflow-wrap:anywhere">${esc(x.address)}${x.consent ? ' <span class="yes" title="Consent to receive marketing information">✓ consent</span>' : ''}</div>`).join('') || '<span class="muted">-</span>'}</td></tr>`).join('')}
        </tbody></table></div>` : '<p class="muted">No contact.</p>'}`;
    drawIcons($('#contacts'));
  }

  async function contactDialog(contactId) {
    const answer = await api('GET', `companies/${id}/contacts/${contactId}`);
    if (!answer.result.ok) { toast(answer.result.message, true); return; }
    const c = answer.contact || {salutation: '', firstName: '', middleName: '', lastName: '', department: '', designation: '', isLinkedIn: false, isEqList: false, isDefault: false};
    let emails = answer.emails.map(e => ({...e})), phones = answer.phones.map(p => ({...p}));
    const footer = contactId ? '<button type="button" class="button danger-btn" data-delete>Delete Contact</button>' : '';
    const el = dialog(contactId ? 'Edit Contact' : 'Contact', `<div class="form-grid two">
        ${text('salutation', 'Salutation', c.salutation, {max: 250})}${text('firstName', 'First Name', c.firstName, {max: 250})}
        ${text('middleName', 'Christian Name (middle name)', c.middleName, {max: 250})}${text('lastName', 'Last Name / Surname', c.lastName, {required: true, max: 250})}
        ${text('department', 'Department', c.department, {max: 50})}${text('designation', 'Designation', c.designation, {max: 250})}
        <div class="field wide" style="display:flex;flex-direction:row;flex-wrap:wrap;gap:8px 22px">
          <label class="check"><input type="checkbox" data-field="isDefault"${c.isDefault ? ' checked' : ''}>Is Default (the company's primary contact)</label>
          <label class="check"><input type="checkbox" data-field="isLinkedIn"${c.isLinkedIn ? ' checked' : ''}>Is Sales Navigator</label>
          <label class="check"><input type="checkbox" data-field="isEqList"${c.isEqList ? ' checked' : ''}>Is Eq. List <span class="muted small">(needs an email)</span></label></div>
        <div class="wide"><div class="card-heading" style="margin:4px 0 6px"><h3 style="margin:0;font-size:15px">Email</h3><button type="button" class="button secondary small-btn" data-add-email><span data-icon="plus"></span>Add Email</button></div><div data-emails></div></div>
        <div class="wide"><div class="card-heading" style="margin:4px 0 6px"><h3 style="margin:0;font-size:15px">Phone</h3><button type="button" class="button secondary small-btn" data-add-phone><span data-icon="plus"></span>Add Phone</button></div><div data-phones></div></div>
      </div>`, {wide: true, footer});
    const keep = () => {
      $$('[data-email]', el).forEach(row => { const e = emails[Number(row.dataset.email)]; e.address = $('input[type=email]', row).value; e.consentToMarketingInfo = $('input[type=checkbox]', row).checked; });
      $$('[data-phone]', el).forEach(row => { const p = phones[Number(row.dataset.phone)]; p.number = $('input', row).value; p.phoneType = $('select', row).value; });
    };
    const draw = () => {
      $('[data-emails]', el).innerHTML = emails.length ? emails.map((e, i) => `<div data-email="${i}" style="display:flex;gap:8px;align-items:center;flex-wrap:wrap;margin-bottom:6px">
          <input type="email" class="cell-input" data-field="emails.${i}" value="${esc(e.address)}" maxlength="200" placeholder="name@company.com" style="flex:1 1 220px" aria-label="Email">
          <label class="check small"><input type="checkbox"${e.consentToMarketingInfo ? ' checked' : ''}>Consent To Marketing Information</label>
          <button type="button" class="icon-button small" data-remove-email="${i}" title="Remove the email" aria-label="Remove the email"><span data-icon="trash"></span></button></div>`).join('') : '<p class="muted small" style="margin:0">No email.</p>';
      $('[data-phones]', el).innerHTML = phones.length ? phones.map((p, i) => `<div data-phone="${i}" style="display:flex;gap:8px;align-items:center;flex-wrap:wrap;margin-bottom:6px">
          <input class="cell-input" data-field="phones.${i}" value="${esc(p.number)}" maxlength="20" placeholder="Phone number" style="flex:1 1 160px" aria-label="Phone">
          <select class="cell-input" aria-label="Phone Type" style="max-width:160px">${options(lists.phoneTypes, p.phoneType, '-')}</select>
          <button type="button" class="icon-button small" data-remove-phone="${i}" title="Remove the phone" aria-label="Remove the phone"><span data-icon="trash"></span></button></div>`).join('') : '<p class="muted small" style="margin:0">No phone.</p>';
      const eq = $('[data-field=isEqList]', el);
      eq.disabled = emails.length === 0;
      if (eq.disabled) eq.checked = false;
      drawIcons(el);
    };
    draw();
    el.addEventListener('click', async event => {
      if (event.target.closest('[data-add-email]')) { keep(); emails.push({id: 0, address: '', consentToMarketingInfo: true}); draw(); $$('[data-email] input[type=email]', el).pop().focus(); return; }
      if (event.target.closest('[data-add-phone]')) { keep(); phones.push({id: 0, number: '', phoneType: ''}); draw(); $$('[data-phone] input', el).pop().focus(); return; }
      const re = event.target.closest('[data-remove-email]');
      if (re) { keep(); emails.splice(Number(re.dataset.removeEmail), 1); draw(); return; }
      const rp = event.target.closest('[data-remove-phone]');
      if (rp) { keep(); phones.splice(Number(rp.dataset.removePhone), 1); draw(); return; }
      if (event.target.closest('[data-delete]')) {
        if (!await ask({title: 'Are you sure you want to delete the Contact?', text: 'It is no longer shown (it is kept, marked as deleted).', ok: 'Delete', danger: true})) return;
        const result = await api('POST', `companies/${id}/contacts/${contactId}/remove`, {});
        toast(result.result.message, !result.result.ok);
        if (result.result.ok) { el.close(); load(); }
      }
    });
    $('form', el).addEventListener('submit', async event => {
      event.preventDefault();
      keep();
      const v = name => $(`[data-field="${name}"]`, el);
      const body = {isDefault: v('isDefault').checked, isLinkedIn: v('isLinkedIn').checked, isEqList: v('isEqList').checked && emails.length > 0,
        emails: emails.map(e => ({id: e.id, address: e.address, consentToMarketingInfo: e.consentToMarketingInfo})), phones: phones.map(p => ({id: p.id, number: p.number, phoneType: p.phoneType}))};
      for (const name of ['salutation', 'firstName', 'middleName', 'lastName', 'department', 'designation']) body[name] = v(name).value;
      const result = await api('POST', `companies/${id}/contacts/${contactId}`, body);
      if (!result.result.ok) { refused(el, result); return; }
      toast(result.result.message);
      el.close();
      load();
    });
    $('[data-field=firstName]', el).focus();
  }

  // ---- Relationships ----
  function relationships() {
    const r = d.relationships, can = d.can.editContacts;
    const row = x => `<li><a href="company.aspx?id=${x.id}">${esc(x.name)}</a>${can ? ` <button type="button" class="text-button danger-text small" data-remove-relationship="${x.relationshipId}" data-name="${esc(x.name)}">Remove</button>` : ''}</li>`;
    const list = (items, none) => items.length ? `<ul class="check-lines" style="margin:6px 0 0">${items.map(row).join('')}</ul>` : `<p class="muted small" style="margin:6px 0 0">${none}</p>`;
    $('#relationships').innerHTML = `<div class="card-heading"><h2>Company Relationships</h2>${can ? `<div class="row-first">
        ${r.parent ? '<button type="button" class="button secondary small-btn" data-relate="sibling"><span data-icon="plus"></span>Add Sibling</button>' : '<button type="button" class="button secondary small-btn" data-relate="parent"><span data-icon="plus"></span>Add Parent</button>'}
        <button type="button" class="button secondary small-btn" data-relate="subsidiary"><span data-icon="plus"></span>Add Subsidiary</button></div>` : ''}</div>
      <div class="facts">
        <div><div class="fact-label">Parent Company</div>${list(r.parent ? [r.parent] : [], 'No parent company exists')}</div>
        <div><div class="fact-label">Sibling Companies</div>${list(r.siblings, 'No sibling company exists')}</div>
        <div class="half"><div class="fact-label">Subsidiary Companies</div>${list(r.subsidiaries, 'No subsidiary company exists')}</div></div>`;
    drawIcons($('#relationships'));
  }

  /** The company picker (MyCRM's CompanyList_PopUp): the companies the person may open, by name or BRN. */
  function pickCompany(kind) {
    const titles = {parent: 'Add Parent', sibling: 'Add Sibling', subsidiary: 'Add Subsidiary'};
    const el = dialog(titles[kind], `<p class="muted">Find the company by its name or BRN, then choose it. A company can have only one parent.</p>
      <label class="search-box" style="margin:10px 0"><span data-icon="search"></span><input data-q type="search" placeholder="Company name or BRN" autocomplete="off" aria-label="Company name or BRN"></label>
      <div data-found style="max-height:340px;overflow:auto"></div>`);
    $('[data-submit]', el).hidden = true;
    let timer = 0;
    const found = $('[data-found]', el);
    $('[data-q]', el).addEventListener('input', event => {
      clearTimeout(timer);
      const q = event.target.value.trim();
      if (q.length < 2) { found.innerHTML = ''; return; }
      timer = setTimeout(async () => {
        const answer = await api('GET', 'companies?' + new URLSearchParams({q}));
        if (!answer.result.ok) { found.innerHTML = `<p class="no">${esc(answer.result.message)}</p>`; return; }
        const rows = answer.rows.filter(x => x.id !== id);
        found.innerHTML = rows.length ? rows.map(x => `<button type="button" class="person" data-pick="${x.id}" style="display:block;width:100%;text-align:left;padding:8px 10px;border:0;background:none;border-bottom:1px solid var(--border-soft)">
          <b>${esc(x.name)}</b><span class="muted small" style="display:block">${esc([x.brn && 'BRN ' + x.brn, x.salesperson].filter(Boolean).join(' · '))}</span></button>`).join('')
          + (answer.total > answer.rows.length ? `<p class="muted small">${answer.total} match: type more to narrow it down.</p>` : '') : '<p class="muted">No company found.</p>';
      }, 300);
    });
    found.addEventListener('click', async event => {
      const pick = event.target.closest('[data-pick]');
      if (!pick) return;
      const answer = await api('POST', `companies/${id}/relationships`, {companyId: Number(pick.dataset.pick), kind});
      if (!answer.result.ok) { refused(el, answer); return; }
      toast(answer.result.message);
      el.close();
      load();
    });
    $('[data-q]', el).focus();
  }

  function movements() {
    if (!d.movements.length) return '<p class="muted">No items to show...</p>';
    return `<div class="table-wrap"><table class="data"><thead><tr><th>Previous Sales Rep</th><th>Current Sales Rep</th><th>Updated</th></tr></thead><tbody>
      ${d.movements.map(m => `<tr><td>${m.from ? esc(m.from) : '<span class="muted">Nobody</span>'}${m.fromTeam ? `<span class="sub">${esc(m.fromTeam)}</span>` : ''}</td>
        <td>${m.to ? esc(m.to) : '<span class="muted">Nobody</span>'}${m.toTeam ? `<span class="sub">${esc(m.toTeam)}</span>` : ''}</td>
        <td class="small">${esc(dateTime(m.at))}<span class="sub">${esc(m.by)}</span></td></tr>`).join('')}</tbody></table></div>`;
  }

  page.addEventListener('click', async event => {
    const tabButton = event.target.closest('[data-tab]');
    if (tabButton) { showTab(tabButton.dataset.tab); if (!tabButton.classList.contains('tab')) scrollTo(0, 0); return; }
    if (event.target.closest('#section-back')) { showTab('overview'); scrollTo(0, 0); return; }
    const address = event.target.closest('[data-address]');
    if (address) { addressDialog(Number(address.dataset.address)); return; }
    const removeAddress = event.target.closest('[data-delete-address]');
    if (removeAddress) {
      if (!await ask({title: 'Are you sure you want to delete the address?', text: 'It is no longer shown (it is kept, marked as deleted).', ok: 'Delete', danger: true})) return;
      const answer = await api('POST', `companies/${id}/addresses/${removeAddress.dataset.deleteAddress}/remove`, {});
      toast(answer.result.message, !answer.result.ok);
      if (answer.result.ok) load();
      return;
    }
    const contact = event.target.closest('[data-contact]');
    if (contact) { contactDialog(Number(contact.dataset.contact)); return; }
    const removeContact = event.target.closest('[data-delete-contact]');
    if (removeContact) {
      if (!await ask({title: `Are you sure you want to delete the Contact ${removeContact.dataset.name}?`, text: 'It is no longer shown (it is kept, marked as deleted).', ok: 'Delete', danger: true})) return;
      const answer = await api('POST', `companies/${id}/contacts/${removeContact.dataset.deleteContact}/remove`, {});
      toast(answer.result.message, !answer.result.ok);
      if (answer.result.ok) load();
      return;
    }
    const relate = event.target.closest('[data-relate]');
    if (relate) { pickCompany(relate.dataset.relate); return; }
    const removeRelationship = event.target.closest('[data-remove-relationship]');
    if (removeRelationship) {
      if (!await ask({title: `Remove the relationship with ${removeRelationship.dataset.name}?`, text: 'Both companies stay; only the link between them is taken away.', ok: 'Remove', danger: true})) return;
      const answer = await api('POST', `companies/${id}/relationships/${removeRelationship.dataset.removeRelationship}/remove`, {});
      toast(answer.result.message, !answer.result.ok);
      if (answer.result.ok) load();
    }
  });
  page.addEventListener('change', async event => {
    const type = event.target.closest('[data-address-type]');
    if (!type || !type.value) return;
    const answer = await api('POST', `companies/${id}/addresses/${type.dataset.addressType}/type`, {addressType: type.value});
    toast(answer.result.message, !answer.result.ok);
    if (answer.result.ok) load();
  });

  page.addEventListener('keydown', event => {
    const current = event.target.closest('.company-tabs .tab');
    if (!current || (event.key !== 'ArrowRight' && event.key !== 'ArrowLeft')) return;
    const tabs = $$('.company-tabs .tab', page);
    const next = tabs[(tabs.indexOf(current) + (event.key === 'ArrowRight' ? 1 : tabs.length - 1)) % tabs.length];
    showTab(next.dataset.tab);
    next.focus();
  });

  if (!id) { $('#loading').hidden = true; page.hidden = false; page.innerHTML = '<div class="empty"><h3>No company chosen.</h3><p><a class="button secondary" href="./">Back to the companies</a></p></div>'; }
  else load();
})();
