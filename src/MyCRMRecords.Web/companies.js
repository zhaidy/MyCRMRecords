(() => {
  'use strict';
  // default.aspx: the companies (MyCRM's Companies): those the person may open (MyCRM's rule: their and their team's
  // territories, or every company for the roles that see all), searched by name or BRN, customer type, group name and
  // postal code, 50 at a time by name, one line each; the name opens the company, and so does a click anywhere on its
  // row. The first page shows at once (MyCRM showed nothing before Search). Those who see every company also choose
  // All, Mine (their and their team's permanent territories) or No salesperson (in no permanent territory: MyCRM's
  // "Unassigned Companies"), and those who keep territories tick companies without a salesperson and put them in a
  // territory (the Territories page's own "add"). Sales Admins add companies; MyCRM Admins upload the number of
  // employees; administrators may sign in as someone else. On a phone: a card per company, the filters in a sheet.
  const {$, $$, esc, api, toast, drawIcons, pager} = window.WH;
  const {companyForm, uploadDialog, dialog, refused} = window.CRM;
  const page = $('#page');
  const saved = (() => { try { return JSON.parse(localStorage.getItem('companies-filter') || '{}'); } catch { return {}; } })();
  const filter = {q: saved.q || '', customerType: saved.customerType || '', groupName: saved.groupName || '', postalCode: saved.postalCode || '', show: saved.show || '', skip: 0};
  const ticked = new Map(); // company Id → name: those ticked to put in a territory (No salesperson)
  let data = null, lists = null, session = null, timer = 0;

  const seesAll = () => !!(session && session.can.seesAll);
  const shows = () => seesAll() ? [['', 'All'], ['mine', 'Mine'], ['none', 'No salesperson']] : [];
  const canPut = () => filter.show === 'none' && !!(session && session.can.territories);
  const filtered = () => !!(filter.q || filter.customerType || filter.groupName || filter.postalCode);
  const companies = n => `${n.toLocaleString('en-SG')} compan${n === 1 ? 'y' : 'ies'}`;

  async function load() {
    if (!shows().some(s => s[0] === filter.show)) filter.show = '';
    try { localStorage.setItem('companies-filter', JSON.stringify({...filter, skip: undefined})); } catch { /* not kept */ }
    const answer = await api('GET', 'companies?' + new URLSearchParams(filter));
    $('#loading').hidden = true;
    page.hidden = false;
    if (!$('#rows')) shell();
    if (!answer.result.ok) { $('#rows').innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3></div>`; $('#cards').innerHTML = ''; $('#count').textContent = ''; $('#pager').hidden = true; return; }
    data = answer;
    render();
  }

  const typeOptions = () => (lists ? lists.companyGroups : []).map(g => `<option value="${esc(g.code)}"${g.code === filter.customerType ? ' selected' : ''}>${esc(g.label)}</option>`).join('');
  const groupOptions = () => (lists ? lists.groupNames : []).map(g => `<option value="${g.id}"${String(g.id) === String(filter.groupName) ? ' selected' : ''}>${esc(g.groupName)}</option>`).join('');

  function shell() {
    page.innerHTML = `
      <div class="page-heading companies-heading"><div class="companies-title"><h1>Companies</h1><p id="count">&nbsp;</p></div>
        <div class="heading-actions" id="actions"></div></div>
      <div class="companies-filters" role="search">
        <div class="companies-filter-row">
          <label class="search-box"><span data-icon="search"></span><input id="q" type="search" placeholder="Company name or BRN" value="${esc(filter.q)}" aria-label="Company name or BRN"></label>
          <label class="filter-capsule companies-wide"><span>Customer type</span><select id="customerType"><option value="">All</option>${typeOptions()}</select></label>
          <label class="filter-capsule companies-wide"><span>Group name</span><select id="groupName"><option value="">All</option>${groupOptions()}</select></label>
          <label class="filter-capsule companies-wide"><span>Postal code</span><input id="postalCode" maxlength="50" placeholder="Any" value="${esc(filter.postalCode)}"></label>
          <button type="button" class="text-button companies-wide" id="reset">Reset</button>
          <button type="button" class="icon-button companies-filter-button" id="open-filters" aria-haspopup="dialog" aria-label="Filters"><span data-icon="settings"></span><span class="filter-dot" id="filter-dot" hidden></span></button>
        </div>
        ${shows().length ? `<div class="companies-show-row"><div class="tabs companies-show" role="group" aria-label="Show">${shows().map(([value, label]) =>
          `<button type="button" class="tab" data-show="${value}" aria-pressed="${String(filter.show === value)}">${label}</button>`).join('')}</div>
          <p class="companies-show-note" id="show-note"></p></div>` : ''}
      </div>
      <section class="companies-list" aria-label="Company list"><div id="rows"></div><div id="cards" class="file-cards"></div><div class="pager" id="pager" hidden></div></section>`;
    drawIcons(page);
    const later = () => { clearTimeout(timer); filter.skip = 0; timer = setTimeout(load, 400); };
    $('#q').addEventListener('input', event => { filter.q = event.target.value; later(); });
    // The top bar's search button opens this page at #search: the search box is ready to type in.
    const focusSearch = () => {
      if (location.hash !== '#search') return;
      history.replaceState(history.state, '', location.pathname + location.search);
      $('#q').focus();
      $('#q').select();
    };
    focusSearch();
    addEventListener('hashchange', focusSearch);
    $('#postalCode').addEventListener('input', event => { filter.postalCode = event.target.value; later(); });
    $('#customerType').addEventListener('change', event => { filter.customerType = event.target.value; filter.skip = 0; load(); });
    $('#groupName').addEventListener('change', event => { filter.groupName = event.target.value; filter.skip = 0; load(); });
    $('#reset').addEventListener('click', reset);
    $('#open-filters').addEventListener('click', filterSheet);
    $$('[data-show]', page).forEach(b => b.addEventListener('click', () => {
      if (filter.show === b.dataset.show) return;
      filter.show = b.dataset.show;
      filter.skip = 0;
      ticked.clear();
      load();
    }));
  }

  function reset() {
    Object.assign(filter, {q: '', customerType: '', groupName: '', postalCode: '', skip: 0});
    page.innerHTML = '';
    load();
  }

  function actions() {
    const can = data.can;
    const signIn = session && session.can.actAs && !session.impersonating;
    const more = [
      signIn ? '<button type="button" class="capsule-button" id="act-as" title="Sign in as…"><span data-icon="users"></span><span class="action-label">Sign in as…</span></button>' : '',
      can.uploadEmployees ? '<button type="button" class="capsule-button" id="employees" title="Upload number of employees"><span data-icon="upload"></span><span class="action-label">Upload number of employees</span></button>' : '',
    ].filter(Boolean);
    $('#actions').innerHTML = `
      ${canPut() ? `<span class="selection-pill companies-ticked" role="group" aria-label="Ticked companies"><span id="ticked-count"></span>
        <button type="button" class="text-button" id="clear-ticks">Clear</button>
        <button type="button" class="button primary small-btn" id="put">Put in a territory…</button></span>` : ''}
      ${more.length ? `<div class="action-capsule" role="group" aria-label="More actions">${more.join('<span class="capsule-divider" aria-hidden="true"></span>')}</div>` : ''}
      ${can.create ? '<button type="button" class="button primary" id="new-company" title="New company"><span data-icon="plus"></span><span class="action-label">New company</span></button>' : ''}`;
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
    $('#clear-ticks')?.addEventListener('click', () => { ticked.clear(); render(); });
    $('#put')?.addEventListener('click', putInTerritory);
  }

  function scope() {
    if (filter.show === 'mine') return "in your and your team's territories";
    if (filter.show === 'none') return 'no permanent territory';
    return seesAll() ? 'every active company' : "of your and your team's territories (and of MyCRM's projects you are in)";
  }

  function render() {
    actions();
    const rows = data.rows;
    $('#count').innerHTML = `<b>${companies(data.total)}</b>${filtered() ? ' match' : ''}<span class="count-scope"> · ${esc(scope())}</span>`;
    $('#filter-dot').hidden = !(filter.customerType || filter.groupName || filter.postalCode);
    $('#open-filters').setAttribute('aria-label', $('#filter-dot').hidden ? 'Filters' : 'Filters (on)');
    $$('[data-show]', page).forEach(b => b.setAttribute('aria-pressed', String(b.dataset.show === filter.show)));
    if ($('#show-note')) $('#show-note').innerHTML = filter.show === 'none'
      ? `Active companies in no salesperson's permanent territory (MyCRM's projects don't count).${canPut() ? '<span class="companies-wide"> Tick them and put them in a territory.</span>' : ''}`
      : filter.show === 'mine' ? "The companies of your and your team's permanent territories." : '';
    const ticks = canPut();
    const all = rows.length > 0 && rows.every(r => ticked.has(r.id));
    const none = '<span class="no-salesperson">No salesperson</span>';
    const empty = `<div class="empty"><h3>Nothing to show</h3><p class="muted">${filter.show === 'none' && !filtered() ? 'Every active company is in a permanent territory.'
      : filtered() ? 'No company matches. Try the BRN or part of the name.' : "No company is in your or your team's territories."}</p></div>`;
    $('#rows').innerHTML = rows.length ? `<table class="data companies-table"><thead><tr>
        ${ticks ? `<th class="tick"><input type="checkbox" id="tick-all" aria-label="Tick all companies shown"${all ? ' checked' : ''}></th>` : ''}
        <th class="co-name">Company</th><th class="co-brn">BRN</th><th class="co-postal">Postal code</th><th class="co-person">Salesperson</th><th class="co-territory">Territory</th><th class="co-type">Customer type</th><th class="co-person">Created by</th></tr></thead><tbody>
      ${rows.map(r => `<tr data-href="company.aspx?id=${r.id}"${ticked.has(r.id) ? ' class="ticked"' : ''}>
        ${ticks ? `<td class="tick"><input type="checkbox" data-tick="${r.id}" aria-label="Tick ${esc(r.name)}"${ticked.has(r.id) ? ' checked' : ''}></td>` : ''}
        <td class="co-name" title="${esc(r.name)}"><a href="company.aspx?id=${r.id}">${esc(r.name)}</a></td>
        <td class="co-brn" title="${esc(r.brn)}">${esc(r.brn)}</td>
        <td class="co-postal">${esc(r.postalCode)}</td>
        <td title="${esc(r.salesperson)}">${r.salesperson ? esc(r.salesperson) : none}</td>
        <td title="${esc(r.territory)}">${r.territory ? esc(r.territory) : '<span class="muted">—</span>'}</td>
        <td class="co-type" title="${esc(r.customerType)}">${esc(r.customerType)}</td>
        <td title="${esc(r.createdBy)}">${esc(r.createdBy)}</td></tr>`).join('')}
      </tbody></table>`
      : empty;
    // On a phone: a card per company (its name, BRN and postal code, salesperson and territory).
    $('#cards').innerHTML = rows.map(r => `<a class="file-card" href="company.aspx?id=${r.id}">
        <span class="file-card-body"><span class="file-card-top"><b>${esc(r.name)}</b></span>
          <span class="file-card-line">${esc([r.brn ? 'BRN ' + r.brn : '', r.postalCode].filter(Boolean).join(' · '))}</span>
          <span class="file-card-line company-card-owner">${r.salesperson ? `<span class="salesperson">${esc(r.salesperson)}</span>` : none}<span>${esc(r.territory)}</span></span></span>
        <span data-icon="arrow-right" class="section-chevron"></span></a>`).join('') || empty;
    drawIcons($('#cards'));
    pager($('#pager'), data.total, data.skip, data.take);
    if (ticks) {
      $('#ticked-count').textContent = `${ticked.size} ticked`;
      $('#clear-ticks').hidden = ticked.size === 0;
      $('#put').disabled = ticked.size === 0;
    }
  }

  // The row opens its company (its name is the link for the keyboard); its tick box and links do their own thing.
  page.addEventListener('click', event => {
    const move = event.target.closest('[data-page]');
    if (move && data) { filter.skip = Math.max(0, data.skip + Number(move.dataset.page) * data.take); load(); window.scrollTo({top: 0}); return; }
    const row = event.target.closest('tr[data-href]');
    if (!row || event.target.closest('a, button, input, label, select')) return;
    if (window.getSelection && String(window.getSelection())) return;
    if (event.ctrlKey || event.metaKey) window.open(row.dataset.href, '_blank'); else location.href = row.dataset.href;
  });

  page.addEventListener('change', event => {
    const box = event.target;
    if (box.id === 'tick-all') {
      data.rows.forEach(r => { if (box.checked) ticked.set(r.id, r.name); else ticked.delete(r.id); });
      render();
    } else if (box.dataset && box.dataset.tick) {
      const r = data.rows.find(x => String(x.id) === box.dataset.tick);
      if (box.checked) ticked.set(r.id, r.name); else ticked.delete(r.id);
      render();
    }
  });

  /** The phone's filters: customer type, group name and postal code in a sheet, applied together. */
  function filterSheet() {
    const sheet = document.createElement('dialog');
    sheet.className = 'companies-sheet';
    sheet.setAttribute('aria-labelledby', 'filter-title');
    sheet.innerHTML = `<form method="dialog" class="sheet-form"><span class="sheet-grabber" aria-hidden="true"></span>
      <div class="sheet-head"><button type="button" class="icon-button" value="cancel" aria-label="Close filters"><span data-icon="close"></span></button>
        <h2 id="filter-title">Filter companies</h2><button type="submit" class="button primary" id="apply">Apply</button></div>
      <div class="sheet-group">
        <label><span>Customer type</span><select id="sheet-type"><option value="">Any type</option>${typeOptions()}</select></label>
        <label><span>Group name</span><select id="sheet-group"><option value="">Any group</option>${groupOptions()}</select></label>
        <label><span>Postal code</span><input id="sheet-postal" maxlength="50" placeholder="Any postal code" value="${esc(filter.postalCode)}"></label>
      </div>
      <button type="button" class="button secondary" id="sheet-reset">Reset filters</button></form>`;
    document.body.append(sheet);
    drawIcons(sheet);
    $('button[value=cancel]', sheet).addEventListener('click', () => sheet.close());
    sheet.addEventListener('close', () => sheet.remove());
    sheet.addEventListener('click', event => { if (event.target === sheet) sheet.close(); });
    $('form', sheet).addEventListener('submit', () => {
      filter.customerType = $('#sheet-type', sheet).value;
      filter.groupName = $('#sheet-group', sheet).value;
      filter.postalCode = $('#sheet-postal', sheet).value;
      filter.skip = 0;
      page.innerHTML = '';
      load();
    });
    $('#sheet-reset', sheet).addEventListener('click', () => { sheet.close(); reset(); });
    sheet.showModal();
  }

  /**
   * Puts the ticked companies (no salesperson) in a permanent territory, through the Territories page's own change
   * (territories/{id}/add): each gets that territory's salesperson, a territory movement is recorded, nothing else changes.
   */
  async function putInTerritory() {
    const picked = [...ticked.entries()];
    if (!picked.length) return;
    const answer = await api('GET', 'territories');
    if (!answer.result.ok) { toast(answer.result.message, true); return; }
    const territories = answer.territories.filter(t => t.isActive);
    const n = picked.length, word = n === 1 ? 'company' : 'companies';
    const el = dialog('Put in a territory', `
      <p class="muted" style="margin:0 0 6px">${n} ${word} ticked:</p>
      <ul class="put-companies">${picked.slice(0, 8).map(([, name]) => `<li>${esc(name)}</li>`).join('')}${n > 8 ? `<li class="muted">and ${n - 8} more</li>` : ''}</ul>
      <label class="field"><span class="required">Territory</span><select data-field="territory">${territories.length ? '' : '<option value="">No territory</option>'}${territories.map(t =>
        `<option value="${t.id}">${esc(t.title)} · ${esc(t.owner && t.owner.name ? t.owner.name : 'no salesperson')}</option>`).join('')}</select>
        <small class="muted">Only permanent territories are listed. MyCRM's project territories don't count.</small><span class="field-error"></span></label>
      <div class="notice warn put-changes"><b>What changes</b><p id="put-text"></p></div>
      ${session && session.myCrm && !session.myCrm.writes ? `<div class="notice error">${esc(session.myCrm.writesOff)}</div>` : ''}`);
    const select = $('[data-field=territory]', el);
    const submit = $('[data-submit]', el);
    const explain = () => {
      const t = territories.find(x => String(x.id) === select.value);
      if (!t) { $('#put-text', el).textContent = 'There is no territory to put them in.'; submit.disabled = true; return; }
      const who = t.owner && t.owner.name ? t.owner.name : '';
      $('#put-text', el).textContent = `The ${word} will be put in ${t.title}` + (who ? `, so ${who} becomes the salesperson and can open ${n === 1 ? 'it' : 'them'}.` : ', which has no salesperson.')
        + ` ${n === 1 ? 'It leaves' : 'They leave'} the No salesperson list and a territory movement is recorded. Nothing else on the ${word} changes. Saved in MyCRM.`;
      submit.textContent = `Put ${n} ${word} in ${t.title}`;
      submit.disabled = false;
    };
    select.addEventListener('change', explain);
    explain();
    $('form', el).addEventListener('submit', async event => {
      event.preventDefault();
      if (!select.value) return;
      submit.disabled = true;
      const result = await api('POST', `territories/${select.value}/add`, {companies: picked.map(([id]) => id)});
      submit.disabled = false;
      if (!result.result.ok) { refused(el, result); return; }
      el.close();
      toast(result.result.message);
      ticked.clear();
      load();
    });
  }

  /** Sign in as someone else (administrators): MyCRMRecords then works as that person; everything is recorded with both names. */
  async function actAs() {
    const answer = await api('GET', 'people');
    if (!answer.result.ok) { toast(answer.result.message, true); return; }
    const dialog = document.createElement('dialog');
    dialog.className = 'act-panel dlg';
    dialog.innerHTML = `<form method="dialog" class="act-panel-in">${window.WH.dialogHead('Sign in as someone else', {closeLabel: 'Cancel'})}
      <div class="act-panel-body"><p class="dlg-text">MyCRMRecords works as that person until you choose Return in the bar at the top. Everything you do is recorded as you on behalf of them.</p>
      <label class="field"><span>Find</span><input id="act-q" type="search" placeholder="Name, login or email" autocomplete="off"></label>
      <div class="people-list dlg-card" id="act-people" style="max-height:320px;overflow:auto;padding:0"></div></div></form>`;
    document.body.append(dialog);
    drawIcons(dialog);
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

  try {
    const flash = sessionStorage.getItem('flash');
    if (flash) { sessionStorage.removeItem('flash'); setTimeout(() => toast(flash), 300); }
  } catch { /* nothing to show */ }
  (async () => {
    const [answer, listsAnswer] = await Promise.all([api('GET', 'session'), api('GET', 'lists')]);
    session = answer.result.ok ? {can: answer.can, myCrm: answer.myCrm, impersonating: !!$('.impersonation-bar:not(.warning-bar)')} : null;
    lists = listsAnswer.result.ok ? listsAnswer : {companyGroups: [], groupNames: [], advanceCustomerTypes: [], compAccountTypes: [], compOwnerships: []};
    load();
  })();
})();
