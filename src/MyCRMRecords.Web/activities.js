(() => {
  'use strict';
  // activities.aspx: MyCRM's sales activities of the person and their team (MyCRM's Activity page), as a list by day or
  // a month's calendar. The search and filters apply as they are typed or chosen and are kept for next time. New activity
  // and each activity open the activity panel (activityform.js). On a phone the list is a card per activity.
  const {$, $$, esc, api, drawIcons, pager, param} = window.WH;
  const page = $('#page');
  const blank = {when: 'upcoming', q: '', type: '', owner: 'team', view: 'list'};
  const saved = (() => { try { return JSON.parse(localStorage.getItem('activities-filter') || '{}'); } catch { return {}; } })();
  const filter = {...blank, ...Object.fromEntries(Object.entries(saved).filter(([k]) => k in blank)), skip: 0};
  if (param('view') === 'calendar') filter.view = 'calendar';
  const now = new Date();
  let month = /^\d{4}-\d{2}$/.test(param('month') || '') ? param('month') : `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`;
  let data = null, timer = 0;
  const months = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
  const wd = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
  const mo = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  const parse = s => { const [y, m, d] = s.split('-').map(Number); return new Date(y, m - 1, d); };
  const dayLabel = s => { const d = parse(s); return `${wd[d.getDay()]} ${d.getDate()} ${mo[d.getMonth()]}`; };

  function monthRange() {
    const [y, m] = month.split('-').map(Number);
    const first = new Date(y, m - 1, 1), last = new Date(y, m, 0);
    // Weeks from Monday: the grid starts on the Monday on or before the 1st and ends on the Sunday on or after the last day.
    const start = new Date(first); start.setDate(first.getDate() - ((first.getDay() + 6) % 7));
    const end = new Date(last); end.setDate(last.getDate() + (7 - ((last.getDay() + 6) % 7) - 1));
    return {first, last, start, end};
  }

  const query = () => {
    const q = {q: filter.q, type: filter.type, owner: filter.owner};
    if (filter.view === 'calendar') { const r = monthRange(); q.from = iso(r.start); q.to = iso(r.end); }
    else { q.when = filter.when; if (filter.skip) q.skip = String(filter.skip); }
    return new URLSearchParams(Object.entries(q).filter(([, v]) => v !== '' && v !== undefined));
  };

  async function load() {
    try { localStorage.setItem('activities-filter', JSON.stringify({when: filter.when, q: filter.q, type: filter.type, owner: filter.owner, view: filter.view})); } catch { /* not kept */ }
    const answer = await api('GET', 'activities?' + query());
    $('#loading').hidden = true;
    page.hidden = false;
    if (!$('#rows')) shell(answer);
    if (!answer.result.ok) {
      $('#rows').innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3></div>`;
      $('#cards').innerHTML = '';
      $('#count').textContent = '';
      return;
    }
    data = answer;
    render();
  }

  function shell(answer) {
    const types = answer.types || [];
    const team = answer.team || [];
    page.innerHTML = `
      <div class="page-heading companies-heading"><div class="companies-title"><h1>Activities</h1><p id="count"></p></div>
        <div class="heading-actions">
          <span class="action-capsule"><a class="capsule-button" id="download" href="#"><span data-icon="download"></span><span class="action-label">Download Excel</span></a></span>
          ${answer.canCreate === false ? '' : '<button type="button" class="button primary phone-fab" id="new" title="New activity"><span data-icon="plus"></span><span class="action-label">New activity</span></button>'}</div></div>
      <div class="companies-filters act-filters" role="search">
        <div class="companies-filter-row">
          <label class="search-box"><span data-icon="search"></span><span class="sr-only">Search activities</span>
            <input id="q" type="search" placeholder="Company, remarks, action or type" value="${esc(filter.q)}" aria-label="Search activities"></label>
          <div class="tabs act-when" role="group" aria-label="When">${[['upcoming', 'Upcoming'], ['today', 'Today'], ['past', 'Past'], ['all', 'All']].map(([v, l]) =>
            `<button type="button" class="tab" data-when="${v}" aria-pressed="false">${l}</button>`).join('')}</div>
          <label class="filter-capsule act-wide"><span>Type</span><select id="type"><option value="">All types</option>${types.map(t =>
            `<option value="${esc(t.code)}"${filter.type === t.code ? ' selected' : ''}>${esc(t.label)}</option>`).join('')}</select></label>
          <label class="filter-capsule act-wide"><span>Owner</span><select id="owner"><option value="me">Me</option><option value="team">My team</option>${team.map(p =>
            `<option value="${esc(p.login)}">${esc(p.name)}</option>`).join('')}</select></label>
          <div class="tabs act-view" role="group" aria-label="View">${[['list', 'List'], ['calendar', 'Calendar']].map(([v, l]) =>
            `<button type="button" class="tab" data-view="${v}" aria-pressed="false">${l}</button>`).join('')}</div>
        </div>
      </div>
      <section aria-label="Activities" class="companies-list act-list"><div id="rows"></div><div id="cards" class="file-cards"></div><div class="pager" id="pager" hidden></div></section>`;
    $('#owner').value = [...$('#owner').options].some(o => o.value === filter.owner) ? filter.owner : 'team';
    filter.owner = $('#owner').value;
    drawIcons(page);
    $('#q').addEventListener('input', event => { filter.q = event.target.value; filter.skip = 0; clearTimeout(timer); timer = setTimeout(load, 350); });
    $('#type').addEventListener('change', event => { filter.type = event.target.value; filter.skip = 0; load(); });
    $('#owner').addEventListener('change', event => { filter.owner = event.target.value; filter.skip = 0; load(); });
    $('#new')?.addEventListener('click', () => window.ACT.create(null, load));
    $('#download').addEventListener('click', event => {
      event.preventDefault();
      const q = query();
      if (filter.view !== 'calendar') q.delete('skip');
      location.href = 'file.ashx/activities?' + q;
    });
  }

  const ownerNames = {me: 'mine', team: 'me and my team'};
  const scopeNames = {upcoming: 'today and later', today: 'today', past: 'before today, newest first', all: 'all dates, newest first'};

  function render() {
    $$('[data-when]', page).forEach(b => b.setAttribute('aria-pressed', String(b.dataset.when === filter.when)));
    $$('[data-view]', page).forEach(b => b.setAttribute('aria-pressed', String(b.dataset.view === filter.view)));
    $('.act-when').hidden = filter.view === 'calendar';
    const who = ownerNames[filter.owner] || ($('#owner').selectedOptions[0] || {}).textContent || '';
    page.classList.toggle('act-calendar-view', filter.view === 'calendar');
    if (filter.view === 'calendar') { calendar(who); return; }
    $('#count').innerHTML = `<b>${data.total.toLocaleString('en-SG')} activit${data.total === 1 ? 'y' : 'ies'}</b> · ${esc(scopeNames[filter.when])} · ${esc(who)}`;
    const rows = data.rows;
    const groups = [];
    for (const r of rows) {
      const last = groups[groups.length - 1];
      if (last && last.date === r.date) last.rows.push(r); else groups.push({date: r.date, rows: [r]});
    }
    const types = r => r.types.length ? `<span class="act-type">${esc(r.types[0])}</span>${r.types.length > 1 ? `<span class="muted small"> +${r.types.length - 1}</span>` : ''}` : '';
    $('#rows').innerHTML = rows.length ? `<table class="data companies-table act-table"><thead><tr><th class="act-time">Time</th><th class="act-types">Type</th><th>Remarks · action</th>
        <th class="act-company">Company</th><th class="act-contacts">Contacts</th><th class="act-owner-col">Owner</th><th class="act-status-col">Status</th></tr></thead>
      ${groups.map(g => `<tbody><tr class="act-day${g.date === data.today ? ' today' : ''}"><th colspan="7" scope="rowgroup">${g.date === data.today ? 'Today · ' : ''}${esc(g.date ? dayLabel(g.date) + ' ' + g.date.slice(0, 4) : 'No date')}</th></tr>
        ${g.rows.map(r => `<tr data-open="${r.id}"><td class="act-time">${esc(r.start)}${r.end ? '–' + esc(r.end) : ''}</td><td>${types(r)}</td>
          <td><button type="button" class="act-remarks" data-open="${r.id}">${esc(r.remarks || r.action)}</button><span class="muted"> · ${esc(r.action)}</span></td>
          <td class="co-name">${r.companyId ? `<a href="company.aspx?id=${r.companyId}#activities">${esc(r.company)}</a>` : esc(r.company)}</td>
          <td>${esc(r.contacts.join(', ')) || '<span class="muted">–</span>'}</td><td>${r.ownerLogin.toLowerCase() === data.me.toLowerCase() ? 'Me' : esc(r.owner)}</td>
          <td>${window.ACT.statusTag(r.status)}</td></tr>`).join('')}</tbody>`).join('')}</table>`
      : `<div class="empty"><h3>No activities</h3><p class="muted">No activities match. Try another type or owner, or choose All.</p></div>`;
    $('#cards').innerHTML = groups.map(g => `<div class="act-card-day">${esc(g.date ? dayLabel(g.date) : 'No date')}</div>${g.rows.map(r => `<button type="button" class="file-card act-card-row" data-open="${r.id}">
        <span class="file-card-body"><span class="file-card-top"><b>${esc(r.remarks || r.action)}</b></span>
          <span class="file-card-line">${esc(r.start)}${r.end ? '–' + esc(r.end) : ''} · ${esc(r.action)}${r.types.length ? ' · ' + esc(r.types[0]) : ''}</span>
          <span class="file-card-line strong">${esc(r.company)}</span></span>${window.ACT.statusTag(r.status)}</button>`).join('')}`).join('');
    drawIcons($('#rows'));
    drawIcons($('#cards'));
    pager($('#pager'), data.total, data.skip, data.take);
  }

  function calendar(who) {
    const {first, start, end} = monthRange();
    const byDay = {};
    for (const r of data.rows) (byDay[r.date] = byDay[r.date] || []).push(r);
    const inMonth = data.rows.filter(r => r.date.slice(0, 7) === month).length;
    $('#count').innerHTML = `<b>${inMonth} activit${inMonth === 1 ? 'y' : 'ies'} in ${months[first.getMonth()]}</b> · ${esc(who)}`;
    const cells = [];
    for (let d = new Date(start); d <= end; d.setDate(d.getDate() + 1)) cells.push(new Date(d));
    const chip = r => `<button type="button" class="act-chip-cal ${window.ACT.statusClass(r.status)}" data-open="${r.id}" title="${esc(r.start + ' ' + (r.remarks || r.action) + ', ' + r.company + ', ' + r.status)}"><b>${esc(r.start)}</b> ${esc(r.remarks || r.action)}</button>`;
    $('#rows').innerHTML = `<div class="act-cal"><div class="act-cal-head">
        <div class="act-cal-nav"><button type="button" class="icon-button small" data-month="-1" aria-label="Previous month"><span data-icon="arrow-left"></span></button>
          <h2>${months[first.getMonth()]} ${first.getFullYear()}</h2><button type="button" class="icon-button small" data-month="1" aria-label="Next month"><span data-icon="arrow-right"></span></button>
          <button type="button" class="button secondary small-btn" data-month="0">Today</button></div>
        <div class="act-legend"><span><i class="done"></i>Completed</span><span><i class="pending"></i>Pending</span><span><i class="late"></i>Not completed</span></div></div>
      <div class="act-grid" role="grid" aria-label="${months[first.getMonth()]} ${first.getFullYear()}">${['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'].map(w => `<span class="act-wd" role="columnheader">${w}</span>`).join('')}
        ${cells.map(d => {
          const key = iso(d), list = (byDay[key] || []).slice().sort((x, y) => x.start < y.start ? -1 : 1);
          const out = d.getMonth() !== first.getMonth(), isToday = key === data.today;
          return `<div class="act-cell${out ? ' out' : ''}${isToday ? ' today' : ''}" role="gridcell" aria-label="${esc(dayLabel(key))}, ${list.length} activit${list.length === 1 ? 'y' : 'ies'}">
            <span class="act-num">${d.getDate() === 1 ? d.getDate() + ' ' + mo[d.getMonth()] : d.getDate()}</span>${list.slice(0, 3).map(chip).join('')}
            ${list.length > 3 ? `<button type="button" class="text-button act-more" data-day="${key}">+${list.length - 3} more</button>` : ''}</div>`;
        }).join('')}</div></div>`;
    // On a phone: the month's days with activities, as a list.
    const days = Object.keys(byDay).filter(k => k.slice(0, 7) === month).sort();
    $('#cards').innerHTML = days.length ? days.map(k => `<div class="act-card-day">${esc(dayLabel(k))}</div>${byDay[k].map(r => `<button type="button" class="file-card act-card-row" data-open="${r.id}">
        <span class="file-card-body"><span class="file-card-top"><b>${esc(r.start)} ${esc(r.remarks || r.action)}</b></span><span class="file-card-line">${esc(r.company)}</span></span>${window.ACT.statusTag(r.status)}</button>`).join('')}`).join('')
      : '<div class="empty"><p class="muted">No activities this month.</p></div>';
    $('#pager').hidden = true;
    drawIcons($('#rows'));
    drawIcons($('#cards'));
  }

  function showDay(key) {
    const list = data.rows.filter(r => r.date === key);
    const el = document.createElement('dialog');
    el.className = 'act-panel';
    el.innerHTML = `<div class="act-panel-in"><div class="act-panel-head"><button type="button" class="icon-button" data-close aria-label="Close"><span data-icon="close"></span></button>
      <div class="act-panel-title"><h2>${esc(dayLabel(key))}</h2><span class="muted small">${list.length} activities</span></div><div class="act-panel-action"></div></div>
      <div class="act-panel-body">${list.map(r => `<button type="button" class="file-card act-card-row" data-open="${r.id}"><span class="file-card-body"><span class="file-card-top"><b>${esc(r.start)} ${esc(r.remarks || r.action)}</b></span>
        <span class="file-card-line">${esc(r.company)} · ${esc(r.owner)}</span></span>${window.ACT.statusTag(r.status)}</button>`).join('')}</div></div>`;
    document.body.append(el);
    drawIcons(el);
    $('[data-close]', el).addEventListener('click', () => el.close());
    el.addEventListener('close', () => el.remove());
    el.addEventListener('click', event => { const open = event.target.closest('[data-open]'); if (open) { el.close(); window.ACT.open(Number(open.dataset.open), load); } });
    el.showModal();
  }

  page.addEventListener('click', event => {
    const when = event.target.closest('[data-when]');
    if (when) { filter.when = when.dataset.when; filter.skip = 0; load(); return; }
    const view = event.target.closest('[data-view]');
    if (view) { filter.view = view.dataset.view; filter.skip = 0; load(); return; }
    const step = event.target.closest('[data-month]');
    if (step) {
      const [y, m] = month.split('-').map(Number);
      const d = Number(step.dataset.month) === 0 ? new Date() : new Date(y, m - 1 + Number(step.dataset.month), 1);
      month = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
      load();
      return;
    }
    const day = event.target.closest('[data-day]');
    if (day) { showDay(day.dataset.day); return; }
    if (event.target.closest('a')) return;
    const open = event.target.closest('[data-open]');
    if (open) { window.ACT.open(Number(open.dataset.open), load); return; }
    const move = event.target.closest('[data-page]');
    if (move && data) { filter.skip = Math.max(0, data.skip + Number(move.dataset.page) * data.take); load(); window.scrollTo({top: 0}); }
  });

  load();
})();
