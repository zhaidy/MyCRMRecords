(() => {
  'use strict';
  // territories.aspx (Sales Admins and MD): the named territories, each with a number, a salesperson and its companies,
  // replacing MyCRM's territories built from Advance. territories.aspx?id= is one territory: change its name, number or
  // salesperson (who then has all its companies), take it away, take companies out, and put companies in by searching,
  // by pasting a list (company names or BRNs) or from an Excel workbook (MyCRM's bulk transfer). A company put in a
  // territory leaves every other territory; each change of salesperson is recorded as a territory movement.
  const {$, esc, api, toast, ask, dateTime, drawIcons, pager, param} = window.WH;
  const {dialog, refused, text, uploadDialog} = window.CRM;
  const page = $('#page');
  const id = Number(param('id') || 0);
  let lists = null, list = null, showInactive = false, q = '';
  let d = null, companyQ = '', companySkip = 0, ticked = new Set();
  let search = {q: '', unassigned: false, skip: 0, answer: null, ticked: new Set()}, matches = null, addTab = 'search', timer = 0;

  const salespersonOf = owner => owner && owner.login ? esc(owner.name || owner.login) : '<span class="muted">No salesperson (vacant)</span>';

  // ---- The list of territories ----
  async function loadList() {
    const answer = await api('GET', 'territories' + (showInactive ? '?inactive=1' : ''));
    $('#loading').hidden = true;
    page.hidden = false;
    if (!answer.result.ok) { page.innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3></div>`; return; }
    list = answer.territories;
    renderList();
  }

  function renderList() {
    if (!$('#rows')) {
      page.innerHTML = `<div class="page-heading"><div><h1>Territories</h1><p>Each territory has a number, a salesperson and its companies. A company is in one territory; putting it in another moves it there.</p></div>
          <div class="heading-actions"><button class="button primary" id="new-territory"><span data-icon="plus"></span>New territory</button></div></div>
        <section class="card"><div class="filters">
            <label class="search-box"><span data-icon="search"></span><input id="q" type="search" placeholder="Territory, number or salesperson" value="${esc(q)}" aria-label="Territory, number or salesperson"></label>
            <label class="check"><input type="checkbox" id="inactive"${showInactive ? ' checked' : ''}>Show territories taken away</label></div>
          <p class="muted small" id="count" style="margin:10px 0 0"></p><div id="rows"></div></section>`;
      drawIcons(page);
      $('#q').addEventListener('input', event => { q = event.target.value; renderList(); });
      $('#inactive').addEventListener('change', event => { showInactive = event.target.checked; loadList(); });
      $('#new-territory').addEventListener('click', () => territoryDialog(null));
    }
    const term = q.trim().toLowerCase();
    const rows = list.filter(t => !term || `${t.title} ${t.number} ${t.owner.name} ${t.owner.login}`.toLowerCase().includes(term));
    $('#count').textContent = `${rows.length} territor${rows.length === 1 ? 'y' : 'ies'}.`;
    $('#rows').innerHTML = rows.length ? `<div class="table-wrap" style="margin-top:10px"><table class="data"><thead><tr><th></th><th>Territory</th><th>Salesperson</th><th class="num">Companies</th></tr></thead><tbody>
      ${rows.map(t => `<tr class="${t.isActive ? '' : 'inactive'}"><td class="nowrap"><a class="button secondary small-btn" href="territories.aspx?id=${t.id}"><span data-icon="arrow-right"></span>Open</a></td>
        <td class="wide-text"><a href="territories.aspx?id=${t.id}"><b>${esc(t.title)}</b></a><span class="sub">No. ${t.number}${t.isActive ? '' : ' · taken away'}</span></td>
        <td>${salespersonOf(t.owner)}</td><td class="num">${t.companies}</td></tr>`).join('')}</tbody></table></div>`
      : `<div class="empty" style="margin-top:10px"><h3>Nothing to show</h3><p class="muted">${term ? 'No territory matches.' : 'No territory yet.'}</p></div>`;
    drawIcons($('#rows'));
  }

  /** Creates (territory null) or changes a territory: its name, number and salesperson. */
  function territoryDialog(t) {
    const owner = t && t.owner.login ? t.owner : null;
    const people = lists.salespeople.slice();
    if (owner && !people.some(p => p.login.toLowerCase() === owner.login.toLowerCase())) people.unshift({login: owner.login, name: owner.name || owner.login});
    const el = dialog(t ? `Edit ${t.title}` : 'New territory', `<div class="form-grid two">
        ${text('name', 'Name', t ? t.name : '', {required: true, max: 100, cls: 'wide'})}
        ${text('number', 'Number', t ? t.number : '', {required: true, type: 'number'})}
        <label class="field"><span>Salesperson</span><select data-field="owner"><option value="">No salesperson (vacant)</option>
          ${people.map(p => `<option value="${esc(p.login)}"${owner && owner.login.toLowerCase() === p.login.toLowerCase() ? ' selected' : ''}>${esc(p.name)}</option>`).join('')}</select>
          <small class="muted">People with the MyCRM role Sales.</small><span class="field-error"></span></label>
        ${t ? '<p class="muted small wide" style="margin:0">A new salesperson takes all the territory\'s companies; each company\'s move is recorded.</p>' : ''}</div>`);
    $('form', el).addEventListener('submit', async event => {
      event.preventDefault();
      const v = name => $(`[data-field="${name}"]`, el).value;
      const answer = await api('POST', 'territories/' + (t ? t.id : 0), {name: v('name'), number: v('number') === '' ? null : Number(v('number')), owner: v('owner')});
      if (!answer.result.ok) { refused(el, answer); return; }
      toast(answer.result.message);
      el.close();
      if (t) loadDetail(); else location.href = 'territories.aspx?id=' + answer.result.id;
    });
    $('[data-field=name]', el).focus();
  }

  // ---- One territory ----
  async function loadDetail() {
    const answer = await api('GET', `territories/${id}?` + new URLSearchParams({q: companyQ, skip: companySkip}));
    $('#loading').hidden = true;
    page.hidden = false;
    if (!answer.result.ok) {
      page.innerHTML = `<div class="empty"><h3>${esc(answer.result.code === 'NOT_FOUND' ? 'That territory is not there.' : answer.result.message)}</h3><p><a class="button secondary" href="territories.aspx">Back to the territories</a></p></div>`;
      return;
    }
    d = answer;
    renderDetail();
  }

  function renderDetail() {
    const t = d.territory;
    if (!$('#companies')) {
      page.innerHTML = `<div class="page-heading"><div><a class="crumb" href="territories.aspx"><span data-icon="arrow-left"></span>Territories</a><h1 id="title"></h1><p id="subtitle"></p></div>
          <div class="heading-actions" id="actions"></div></div>
        <div class="stack"><section class="card" id="facts"></section><section class="card" id="companies"></section><section class="card" id="add"></section></div>`;
    }
    $('#title').innerHTML = `${esc(t.title)} ${t.isActive ? '' : '<span class="pill warn">Taken away</span>'}`;
    $('#subtitle').textContent = `Created${t.createdBy ? ' by ' + t.createdBy : ''}${t.createdOn ? ' on ' + dateTime(t.createdOn) : ''}`;
    $('#actions').innerHTML = t.isActive ? `<button class="button secondary" id="take-away"><span data-icon="trash"></span>Take away</button><button class="button primary" id="edit"><span data-icon="edit"></span>Edit</button>` : '';
    $('#facts').innerHTML = `<div class="facts"><div><div class="fact-label">Number</div><div>${t.number}</div></div><div><div class="fact-label">Salesperson</div><div>${salespersonOf(t.owner)}</div></div>
      <div><div class="fact-label">Companies</div><div>${d.companies.total}${companyQ ? ' match' : ''}</div></div><div><div class="fact-label">Name</div><div>${t.name ? esc(t.name) : '<span class="muted">None (made in MyCRM)</span>'}</div></div></div>`;
    companies();
    if (t.isActive) addPanel(); else $('#add').innerHTML = '<div class="notice warn">This territory was taken away: its companies were taken out of it.</div>';
    drawIcons(page);
    $('#edit')?.addEventListener('click', () => territoryDialog(t));
    $('#take-away')?.addEventListener('click', async () => {
      if (!await ask({title: `Take ${t.title} away?`, text: `Its companies are taken out of it and have no salesperson from it (each move is recorded). The territory is kept, marked as taken away.`, ok: 'Take away', danger: true})) return;
      const answer = await api('POST', `territories/${id}/remove`, {});
      toast(answer.result.message, !answer.result.ok);
      if (answer.result.ok) { companySkip = 0; loadDetail(); }
    });
  }

  function companies() {
    const c = d.companies, active = d.territory.isActive;
    $('#companies').innerHTML = `<div class="card-heading"><h2>Companies of the territory</h2>${active ? `<div class="row-first">
        <button type="button" class="button secondary small-btn" data-tick-page>Tick all shown</button><button type="button" class="button secondary small-btn" data-untick>Untick all</button>
        <button type="button" class="button danger-btn small-btn" data-take-out${ticked.size ? '' : ' disabled'}>Take out of territory (${ticked.size})</button></div>` : ''}</div>
      <label class="search-box"><span data-icon="search"></span><input id="company-q" type="search" placeholder="Company name or BRN" value="${esc(companyQ)}" aria-label="Company name or BRN"></label>
      ${c.rows.length ? `<div class="table-wrap" style="margin-top:10px"><table class="data"><thead><tr>${active ? '<th></th>' : ''}<th>Company</th></tr></thead><tbody>
        ${c.rows.map(r => `<tr>${active ? `<td class="center"><input type="checkbox" data-tick="${r.id}"${ticked.has(r.id) ? ' checked' : ''} aria-label="Tick ${esc(r.name)}"></td>` : ''}
          <td class="wide-text"><a href="company.aspx?id=${r.id}"><b>${esc(r.name)}</b></a>${r.brn ? `<span class="sub">BRN ${esc(r.brn)}</span>` : ''}</td></tr>`).join('')}</tbody></table></div>`
        : `<p class="muted" style="margin-top:10px">${companyQ ? 'No company of this territory matches.' : 'No company in this territory.'}</p>`}
      <div class="pager" id="company-pager" hidden></div>`;
    pager($('#company-pager'), c.total, c.skip, c.take);
    drawIcons($('#companies'));
    $('#company-q').addEventListener('input', event => {
      clearTimeout(timer);
      companyQ = event.target.value;
      companySkip = 0;
      timer = setTimeout(async () => { await loadDetail(); const box = $('#company-q'); box.focus(); box.setSelectionRange(box.value.length, box.value.length); }, 350);
    });
  }

  // ---- Putting companies in ----
  function addPanel() {
    const tabs = [['search', 'Search'], ['paste', 'Paste a list'], ['excel', 'Excel workbook']];
    $('#add').innerHTML = `<h2>Put companies in this territory</h2>
      <p class="muted">A company put here leaves the territory it was in, and this territory's salesperson has it from then on.</p>
      <div class="page-tabs" role="tablist">${tabs.map(([key, label]) => `<button type="button" class="page-tab" role="tab" data-add-tab="${key}" aria-selected="${addTab === key}">${esc(label)}</button>`).join('')}</div>
      <div id="add-body"></div>`;
    ({search: searchTab, paste: pasteTab, excel: excelTab})[addTab]();
  }

  function searchTab() {
    $('#add-body').innerHTML = `<div class="filters">
        <label class="search-box"><span data-icon="search"></span><input id="search-q" type="search" placeholder="Company name or BRN (2 letters or more)" value="${esc(search.q)}" aria-label="Company name or BRN"></label>
        <label class="check"><input type="checkbox" id="search-unassigned"${search.unassigned ? ' checked' : ''}>Only companies with no salesperson</label></div>
      <div id="search-rows"></div><div class="pager" id="search-pager" hidden></div>`;
    drawIcons($('#add-body'));
    $('#search-q').addEventListener('input', event => { clearTimeout(timer); search.q = event.target.value; search.skip = 0; timer = setTimeout(runSearch, 350); });
    $('#search-unassigned').addEventListener('change', event => { search.unassigned = event.target.checked; search.skip = 0; runSearch(); });
    if (search.q.trim().length >= 2 || search.unassigned) runSearch(); else drawSearch();
  }

  async function runSearch() {
    if (search.q.trim().length < 2 && !search.unassigned) { search.answer = null; drawSearch(); return; }
    $('#search-rows').innerHTML = '<p class="loading">Searching…</p>';
    const answer = await api('GET', 'territories/search?' + new URLSearchParams({q: search.q, unassigned: search.unassigned ? '1' : '', skip: search.skip}));
    if (!answer.result.ok) { $('#search-rows').innerHTML = `<p class="no">${esc(answer.result.message)}</p>`; return; }
    search.answer = answer;
    drawSearch();
  }

  function drawSearch() {
    const a = search.answer;
    if (!a) { $('#search-rows').innerHTML = '<p class="muted small" style="margin-top:10px">Type at least 2 letters of a company name or BRN, or tick Only companies with no salesperson.</p>'; $('#search-pager').hidden = true; return; }
    $('#search-rows').innerHTML = a.rows.length ? `<div class="row-first" style="margin:10px 0">
        <button type="button" class="button secondary small-btn" data-search-tick>Tick all shown</button>
        <button type="button" class="button primary small-btn" data-put-search${search.ticked.size ? '' : ' disabled'}><span data-icon="plus"></span>Put the ticked companies here (${search.ticked.size})</button></div>
      <div class="table-wrap"><table class="data"><thead><tr><th></th><th>Company</th><th>Salesperson now</th></tr></thead><tbody>
        ${a.rows.map(r => {
          const here = r.territoryId === id;
          return `<tr><td class="center">${here ? '<span class="muted small">Here</span>' : `<input type="checkbox" data-search-pick="${r.id}"${search.ticked.has(r.id) ? ' checked' : ''} aria-label="Tick ${esc(r.name)}">`}</td>
            <td class="wide-text"><a href="company.aspx?id=${r.id}"><b>${esc(r.name)}</b></a>${r.brn ? `<span class="sub">BRN ${esc(r.brn)}</span>` : ''}</td>
            <td>${r.owner || r.territory ? `${esc(r.ownerName || 'Vacant')}<span class="sub">${esc(r.territory)}</span>` : '<span class="muted">None</span>'}</td></tr>`;
        }).join('')}</tbody></table></div>` : '<p class="muted" style="margin-top:10px">No company found.</p>';
    drawIcons($('#search-rows'));
    pager($('#search-pager'), a.total, a.skip, a.take);
  }

  function pasteTab() {
    $('#add-body').innerHTML = `<label class="field"><span>Companies, one per line: the company name or the BRN (or both, separated by a tab or a comma, as pasted from Excel)</span>
        <textarea id="paste" rows="6" placeholder="Tan &amp; Sons Pte Ltd&#10;199912345K"></textarea></label>
      <div class="action-buttons" style="margin-top:8px"><button type="button" class="button secondary" id="find"><span data-icon="search"></span>Find the companies</button></div>
      <div id="matches"></div>`;
    drawIcons($('#add-body'));
    $('#find').addEventListener('click', async () => {
      const answer = await api('POST', `territories/${id}/match`, {text: $('#paste').value});
      if (!answer.result.ok) { toast(answer.result.message, true); return; }
      matches = answer.matches;
      drawMatches();
    });
    if (matches) drawMatches();
  }

  function excelTab() {
    $('#add-body').innerHTML = `<p class="muted">An Excel workbook with the columns <b>Company</b> and <b>BRN</b> (MyCRM's bulk transfer file). The companies found are shown first; nothing changes until you put them here.</p>
      <div class="action-buttons" style="margin-top:8px"><button type="button" class="button secondary" id="excel"><span data-icon="upload"></span>Choose the workbook…</button></div>
      <div id="matches"></div>`;
    drawIcons($('#add-body'));
    $('#excel').addEventListener('click', async () => {
      const answer = await uploadDialog('Companies from Excel', '<p class="muted">Columns Company and BRN; one company per row.</p>', 'territory-list?territory=' + id, 'territory');
      if (!answer) return;
      if (!answer.matches) { toast((answer.result && answer.result.message) || 'The workbook could not be read.', true); return; }
      matches = answer.matches;
      drawMatches();
    });
    if (matches) drawMatches();
  }

  function drawMatches() {
    const found = matches.filter(m => m.companyId && !m.error);
    const moving = found.filter(m => m.salesperson).length;
    $('#matches').innerHTML = matches.length ? `<p style="margin:12px 0 6px"><b>${found.length}</b> of ${matches.length} line${matches.length === 1 ? '' : 's'} can be put here${moving ? ` (${moving} move from another salesperson)` : ''}.</p>
      <div class="table-wrap"><table class="data"><thead><tr><th>Line</th><th>Company</th><th>Salesperson now</th></tr></thead><tbody>
        ${matches.map(m => `<tr><td class="nowrap small">${m.line}<span class="sub">${esc(m.text)}</span></td>
          <td class="wide-text">${m.companyId ? `<a href="company.aspx?id=${m.companyId}"><b>${esc(m.company)}</b></a>${m.brn ? `<span class="sub">BRN ${esc(m.brn)}</span>` : ''}` : ''}${m.error ? `<span class="sub no">${esc(m.error)}</span>` : ''}</td>
          <td>${m.salesperson ? esc(m.salesperson) : '<span class="muted">None</span>'}</td></tr>`).join('')}</tbody></table></div>
      <div class="action-buttons" style="margin-top:10px"><button type="button" class="button primary" data-put-matches${found.length ? '' : ' disabled'}><span data-icon="plus"></span>Put the ${found.length} found compan${found.length === 1 ? 'y' : 'ies'} here</button></div>`
      : '<p class="muted" style="margin-top:10px">No line to read.</p>';
    drawIcons($('#matches'));
  }

  async function put(companyIds, movingCount) {
    if (!companyIds.length) return;
    if (!await ask({title: `Put ${companyIds.length} compan${companyIds.length === 1 ? 'y' : 'ies'} in ${d.territory.title}?`,
      text: (movingCount ? `${movingCount} of them leave the territory they are in now. ` : '') + `${d.territory.owner.login ? d.territory.owner.name : 'Nobody (the territory is vacant)'} has them from then on; each move is recorded.`, ok: 'Put them here'})) return;
    const answer = await api('POST', `territories/${id}/add`, {companies: companyIds});
    toast(answer.result.message, !answer.result.ok);
    if (answer.result.ok) { search.ticked.clear(); matches = null; await loadDetail(); if (search.answer) runSearch(); }
  }

  page.addEventListener('click', async event => {
    const move = event.target.closest('[data-page]');
    if (move) {
      if (move.closest('#company-pager')) { companySkip = Math.max(0, d.companies.skip + Number(move.dataset.page) * d.companies.take); loadDetail(); }
      else if (move.closest('#search-pager') && search.answer) { search.skip = Math.max(0, search.answer.skip + Number(move.dataset.page) * search.answer.take); runSearch(); }
      return;
    }
    const tab = event.target.closest('[data-add-tab]');
    if (tab) { addTab = tab.dataset.addTab; addPanel(); return; }
    if (event.target.closest('[data-tick-page]')) { d.companies.rows.forEach(r => ticked.add(r.id)); companies(); return; }
    if (event.target.closest('[data-untick]')) { ticked.clear(); companies(); return; }
    if (event.target.closest('[data-take-out]')) {
      if (!ticked.size) return;
      if (!await ask({title: `Take ${ticked.size} compan${ticked.size === 1 ? 'y' : 'ies'} out of ${d.territory.title}?`, text: 'They have no salesperson from this territory then (each move is recorded).', ok: 'Take out', danger: true})) return;
      const answer = await api('POST', `territories/${id}/takeout`, {companies: [...ticked]});
      toast(answer.result.message, !answer.result.ok);
      if (answer.result.ok) { ticked.clear(); companySkip = 0; loadDetail(); }
      return;
    }
    if (event.target.closest('[data-search-tick]')) { search.answer.rows.filter(r => r.territoryId !== id).forEach(r => search.ticked.add(r.id)); drawSearch(); return; }
    if (event.target.closest('[data-put-search]')) {
      const rows = search.answer ? search.answer.rows : [];
      put([...search.ticked], rows.filter(r => search.ticked.has(r.id) && r.territoryId).length);
      return;
    }
    if (event.target.closest('[data-put-matches]')) {
      const found = matches.filter(m => m.companyId && !m.error);
      put([...new Set(found.map(m => m.companyId))], found.filter(m => m.salesperson).length);
    }
  });
  page.addEventListener('change', event => {
    const tick = event.target.closest('[data-tick]');
    if (tick) { const n = Number(tick.dataset.tick); if (tick.checked) ticked.add(n); else ticked.delete(n); const b = $('[data-take-out]'); b.disabled = !ticked.size; b.textContent = `Take out of territory (${ticked.size})`; return; }
    const pick = event.target.closest('[data-search-pick]');
    if (pick) {
      const n = Number(pick.dataset.searchPick);
      if (pick.checked) search.ticked.add(n); else search.ticked.delete(n);
      const b = $('[data-put-search]');
      b.disabled = !search.ticked.size;
      b.innerHTML = `${window.WH.icon('plus')}Put the ticked companies here (${search.ticked.size})`;
    }
  });

  (async () => {
    const answer = await api('GET', 'lists');
    lists = answer.result.ok ? answer : {salespeople: []};
    if (id) loadDetail(); else loadList();
  })();
})();
