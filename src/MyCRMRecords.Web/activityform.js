(() => {
  'use strict';
  // An activity's panel (window.ACT), shared by the Activities page and a company's Activities tab: an activity to read
  // (Edit and Delete for its owner, as MyCRM), and the New activity / edit form with MyCRM's fields and checks. Saved in
  // MyCRM's tables. ACT.open(id, done) shows one; ACT.create({companyId, companyName}, done) makes one; done() runs after a
  // change.
  const {$, $$, esc, api, toast, drawIcons, date, showErrors} = window.WH;

  const today = () => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`; };
  const statusClass = s => s === 'Completed' ? 'done' : s === 'Not completed' ? 'late' : 'pending';
  const statusTag = s => `<span class="act-status ${statusClass(s)}">${esc(s)}</span>`;

  /** A glass panel (a <dialog>) with a round close button, a centred title and an action on the right. */
  function panel(title, sub, {wide = false, action = ''} = {}) {
    const el = document.createElement('dialog');
    el.className = 'act-panel' + (wide ? ' wide' : '');
    el.innerHTML = `<div class="act-panel-in"><div class="act-panel-head">
        <button type="button" class="icon-button" data-close aria-label="Close"><span data-icon="close"></span></button>
        <div class="act-panel-title"><h2>${esc(title)}</h2><span class="muted small" data-sub>${esc(sub)}</span></div>
        <div class="act-panel-action">${action}</div></div><div class="act-panel-body" data-body></div><div class="act-panel-foot" data-foot></div></div>`;
    document.body.append(el);
    $('[data-close]', el).addEventListener('click', () => el.close());
    el.addEventListener('close', () => el.remove());
    el.addEventListener('click', event => { if (event.target === el) el.close(); });
    drawIcons(el);
    el.showModal();
    return el;
  }

  const fmtWhen = a => {
    const range = a.start ? `${a.start}${a.end ? '–' + a.end : ''}` : '';
    return `${date(a.date)}${range ? ', ' + range : ''}${a.endDate && a.endDate !== a.date ? ' to ' + date(a.endDate) : ''}`;
  };
  const names = list => list && list.length ? list.map(x => esc(x.name || x)).join(', ') : '<span class="muted">–</span>';

  /** An activity to read; its owner gets Edit and Delete. */
  async function open(id, done) {
    const answer = await api('GET', 'activities/' + id);
    if (!answer.result.ok) { toast(answer.result.code === 'NOT_FOUND' ? 'That activity is not there, or it is not yours or your team\'s.' : answer.result.message, true); return; }
    const a = answer.activity;
    const el = panel(a.remarks || a.actionLabel || 'Activity', `${a.company} · ${a.status}`,
      {action: answer.canEdit ? '<button type="button" class="button primary" data-edit><span data-icon="edit"></span>Edit</button>' : ''});
    const body = $('[data-body]', el);
    body.innerHTML = `${answer.canEdit ? '' : `<div class="notice act-locked"><span data-icon="lock"></span>Only the owner, ${esc(a.ownerName)}, can change this activity.</div>`}
      <dl class="act-facts">
        <div><dt>Company</dt><dd>${a.companyId ? `<a href="company.aspx?id=${a.companyId}#activities">${esc(a.company)}</a>` : esc(a.company)}</dd></div>
        <div><dt>Action</dt><dd>${esc(a.actionLabel)}</dd></div>
        <div><dt>When</dt><dd>${esc(fmtWhen(a))}</dd></div>
        <div><dt>Owner</dt><dd>${esc(a.ownerName)}</dd></div>
        <div><dt>Completed</dt><dd>${a.completed ? 'Yes' : 'No'} ${statusTag(a.status)}</dd></div>
        <div><dt>Activity Type</dt><dd>${names(a.typeLabels)}</dd></div>
        <div><dt>Participants</dt><dd>${names(a.participants)}</dd></div>
        <div><dt>Project</dt><dd>${a.projects.length ? a.projects.map(p => `<a href="project.aspx?id=${p.id}">${esc(p.name)}</a>`).join(', ') : '<span class="muted">–</span>'}</dd></div>
        <div><dt>Attendees</dt><dd>${names(a.contacts)}</dd></div>
        <div><dt>Related Prospects</dt><dd>${names(a.prospects)}</dd></div>
        <div><dt>Remarks</dt><dd class="pre">${esc(a.remarks) || '<span class="muted">–</span>'}</dd></div>
        <div><dt>Last updated</dt><dd>${esc(date(a.updatedDate))}${a.updatedBy ? ' by ' + esc(a.updatedBy) : ''}</dd></div>
      </dl>`;
    const foot = $('[data-foot]', el);
    foot.innerHTML = `${answer.canEdit ? '<button type="button" class="button secondary danger-text" data-delete><span data-icon="trash"></span>Delete</button>' : ''}<span class="muted small">Saved in MyCRM, on the company's activities.</span>`;
    drawIcons(el);
    $('[data-edit]', el)?.addEventListener('click', () => { el.close(); form(a, done); });
    $('[data-delete]', el)?.addEventListener('click', () => {
      foot.innerHTML = `<span class="act-confirm">Delete this activity? It disappears from the company's activities.</span>
        <span class="act-confirm-buttons"><button type="button" class="button secondary" data-keep>Cancel</button><button type="button" class="button danger-btn solid" data-yes>Delete</button></span>`;
      $('[data-keep]', foot).addEventListener('click', () => { el.close(); open(id, done); });
      $('[data-yes]', foot).addEventListener('click', async () => {
        const result = await api('POST', `activities/${id}/delete`, {});
        toast(result.result.message, !result.result.ok);
        if (result.result.ok) { el.close(); done && done(); }
      });
    });
  }

  /** A new activity (with the company when it comes from one). */
  function create(company, done) {
    form({id: 0, companyId: company && company.companyId || null, company: company && company.companyName || '', action: 'FaceToFace', remarks: '', date: today(), endDate: today(),
      start: '', end: '', owner: '', completed: false, types: [], participants: [], contacts: [], prospects: [], projects: [], fixedCompany: !!(company && company.companyId)}, done);
  }

  /** The form: MyCRM's fields in its order, the lists as pickers (choose, Add, a removable chip each). */
  async function form(a, done) {
    const isNew = !a.id;
    const el = panel(isNew ? 'New Activity' : (a.remarks || 'Edit activity'), isNew ? 'Saved in MyCRM' : 'Editing · saved in MyCRM',
      {wide: true, action: '<button type="button" class="button primary" data-save>Save</button>'});
    const body = $('[data-body]', el);
    body.innerHTML = '<p class="muted">Loading…</p>';
    const state = {
      companyId: a.companyId || null, company: a.company || '', types: [...(a.types || [])],
      participants: (a.participants || []).map(p => ({login: p.login, name: p.name})),
      contacts: (a.contacts || []).map(c => ({id: c.id, name: c.name})), prospects: (a.prospects || []).map(p => ({id: p.id, name: p.name})),
      projects: (a.projects || []).map(p => ({id: p.id, name: p.name})),
    };
    let choices = null, companies = null;
    const loadChoices = async () => {
      const answer = await api('GET', 'activities/choices?' + new URLSearchParams({company: String(state.companyId || 0)}));
      if (!answer.result.ok) { body.innerHTML = `<div class="notice error">${esc(answer.result.message)}</div>`; return false; }
      choices = answer;
      if (!state.companyId && answer.companies) companies = answer.companies;
      return true;
    };
    if (!await loadChoices()) return;
    if (isNew && !state.participants.length) state.participants = [{login: choices.me, name: 'You'}];
    const me = choices.me;
    const ownerLogin = a.owner || me;

    const companyField = () => {
      if (a.fixedCompany || !isNew) return `<div class="field"><span>Company</span><div class="act-fixed">${esc(state.company)}</div></div>`;
      if (choices.searchCompanies)
        return `<label class="field"><span class="required">Company</span><input type="search" data-field="company" data-company-search placeholder="Type 2 letters of the company or BRN" value="${esc(state.company)}" autocomplete="off">
          <div class="act-suggest" data-suggest hidden></div><span class="field-error"></span></label>`;
      return `<label class="field"><span class="required">Company <small class="muted">Only your companies are listed.</small></span><select data-field="company" data-company-select>
          <option value="">- Select Company -</option>${(companies || []).map(c => `<option value="${c.id}"${c.id === state.companyId ? ' selected' : ''}>${esc(c.name)}</option>`).join('')}</select><span class="field-error"></span></label>`;
    };
    const actionOptions = () => choices.actions.filter(x => x.active || x.code === a.action).map(x => `<option value="${esc(x.code)}"${x.code === a.action ? ' selected' : ''}>${esc(x.label)}</option>`).join('');
    const ownerOptions = () => {
      const list = choices.owners.slice();
      if (ownerLogin && !list.some(o => o.login.toLowerCase() === ownerLogin.toLowerCase())) list.unshift({login: ownerLogin, name: a.ownerName || ownerLogin});
      return list.map(o => `<option value="${esc(o.login)}"${o.login.toLowerCase() === ownerLogin.toLowerCase() ? ' selected' : ''}>${esc(o.login.toLowerCase() === me.toLowerCase() ? o.name + ' (you)' : o.name)}</option>`).join('');
    };

    // Pickers: key → [label, options(), the chosen list, how to name one, required, hint, empty text]
    const pickers = () => [
      {key: 'types', label: 'Activity Type', required: true, placeholder: '- Select Activity Type -', options: choices.types.map(t => ({value: t.code, name: t.label})),
        chosen: state.types.map(code => ({value: code, name: (choices.types.find(t => t.code === code) || {label: code}).label})), empty: 'Add at least one type.'},
      {key: 'participants', label: 'Participants', hint: 'The salespeople of this company in your team', placeholder: state.companyId ? '- Select Participant -' : 'Choose a company first',
        options: state.companyId ? choices.participants.map(p => ({value: p.login, name: p.name})) : [],
        chosen: state.participants.map(p => ({value: p.login, name: p.name, locked: p.login.toLowerCase() === ($('[data-field=owner]', el)?.value || ownerLogin).toLowerCase()})), empty: ''},
      ...(choices.projectsShown ? [{key: 'projects', label: 'Project', hint: 'Open projects with this company', placeholder: state.companyId ? '- Select Project -' : 'Choose a company first',
        options: choices.projects.map(p => ({value: String(p.id), name: p.name})), chosen: state.projects.map(p => ({value: String(p.id), name: p.name})), empty: 'No project linked.',
        link: '<a href="projects.aspx">New project</a>'}] : []),
      {key: 'contacts', label: 'Attendees', hint: 'The company\'s contacts', placeholder: state.companyId ? '- Select Contact -' : 'Choose a company first',
        options: choices.contacts.map(c => ({value: String(c.id), name: c.name})), chosen: state.contacts.map(c => ({value: String(c.id), name: c.name})), empty: 'No contacts added.',
        link: state.companyId ? `<a href="company.aspx?id=${state.companyId}#contacts">New contact</a>` : ''},
      {key: 'prospects', label: 'Related Prospects', placeholder: state.companyId ? '- Select Prospect -' : 'Choose a company first',
        options: choices.prospects.map(p => ({value: String(p.id), name: p.name})), chosen: state.prospects.map(p => ({value: String(p.id), name: p.name})), empty: 'No prospects linked.'},
    ];
    const pickerHtml = p => {
      const left = p.options.filter(o => !p.chosen.some(c => c.value === o.value));
      return `<div class="act-picker" data-picker="${p.key}" data-field="${p.key}">
        <div class="act-picker-head"><span class="act-picker-label">${esc(p.label)}${p.required ? ' <span class="req">*</span>' : ''}${p.hint ? ` <span class="muted small">${esc(p.hint)}</span>` : ''}</span>${p.link || ''}</div>
        <div class="act-picker-row"><select aria-label="Choose ${esc(p.label.toLowerCase())}"><option value="">${esc(p.placeholder)}</option>${left.map(o => `<option value="${esc(o.value)}">${esc(o.name)}</option>`).join('')}</select>
          <button type="button" class="button secondary small-btn" data-add><span data-icon="plus"></span>Add</button></div>
        <div class="act-chips">${p.chosen.map(c => `<span class="act-chip">${esc(c.name)}${c.locked ? ' <span class="muted small">owner</span>' : `<button type="button" data-remove="${esc(c.value)}" aria-label="Remove ${esc(c.name)}"><span data-icon="close"></span></button>`}</span>`).join('')}
          ${p.chosen.length ? '' : `<span class="muted small">${esc(p.empty)}</span>`}</div><span class="field-error"></span></div>`;
    };
    const drawPickers = () => { $('[data-pickers]', el).innerHTML = pickers().map(pickerHtml).join(''); drawIcons($('[data-pickers]', el)); };

    body.innerHTML = `<div class="notice error" data-notice hidden></div><div class="act-form"><div class="act-col">
      <div class="act-card">
        ${companyField()}
        <label class="field"><span>Action</span><select data-field="action">${actionOptions()}</select><span class="field-error"></span></label>
        <label class="field"><span>Remarks</span><input type="text" data-field="remarks" maxlength="4000" placeholder="What is this activity about?" value="${esc(a.remarks)}"><span class="field-error"></span></label>
      </div>
      <div class="act-card">
        <div class="act-dates">
          <label class="field"><span class="required">Start Date</span><input type="date" data-field="startDate" value="${esc(a.date)}"><span class="field-error"></span></label>
          <label class="field"><span>End Date</span><input type="date" data-field="endDate" value="${esc(a.endDate || a.date)}"><span class="field-error"></span></label>
          <label class="field"><span>Start Time</span><input type="time" data-field="startTime" value="${esc(a.start)}"><span class="field-error"></span></label>
          <label class="field"><span>End Time</span><input type="time" data-field="endTime" value="${esc(a.end)}"><span class="field-error"></span></label>
        </div>
        <p class="muted small act-note"><span data-day-note></span> No times? Start is set to now, end to one hour later.</p>
        <div class="act-owner">
          <label class="field"><span>Owner</span><select data-field="owner">${ownerOptions()}</select><span class="field-error"></span></label>
          <label class="check act-done"><input type="checkbox" data-field="completed"${a.completed ? ' checked' : ''}>Completed</label>
        </div>
      </div></div>
      <div class="act-card act-pickers" data-pickers></div>
    </div>`;
    $('[data-foot]', el).innerHTML = '<span class="muted small">Saved in MyCRM, on the company\'s activities.</span>';
    drawPickers();
    drawIcons(el);

    // How many other activities the owner has on the start date (MyCRM's calendar check).
    const dayNote = async () => {
      const day = $('[data-field=startDate]', el).value, owner = $('[data-field=owner]', el).value;
      const note = $('[data-day-note]', el);
      if (!day) { note.textContent = ''; return; }
      const answer = await api('GET', 'activities?' + new URLSearchParams({from: day, to: day, owner: owner.toLowerCase() === me.toLowerCase() ? 'me' : owner}));
      if (!answer.result.ok) { note.textContent = ''; return; }
      const others = answer.rows.filter(r => r.id !== a.id).length;
      note.innerHTML = `${owner.toLowerCase() === me.toLowerCase() ? 'You have' : 'The owner has'} ${others} other activit${others === 1 ? 'y' : 'ies'} that day. <a href="activities.aspx?view=calendar&month=${day.slice(0, 7)}">See the calendar</a>.`;
    };
    dayNote();
    $('[data-field=startDate]', el).addEventListener('change', event => {
      const end = $('[data-field=endDate]', el);
      if (!end.value || end.value < event.target.value) end.value = event.target.value;
      dayNote();
    });
    $('[data-field=owner]', el).addEventListener('change', () => {
      const owner = $('[data-field=owner]', el);
      if (!state.participants.some(p => p.login.toLowerCase() === owner.value.toLowerCase()))
        state.participants.push({login: owner.value, name: owner.selectedOptions[0].textContent.replace(' (you)', '')});
      drawPickers();
      dayNote();
    });

    // Choosing the company loads its contacts, prospects, projects and salespeople; what was chosen for another goes.
    const setCompany = async (id, name) => {
      state.companyId = id;
      state.company = name;
      state.contacts = []; state.prospects = []; state.projects = [];
      state.participants = state.participants.filter(p => p.login.toLowerCase() === ($('[data-field=owner]', el).value || me).toLowerCase());
      if (await loadChoices()) drawPickers();
    };
    $('[data-company-select]', el)?.addEventListener('change', event => {
      const option = event.target.selectedOptions[0];
      setCompany(event.target.value ? Number(event.target.value) : null, option ? option.textContent : '');
    });
    const search = $('[data-company-search]', el);
    if (search) {
      let timer = 0;
      const suggest = $('[data-suggest]', el);
      search.addEventListener('input', () => {
        clearTimeout(timer);
        state.companyId = null;
        timer = setTimeout(async () => {
          const q = search.value.trim();
          if (q.length < 2) { suggest.hidden = true; return; }
          const answer = await api('GET', 'activities/companies?' + new URLSearchParams({q}));
          const rows = answer.result.ok ? answer.companies : [];
          suggest.hidden = false;
          suggest.innerHTML = rows.length ? rows.map(c => `<button type="button" data-pick="${c.id}">${esc(c.name)}</button>`).join('') : '<span class="muted small">No company matches.</span>';
        }, 300);
      });
      suggest.addEventListener('click', event => {
        const pick = event.target.closest('[data-pick]');
        if (!pick) return;
        search.value = pick.textContent;
        suggest.hidden = true;
        setCompany(Number(pick.dataset.pick), pick.textContent);
      });
    }

    $('[data-pickers]', el).addEventListener('click', event => {
      const box = event.target.closest('[data-picker]');
      if (!box) return;
      const key = box.dataset.picker;
      const p = pickers().find(x => x.key === key);
      const add = event.target.closest('[data-add]');
      const remove = event.target.closest('[data-remove]');
      if (add) {
        const value = $('select', box).value;
        if (!value) return;
        const option = p.options.find(o => o.value === value);
        if (key === 'types') state.types.push(value);
        else if (key === 'participants') state.participants.push({login: value, name: option.name});
        else state[key].push({id: Number(value), name: option.name});
      }
      if (remove) {
        const value = remove.dataset.remove;
        if (key === 'types') state.types = state.types.filter(t => t !== value);
        else if (key === 'participants') state.participants = state.participants.filter(x => x.login !== value);
        else state[key] = state[key].filter(x => String(x.id) !== value);
      }
      if (add || remove) drawPickers();
    });

    $('[data-save]', el).addEventListener('click', async () => {
      const v = name => $(`[data-field=${name}]`, el);
      const input = {
        companyId: state.companyId, action: v('action').value, remarks: v('remarks').value, startDate: v('startDate').value, endDate: v('endDate').value,
        startTime: v('startTime').value, endTime: v('endTime').value, owner: v('owner').value, completed: v('completed').checked,
        types: state.types, participants: state.participants.map(p => p.login), contacts: state.contacts.map(c => c.id), prospects: state.prospects.map(p => p.id),
        projects: state.projects.map(p => p.id),
      };
      const button = $('[data-save]', el);
      button.disabled = true;
      const answer = await api('POST', 'activities/' + (a.id || 0), input);
      button.disabled = false;
      if (!answer.result.ok) {
        const notice = $('[data-notice]', el);
        notice.hidden = false;
        notice.textContent = (answer.result.errors || []).length > 1 ? answer.result.errors.map(e => e.message).join(' · ') : answer.result.message;
        showErrors(el, answer.result.errors || []);
        notice.scrollIntoView({block: 'nearest'});
        return;
      }
      toast(answer.result.message);
      el.close();
      done && done(answer.result.id);
    });
  }

  window.ACT = {open, create, statusTag, statusClass};
})();
