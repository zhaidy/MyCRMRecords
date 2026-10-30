(() => {
  'use strict';
  // dormant.aspx: old prospects that were lost or left open, and companies visited with no prospect made, in the
  // territories of the person (or their team's), worth a second look. The person records what they decided (Followed up,
  // No opportunity with a reason, or Snooze until a day); the follow-up itself is done in D365, so the company's name can be
  // copied. "Download Excel (all)" always has everything in the person's scope, whatever the filters.
  const {$, $$, esc, api, drawIcons, pager, change, date} = window.WH;
  const page = $('#page');
  const blank = {list: 'prospects', decision: 'todo', owner: 'team', territory: '', reached: '', years: '', type: '', q: ''};
  const saved = (() => { try { return JSON.parse(localStorage.getItem('dormant-filter') || '{}'); } catch { return {}; } })();
  const filter = {...blank, ...Object.fromEntries(Object.entries(saved).filter(([k]) => k in blank)), skip: 0};
  let data = null, timer = 0, openKey = '';
  const money = v => v > 0 ? 'S$ ' + Math.round(v).toLocaleString('en-SG') : '–';
  const bigMoney = v => v >= 1000000 ? 'S$ ' + (v / 1000000).toFixed(2) + 'M' : money(v) === '–' ? 'S$ 0' : money(v);
  const month = s => { const m = /^(\d{4})-(\d{2})/.exec(s || ''); return m ? ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'][Number(m[2]) - 1] + ' ' + m[1] : 'no date'; };
  const key = r => r.kind + ':' + r.id;
  const reached = [['', 'Any'], ['2', 'First appointment or beyond'], ['3', 'File / media test or beyond'], ['4', 'Demo / POC or beyond'], ['5', 'Proposal presented or beyond'], ['6', 'Negotiation']];
  const looks = [['', 'All time'], ['1', 'Last year'], ['2', 'Last 2 years'], ['3', 'Last 3 years'], ['5', 'Last 5 years']];
  const decisions = [['todo', 'To follow up'], ['followedup', 'Followed up'], ['noopportunity', 'No opportunity'], ['snoozed', 'Snoozed']];

  const query = () => {
    const q = {list: filter.list, decision: filter.decision, owner: filter.owner, territory: filter.territory, reached: filter.reached, years: filter.years, type: filter.type, q: filter.q};
    if (filter.skip) q.skip = String(filter.skip);
    if (filter.list === 'visited') delete q.type;
    return new URLSearchParams(Object.entries(q).filter(([, v]) => v !== '' && v !== undefined));
  };

  async function load() {
    try { localStorage.setItem('dormant-filter', JSON.stringify(Object.fromEntries(Object.keys(blank).map(k => [k, filter[k]])))); } catch { /* not kept */ }
    const answer = await api('GET', 'dormant?' + query());
    $('#loading').hidden = true;
    page.hidden = false;
    if (!$('#rows')) shell(answer);
    if (!answer.result.ok) {
      $('#rows').innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3></div>`;
      $('#count').textContent = '';
      return;
    }
    data = answer;
    fillChoices();
    render();
  }

  function shell(answer) {
    const manager = (answer.team || []).length > 0;
    page.innerHTML = `
      <div class="page-heading companies-heading"><div class="companies-title"><h1>Dormant opportunities</h1><p id="count"></p></div>
        <div class="heading-actions"><span class="action-capsule"><a class="capsule-button" id="download" href="file.ashx/dormant"><span data-icon="download"></span><span class="action-label">Download Excel (all)</span></a></span></div></div>
      <div class="companies-filters dormant-filters" role="search">
        <div class="companies-filter-row">
          <label class="search-box"><span data-icon="search"></span><span class="sr-only">Search dormant opportunities</span>
            <input id="q" type="search" placeholder="Company name or BRN" value="${esc(filter.q)}" aria-label="Search dormant opportunities"></label>
          <div class="tabs dormant-tabs" role="group" aria-label="List" id="lists"></div>
        </div>
        <div class="companies-filter-row">
          <div class="tabs dormant-tabs" role="group" aria-label="Decision">${decisions.map(([v, l]) => `<button type="button" class="tab" data-decision="${v}" aria-pressed="false">${l}</button>`).join('')}</div>
          ${manager ? '<label class="filter-capsule dormant-cap"><span>Salesperson</span><select id="owner"></select></label>' : ''}
          <label class="filter-capsule dormant-cap"><span>Territory</span><select id="territory"></select></label>
          <label class="filter-capsule dormant-cap"><span>Reached</span><select id="reached">${reached.map(([v, l]) => `<option value="${v}">${l}</option>`).join('')}</select></label>
          <label class="filter-capsule dormant-cap"><span>Look back</span><select id="years">${looks.map(([v, l]) => `<option value="${v}">${l}</option>`).join('')}</select></label>
          <label class="filter-capsule dormant-cap" id="type-box"><span>Type</span><select id="type"></select></label>
        </div>
      </div>
      ${manager ? '<section class="tiles dormant-tiles" aria-label="Summary" id="tiles"></section>' : ''}
      <section aria-label="Dormant opportunities" class="companies-list dormant-list"><div id="rows"></div><div id="cards" class="file-cards"></div><div class="pager" id="pager" hidden></div></section>`;
    drawIcons(page);
    $('#q').addEventListener('input', event => { filter.q = event.target.value; filter.skip = 0; clearTimeout(timer); timer = setTimeout(load, 350); });
    for (const id of ['owner', 'territory', 'reached', 'years', 'type']) $('#' + id)?.addEventListener('change', event => { filter[id] = event.target.value; filter.skip = 0; if (id === 'owner') filter.territory = ''; load(); });
  }

  function fillChoices() {
    const set = (id, options, value) => {
      const el = $('#' + id);
      if (!el) return;
      el.innerHTML = options.map(([v, l]) => `<option value="${esc(v)}">${esc(l)}</option>`).join('');
      el.value = options.some(([v]) => String(v) === String(value)) ? value : options[0][0];
      filter[id] = el.value;
    };
    set('owner', [['me', 'Me'], ['team', 'My team'], ...data.team.map(p => [p.login, p.name])], filter.owner);
    set('territory', [['', 'All'], ...data.territories.map(t => [String(t.id), t.name])], filter.territory);
    set('type', [['', 'All types'], ...data.types.map(t => [t.code, t.label])], filter.type);
    $('#reached').value = filter.reached;
    $('#years').value = filter.years;
    $('#type-box').hidden = filter.list === 'visited';
  }

  function render() {
    const c = data.counts;
    const tabs = [['prospects', 'Prospects', c.prospectsToFollowUp], ['visited', 'Visited, no prospect', c.visitedToFollowUp]];
    $('#lists').innerHTML = tabs.map(([v, l, n]) => `<button type="button" class="tab" data-list="${v}" aria-pressed="${filter.list === v}">${l} <span class="count">${n}</span></button>`).join('');
    $$('[data-decision]', page).forEach(b => b.setAttribute('aria-pressed', String(b.dataset.decision === filter.decision)));
    const who = filter.owner === 'me' ? 'mine' : filter.owner === 'team' ? (data.team.length ? 'my team' : 'mine') : ($('#owner').selectedOptions[0] || {}).textContent || '';
    const word = filter.list === 'visited' ? 'compan' + (data.total === 1 ? 'y' : 'ies') : 'prospect' + (data.total === 1 ? '' : 's');
    $('#count').innerHTML = `<b>${data.total.toLocaleString('en-SG')} ${word}</b> · ${esc((decisions.find(d => d[0] === filter.decision) || decisions[0])[1].toLowerCase())} · ${esc(who)}`;
    if ($('#tiles')) $('#tiles').innerHTML = [['To follow up', c.toFollowUp], ['Value in these', bigMoney(c.valueToFollowUp)], ['Followed up', c.followedUp], ['No opportunity · snoozed', `${c.noOpportunity} · ${c.snoozed}`]]
      .map(([l, v]) => `<div class="tile"><div class="tile-label">${l}</div><div class="big-number">${v}</div></div>`).join('');
    const rows = data.rows;
    const prospects = filter.list === 'prospects';
    const line = r => `<tr class="dormant-line${openKey === key(r) ? ' open' : ''}" data-row="${esc(key(r))}">
        <td><a class="row-link" href="company.aspx?id=${r.companyId}">${esc(r.company)}</a><span class="sub">${esc(r.territory)}</span></td>
        <td>${esc(r.owner)}</td>
        <td class="dormant-why"><span class="pill ${r.priority.toLowerCase()}">${esc(r.priority)}</span>${esc(r.why)}${r.furthest ? `<span class="sub">Furthest activity: ${esc(r.furthest)}</span>` : ''}</td>
        <td class="num">${prospects ? money(r.value) : '–'}</td>
        <td>${stateTag(r)}</td>
        <td class="actions"><button type="button" class="text-button" data-copy="${esc(r.company)}"><span data-icon="copy"></span>Copy name</button></td></tr>
      ${openKey === key(r) ? `<tr><td colspan="6" class="detail">${detail(r, prospects, 't')}</td></tr>` : ''}`;
    $('#rows').innerHTML = rows.length ? `<div class="table-wrap"><table class="data"><thead><tr><th>Company</th><th>Salesperson</th><th>Why it's here</th><th class="num">Value</th><th>Decision</th><th></th></tr></thead>
      <tbody>${rows.map(line).join('')}</tbody></table></div>`
      : `<div class="empty"><h3>Nothing here</h3><p class="muted">${filter.decision === 'todo' ? 'No dormant opportunities match. Try All time, or another salesperson or territory.' : 'Nothing has this decision yet.'}</p></div>`;
    // On a phone: a card per company (the table is hidden), opening in place.
    $('#cards').innerHTML = rows.map(r => `<div class="dormant-card${openKey === key(r) ? ' open' : ''}">
        <button type="button" class="file-card" data-row="${esc(key(r))}"><span class="file-card-body">
          <span class="file-card-top"><b>${esc(r.company)}</b><span class="pill ${r.priority.toLowerCase()}">${esc(r.priority)}</span></span>
          <span class="file-card-line">${esc(r.why)}</span>
          <span class="file-card-line">${esc(r.owner)} · ${esc(r.territory)}${prospects ? ' · ' + esc(money(r.value)) : ''}</span>
          <span class="file-card-line">${stateTag(r)}</span></span></button>
        ${openKey === key(r) ? `<div class="dormant-card-detail">${detail(r, prospects)}<button type="button" class="text-button" data-copy="${esc(r.company)}"><span data-icon="copy"></span>Copy name</button></div>` : ''}</div>`).join('');
    drawIcons($('#rows'));
    drawIcons($('#cards'));
    pager($('#pager'), data.total, data.skip, data.take);
    if (!data.total) $('#pager').hidden = true;
  }

  function stateTag(r) {
    if (r.state === 'FollowedUp') return `<span class="status ok">Followed up</span><span class="sub">${esc(who(r))}</span>`;
    if (r.state === 'NoOpportunity') return `<span class="status off">No opportunity</span><span class="sub">${esc(r.reason)}${r.decidedBy ? ' · ' + esc(who(r)) : ''}</span>`;
    if (r.state === 'Snoozed') return `<span class="status waiting">Snoozed</span><span class="sub">Until ${esc(date(r.snoozeUntil))}</span>`;
    return '<span class="status pending">To follow up</span>';
  }
  const who = r => [r.decidedBy, r.decidedAt ? date(r.decidedAt) : ''].filter(Boolean).join(', ');

  function detail(r, prospects, where = 'c') {
    const facts = prospects
      ? [['Prospect status', r.status], ['Type', r.type || '–'], ['Value', money(r.value)], [r.state === 'ToFollowUp' && r.status !== 'Lost' ? 'Last activity' : 'Closed', r.status === 'Lost' ? month(r.closedOn) : month(r.lastActivity)]]
      : [['Last activity', month(r.lastActivity)], ['Activities', String(r.activities)], ['Furthest activity', r.furthest || '–'], ['BRN', r.brn || '–']];
    const canDecide = data.canDecide;
    return `<div class="dormant-detail"><div>
        <div class="facts">${facts.map(([l, v]) => `<div><div class="fact-label">${esc(l)}</div>${esc(v)}</div>`).join('')}</div>
        <div><div class="fact-label">Activity history</div>${r.history.length ? `<ul class="timeline">${r.history.map(h => `<li><span>${esc(h.label)}</span><span class="muted">${esc(h.date ? date(h.date) : 'no date')}</span></li>`).join('')}</ul>` : '<p class="muted">No activities recorded.</p>'}</div>
      </div><div>
        <div class="fact-label">Your decision</div>
        ${canDecide ? `<div class="dormant-row"><button type="button" class="button ok-btn small-btn" data-decide="FollowedUp">Followed up</button></div>
        <div class="dormant-row"><label class="field">Reason<input type="text" id="reason-${where}" data-field="reason" maxlength="500" placeholder="Why is there no opportunity?" value="${esc(r.reason)}"><span class="field-error"></span></label>
          <button type="button" class="button secondary small-btn" data-decide="NoOpportunity">No opportunity</button></div>
        <div class="dormant-row"><label class="field">Snooze until<input type="date" id="snooze-${where}" data-field="snoozeUntil" min="${esc(data.today)}" value="${esc(r.snoozeUntil || '')}"><span class="field-error"></span></label>
          <button type="button" class="button secondary small-btn" data-decide="Snoozed">Snooze</button></div>`
        : '<p class="muted">Your role can read this page but not record decisions.</p>'}
        <div class="notice info">Follow-ups are logged in D365. Copy the company name and search for it there; this page records only your decision.</div>
      </div></div>`;
  }

  async function copy(text, button) {
    try { await navigator.clipboard.writeText(text); }
    catch {
      const area = document.createElement('textarea');
      area.value = text; area.style.position = 'fixed'; area.style.opacity = '0';
      document.body.append(area); area.select();
      try { document.execCommand('copy'); } catch { /* not copied */ }
      area.remove();
    }
    window.WH.toast('Copied ' + text);
  }

  async function decide(decision) {
    const r = data.rows.find(x => key(x) === openKey);
    if (!r) return;
    const body = {kind: r.kind, recordId: r.id, decision};
    const where = $('#reason-t')?.offsetParent ? 't' : 'c';
    if (decision === 'NoOpportunity') body.reason = $('#reason-' + where).value.trim();
    if (decision === 'Snoozed') body.snoozeUntil = $('#snooze-' + where).value;
    const answer = await change('dormant/decision', body);
    if (!answer.result.ok) { window.WH.showErrors($('#reason-' + where).closest('.dormant-detail'), answer.result.errors); return; }
    openKey = '';
    await load();
  }

  page.addEventListener('click', event => {
    const copyButton = event.target.closest('[data-copy]');
    if (copyButton) { event.stopPropagation(); copy(copyButton.dataset.copy, copyButton); return; }
    const list = event.target.closest('[data-list]');
    if (list) { filter.list = list.dataset.list; filter.skip = 0; openKey = ''; load(); return; }
    const decision = event.target.closest('[data-decision]');
    if (decision) { filter.decision = decision.dataset.decision; filter.skip = 0; openKey = ''; load(); return; }
    const decide_ = event.target.closest('[data-decide]');
    if (decide_) { decide(decide_.dataset.decide); return; }
    const move = event.target.closest('[data-page]');
    if (move && data) { filter.skip = Math.max(0, data.skip + Number(move.dataset.page) * data.take); openKey = ''; load(); window.scrollTo({top: 0}); return; }
    if (event.target.closest('a, input, select, label, .detail')) return;
    const row = event.target.closest('[data-row]');
    if (row) { openKey = openKey === row.dataset.row ? '' : row.dataset.row; render(); }
  });

  load();
})();
