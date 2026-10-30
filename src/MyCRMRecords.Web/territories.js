(() => {
  'use strict';
  // territories.aspx (Sales Admins and MD): the named territories, each with a number, a salesperson and its companies,
  // replacing MyCRM's territories built from Advance. territories.aspx?id= is one territory: change its name, number or
  // salesperson (who then has all its companies), take it away, take companies out, and put companies in by searching,
  // by pasting a list (company names or BRNs) or from an Excel workbook (MyCRM's bulk transfer). A company put in a
  // territory leaves every other territory; each change of salesperson is recorded as a territory movement.
  // Laid out as the approved design: the list is one line per territory under glass filter capsules (All, Vacant,
  // Taken away); a territory has its actions in one glass capsule and two tabs, Companies and Add companies. On a phone
  // the list is a card per territory and the switches fit the screen.
  const {$, $$, esc, api, toast, ask, date, drawIcons, pager, param, upload} = window.WH;
  const {dialog, refused} = window.CRM;
  const page = $('#page');
  const id = Number(param('id') || 0);
  let lists = null, list = null, show = 'all', q = '';
  let d = null, companyQ = '', companySkip = 0, ticked = new Set(), tab = 'companies';
  let search = {q: '', unassigned: false, skip: 0, answer: null, ticked: new Set()}, matches = null, workbook = '', addTab = 'search', timer = 0;

  const plural = (n, one, many) => `${n.toLocaleString('en-SG')} ${n === 1 ? one : many}`;
  const companiesOf = n => plural(n, 'company', 'companies');
  const vacant = owner => !(owner && owner.login);
  const ownerName = owner => vacant(owner) ? '' : (owner.name || owner.login);
  const salespersonOf = owner => vacant(owner) ? '<span class="terr-vacant">Vacant</span>' : esc(ownerName(owner));

  // ---- The list of territories ----
  async function loadList() {
    const answer = await api('GET', 'territories' + (show === 'taken' ? '?inactive=1' : ''));
    $('#loading').hidden = true;
    page.hidden = false;
    if (!answer.result.ok) { page.innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3></div>`; return; }
    list = answer.territories;
    renderList();
  }

  const shows = [['all', 'All'], ['vacant', 'Vacant'], ['taken', 'Taken away']];
  const notes = {all: 'A company is in one territory; putting it in another moves it there.', vacant: 'no salesperson, so their companies have no salesperson either',
    taken: 'kept for history; their companies were taken out'};

  function renderList() {
    if (!$('#rows')) {
      page.innerHTML = `<div class="page-heading terr-heading"><div class="terr-title"><h1>Territories</h1><p id="count">&nbsp;</p></div>
          <div class="heading-actions"><button type="button" class="button primary" id="new-territory" title="New territory"><span data-icon="plus"></span><span class="action-label">New territory</span></button></div></div>
        <div class="terr-filter-row" role="search" aria-label="Filter territories">
          <label class="search-box"><span data-icon="search"></span><input id="q" type="search" placeholder="Territory, number or salesperson" value="${esc(q)}" aria-label="Territory, number or salesperson"></label>
          <div class="tabs terr-segments" role="group" aria-label="Show">${shows.map(([value, label]) =>
            `<button type="button" class="tab" data-show="${value}" aria-pressed="${String(show === value)}">${label}</button>`).join('')}</div></div>
        <section class="terr-list" aria-label="Territories list"><div id="rows"></div><div id="cards" class="file-cards"></div></section>`;
      drawIcons(page);
      $('#q').addEventListener('input', event => { q = event.target.value; renderList(); });
      $$('[data-show]', page).forEach(b => b.addEventListener('click', () => {
        if (show === b.dataset.show) return;
        const reload = (show === 'taken') !== (b.dataset.show === 'taken');
        show = b.dataset.show;
        $$('[data-show]', page).forEach(x => x.setAttribute('aria-pressed', String(x.dataset.show === show)));
        if (reload) loadList(); else renderList();
      }));
      $('#new-territory').addEventListener('click', () => territoryDialog(null));
    }
    const term = q.trim().toLowerCase();
    const rows = list.filter(t => (show === 'taken' ? !t.isActive : t.isActive) && (show !== 'vacant' || vacant(t.owner))
      && (!term || `${t.title} ${t.number} ${ownerName(t.owner)} ${t.owner.login} ${vacant(t.owner) ? 'vacant' : ''}`.toLowerCase().includes(term)));
    $('#count').innerHTML = `<b>${plural(rows.length, 'territory', 'territories')}</b><span class="terr-note"> · ${esc(notes[show])}</span>`;
    const last = t => t.isActive ? (t.createdOn ? `${date(t.createdOn)}${t.createdBy ? ' · ' + t.createdBy : ''}` : '')
      : `Taken away${t.removedOn ? ' ' + date(t.removedOn) : ''}${t.removedBy ? ' · ' + t.removedBy : ''}`;
    const empty = `<div class="empty"><h3>Nothing to show</h3><p class="muted">${term ? 'No territory matches. Try another name, number or salesperson.'
      : show === 'taken' ? 'No territory has been taken away.' : show === 'vacant' ? 'Every territory has a salesperson.' : 'No territory yet.'}</p></div>`;
    $('#rows').innerHTML = rows.length ? `<table class="data terr-table"><thead><tr><th class="t-name">Name</th><th class="t-number">Number</th><th>Salesperson</th>
        <th class="num t-count">Companies</th><th class="t-last">${show === 'taken' ? 'Taken away' : 'Created'}</th></tr></thead><tbody>
      ${rows.map(t => `<tr data-href="territories.aspx?id=${t.id}"${t.isActive ? '' : ' class="inactive"'}>
        <td class="t-name" title="${esc(t.title)}"><a href="territories.aspx?id=${t.id}">${esc(t.title)}</a></td>
        <td class="t-number">${t.number}</td>
        <td title="${esc(ownerName(t.owner))}">${t.isActive ? salespersonOf(t.owner) : `${esc(ownerName(t.owner))} <span class="terr-taken">Taken away</span>`}</td>
        <td class="num t-count">${t.companies.toLocaleString('en-SG')}</td>
        <td class="t-last" title="${esc(last(t))}">${esc(last(t))}</td></tr>`).join('')}</tbody></table>`
      : empty;
    $('#cards').innerHTML = rows.map(t => `<a class="file-card" href="territories.aspx?id=${t.id}">
        <span class="file-card-body"><span class="file-card-top"><b>${esc(t.title)}</b>${t.isActive ? (vacant(t.owner) ? '<span class="terr-vacant">Vacant</span>' : '') : '<span class="terr-taken">Taken away</span>'}</span>
          <span class="file-card-line">No. ${t.number} · ${companiesOf(t.companies)}</span>
          ${vacant(t.owner) ? '' : `<span class="file-card-line strong">${esc(ownerName(t.owner))}</span>`}</span>
        <span data-icon="arrow-right" class="section-chevron"></span></a>`).join('') || empty;
    drawIcons($('#cards'));
  }

  /** Creates (territory null) or changes a territory: its name, number and salesperson. */
  function territoryDialog(t) {
    const owner = t && t.owner.login ? t.owner : null;
    const people = lists.salespeople.slice();
    if (owner && !people.some(p => p.login.toLowerCase() === owner.login.toLowerCase())) people.unshift({login: owner.login, name: owner.name || owner.login});
    let chosen = owner ? owner.login : '';
    const same = (a, b) => (a || '').toLowerCase() === (b || '').toLowerCase();
    const el = dialog(t ? `Edit ${t.title}` : 'New territory', `
      <div class="terr-group">
        <label class="field"><span class="required">Name</span><input data-field="name" type="text" maxlength="100" value="${esc(t ? t.name : '')}" placeholder="e.g. Central-04"><span class="field-error"></span></label>
        <label class="field"><span class="required">Number</span><input data-field="number" type="number" inputmode="numeric" value="${t ? t.number : ''}"><span class="field-error"></span></label>
        ${t ? `<label class="field"><span>Salesperson</span><select data-field="owner"><option value="">No salesperson (vacant)</option>
          ${people.map(p => `<option value="${esc(p.login)}"${same(chosen, p.login) ? ' selected' : ''}>${esc(p.name)}</option>`).join('')}</select><span class="field-error"></span></label>` : ''}
      </div>
      ${t ? `<p class="terr-hint">Was ${esc(owner ? (owner.name || owner.login) : 'vacant')}. Only people with the MyCRM role Sales are listed.</p>
        <div class="notice terr-move" id="move-note" hidden></div>`
      : `<p class="terr-group-label" id="sp-label">Salesperson</p>
        <div class="terr-group terr-people"><label class="search-box terr-people-search"><span data-icon="search"></span><input id="sp-q" type="search" placeholder="Search people with the Sales role" aria-label="Search people with the Sales role"></label>
          <div class="terr-people-list" role="radiogroup" aria-labelledby="sp-label" id="sp-list"></div><span class="field-error" data-field="owner"></span></div>
        <p class="terr-hint">People with the MyCRM role Sales. Choose No salesperson (vacant) if no one looks after it yet.</p>
        <p class="terr-hint center">It starts with no companies. Next, open it and put companies in it.</p>`}`);
    $('[data-submit]', el).textContent = t ? 'Save changes' : 'Create territory';
    const numberBox = $('[data-field=number]', el);
    // Another active territory's number is said at once (the server checks it again when saved).
    const checkNumber = () => {
      const n = Number(numberBox.value), field = numberBox.closest('.field');
      const used = numberBox.value !== '' && (!t || n !== t.number) ? (list || []).find(o => o.isActive && o.number === n && (!t || o.id !== t.id)) : null;
      field.classList.toggle('invalid', !!used);
      $('.field-error', field).textContent = used ? `Territory number ${n} is ${used.title}'s.` : '';
    };
    numberBox.addEventListener('input', checkNumber);
    if (t) {
      const select = $('[data-field=owner]', el);
      select.addEventListener('change', () => {
        const note = $('#move-note', el);
        note.hidden = same(select.value, owner ? owner.login : '');
        const who = select.value ? select.options[select.selectedIndex].text : '';
        note.textContent = d ? (who ? `All ${companiesOf(d.companies.total)} move to ${who}; each move is recorded.` : `All ${companiesOf(d.companies.total)} will have no salesperson; each move is recorded.`)
          : 'A new salesperson takes all the territory\'s companies; each company\'s move is recorded.';
      });
    } else {
      const choices = [{login: '', name: 'No salesperson (vacant)'}].concat(people);
      const draw = () => {
        const term = $('#sp-q', el).value.trim().toLowerCase();
        const shown = choices.filter(p => !term || `${p.name} ${p.login}`.toLowerCase().includes(term) || same(p.login, chosen));
        $('#sp-list', el).innerHTML = shown.map(p => `<button type="button" role="radio" class="terr-person" data-login="${esc(p.login)}" aria-checked="${same(p.login, chosen)}">
          <span>${esc(p.name)}</span><span data-icon="check" class="terr-person-check"></span></button>`).join('') || '<p class="muted terr-none">Nobody found.</p>';
        drawIcons($('#sp-list', el));
      };
      draw();
      $('#sp-q', el).addEventListener('input', draw);
      $('#sp-list', el).addEventListener('click', event => {
        const person = event.target.closest('[data-login]');
        if (!person) return;
        chosen = person.dataset.login;
        draw();
      });
    }
    $('form', el).addEventListener('submit', async event => {
      event.preventDefault();
      const v = name => $(`[data-field="${name}"]`, el).value;
      const answer = await api('POST', 'territories/' + (t ? t.id : 0), {name: v('name'), number: v('number') === '' ? null : Number(v('number')), owner: t ? v('owner') : chosen});
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
    if (!d.territory.isActive) tab = 'companies';
    renderDetail();
  }

  function renderDetail() {
    const t = d.territory;
    if (!$('#panel')) {
      page.innerHTML = `<div class="page-heading terr-heading"><div class="terr-title"><a class="crumb" href="territories.aspx"><span data-icon="arrow-left"></span>Territories</a>
            <div class="terr-name"><h1 id="title"></h1><span class="terr-number" id="number"></span><span id="taken"></span></div><p id="subtitle"></p></div>
          <div class="heading-actions" id="actions"></div></div>
        <section class="terr-owner-card" id="owner-card" aria-label="Salesperson"></section>
        <div class="tabs terr-tabs terr-segments" role="tablist" aria-label="Territory sections" id="tabs"></div>
        <div id="panel"></div>
        <div id="take-away-row"></div>`;
    }
    $('#title').textContent = t.title;
    $('#number').textContent = `No. ${t.number}`;
    $('#taken').innerHTML = t.isActive ? '' : '<span class="terr-taken">Taken away</span>';
    $('#subtitle').innerHTML = `<span class="terr-wide">Salesperson <b>${vacant(t.owner) ? 'none (vacant)' : esc(ownerName(t.owner))}</b> · </span>${companiesOf(d.companies.total)}${companyQ ? ' match' : ''}`
      + `<span class="terr-note"> · ${t.name ? '' : 'no name (made in MyCRM) · '}created${t.createdBy ? ' by ' + esc(t.createdBy) : ''}${t.createdOn ? ' on ' + esc(date(t.createdOn)) : ''}`
      + `${t.isActive ? '' : ` · taken away${t.removedOn ? ' on ' + esc(date(t.removedOn)) : ''}${t.removedBy ? ' by ' + esc(t.removedBy) : ''}`}</span>`;
    $('#owner-card').innerHTML = `<span class="terr-avatar" aria-hidden="true">${esc((ownerName(t.owner) || '–').trim().charAt(0).toUpperCase())}</span>
      <span class="terr-owner-text"><span class="muted">Salesperson</span><b>${vacant(t.owner) ? 'None (vacant)' : esc(ownerName(t.owner))}</b></span><span class="terr-number">No. ${t.number}</span>`;
    $('#actions').innerHTML = t.isActive ? `<div class="action-capsule" role="group" aria-label="Territory actions">
        <button type="button" class="capsule-button" id="edit" title="Edit"><span data-icon="edit"></span><span class="action-label">Edit</span></button>
        <span class="capsule-divider terr-wide" aria-hidden="true"></span>
        <button type="button" class="capsule-button terr-danger terr-wide" id="take-away" title="Take away"><span data-icon="trash"></span><span class="action-label">Take away</span></button></div>` : '';
    $('#take-away-row').innerHTML = t.isActive ? '<button type="button" class="terr-take-row" id="take-away-phone">Take away territory</button>' : '';
    const tabs = [['companies', `Companies · ${d.companies.total.toLocaleString('en-SG')}`]].concat(t.isActive ? [['add', 'Add companies']] : []);
    $('#tabs').innerHTML = tabs.map(([key, label]) => `<button type="button" class="tab" role="tab" data-tab="${key}" aria-selected="${tab === key}">${esc(label)}</button>`).join('');
    $('#tabs').hidden = tabs.length < 2;
    if (tab === 'add') addPanel(); else companies();
    drawIcons(page);
    $('#edit')?.addEventListener('click', () => territoryDialog(t));
    const takeAway = async () => {
      if (!await ask({title: `Take ${t.title} away?`, text: `Its ${companiesOf(d.companies.total)} are taken out of it and have no salesperson from it (each move is recorded). The territory is kept, marked as taken away.`, ok: 'Take away', danger: true})) return;
      const answer = await api('POST', `territories/${id}/remove`, {});
      toast(answer.result.message, !answer.result.ok);
      if (answer.result.ok) { companySkip = 0; loadDetail(); }
    };
    $('#take-away')?.addEventListener('click', takeAway);
    $('#take-away-phone')?.addEventListener('click', takeAway);
  }

  function companies() {
    const c = d.companies, active = d.territory.isActive;
    $('#panel').innerHTML = `<section role="tabpanel" aria-label="Companies of the territory" class="terr-panel">
      <div class="terr-filter-row" role="search">
        <label class="search-box"><span data-icon="search"></span><input id="company-q" type="search" placeholder="Company name or BRN" value="${esc(companyQ)}" aria-label="Company name or BRN"></label>
        ${active && c.rows.length ? '<button type="button" class="button secondary terr-wide" data-tick-page>Tick all shown</button>' : ''}
        <span class="terr-shown">${c.rows.length ? `Showing ${c.skip + 1}–${c.skip + c.rows.length} of ${c.total.toLocaleString('en-SG')}` : ''}</span></div>
      ${active ? `<div class="selection-pill terr-ticked" role="region" aria-label="Ticked companies" id="ticked-bar"${ticked.size ? '' : ' hidden'}>
        <b id="ticked-count"></b><span class="capsule-divider terr-wide" aria-hidden="true"></span>
        <span class="terr-ticked-note terr-wide">Taken-out companies have no territory until you put them in another.</span>
        <button type="button" class="text-button" data-untick>Clear</button>
        <button type="button" class="button danger-btn small-btn" data-take-out>Take out of territory</button></div>` : ''}
      <div class="terr-list">
      ${c.rows.length ? `<table class="data terr-table terr-companies"><thead><tr>${active ? '<th class="tick"></th>' : ''}<th class="t-name">Company</th><th>BRN</th><th class="t-postal">Postal code</th><th class="t-since">Since</th></tr></thead><tbody>
        ${c.rows.map(r => `<tr${ticked.has(r.id) ? ' class="ticked"' : ''}>${active ? `<td class="tick"><input type="checkbox" data-tick="${r.id}"${ticked.has(r.id) ? ' checked' : ''} aria-label="Tick ${esc(r.name)}"></td>` : ''}
          <td class="t-name" title="${esc(r.name)}"><a href="company.aspx?id=${r.id}">${esc(r.name)}</a></td><td title="${esc(r.brn)}">${esc(r.brn)}</td>
          <td class="t-postal">${esc(r.postalCode || '')}</td><td class="t-since">${esc(date(r.since))}</td></tr>`).join('')}</tbody></table>
        <div class="terr-cards">${c.rows.map(r => `<div class="terr-card${ticked.has(r.id) ? ' ticked' : ''}">
          ${active ? `<label class="terr-card-tick"><input type="checkbox" data-tick="${r.id}"${ticked.has(r.id) ? ' checked' : ''} aria-label="Tick ${esc(r.name)}"></label>` : ''}
          <a class="file-card" href="company.aspx?id=${r.id}"><span class="file-card-body"><span class="file-card-top"><b>${esc(r.name)}</b></span>
            <span class="file-card-line">${esc([r.brn ? 'BRN ' + r.brn : '', r.postalCode, r.since ? 'since ' + date(r.since) : ''].filter(Boolean).join(' · '))}</span></span>
            <span data-icon="arrow-right" class="section-chevron"></span></a></div>`).join('')}</div>`
        : `<div class="empty"><h3>Nothing to show</h3><p class="muted">${companyQ ? 'No company of this territory matches. Try another name or BRN.' : 'No company in this territory.'}</p></div>`}
      <div class="pager" id="company-pager" hidden></div></div></section>`;
    pager($('#company-pager'), c.total, c.skip, c.take);
    drawIcons($('#panel'));
    tickedBar();
    $('#company-q').addEventListener('input', event => {
      clearTimeout(timer);
      companyQ = event.target.value;
      companySkip = 0;
      timer = setTimeout(async () => { await loadDetail(); const box = $('#company-q'); box.focus(); box.setSelectionRange(box.value.length, box.value.length); }, 350);
    });
  }

  function tickedBar() {
    const bar = $('#ticked-bar');
    if (!bar) return;
    bar.hidden = !ticked.size;
    document.body.classList.toggle('terr-ticking', ticked.size > 0);
    $('#ticked-count').textContent = `${ticked.size} ticked`;
    $$('tr', $('#panel')).forEach(tr => { const box = $('[data-tick]', tr); if (box) tr.classList.toggle('ticked', box.checked); });
    $$('.terr-card', $('#panel')).forEach(card => card.classList.toggle('ticked', $('[data-tick]', card).checked));
  }

  // ---- Putting companies in ----
  const modes = [['search', 'Search', 'Find companies one by one'], ['paste', 'Paste a list', 'From an email or spreadsheet'], ['excel', 'Excel workbook', 'Upload a file with many companies']];

  function addPanel() {
    document.body.classList.remove('terr-ticking');
    $('#panel').innerHTML = `<section role="tabpanel" aria-label="Put companies in this territory" class="terr-panel">
      <div class="terr-step"><h2>1. Choose how to find the companies</h2>
        <p class="muted">A company can be in one territory only. Putting it here moves it out of the territory it was in, and this territory's salesperson has it from then on.</p></div>
      <div class="terr-modes" role="radiogroup" aria-label="How to find companies">${modes.map(([key, title, hint]) => `<div class="terr-mode${addTab === key ? ' on' : ''}">
        <button type="button" role="radio" class="terr-mode-pick" data-add-tab="${key}" aria-checked="${addTab === key}"><span class="terr-radio" aria-hidden="true"></span>
          <span class="terr-mode-text"><b>${esc(title)}</b><span class="muted">${esc(hint)}</span></span></button>
        ${addTab === key ? `<div class="terr-mode-body" id="mode-body"></div>` : ''}</div>`).join('')}</div>
      <section class="terr-results" id="results" aria-labelledby="results-title"></section></section>`;
    ({search: searchTab, paste: pasteTab, excel: excelTab})[addTab]();
  }

  function searchTab() {
    $('#mode-body').innerHTML = `<label class="terr-inset-search"><span data-icon="search"></span><input id="search-q" type="search" placeholder="Company name or BRN (2 letters or more)" value="${esc(search.q)}" aria-label="Company name or BRN"></label>
      <label class="check"><input type="checkbox" id="search-unassigned"${search.unassigned ? ' checked' : ''}>Only companies with no salesperson</label>`;
    drawIcons($('#mode-body'));
    $('#search-q').addEventListener('input', event => { clearTimeout(timer); search.q = event.target.value; search.skip = 0; timer = setTimeout(runSearch, 350); });
    $('#search-unassigned').addEventListener('change', event => { search.unassigned = event.target.checked; search.skip = 0; runSearch(); });
    if (search.q.trim().length >= 2 || search.unassigned) runSearch(); else drawSearch();
  }

  async function runSearch() {
    if (search.q.trim().length < 2 && !search.unassigned) { search.answer = null; drawSearch(); return; }
    $('#results').hidden = false;
    $('#results').innerHTML = '<p class="loading">Searching…</p>';
    const answer = await api('GET', 'territories/search?' + new URLSearchParams({q: search.q, unassigned: search.unassigned ? '1' : '', skip: search.skip}));
    if (!$('#results')) return;
    if (!answer.result.ok) { $('#results').innerHTML = `<p class="no terr-pad">${esc(answer.result.message)}</p>`; return; }
    search.answer = answer;
    drawSearch();
  }

  const nowIn = (territory, who) => territory ? `${esc(territory)} · ${who ? esc(who) : 'Vacant'}` : 'No territory';

  function drawSearch() {
    const a = search.answer, results = $('#results');
    if (!a) { results.hidden = false; results.innerHTML = '<p class="muted terr-pad">Type at least 2 letters of a company name or BRN, or tick Only companies with no salesperson.</p>'; return; }
    results.hidden = false;
    const leaving = a.rows.filter(r => search.ticked.has(r.id) && r.territoryId && r.ownerName).length;
    results.innerHTML = `<div class="terr-results-head"><h2 id="results-title">2. Tick the companies · ${plural(a.total, 'found', 'found')}</h2>
        ${leaving ? `<span class="terr-leaves-note">${leaving} ticked will leave another salesperson</span>` : ''}</div>
      ${a.rows.length ? `<table class="data terr-table"><thead><tr><th class="tick"><span class="sr-only">Tick</span></th><th class="t-name">Company</th><th>BRN</th><th class="t-now">Salesperson now</th></tr></thead><tbody>
        ${a.rows.map(r => {
          const here = r.territoryId === id;
          return `<tr${search.ticked.has(r.id) ? ' class="ticked"' : ''}><td class="tick">${here ? '<span class="muted small">Here</span>' : `<input type="checkbox" data-search-pick="${r.id}"${search.ticked.has(r.id) ? ' checked' : ''} aria-label="Tick ${esc(r.name)}">`}</td>
            <td class="t-name" title="${esc(r.name)}"><a href="company.aspx?id=${r.id}">${esc(r.name)}</a></td><td>${esc(r.brn)}</td>
            <td class="t-now">${here ? 'This territory' : `<span class="terr-now">${nowIn(r.territory, r.ownerName)}</span>${r.ownerName ? `<span class="terr-leaves">leaves ${esc(r.ownerName)}</span>` : ''}`}</td></tr>`;
        }).join('')}</tbody></table>
        <div class="pager terr-pad-x" id="search-pager" hidden></div>
        <div class="terr-results-foot"><button type="button" class="button secondary" data-search-tick>Tick all shown</button>
          <button type="button" class="button primary" data-put-search${search.ticked.size ? '' : ' disabled'}><span data-icon="plus"></span>Put the ticked companies here (${search.ticked.size})</button>
          <span class="muted terr-wide">You will be asked before anything changes.</span></div>`
        : '<p class="muted terr-pad">No company found.</p>'}`;
    drawIcons(results);
    if (a.rows.length) pager($('#search-pager'), a.total, a.skip, a.take);
  }

  function pasteTab() {
    $('#mode-body').innerHTML = `<label class="field terr-paste"><span>Companies, one per line: the company name or the BRN (or both, separated by a tab or a comma, as pasted from Excel)</span>
        <textarea id="paste" rows="5" placeholder="Tan &amp; Sons Pte Ltd&#10;199912345K"></textarea></label>
      <button type="button" class="button secondary" id="find"><span data-icon="search"></span>Find the companies</button>`;
    drawIcons($('#mode-body'));
    $('#find').addEventListener('click', async () => {
      const answer = await api('POST', `territories/${id}/match`, {text: $('#paste').value});
      if (!answer.result.ok) { toast(answer.result.message, true); return; }
      matches = answer.matches;
      workbook = '';
      drawMatches();
    });
    drawMatches();
  }

  function excelTab() {
    $('#mode-body').innerHTML = `<label class="terr-drop"><span data-icon="upload"></span><span><b>Choose the workbook…</b> · columns <b>Company</b> and <b>BRN</b> (MyCRM's bulk transfer file)</span>
        <input type="file" id="excel" accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" aria-label="Excel workbook with the columns Company and BRN"></label>
      <p class="muted terr-drop-note">${workbook ? `${esc(workbook)} · ` : ''}The companies found are shown first; nothing changes until you put them here. <a href="file.ashx/sample?of=territory">Download a sample</a></p>`;
    drawIcons($('#mode-body'));
    $('#excel').addEventListener('change', async event => {
      const file = event.target.files[0];
      if (!file) return;
      const answer = await upload('territory-list?territory=' + id, file);
      event.target.value = '';
      if (!answer.matches) { toast((answer.result && answer.result.message) || 'The workbook could not be read.', true); return; }
      matches = answer.matches;
      workbook = `${file.name} · ${plural(matches.length, 'row', 'rows')} read`;
      excelTab();
    });
    drawMatches();
  }

  function drawMatches() {
    const results = $('#results');
    if (!matches) { results.hidden = true; results.innerHTML = ''; return; }
    results.hidden = false;
    const found = matches.filter(m => m.companyId && !m.error), missed = matches.filter(m => !(m.companyId && !m.error));
    const moving = found.filter(m => m.salesperson).length;
    const head = cols => `<tr><th class="t-line">Line</th><th class="t-name">Company</th><th>BRN</th><th class="t-now">${cols}</th></tr>`;
    results.innerHTML = matches.length ? `<div class="terr-results-head"><h2 id="results-title">2. Check the list · ${found.length} of ${plural(matches.length, 'line', 'lines')} can be put here</h2>
        ${moving ? `<span class="terr-leaves-note">${moving} will leave another salesperson</span>` : ''}</div>
      ${found.length ? `<h3 class="terr-group-head">Found · ${found.length}</h3><table class="data terr-table"><thead>${head('Salesperson now')}</thead><tbody>
        ${found.map(m => `<tr${m.salesperson ? ' class="leaving"' : ''}><td class="t-line" title="${esc(m.text)}">${m.line}<span class="terr-line-text"> · ${esc(m.text)}</span></td>
          <td class="t-name" title="${esc(m.company)}"><a href="company.aspx?id=${m.companyId}">${esc(m.company)}</a></td><td>${esc(m.brn)}</td>
          <td class="t-now">${m.salesperson ? `<span class="terr-leaves">leaves ${esc(m.salesperson)}</span>` : '<span class="muted">None</span>'}</td></tr>`).join('')}</tbody></table>` : ''}
      ${missed.length ? `<h3 class="terr-group-head">Not found · ${missed.length} · these are skipped</h3><table class="data terr-table"><thead>${head('Why')}</thead><tbody>
        ${missed.map(m => `<tr class="missed"><td class="t-line">${m.line}</td><td class="t-name" title="${esc(m.company || m.text)}">${esc(m.company || m.text)}</td><td>${esc(m.brn)}</td>
          <td class="t-now">${esc(m.error || 'Company could not be found.')}</td></tr>`).join('')}</tbody></table>` : ''}
      <div class="terr-results-foot"><button type="button" class="button primary" data-put-matches${found.length ? '' : ' disabled'}><span data-icon="plus"></span>Put the ${found.length} found compan${found.length === 1 ? 'y' : 'ies'} here</button>
        <span class="muted terr-wide">You will be asked before anything changes.</span></div>`
      : '<p class="muted terr-pad">No line to read.</p>';
    drawIcons(results);
  }

  /** Asks, then puts the companies here; leaving lists those that leave another salesperson ([name, from]). */
  function put(companyIds, leaving) {
    if (!companyIds.length) return;
    const t = d.territory, n = companyIds.length;
    const who = vacant(t.owner) ? 'Nobody (the territory is vacant)' : ownerName(t.owner);
    const el = dialog(`Put ${companiesOf(n)} in ${t.title}?`, `
      <p>${leaving.length ? `${leaving.length} of them leave the territory they are in now. ` : ''}${esc(who)} has them from then on; each move is recorded in the company's Territory Movement History.</p>
      ${leaving.length ? `<ul class="put-companies">${leaving.slice(0, 8).map(([name, from]) => `<li class="terr-put-row"><span>${esc(name)}</span><span class="terr-leaves">from ${esc(from)}</span></li>`).join('')}
        ${leaving.length > 8 ? `<li class="muted">and ${leaving.length - 8} more</li>` : ''}</ul>` : ''}`);
    $('[data-submit]', el).textContent = 'Put them here';
    $('form', el).addEventListener('submit', async event => {
      event.preventDefault();
      $('[data-submit]', el).disabled = true;
      const answer = await api('POST', `territories/${id}/add`, {companies: companyIds});
      $('[data-submit]', el).disabled = false;
      if (!answer.result.ok) { refused(el, answer); return; }
      el.close();
      toast(answer.result.message);
      search.ticked.clear(); matches = null; workbook = '';
      await loadDetail();
      if (search.answer && addTab === 'search') runSearch();
    });
  }

  page.addEventListener('click', async event => {
    const move = event.target.closest('[data-page]');
    if (move) {
      if (move.closest('#company-pager')) { companySkip = Math.max(0, d.companies.skip + Number(move.dataset.page) * d.companies.take); loadDetail(); }
      else if (move.closest('#search-pager') && search.answer) { search.skip = Math.max(0, search.answer.skip + Number(move.dataset.page) * search.answer.take); runSearch(); }
      return;
    }
    const tabButton = event.target.closest('[data-tab]');
    if (tabButton) { tab = tabButton.dataset.tab; renderDetail(); return; }
    const mode = event.target.closest('[data-add-tab]');
    if (mode) { if (addTab !== mode.dataset.addTab) { addTab = mode.dataset.addTab; addPanel(); } return; }
    if (event.target.closest('[data-tick-page]')) { d.companies.rows.forEach(r => ticked.add(r.id)); $$('[data-tick]', $('#panel')).forEach(b => { b.checked = true; }); tickedBar(); return; }
    if (event.target.closest('[data-untick]')) { ticked.clear(); $$('[data-tick]', $('#panel')).forEach(b => { b.checked = false; }); tickedBar(); return; }
    if (event.target.closest('[data-take-out]')) {
      if (!ticked.size) return;
      if (!await ask({title: `Take ${companiesOf(ticked.size)} out of ${d.territory.title}?`, text: 'They have no salesperson from this territory then (each move is recorded).', ok: 'Take out', danger: true})) return;
      const answer = await api('POST', `territories/${id}/takeout`, {companies: [...ticked]});
      toast(answer.result.message, !answer.result.ok);
      if (answer.result.ok) { ticked.clear(); companySkip = 0; document.body.classList.remove('terr-ticking'); loadDetail(); }
      return;
    }
    if (event.target.closest('[data-search-tick]')) { search.answer.rows.filter(r => r.territoryId !== id).forEach(r => search.ticked.add(r.id)); drawSearch(); return; }
    if (event.target.closest('[data-put-search]')) {
      const rows = search.answer ? search.answer.rows : [];
      put([...search.ticked], rows.filter(r => search.ticked.has(r.id) && r.territoryId && r.ownerName).map(r => [r.name, r.ownerName]));
      return;
    }
    if (event.target.closest('[data-put-matches]')) {
      const found = matches.filter(m => m.companyId && !m.error);
      const seen = new Set();
      put([...new Set(found.map(m => m.companyId))], found.filter(m => m.salesperson && !seen.has(m.companyId) && seen.add(m.companyId)).map(m => [m.company, m.salesperson]));
      return;
    }
    // A territory's line in the list opens it (its name is the link for the keyboard).
    const row = event.target.closest('tr[data-href]');
    if (!row || event.target.closest('a, button, input, label, select')) return;
    if (window.getSelection && String(window.getSelection())) return;
    if (event.ctrlKey || event.metaKey) window.open(row.dataset.href, '_blank'); else location.href = row.dataset.href;
  });
  page.addEventListener('change', event => {
    const tick = event.target.closest('[data-tick]');
    if (tick) {
      const n = Number(tick.dataset.tick);
      if (tick.checked) ticked.add(n); else ticked.delete(n);
      $$(`[data-tick="${n}"]`, $('#panel')).forEach(b => { b.checked = tick.checked; });
      tickedBar();
      return;
    }
    const pick = event.target.closest('[data-search-pick]');
    if (pick) {
      const n = Number(pick.dataset.searchPick);
      if (pick.checked) search.ticked.add(n); else search.ticked.delete(n);
      drawSearch();
    }
  });

  (async () => {
    const answer = await api('GET', 'lists');
    lists = answer.result.ok ? answer : {salespeople: []};
    if (id) loadDetail(); else loadList();
  })();
})();
