(() => {
  'use strict';
  // prospects.aspx: MyCRM's prospects of the person and their team, read only (they are created and changed in MyCRM):
  // searched by company or remarks, by stage, type, status, owner and estimated order dates, newest first, 50 a page.
  // The search and filters apply as they are typed or chosen and are kept for next time. A row opens the company's
  // Prospects tab. On a phone a card per prospect.
  const {$, $$, esc, api, drawIcons, pager, date, number} = window.WH;
  const page = $('#page');
  const blank = {q: '', stage: '', type: '', status: 'open', owner: 'team', from: '', to: ''};
  const saved = (() => { try { return JSON.parse(localStorage.getItem('prospects-filter') || '{}'); } catch { return {}; } })();
  const filter = {...blank, ...Object.fromEntries(Object.entries(saved).filter(([k, v]) => k in blank && typeof v === 'string')), skip: 0};
  let data = null, timer = 0, datesOpen = !!(filter.from || filter.to);

  const query = () => new URLSearchParams(Object.entries({...filter, skip: filter.skip ? String(filter.skip) : ''}).filter(([, v]) => v !== '' && v !== undefined));

  async function load() {
    try { localStorage.setItem('prospects-filter', JSON.stringify({...filter, skip: undefined})); } catch { /* not kept */ }
    const answer = await api('GET', 'prospects?' + query());
    $('#loading').hidden = true;
    page.hidden = false;
    if (!$('#rows')) shell(answer);
    if (!answer.result.ok) {
      $('#rows').innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3></div>`;
      $('#cards').innerHTML = '';
      $('#count').textContent = '';
      $('#pager').hidden = true;
      return;
    }
    data = answer;
    render();
  }

  const options = (list, current, all) => `<option value="">${esc(all)}</option>${(list || []).map(c =>
    `<option value="${esc(c.code)}"${c.code === current ? ' selected' : ''}>${esc(c.label)}</option>`).join('')}`;

  function shell(answer) {
    page.innerHTML = `
      <div class="page-heading companies-heading"><div class="companies-title"><h1>Prospects</h1>
        <p class="pros-note">From MyCRM; prospects are created and changed in MyCRM.</p></div></div>
      <div class="companies-filters pros-filters" role="search">
        <div class="companies-filter-row">
          <label class="search-box"><span data-icon="search"></span><span class="sr-only">Search prospects</span>
            <input id="q" type="search" placeholder="Company or remarks" value="${esc(filter.q)}" aria-label="Search prospects"></label>
          <div class="tabs pros-status" role="group" aria-label="Status">${[['open', 'Open'], ['closed', 'Closed'], ['cancelled', 'Cancelled'], ['all', 'All']].map(([v, l]) =>
            `<button type="button" class="tab" data-status="${v}" aria-pressed="false">${l}</button>`).join('')}</div>
        </div>
        <div class="companies-filter-row pros-more">
          <label class="filter-capsule"><span>Stage</span><select id="stage">${options(answer.stages, filter.stage, 'All stages')}</select></label>
          <label class="filter-capsule"><span>Type</span><select id="type">${options(answer.types, filter.type, 'All types')}</select></label>
          <label class="filter-capsule"><span>Owner</span><select id="owner"><option value="me">Me</option><option value="team">My team</option>${(answer.team || []).map(p =>
            `<option value="${esc(p.login)}">${esc(p.name)}</option>`).join('')}</select></label>
          <button type="button" class="filter-capsule pros-dates-button" id="dates-button" aria-expanded="${datesOpen}" aria-controls="dates"><span data-icon="calendar"></span>Estimated order</button>
          <span class="pros-dates" id="dates"${datesOpen ? '' : ' hidden'}>
            <label class="filter-capsule"><span>From</span><input type="date" id="from" value="${esc(filter.from)}"></label>
            <label class="filter-capsule"><span>To</span><input type="date" id="to" value="${esc(filter.to)}"></label></span>
          <button type="button" class="text-button" id="reset">Reset</button>
          <p class="companies-show-note pros-count" id="count"></p>
        </div>
      </div>
      <section aria-label="Prospects" class="companies-list pros-list"><div id="rows"></div><div id="cards" class="file-cards"></div><div class="pager" id="pager" hidden></div></section>`;
    $('#owner').value = [...$('#owner').options].some(o => o.value === filter.owner) ? filter.owner : 'team';
    filter.owner = $('#owner').value;
    drawIcons(page);
    $('#q').addEventListener('input', event => { filter.q = event.target.value; filter.skip = 0; clearTimeout(timer); timer = setTimeout(load, 350); });
    ['stage', 'type', 'owner', 'from', 'to'].forEach(name =>
      $('#' + name).addEventListener('change', event => { filter[name] = event.target.value; filter.skip = 0; load(); }));
    $('#dates-button').addEventListener('click', () => { datesOpen = !datesOpen; $('#dates').hidden = !datesOpen; $('#dates-button').setAttribute('aria-expanded', String(datesOpen)); });
    $('#reset').addEventListener('click', () => { Object.assign(filter, blank, {skip: 0}); datesOpen = false; page.innerHTML = ''; load(); });
  }

  const statusNames = {open: 'open', closed: 'closed', cancelled: 'cancelled', all: 'every status'};
  // The stage as a soft tag: won teal, lost coral, the others grey.
  const stageTag = r => r.stage ? `<span class="pros-stage${r.stageCode === 'Close' ? ' won' : r.stageCode === 'CloseLost' ? ' lost' : ''}">${esc(r.stage)}</span>` : '<span class="muted">–</span>';
  const state = r => r.isCancelled ? ' · Cancelled' : r.isClosed ? ' · Closed' : '';
  const ownerName = r => r.ownerLogin && r.ownerLogin.toLowerCase() === (data.me || '').toLowerCase() ? 'Me' : r.owner;
  const href = r => `company.aspx?id=${r.companyId}#prospects`;

  function render() {
    $$('[data-status]', page).forEach(b => b.setAttribute('aria-pressed', String(b.dataset.status === filter.status)));
    const who = filter.owner === 'me' ? 'mine' : filter.owner === 'team' ? 'me and my team' : ($('#owner').selectedOptions[0] || {}).textContent || '';
    $('#count').innerHTML = `<b>${data.total.toLocaleString('en-SG')} prospect${data.total === 1 ? '' : 's'}</b> · ${esc(statusNames[filter.status] || '')} · ${esc(who)} · newest first`;
    $('#reset').hidden = Object.keys(blank).every(k => filter[k] === blank[k]);
    const rows = data.rows;
    $('#rows').innerHTML = rows.length ? `<table class="data companies-table pros-table"><thead><tr><th>Company</th><th>Remarks</th><th>Stage</th><th>Type</th>
        <th class="num">Forecast</th><th class="pros-date">Estimated order</th><th>Owner</th><th class="pros-date">Updated</th></tr></thead><tbody>
      ${rows.map(r => `<tr data-href="${href(r)}"${r.isClosed || r.isCancelled ? ' class="closed"' : ''}><td class="co-name"><a href="${href(r)}">${esc(r.company || 'Company ' + r.companyId)}</a></td>
        <td>${esc(r.remarks) || '<span class="muted">–</span>'}${state(r) ? `<span class="muted">${esc(state(r))}</span>` : ''}</td><td>${stageTag(r)}</td><td class="nowrap">${esc(r.type) || '<span class="muted">–</span>'}</td>
        <td class="num">${r.forecast ? number(r.forecast) : '<span class="muted">–</span>'}</td><td class="nowrap">${esc(date(r.estimatedOrder)) || '<span class="muted">–</span>'}</td>
        <td>${esc(ownerName(r))}</td><td class="nowrap">${esc(date(r.updated))}</td></tr>`).join('')}</tbody></table>`
      : `<div class="empty"><h3>No prospects</h3><p class="muted">No prospects match. Try another stage, type or owner, or choose All.</p></div>`;
    $('#cards').innerHTML = rows.map(r => `<a class="file-card" href="${href(r)}"><span class="file-card-body"><span class="file-card-top"><b>${esc(r.company || 'Company ' + r.companyId)}</b></span>
        <span class="file-card-line">${esc(r.remarks)}${esc(state(r))}</span>
        <span class="file-card-line">${[r.type, r.forecast ? number(r.forecast) : '', r.estimatedOrder ? 'order ' + date(r.estimatedOrder) : '', ownerName(r)].filter(Boolean).map(esc).join(' · ')}</span></span>
        ${r.stage ? stageTag(r) : ''}</a>`).join('');
    drawIcons($('#rows'));
    pager($('#pager'), data.total, data.skip, data.take);
  }

  page.addEventListener('click', event => {
    const status = event.target.closest('[data-status]');
    if (status) { filter.status = status.dataset.status; filter.skip = 0; load(); return; }
    const move = event.target.closest('[data-page]');
    if (move && data) { filter.skip = Math.max(0, data.skip + Number(move.dataset.page) * data.take); load(); window.scrollTo({top: 0}); return; }
    const row = event.target.closest('tr[data-href]');
    if (!row || event.target.closest('a')) return;
    if (event.ctrlKey || event.metaKey) window.open(row.dataset.href, '_blank'); else location.href = row.dataset.href;
  });

  load();
})();
