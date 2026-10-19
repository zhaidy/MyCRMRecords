(() => {
  'use strict';
  // default.aspx: the feedbacks (MyCRM's Feedbacks): a button per status with how many there are (and All), a search,
  // the division filter, and the list, newest first, with the row's button first. Feedback Admins see every feedback,
  // and may sign in as someone else (MyCRM's user switch).
  const {$, $$, esc, api, toast, date, drawIcons, pager} = window.WH;
  const page = $('#page');
  const TAKE = 50;
  const STATUS_CLASS = {New: 'approved', PendingHR: 'pending', Acknowledged: 'acknowledgement', InvestigationResolution: 'finance', ConfirmedSatisfaction: 'completed', Closed: 'off'};
  const STATUS_COLOR = {New: '#4b6bd8', PendingHR: '#c98a00', Acknowledged: '#1a9a86', InvestigationResolution: '#5b4fd1', ConfirmedSatisfaction: '#2e9b62', Closed: '#7a8299'};
  const saved = (() => { try { return JSON.parse(localStorage.getItem('feedbacks-filter') || '{}'); } catch { return {}; } })();
  const filter = {q: saved.q || '', status: saved.status || '', division: saved.division || ''};
  let data = null, session = null, sort = {key: 'id', desc: true}, skip = 0, timer = 0;

  async function load() {
    try { localStorage.setItem('feedbacks-filter', JSON.stringify(filter)); } catch { /* not kept */ }
    const answer = await api('GET', 'feedbacks?' + new URLSearchParams(filter));
    $('#loading').hidden = true;
    page.hidden = false;
    if (!answer.result.ok) {
      if (!$('#rows')) page.innerHTML = '<div id="rows"></div>';
      $('#rows').innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3></div>`;
      return;
    }
    data = answer;
    skip = 0;
    render();
  }

  function shell() {
    const seesAll = data.can.seesAll;
    page.innerHTML = `
      <div class="page-heading"><div><h1>Feedbacks</h1><p>${seesAll ? 'Every feedback.' : 'The feedbacks you created, are in charge of, have a task in, or are copied on.'}</p></div>
        <div class="heading-actions">
          ${session?.can.actAs && !session.impersonating ? '<button class="button secondary" id="act-as"><span data-icon="users"></span>Sign in as…</button>' : ''}
          <a class="button primary" href="feedback.aspx"><span data-icon="plus"></span>New feedback</a></div></div>
      <div class="status-buttons" id="statuses" role="group" aria-label="Status" style="margin-bottom:14px"></div>
      <section class="card">
        <div class="filters">
          <label class="search-box"><span data-icon="search"></span><input id="q" type="search" placeholder="Number, company, contact, status, division or a person" value="${esc(filter.q)}"></label>
          <label class="field"><span>Division</span><select id="division"></select></label>
        </div>
        <p class="muted small" id="count" style="margin:10px 0 0"></p>
        <div id="rows"></div><div class="pager" id="pager" hidden></div>
      </section>`;
    drawIcons(page);
    $('#q').addEventListener('input', event => { clearTimeout(timer); filter.q = event.target.value; timer = setTimeout(load, 400); });
    $('#division').addEventListener('change', event => { filter.division = event.target.value; load(); });
    $('#act-as')?.addEventListener('click', actAs);
  }

  const COLUMNS = [['id', 'No.'], ['companyName', 'Feedback'], ['status', 'Status'], ['inCharge', 'In charge'], ['createdOn', 'Created'], ['dateReceived', 'Received']];

  function render() {
    if (!$('#count')) shell();
    const statuses = [{code: '', label: 'All', count: data.all}, ...data.statuses.map(s => ({...s, count: data.counts[s.code] || 0}))];
    $('#statuses').innerHTML = statuses.map(s => `<button type="button" data-status="${esc(s.code)}" aria-pressed="${filter.status === s.code}" style="--tag:${STATUS_COLOR[s.code] || '#3346c8'}">${esc(s.label)} <b>${s.count}</b></button>`).join('');
    const divisions = data.divisions.includes(filter.division) || !filter.division ? data.divisions : [filter.division, ...data.divisions];
    $('#division').innerHTML = `<option value="">All</option>${divisions.map(d => `<option${d === filter.division ? ' selected' : ''}>${esc(d)}</option>`).join('')}`;
    const rows = [...data.rows].sort((a, b) => {
      const x = a[sort.key] ?? '', y = b[sort.key] ?? '';
      const order = typeof x === 'number' && typeof y === 'number' ? x - y : String(x).localeCompare(String(y), undefined, {numeric: true, sensitivity: 'base'});
      return sort.desc ? -order : order;
    });
    const shown = rows.slice(skip, skip + TAKE);
    $('#count').textContent = `${rows.length} feedback${rows.length === 1 ? '' : 's'}.`;
    $('#rows').innerHTML = rows.length ? `<div class="table-wrap" style="margin-top:10px"><table class="data"><thead><tr>
        ${COLUMNS.map(([key, label]) => `<th${sort.key === key ? ` aria-sort="${sort.desc ? 'descending' : 'ascending'}"` : ''}><button data-sort="${key}">${esc(label)}</button></th>`).join('')}</tr></thead><tbody>
      ${shown.map(r => `<tr>
        <td class="nowrap"><a class="button secondary small-btn" href="feedback.aspx?id=${r.id}" title="Open feedback ${r.id}"><span data-icon="edit"></span>${r.id}</a></td>
        <td class="wide-text"><b>${esc(r.companyName || r.group || 'Feedback')}</b>${r.contactName ? ` · ${esc(r.contactName)}` : ''}
          <span class="sub">${esc([r.group, r.type].filter(Boolean).join(' · '))}</span></td>
        <td><span class="status ${STATUS_CLASS[r.status] || ''}">${esc(r.statusLabel)}</span></td>
        <td>${esc(r.inCharge)}<span class="sub">${esc(r.division)}</span></td>
        <td class="small">${esc(r.createdBy)}<span class="sub">${esc(date(r.createdOn))}</span></td>
        <td class="nowrap small">${esc(date(r.dateReceived))}</td></tr>`).join('')}
      </tbody></table></div>`
      : `<div class="empty" style="margin-top:10px"><h3>Nothing to show</h3><p class="muted">${filter.q || filter.status || filter.division ? 'Nothing matches the search and filters.' : 'No feedbacks for you yet.'}</p></div>`;
    drawIcons($('#rows'));
    pager($('#pager'), rows.length, skip, TAKE);
  }

  /** Sign in as someone else (Feedback Admins): MyCRMRecords then works as that person; everything is recorded with both names. */
  async function actAs() {
    const answer = await api('GET', 'people');
    if (!answer.result.ok) { toast(answer.result.message, true); return; }
    const dialog = document.createElement('dialog');
    dialog.innerHTML = `<form method="dialog" class="modal"><h2>Sign in as someone else</h2>
      <p class="muted">MyCRMRecords works as that person until you choose Return in the bar at the top. Everything you do is recorded as you on behalf of them.</p>
      <label class="field"><span>Find</span><input id="act-q" type="search" placeholder="Name, login or email" autocomplete="off"></label>
      <div class="people-list" id="act-people" style="max-height:320px;overflow:auto;margin-top:10px"></div>
      <div class="modal-footer"><button type="button" class="button secondary" value="cancel">Cancel</button></div></form>`;
    document.body.append(dialog);
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

  page.addEventListener('click', event => {
    const head = event.target.closest('[data-sort]');
    if (head) { const key = head.dataset.sort; sort = {key, desc: sort.key === key ? !sort.desc : key === 'id'}; render(); return; }
    const move = event.target.closest('[data-page]');
    if (move) { skip = Math.max(0, skip + Number(move.dataset.page) * TAKE); render(); window.scrollTo({top: 0}); return; }
    const status = event.target.closest('[data-status]');
    if (status) { filter.status = status.dataset.status; load(); }
  });

  try {
    const flash = sessionStorage.getItem('flash');
    if (flash) { sessionStorage.removeItem('flash'); setTimeout(() => toast(flash), 300); }
  } catch { /* nothing to show */ }
  (async () => {
    const answer = await api('GET', 'session');
    session = answer.result.ok ? {can: answer.can, impersonating: !!$('.impersonation-bar:not(.warning-bar)')} : null;
    load();
  })();
})();
