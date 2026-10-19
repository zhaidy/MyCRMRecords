(() => {
  'use strict';
  // lists.aspx (Feedback Admins): MyCRM's Admin screen. The divisions with their person in charge, the feedback
  // categories and mediums, and the KPI dates of each feedback (MyCRM's BackOffice). Each row is changed in a dialog
  // (Edit first in the row); taking one away asks first and keeps it (never erased).
  const {$, $$, esc, api, toast, ask, date, dateTime, dateInput, showErrors, drawIcons, pager} = window.WH;
  const page = $('#page');
  const TABS = [['divisions', 'Divisions'], ['categories', 'Feedback categories'], ['mediums', 'Feedback mediums'], ['kpi', 'KPI dates']];
  let tab = TABS.some(([t]) => t === location.hash.slice(1)) ? location.hash.slice(1) : 'divisions';
  let data = null, kpi = {q: '', skip: 0, answer: null}, timer = 0;

  async function load() {
    const answer = await api('GET', 'setup');
    $('#loading').hidden = true;
    page.hidden = false;
    if (!answer.result.ok) { page.innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3></div>`; return; }
    data = answer;
    render();
  }

  function render() {
    page.innerHTML = `<div class="page-heading"><div><h1>Lists</h1><p>What the feedbacks are chosen from, and their KPI dates.</p></div></div>
      <div class="page-tabs" role="tablist">${TABS.map(([t, label]) => `<button class="page-tab" role="tab" data-tab="${t}" aria-selected="${t === tab}">${esc(label)}</button>`).join('')}</div>
      <div id="tab"></div>`;
    history.replaceState(null, '', '#' + tab);
    ({divisions, categories: () => choices('categories', 'category'), mediums: () => choices('mediums', 'medium'), kpi: kpiTab})[tab]();
    drawIcons(page);
  }

  function divisions() {
    $('#tab').innerHTML = `<section class="card"><div class="card-heading"><h2>Divisions</h2><button class="button primary" data-edit-division="0"><span data-icon="plus"></span>Add a division</button></div>
      <p class="muted">Choosing a division on a feedback makes its person in charge the feedback's.</p>
      ${data.divisions.length ? `<div class="table-wrap" style="margin-top:10px"><table class="data"><thead><tr><th></th><th>Division</th><th>Person in charge</th></tr></thead><tbody>
        ${data.divisions.map(d => `<tr><td class="nowrap"><div class="row-first"><button class="button secondary small-btn" data-edit-division="${d.id}"><span data-icon="edit"></span>Edit</button>
          <button class="icon-button small" data-remove="divisions/${d.id}" data-what="${esc(d.name)}" title="Take it away" aria-label="Take ${esc(d.name)} away"><span data-icon="trash"></span></button></div></td>
          <td><b>${esc(d.name)}</b></td><td>${esc(d.personName)}</td></tr>`).join('')}</tbody></table></div>` : '<p class="muted">No divisions yet.</p>'}</section>`;
  }

  function choices(kind, what) {
    const rows = data[kind];
    $('#tab').innerHTML = `<section class="card"><div class="card-heading"><h2>Feedback ${what === 'category' ? 'categories' : 'mediums'}</h2><button class="button primary" data-edit-choice="${kind}" data-id="0"><span data-icon="plus"></span>Add a ${what}</button></div>
      <p class="muted">Only the active ones are offered on new feedbacks; the feedbacks keep theirs.</p>
      ${rows.length ? `<div class="table-wrap" style="margin-top:10px"><table class="data"><thead><tr><th></th><th>${what === 'category' ? 'Category' : 'Medium'}</th><th class="num">Order</th><th>Active</th></tr></thead><tbody>
        ${rows.map(c => `<tr class="${c.isActive ? '' : 'inactive'}"><td class="nowrap"><div class="row-first"><button class="button secondary small-btn" data-edit-choice="${kind}" data-id="${c.id}"><span data-icon="edit"></span>Edit</button>
          <button class="icon-button small" data-remove="${kind}/${c.id}" data-what="${esc(c.label)}" title="Take it away" aria-label="Take ${esc(c.label)} away"><span data-icon="trash"></span></button></div></td>
          <td><b>${esc(c.label)}</b></td><td class="num">${c.sortOrder}</td><td>${c.isActive ? '<span class="yes">Yes</span>' : '<span class="no">No</span>'}</td></tr>`).join('')}</tbody></table></div>` : '<p class="muted">None yet.</p>'}</section>`;
  }

  async function kpiTab() {
    $('#tab').innerHTML = `<section class="card"><h2>KPI dates</h2>
      <p class="muted">Each feedback's dates the timings are worked out from. Saving a row works its timings out again from the dates (MyCRM's BackOffice); nothing else of the feedback changes.</p>
      <div class="toolbar" style="margin-top:10px"><label class="search-box" style="flex:1"><span data-icon="search"></span><input id="kpi-q" type="search" placeholder="Feedback number or company" value="${esc(kpi.q)}"></label></div>
      <div id="kpi-rows"><div class="loading">Loading…</div></div><div class="pager" id="kpi-pager" hidden></div></section>`;
    drawIcons($('#tab'));
    $('#kpi-q').addEventListener('input', event => { clearTimeout(timer); kpi.q = event.target.value; kpi.skip = 0; timer = setTimeout(kpiRows, 350); });
    kpiRows();
  }

  async function kpiRows() {
    const answer = await api('GET', 'setup/kpi?' + new URLSearchParams({q: kpi.q, skip: kpi.skip}));
    if (!answer.result.ok) { $('#kpi-rows').innerHTML = `<p class="no">${esc(answer.result.message)}</p>`; return; }
    kpi.answer = answer;
    $('#kpi-rows').innerHTML = answer.rows.length ? `<div class="table-wrap" style="margin-top:10px"><table class="data"><thead><tr><th></th><th>Feedback</th><th>Received</th><th>Acknowledged</th><th>Resolution started</th><th>Completed</th></tr></thead><tbody>
      ${answer.rows.map(r => `<tr><td class="nowrap"><button class="button secondary small-btn" data-edit-kpi="${r.id}"><span data-icon="edit"></span>${r.id}</button></td>
        <td class="wide-text"><b>${esc(r.companyName || 'Feedback')}</b><span class="sub">${esc(r.statusLabel)} · created ${esc(dateTime(r.createdOn))}</span></td>
        <td class="nowrap">${esc(date(r.dateReceived))}<span class="sub">${esc(r.timeReceived)}</span></td>
        <td class="nowrap">${esc(date(r.acknowledgedDate))} ${esc(r.acknowledgedTime)}<span class="sub">${r.acknowledgedHours} hour(s)</span></td>
        <td class="nowrap">${esc(dateTime(r.startedResolutionOn))}<span class="sub">${r.startResolutionDays} business day(s)</span></td>
        <td class="nowrap">${r.isCompleted ? esc(dateTime(r.completedResolutionOn)) : '<span class="muted">Not yet</span>'}<span class="sub">${r.closeCaseDays} business day(s)</span></td></tr>`).join('')}
      </tbody></table></div>` : '<p class="muted" style="margin-top:10px">Nothing matches.</p>';
    drawIcons($('#kpi-rows'));
    pager($('#kpi-pager'), answer.total, answer.skip, answer.take);
  }

  /** A dialog with fields [name, label, type, value, options]; resolves when saved. */
  function dialogOf(title, fields, path, extra = {}) {
    const dialog = document.createElement('dialog');
    dialog.innerHTML = `<form class="modal" novalidate><h2>${esc(title)}</h2><div class="form-grid two">
      ${fields.map(([name, label, type, value, options]) => type === 'check'
        ? `<div class="field"><label class="check"><input type="checkbox" data-field="${name}"${value ? ' checked' : ''}>${esc(label)}</label></div>`
        : type === 'person' ? `<label class="field"><span>${esc(label)}</span><select data-field="${name}"><option value="">Nobody</option>
            ${options.map(p => `<option value="${esc(p.login)}"${p.login.toLowerCase() === String(value || '').toLowerCase() ? ' selected' : ''}>${esc(p.name)}</option>`).join('')}</select><span class="field-error"></span></label>`
        : `<label class="field"><span>${esc(label)}</span><input data-field="${name}" type="${type}" value="${esc(value ?? '')}"><span class="field-error"></span></label>`).join('')}
      </div><div class="notice error" hidden></div>
      <div class="modal-footer"><button type="button" class="button secondary" value="cancel">Cancel</button><button type="submit" class="button primary">Save</button></div></form>`;
    document.body.append(dialog);
    $('button[value=cancel]', dialog).addEventListener('click', () => dialog.close());
    dialog.addEventListener('close', () => dialog.remove());
    $('form', dialog).addEventListener('submit', async event => {
      event.preventDefault();
      const body = {...extra};
      for (const [name, , type] of fields) {
        const input = $(`[data-field="${name}"]`, dialog);
        body[name] = type === 'check' ? input.checked : type === 'number' ? Number(input.value || 0) : input.value || (type.startsWith('date') ? null : '');
      }
      const answer = await api('POST', path, body);
      if (!answer.result.ok) { const n = $('.notice', dialog); n.hidden = false; n.textContent = answer.result.message; showErrors(dialog, answer.result.errors); return; }
      toast(answer.result.message);
      dialog.close();
      if (tab === 'kpi') kpiRows(); else load();
    });
    dialog.showModal();
    $('input,select', dialog)?.focus();
  }

  page.addEventListener('click', async event => {
    const t = event.target.closest('[data-tab]');
    if (t) { tab = t.dataset.tab; render(); return; }
    const move = event.target.closest('[data-page]');
    if (move && kpi.answer) { kpi.skip = Math.max(0, kpi.skip + Number(move.dataset.page) * kpi.answer.take); kpiRows(); return; }
    const division = event.target.closest('[data-edit-division]');
    if (division) {
      const id = Number(division.dataset.editDivision), d = data.divisions.find(x => x.id === id) || {name: '', personInCharge: ''};
      dialogOf(id ? `Division ${d.name}` : 'Add a division', [['name', 'Division', 'text', d.name], ['personInCharge', 'Person in charge', 'person', d.personInCharge, data.people]], `setup/divisions/${id}`);
      return;
    }
    const choice = event.target.closest('[data-edit-choice]');
    if (choice) {
      const kind = choice.dataset.editChoice, id = Number(choice.dataset.id), what = kind === 'categories' ? 'category' : 'medium';
      const c = data[kind].find(x => x.id === id) || {label: '', sortOrder: (data[kind].length ? Math.max(...data[kind].map(x => x.sortOrder)) : 0) + 1, isActive: true};
      dialogOf(id ? c.label : `Add a ${what}`, [['label', what === 'category' ? 'Category' : 'Medium', 'text', c.label], ['sortOrder', 'Order', 'number', c.sortOrder], ['isActive', 'Active', 'check', c.isActive]], `setup/${kind}/${id}`);
      return;
    }
    const edit = event.target.closest('[data-edit-kpi]');
    if (edit) {
      const r = kpi.answer.rows.find(x => x.id === Number(edit.dataset.editKpi));
      dialogOf(`KPI dates of feedback #${r.id}`, [['createdOn', 'Created on', 'datetime-local', (r.createdOn || '').slice(0, 16)], ['dateReceived', 'Date received', 'date', dateInput(r.dateReceived)],
        ['timeReceived', 'Time received', 'time', r.timeReceived], ['acknowledgedDate', 'Acknowledged on', 'date', dateInput(r.acknowledgedDate)],
        ['acknowledgedTime', 'Acknowledged at', 'time', r.acknowledgedTime], ['completedResolutionOn', 'Completed on', 'datetime-local', (r.completedResolutionOn || '').slice(0, 16)]], `setup/kpi/${r.id}`);
      return;
    }
    const remove = event.target.closest('[data-remove]');
    if (remove) {
      if (!await ask({title: `Take ${remove.dataset.what} away?`, text: 'It is no longer offered; the feedbacks that have it keep it (it is kept, never erased).', ok: 'Take away', danger: true})) return;
      const answer = await api('POST', `setup/${remove.dataset.remove}/remove`, {});
      toast(answer.result.message, !answer.result.ok);
      if (answer.result.ok) load();
    }
  });
  load();
})();
