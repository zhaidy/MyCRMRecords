(() => {
  'use strict';
  // feedback.aspx?id=: a feedback (MyCRM's FeedbackDetail), its sections shown by its category and type, and each field
  // editable only when the person may change it (worked out on the server: rights). The tasks are added and changed in
  // a dialog, the files uploaded on the page. Save goes back to the list, as in MyCRM; a new feedback opens once saved,
  // so its files and tasks can be added.
  const {$, $$, esc, api, toast, ask, date, dateTime, dateInput, showErrors, param, drawIcons, upload} = window.WH;
  const page = $('#page');
  const id = Number(param('id') || 0);
  const STATUS_CLASS = {New: 'approved', PendingHR: 'pending', Acknowledged: 'acknowledgement', InvestigationResolution: 'finance', ConfirmedSatisfaction: 'completed', Closed: 'off'};
  const CUSTOMER_GROUPS = ['CustomerComplaint', 'AreaOfImprovement', ''];
  let d = null, lists = null, v = null;

  const sizeOf = bytes => bytes >= 1048576 ? `${(bytes / 1048576).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;
  const personName = login => (lists.people.find(p => p.login.toLowerCase() === String(login || '').toLowerCase()) || {}).name || login || '';
  const labelOf = (list, code) => (list.find(c => c.code === code) || {}).label || '';

  async function load(keepValues = false) {
    const [answer, listsAnswer] = await Promise.all([api('GET', 'feedbacks/' + id), lists ? Promise.resolve(null) : api('GET', 'lists')]);
    if (listsAnswer) lists = listsAnswer;
    $('#loading').hidden = true;
    page.hidden = false;
    if (!answer.result.ok || (lists && !lists.result.ok)) {
      page.innerHTML = `<div class="empty"><h3>${esc(answer.result.ok ? lists.result.message : answer.result.code === 'NOT_FOUND' ? 'That feedback is not there (or was deleted).' : answer.result.message)}</h3>
        <p><a class="button secondary" href="./">Back to the feedbacks</a></p></div>`;
      return;
    }
    d = answer;
    if (!keepValues || !v) v = values(d.feedback);
    render();
  }

  /** The form's values, from the feedback. */
  function values(f) {
    return {
      feedbackGroup: f.feedbackGroup, divisionId: f.divisionId || '', personResponsible: f.personResponsible.login, companyName: f.companyName, contactName: f.contactName,
      companyEmail: f.companyEmail, mediumId: f.mediumId || '', dateReceived: dateInput(f.dateReceived), timeReceived: f.timeReceived, feedbackType: f.feedbackType,
      categoryId: f.categoryId || '', description: f.description, cc: [...f.cc], mightCauseCustomerComplaint: f.mightCauseCustomerComplaint,
      creatorHasSolution: f.creatorHasSolution, solutionDetail: f.solutionDetail, expectedOutcome: f.expectedOutcome, suggestionMeetCriteria: [...f.suggestionMeetCriteria],
      ricohWayValue: [...f.ricohWayValue], hrAccepted: f.hrAccepted || '-', hrRemarks: f.hrRemarks, isAcknowledged: f.isAcknowledged,
      firstLookAtFeedback: f.firstLookAtFeedback, rootCause: f.rootCause, satisfactionLevel: f.satisfactionLevel,
    };
  }

  // ---- fields: editable (an input named data-field) or read-only (the value as text) ----
  const shown = text => text ? `<div class="field-value">${esc(text)}</div>` : '<div class="field-value blank">–</div>';
  function wrap(name, label, inner, {required = false, cls = ''} = {}) {
    return `<label class="field ${cls}"><span class="${required ? 'required' : ''}">${esc(label)}</span>${inner}<span class="field-error"></span></label>`;
  }
  function text(name, label, editable, {required = false, type = 'text', rows = 0, max = 0, cls = '', display = null} = {}) {
    const value = v[name] ?? '';
    if (!editable) return `<div class="field ${cls}"><span>${esc(label)}</span>${shown(display ?? value)}</div>`;
    const attrs = `data-field="${name}" name="${name}"${max ? ` maxlength="${max}"` : ''}${required ? ' required' : ''}`;
    const input = rows ? `<textarea ${attrs} rows="${rows}">${esc(value)}</textarea>` : `<input ${attrs} type="${type}" value="${esc(value)}">`;
    return wrap(name, label, input, {required, cls});
  }
  function select(name, label, editable, options, {required = false, cls = '', blank = 'Choose…'} = {}) {
    const value = String(v[name] ?? '');
    if (!editable) return `<div class="field ${cls}"><span>${esc(label)}</span>${shown((options.find(o => String(o[0]) === value) || [])[1] || '')}</div>`;
    const known = options.some(o => String(o[0]) === value);
    return wrap(name, label, `<select data-field="${name}" name="${name}"${required ? ' required' : ''}>
      ${blank !== null ? `<option value="">${esc(blank)}</option>` : ''}${!known && value ? `<option value="${esc(value)}" selected>${esc(value)}</option>` : ''}
      ${options.map(([code, text]) => `<option value="${esc(code)}"${String(code) === value ? ' selected' : ''}>${esc(text)}</option>`).join('')}</select>`, {required, cls});
  }
  function check(name, label, editable) {
    return `<label class="check"><input type="checkbox" data-field="${name}" name="${name}"${v[name] ? ' checked' : ''}${editable ? '' : ' disabled'}>${esc(label)}</label>`;
  }
  function checks(name, label, editable, choices, required) {
    return `<div class="field wide" data-field="${name}"><span class="${required && editable ? 'required' : ''}">${esc(label)}</span>
      <div class="role-checks" style="display:flex;flex-wrap:wrap;gap:6px 18px">${choices.map(c => `<label class="check"><input type="checkbox" data-multi="${name}" value="${esc(c.value)}"${v[name].includes(c.value) ? ' checked' : ''}${editable ? '' : ' disabled'}>${esc(c.label)}</label>`).join('')}</div>
      <span class="field-error"></span></div>`;
  }
  const peopleOptions = () => `<datalist id="people">${lists.people.map(p => `<option value="${esc(p.name)} (${esc(p.login)})"></option>`).join('')}</datalist>`;
  /** A person: a text box offering everyone MyCRMRecords knows ("Name (RSP\\login)"), and Find someone else (Active Directory). */
  function person(name, label, editable, {required = false, cls = ''} = {}) {
    const login = v[name] || '';
    if (!editable) return `<div class="field ${cls}"><span>${esc(label)}</span>${shown(personName(login))}</div>`;
    const value = login ? `${personName(login)} (${login})` : '';
    return wrap(name, label, `<span style="display:flex;gap:6px"><input data-field="${name}" data-person="${name}" list="people" value="${esc(value)}" placeholder="Type a name" autocomplete="off" style="flex:1">
      ${lists.directory ? `<button type="button" class="icon-button" data-find="${name}" title="Find someone else in Active Directory" aria-label="Find someone else"><span data-icon="search"></span></button>` : ''}</span>`, {required, cls});
  }
  /** The login of what was typed in a person box: "Name (RSP\\login)", a login, or a name or email MyCRMRecords knows; "" when none. */
  function loginOf(typed) {
    const t = String(typed || '').trim();
    if (!t) return '';
    const m = /\(([^()]+)\)\s*$/.exec(t);
    const key = (m ? m[1] : t).toLowerCase();
    const found = lists.people.find(p => p.login.toLowerCase() === key) || lists.people.find(p => p.name.toLowerCase() === t.toLowerCase() || p.email.toLowerCase() === t.toLowerCase());
    return found ? found.login : (m ? m[1] : t);
  }

  function filesOf(kind, mayKeep) {
    const files = d.files.filter(f => f.kind === kind);
    return `<div class="wide"><div class="card-heading" style="margin-top:4px"><h3 style="margin:0;font-size:15px">Files</h3>
        ${mayKeep ? `<label class="button secondary small-btn"><span data-icon="upload"></span>Upload<input type="file" data-upload="${kind}" hidden></label>` : ''}</div>
      ${files.length ? `<ul class="check-lines" style="margin:8px 0 0">${files.map(f => `<li><a href="file.ashx/attachment?id=${f.id}" style="overflow-wrap:anywhere"><span data-icon="paperclip" style="display:inline-block;vertical-align:-3px;margin-right:4px"></span>${esc(f.fileName)}</a>
          <span class="muted small"> · ${esc(sizeOf(f.size))} · ${esc(f.uploadedBy)} · ${esc(dateTime(f.uploadedOn))}</span>
          ${mayKeep ? ` <button type="button" class="text-button danger-text small" data-remove-file="${f.id}">Remove</button>` : ''}</li>`).join('')}</ul>`
        : `<p class="muted small" style="margin:6px 0 0">${d.rights.isNew ? 'Files are added once the feedback is saved.' : 'No files.'}</p>`}</div>`;
  }

  function kpi() {
    const f = d.feedback, k = d.kpi;
    const cell = (value, unit, target) => `<td class="num${value > target ? ' late' : ''}">${value} ${unit}</td>`;
    return `<div class="wide"><div class="table-wrap"><table class="data"><thead><tr><th>KPI</th><th class="num">Acknowledgement</th><th class="num">Start resolution</th><th class="num">Resolution with satisfaction</th></tr></thead><tbody>
      <tr><td><b>Service target</b></td><td class="num">${k.acknowledgeTarget} hour(s)</td><td class="num">${k.startTarget} business day(s)</td><td class="num">${k.closeTarget} business day(s)</td></tr>
      <tr><td><b>Time taken</b></td>${cell(f.acknowledgedHours, 'hour(s)', k.acknowledgeTarget)}${cell(f.startResolutionDays, 'business day(s)', k.startTarget)}${cell(f.closeCaseDays, 'business day(s)', k.closeTarget)}</tr>
      </tbody></table></div>${d.holidays ? '' : '<p class="muted small" style="margin:6px 0 0">The public holidays cannot be read (AdvanceConnector is not set up): only weekends are left out.</p>'}</div>`;
  }

  function tasksOf(type, title, mayAdd) {
    const tasks = d.tasks.filter(t => t.taskType === type);
    return `<div class="wide"><div class="card-heading" style="margin-top:4px"><h3 style="margin:0;font-size:15px">${esc(title)}</h3>
        ${mayAdd ? `<button type="button" class="button secondary small-btn" data-add-task="${type}"><span data-icon="plus"></span>Add task</button>` : ''}</div>
      ${tasks.length ? `<div class="table-wrap" style="margin-top:8px"><table class="data"><thead><tr><th></th><th>Done</th><th>Action plan</th><th>Person in charge</th><th>Due</th></tr></thead><tbody>
        ${tasks.map(t => `<tr><td class="nowrap"><div class="row-first">${t.canEdit ? `<button type="button" class="button secondary small-btn" data-edit-task="${t.id}"><span data-icon="edit"></span>Edit</button>` : ''}
            ${t.canRemove ? `<button type="button" class="icon-button small" data-remove-task="${t.id}" title="Remove the task" aria-label="Remove the task"><span data-icon="trash"></span></button>` : ''}</div></td>
          <td class="center"><input type="checkbox" data-complete="${t.id}"${t.isCompleted ? ' checked' : ''}${t.canEdit ? '' : ' disabled'} aria-label="Completed"></td>
          <td class="wide-text" style="white-space:pre-wrap">${esc(t.actionPlan)}${t.remarks ? `<span class="sub">${esc(t.remarks)}</span>` : ''}</td>
          <td>${esc(t.personInCharge.name)}</td>
          <td class="nowrap">${esc(date(t.dueDate))}${d.sla ? `<span class="sub">SLA ${esc(date(d.sla))}</span>` : ''}</td></tr>`).join('')}</tbody></table></div>`
        : `<p class="muted small" style="margin:6px 0 0">No ${esc(title.toLowerCase())}.</p>`}</div>`;
  }

  function render() {
    const f = d.feedback, r = d.rights;
    const group = v.feedbackGroup;
    const internal = group === 'InternalChallenges';
    const resolution = (r.isNew ? v.feedbackType : f.feedbackType) !== 'Improvement';
    const divisions = lists.divisions.map(x => [x.id, x.name]);
    if (v.divisionId && !divisions.some(x => String(x[0]) === String(v.divisionId))) divisions.unshift([v.divisionId, f.division]);
    const medium = lists.mediums.filter(m => m.isActive || String(m.id) === String(v.mediumId)).map(m => [m.id, m.label]);
    const categories = lists.categories.filter(c => c.isActive || String(c.id) === String(v.categoryId)).map(c => [c.id, c.label]);
    const choices = list => list.map(c => [c.code, c.label]);
    page.innerHTML = `
      <div class="page-heading"><div><a class="crumb" href="./"><span data-icon="arrow-left"></span>Feedbacks</a>
          <h1>${r.isNew ? 'New feedback' : `Feedback #${f.id}`} ${r.isNew ? '' : `<span class="status big ${STATUS_CLASS[f.status] || ''}">${esc(f.statusLabel)}</span>`}</h1>
          <p>${r.isNew ? 'Fill in what you know; the person in charge is told by email once it is saved.' : `Created by ${esc(f.createdByName)} on ${esc(dateTime(f.createdOn))}${f.updatedOn ? ` · updated by ${esc(f.updatedBy)} on ${esc(dateTime(f.updatedOn))}` : ''}`}</p></div></div>
      <form id="fb" novalidate class="stack">${peopleOptions()}
        <section class="card"><h2>Feedback</h2><div class="form-grid">
          ${select('feedbackGroup', 'Feedback category', r.editGroup, choices(lists.groups), {required: true})}
          ${r.editNew ? select('divisionId', 'Division', true, divisions, {blank: 'None'}) : `<div class="field"><span>Division</span>${shown(f.division)}</div>`}
          ${person('personResponsible', 'In charge', r.editNew, {required: true, cls: 'half'})}
          ${r.isNew ? '' : kpi()}</div></section>
        ${CUSTOMER_GROUPS.includes(group) ? `<section class="card"><h2>Customer details</h2><div class="form-grid">
          ${text('companyName', 'Company name', r.editNew, {required: true, max: 500, cls: 'half'})}${text('contactName', 'Contact name', r.editNew, {required: true, max: 50})}
          ${text('companyEmail', 'Contact email', r.editNew, {required: true, type: 'email', max: 250})}
          ${select('mediumId', 'Medium', r.editNew, medium, {required: true})}
          ${text('dateReceived', 'Date received', r.editNew, {required: true, type: 'date', display: date(v.dateReceived)})}
          ${text('timeReceived', 'Time received', r.editNew, {required: true, type: 'time'})}
          ${f.companyAddress || f.postalCode || f.companyPhone ? `<div class="field half"><span>Address (from MyCRM)</span>${shown([f.companyAddress, f.postalCode, f.companyPhone].filter(Boolean).join(' · '))}</div>` : ''}
          ${filesOf('CD', r.customerFiles)}</div></section>` : ''}
        <section class="card"><h2>Problem identification</h2><div class="form-grid">
          ${select('feedbackType', 'Feedback type', r.editNew, choices(lists.types), {required: true})}
          ${select('categoryId', 'Category', r.editNew, categories, {required: true})}
          <details class="small half" style="align-self:end"><summary class="text-button">What the feedback types mean</summary><div class="muted" style="white-space:pre-wrap;margin-top:6px">${esc(lists.typeHelp)}</div></details>
          ${text('description', 'Description', r.editNew, {required: true, rows: 4, cls: 'wide'})}
          ${copyTo(r.editNew)}
          ${internal ? `<div class="wide" style="display:flex;flex-wrap:wrap;gap:8px 24px">${check('mightCauseCustomerComplaint', 'Might cause a customer complaint', r.editNew)}
              ${check('creatorHasSolution', 'I have a suggestion or solution', r.editNew)}</div>
            ${v.creatorHasSolution ? `
              ${text('solutionDetail', 'How would you suggest to improve the situation or resolve the problem? Please elaborate in details.', r.editNew, {required: true, rows: 4, cls: 'wide'})}
              ${text('expectedOutcome', 'What will be the expected outcome after applying your recommended solutions?', r.editNew, {required: true, rows: 3, cls: 'wide'})}
              ${checks('suggestionMeetCriteria', 'Does your suggestion meet any of these criteria?', r.editNew, lists.criteria, true)}
              ${checks('ricohWayValue', 'Which Ricoh Way values best represent your suggestion? You may tick more than one.', r.editNew, lists.ricohWay, true)}` : ''}` : ''}
        </div></section>
        ${internal || (!r.isNew && !group) ? `<section class="card"><h2>HR - ESS</h2><div class="form-grid">
          ${select('hrAccepted', 'HR accept', r.editHr, lists.hrAnswers.map(a => [a, a]), {blank: null})}
          ${text('hrRemarks', 'HR remarks', r.editHr, {rows: 3, cls: 'wide'})}
          ${r.isNew ? '' : `<div class="field"><span>HR updated on</span>${shown(dateTime(f.hrUpdatedOn))}</div><div class="field"><span>HR updated by</span>${shown(f.hrUpdatedBy)}</div>`}
          ${f.status === 'PendingHR' && !r.editHr ? '<p class="muted small wide" style="margin:0">Waiting for HR to accept or turn down the suggestion.</p>' : ''}</div></section>` : ''}
        ${r.isNew ? '' : `<section class="card"><h2>Acknowledgement</h2><div class="form-grid">
          <div class="field">${check('isAcknowledged', 'Acknowledged', r.editAcknowledge)}</div>
          <div class="field"><span>Acknowledged on</span>${shown(f.acknowledgedDate ? `${date(f.acknowledgedDate)} ${f.acknowledgedTime}` : '')}</div>
          <div class="field half"><span>Signed off by</span>${shown(f.signOff)}</div>
          ${r.editAcknowledge && !f.isAcknowledged ? '<p class="muted small wide" style="margin:0">Tick Acknowledged and save: the date, the time and your name are kept.</p>' : ''}</div></section>`}
        ${!r.isNew && resolution ? `
        <section class="card"><h2>Preliminary investigation / findings</h2><div class="form-grid">
          ${text('firstLookAtFeedback', 'First look at the feedback', r.editInvestigation, {required: f.status === 'Acknowledged', rows: 3, cls: 'wide'})}</div></section>
        <section class="card"><h2>Resolution</h2><div class="form-grid">
          ${tasksOf('Corrective', 'Corrective actions', r.addCorrectiveTask)}
          ${tasksOf('Preventive', 'Preventive actions', r.addPreventiveTask)}
          ${text('rootCause', 'Root cause', r.editInvestigation, {rows: 3, cls: 'wide'})}
          <div class="field half"><span>Staff responsible</span>${shown(f.staffResponsible)}</div></div></section>
        <section class="card"><h2>Customer / employee satisfaction</h2><div class="form-grid">
          ${select('satisfactionLevel', 'Satisfaction level', r.editSatisfaction, choices(lists.satisfaction), {required: f.status === 'InvestigationResolution', cls: 'half'})}
          ${filesOf('CS', r.satisfactionFiles)}</div></section>` : ''}
        ${!r.isNew && !resolution ? '<div class="notice info">A Feedback / Improvement has no investigation, tasks or satisfaction (as in MyCRM).</div>' : ''}
        <div class="notice error" id="problem" hidden></div>
        <div class="action-buttons"><button type="submit" class="button primary" id="save"><span data-icon="check"></span>${r.isNew ? 'Create the feedback' : 'Save'}</button>
          ${r.closeCase ? '<button type="button" class="button secondary" id="close-case"><span data-icon="lock"></span>Close case</button>' : ''}
          ${r.delete ? '<button type="button" class="button secondary danger-text" id="delete"><span data-icon="trash"></span>Delete</button>' : ''}
          <span class="grow"></span><a class="button secondary" href="./">Cancel</a></div>
      </form>`;
    drawIcons(page);
    wire();
  }

  function copyTo(editable) {
    const chips = v.cc.map((e, i) => `<span class="pill" style="gap:6px">${esc(e)}${editable ? `<button type="button" class="text-button" data-cc-remove="${i}" aria-label="Remove ${esc(e)}" style="padding:0">×</button>` : ''}</span>`).join(' ');
    if (!editable) return `<div class="field wide"><span>Copy to</span>${v.cc.length ? `<div>${chips}</div>` : shown('')}</div>`;
    return `<div class="field wide" data-field="cc"><span>Copy to</span><div style="display:flex;flex-wrap:wrap;gap:6px;align-items:center">${chips}
      <input id="cc-add" list="cc-people" placeholder="Add a person or an email address" autocomplete="off" style="flex:1;min-width:220px"></div>
      <datalist id="cc-people">${lists.people.map(p => `<option value="${esc(p.email)}">${esc(p.name)}</option>`).join('')}</datalist><span class="field-error"></span></div>`;
  }

  function wire() {
    const form = $('#fb');
    form.addEventListener('input', event => {
      const name = event.target.dataset.field;
      if (!name || event.target.type === 'checkbox' || event.target.dataset.person !== undefined) return;
      v[name] = event.target.value;
    });
    form.addEventListener('change', event => {
      const t = event.target;
      if (t.dataset.multi) { v[t.dataset.multi] = $$(`[data-multi="${t.dataset.multi}"]`).filter(b => b.checked).map(b => b.value); return; }
      if (t.dataset.person !== undefined) { v[t.dataset.person] = loginOf(t.value); return; }
      const name = t.dataset.field;
      if (!name) return;
      v[name] = t.type === 'checkbox' ? t.checked : t.value;
      // MyCRM: choosing a division makes its person in charge the feedback's.
      if (name === 'divisionId') {
        const division = lists.divisions.find(x => String(x.id) === String(t.value));
        if (division && division.personInCharge) v.personResponsible = division.personInCharge;
      }
      if (['feedbackGroup', 'creatorHasSolution', 'divisionId', 'feedbackType'].includes(name)) render();
    });
    const cc = $('#cc-add');
    const addCc = () => {
      const t = cc.value.trim();
      if (!t) return;
      const known = lists.people.find(p => p.email.toLowerCase() === t.toLowerCase() || p.name.toLowerCase() === t.toLowerCase());
      const email = known ? known.email : t;
      if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email)) { toast('Copy to takes email addresses.', true); return; }
      if (!v.cc.some(e => e.toLowerCase() === email.toLowerCase())) v.cc.push(email);
      render();
      $('#cc-add')?.focus();
    };
    cc?.addEventListener('change', addCc);
    cc?.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); addCc(); } });
    form.addEventListener('submit', event => { event.preventDefault(); save(); });
    $('#close-case')?.addEventListener('click', closeCase);
    $('#delete')?.addEventListener('click', remove);
  }

  function payload() {
    // A person box typed in but not left yet.
    $$('[data-person]').forEach(input => { v[input.dataset.person] = loginOf(input.value); });
    return {...v, divisionId: v.divisionId ? Number(v.divisionId) : null, mediumId: v.mediumId ? Number(v.mediumId) : null,
      categoryId: v.categoryId ? Number(v.categoryId) : null, dateReceived: v.dateReceived || null};
  }

  async function save() {
    const button = $('#save');
    button.disabled = true;
    const answer = await api('POST', 'feedbacks/' + id, payload());
    button.disabled = false;
    const problem = $('#problem');
    if (!answer.result.ok) {
      problem.hidden = false;
      problem.textContent = answer.result.message;
      showErrors($('#fb'), answer.result.errors);
      return;
    }
    try { sessionStorage.setItem('flash', answer.result.message); } catch { /* shown here instead */ }
    location.href = id === 0 ? `feedback.aspx?id=${answer.result.id}` : './';
  }

  async function closeCase() {
    if (!await ask({title: `Close feedback #${id}?`, text: 'Its satisfaction is confirmed. Nothing else on the page is saved.', ok: 'Close case'})) return;
    const answer = await api('POST', `feedbacks/${id}/close`, {});
    if (!answer.result.ok) { toast(answer.result.message, true); return; }
    try { sessionStorage.setItem('flash', answer.result.message); } catch { /* not kept */ }
    location.href = './';
  }

  async function remove() {
    if (!await ask({title: `Delete feedback #${id}?`, text: 'It no longer shows in the lists (it is kept, never erased).', ok: 'Delete', danger: true})) return;
    const answer = await api('POST', `feedbacks/${id}/delete`, {});
    if (!answer.result.ok) { toast(answer.result.message, true); return; }
    try { sessionStorage.setItem('flash', answer.result.message); } catch { /* not kept */ }
    location.href = './';
  }

  /** Find someone in Active Directory for a person box: they can be chosen everywhere from then on. */
  async function findSomeone(name) {
    const dialog = document.createElement('dialog');
    dialog.innerHTML = `<form method="dialog" class="modal"><h2>Find someone else</h2>
      <label class="field"><span>Name or login</span><input id="dir-q" type="search" autocomplete="off"></label>
      <div id="dir-people" style="max-height:300px;overflow:auto;margin-top:10px"></div>
      <div class="modal-footer"><button type="button" class="button secondary" value="cancel">Cancel</button></div></form>`;
    document.body.append(dialog);
    let timer = 0;
    $('#dir-q', dialog).addEventListener('input', event => {
      clearTimeout(timer);
      timer = setTimeout(async () => {
        const answer = await api('GET', 'people/search?' + new URLSearchParams({q: event.target.value.trim()}));
        $('#dir-people', dialog).innerHTML = !answer.result.ok ? `<p class="no">${esc(answer.result.message)}</p>`
          : answer.people.length ? answer.people.map((p, i) => `<button type="button" data-i="${i}" style="display:block;width:100%;text-align:left;padding:8px 10px;border:0;background:none;border-bottom:1px solid var(--border-soft)">
            <b>${esc(p.displayName)}</b><span class="muted small" style="display:block">${esc(p.login)} · ${esc(p.email || 'no email')}</span></button>`).join('') : '<p class="muted">Nobody found.</p>';
        $('#dir-people', dialog).onclick = async click => {
          const chosen = answer.people?.[Number(click.target.closest('[data-i]')?.dataset.i)];
          if (!chosen) return;
          const result = await api('POST', 'people', {login: chosen.login, displayName: chosen.displayName, email: chosen.email});
          if (!result.result.ok) { toast(result.result.message, true); return; }
          const added = result.data.person;
          if (!lists.people.some(p => p.login.toLowerCase() === added.login.toLowerCase())) lists.people.push(added);
          if (name) v[name] = added.login;
          dialog.close();
          render();
        };
      }, 350);
    });
    $('button[value=cancel]', dialog).addEventListener('click', () => dialog.close());
    dialog.addEventListener('close', () => dialog.remove());
    dialog.showModal();
    $('#dir-q', dialog).focus();
  }

  /** A task's dialog (MyCRM's TaskDetail_PopUp): the action plan and person by the feedback's person in charge, the remarks by the task's. */
  function taskDialog(task, type) {
    const r = d.rights;
    const isNew = !task;
    const t = task || {actionPlan: '', remarks: '', personInCharge: {login: '', name: ''}, dueDate: '', isCompleted: false, mine: false};
    const plan = isNew || r.inCharge, remarks = isNew || t.mine;
    const dialog = document.createElement('dialog');
    dialog.innerHTML = `<form class="modal" novalidate><h2>${isNew ? 'Add a task' : 'Task'} <span class="muted" style="font-weight:600">(${esc(labelOf(lists.taskTypes, type))})</span></h2>
      <div class="form-grid two">
        ${plan ? `<label class="field wide"><span class="required">Action plan</span><textarea data-field="actionPlan" rows="3">${esc(t.actionPlan)}</textarea><span class="field-error"></span></label>`
          : `<div class="field wide"><span>Action plan</span>${shown(t.actionPlan)}</div>`}
        ${remarks ? `<label class="field wide"><span>Remarks</span><textarea data-field="remarks" rows="2" maxlength="5000">${esc(t.remarks)}</textarea></label>`
          : `<div class="field wide"><span>Remarks</span>${shown(t.remarks)}</div>`}
        ${plan ? `<label class="field"><span class="required">Person in charge</span><input data-field="personInCharge" list="task-people" autocomplete="off" value="${esc(t.personInCharge.login ? `${t.personInCharge.name} (${t.personInCharge.login})` : '')}"><span class="field-error"></span></label>
          <datalist id="task-people">${lists.people.map(p => `<option value="${esc(p.name)} (${esc(p.login)})"></option>`).join('')}</datalist>`
          : `<div class="field"><span>Person in charge</span>${shown(t.personInCharge.name)}</div>`}
        <label class="field"><span class="required">Due date</span><input type="date" data-field="dueDate" value="${esc(dateInput(t.dueDate))}"><span class="field-error"></span></label>
        <div class="field"><span>SLA expected completion</span>${shown(date(d.sla))}</div>
        <div class="field"><label class="check"><input type="checkbox" data-field="isCompleted"${t.isCompleted ? ' checked' : ''}>Completed</label></div>
      </div>
      <div class="notice error" hidden></div>
      <div class="modal-footer"><button type="button" class="button secondary" value="cancel">Cancel</button><button type="submit" class="button primary">Save</button></div></form>`;
    document.body.append(dialog);
    const get = name => $(`[data-field="${name}"]`, dialog);
    $('button[value=cancel]', dialog).addEventListener('click', () => dialog.close());
    dialog.addEventListener('close', () => dialog.remove());
    $('form', dialog).addEventListener('submit', async event => {
      event.preventDefault();
      const body = {taskType: type, actionPlan: get('actionPlan')?.value ?? t.actionPlan, remarks: get('remarks')?.value ?? t.remarks,
        personInCharge: get('personInCharge') ? loginOf(get('personInCharge').value) : t.personInCharge.login, dueDate: get('dueDate').value || null,
        isCompleted: get('isCompleted').checked};
      const answer = await api('POST', `feedbacks/${id}/tasks/${isNew ? 0 : t.id}`, body);
      if (!answer.result.ok) {
        const notice = $('.notice', dialog);
        notice.hidden = false;
        notice.textContent = answer.result.message;
        showErrors(dialog, answer.result.errors);
        return;
      }
      toast(answer.result.message);
      dialog.close();
      load(true);
    });
    dialog.showModal();
    (get('actionPlan') || get('dueDate')).focus();
  }

  page.addEventListener('click', async event => {
    const find = event.target.closest('[data-find]');
    if (find) { event.preventDefault(); findSomeone(find.dataset.find); return; }
    const ccRemove = event.target.closest('[data-cc-remove]');
    if (ccRemove) { v.cc.splice(Number(ccRemove.dataset.ccRemove), 1); render(); return; }
    const add = event.target.closest('[data-add-task]');
    if (add) { taskDialog(null, add.dataset.addTask); return; }
    const edit = event.target.closest('[data-edit-task]');
    if (edit) { const task = d.tasks.find(t => t.id === Number(edit.dataset.editTask)); if (task) taskDialog(task, task.taskType); return; }
    const removeTask = event.target.closest('[data-remove-task]');
    if (removeTask) {
      if (!await ask({title: 'Remove this task?', text: 'It no longer shows on the feedback (it is kept, never erased).', ok: 'Remove', danger: true})) return;
      const answer = await api('POST', `tasks/${removeTask.dataset.removeTask}/remove`, {});
      toast(answer.result.message, !answer.result.ok);
      if (answer.result.ok) load(true);
      return;
    }
    const removeFile = event.target.closest('[data-remove-file]');
    if (removeFile) {
      if (!await ask({title: 'Remove this file?', text: 'It no longer shows on the feedback (it is kept, never erased).', ok: 'Remove', danger: true})) return;
      const answer = await api('POST', `files/${removeFile.dataset.removeFile}/remove`, {});
      toast(answer.result.message, !answer.result.ok);
      if (answer.result.ok) load(true);
    }
  });

  page.addEventListener('change', async event => {
    const done = event.target.closest('[data-complete]');
    if (done) {
      done.disabled = true;
      const answer = await api('POST', `tasks/${done.dataset.complete}/complete`, {completed: done.checked});
      toast(answer.result.message, !answer.result.ok);
      load(true);
      return;
    }
    const file = event.target.closest('[data-upload]');
    if (file && file.files.length) {
      const answer = await upload(`attachment?feedback=${id}&kind=${file.dataset.upload}`, file.files[0]);
      toast(answer.result.message, !answer.result.ok);
      if (answer.result.ok) load(true);
    }
  });

  try {
    const flash = sessionStorage.getItem('flash');
    if (flash) { sessionStorage.removeItem('flash'); setTimeout(() => toast(flash), 300); }
  } catch { /* nothing to show */ }
  load();
})();
