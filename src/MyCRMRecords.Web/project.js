(() => {
  'use strict';
  // project.aspx?id=: a MyCRM project: its details, Companies, Members (with their temporary territories), linked
  // Activities and History. The owner or a Sales Admin edits, closes and deletes it and adds or removes companies; only
  // the owner adds and removes members. Closing, deleting and removing ask first and say who loses access.
  const {$, $$, esc, api, toast, ask, drawIcons, date, dateTime, param, showErrors, crumbs} = window.WH;
  const page = $('#page');
  const id = Number(param('id') || 0);
  const tabIds = ['companies', 'members', 'activities', 'history'];
  let tab = tabIds.includes(location.hash.slice(1)) ? location.hash.slice(1) : 'companies';
  let d = null;
  const plural = (n, one, many) => `${n} ${n === 1 ? one : many}`;

  async function load() {
    const answer = await api('GET', 'projects/' + id);
    $('#loading').hidden = true;
    page.hidden = false;
    if (!answer.result.ok) {
      // Deleted (MyCRM keeps it, inactive), not there, or not the person's: said as such, with the way back.
      const deleted = answer.result.code === 'DELETED';
      const title = deleted ? 'Project deleted' : answer.result.code === 'NOT_FOUND' ? 'Project not found' : 'Project';
      page.innerHTML = `<div class="page-heading proj-heading"><div>${crumbs(title, [['Projects', 'projects.aspx']])}<h1>${esc(title)}</h1></div></div>
        <div class="empty proj-gone"><span data-icon="${deleted ? 'trash' : 'search'}" class="proj-gone-icon"></span>
          <h3>${esc(deleted ? answer.result.message : answer.result.code === 'NOT_FOUND' ? 'That project is not there.' : answer.result.message)}</h3>
          ${deleted ? '<p class="muted">Its members no longer have temporary access to its companies. Activities linked to it stay on each company.</p>' : ''}
          <p><a class="button secondary" href="projects.aspx"><span data-icon="arrow-left"></span>Back to the projects</a></p></div>`;
      drawIcons(page);
      return;
    }
    d = answer;
    render();
  }

  function render() {
    const p = d.project, can = d.can;
    document.title = p.name + ' · MyCRMRecords';
    const state = p.closed ? '<span class="act-status pending">Closed</span>' : p.open ? '<span class="act-status done">Open</span>' : '<span class="act-status late">Ended</span>';
    page.innerHTML = `
      <div class="page-heading proj-heading"><div>${crumbs(p.name, [['Projects', 'projects.aspx']])}
          <h1>${esc(p.name)}</h1>
          <p class="proj-sub">${state} <span>${esc(p.statusLabel)}</span> <span class="muted">Owner <b>${esc(p.ownerName)}</b> · ${p.closed ? 'closed · was ending' : 'ends'} ${esc(date(p.endDate))} · Saved in MyCRM</span></p>
          ${p.remarks ? `<p class="proj-remarks">${esc(p.remarks)}</p>` : ''}</div>
        <div class="heading-actions">${can.change || can.delete ? `<span class="action-capsule">
          ${can.change ? '<button type="button" class="capsule-button" id="edit"><span data-icon="edit"></span>Edit</button><span class="capsule-divider"></span><button type="button" class="capsule-button" id="close"><span data-icon="lock"></span>Close project</button><span class="capsule-divider"></span>' : ''}
          ${can.delete ? '<button type="button" class="capsule-button danger-text" id="delete"><span data-icon="trash"></span>Delete</button>' : ''}</span>` : ''}</div></div>
      ${p.closed ? '<div class="notice proj-closed">This project is closed. Members no longer have temporary access; activity links stay. Closed projects can\'t be edited or reopened.</div>' : ''}
      ${!p.closed && !p.open ? '<div class="notice proj-closed">This project has passed its end date: its members no longer have temporary access, and the daily task closes it.</div>' : ''}
      <div class="tabs company-tabs proj-tabs" role="tablist" aria-label="Project sections">${[['companies', 'Companies', d.companies.length], ['members', 'Members', d.members.length],
        ['activities', 'Activities', d.activities.length], ['history', 'History', '']].map(([t, l, n]) =>
        `<button type="button" class="tab" role="tab" data-tab="${t}" id="tab-${t}" aria-controls="panel-${t}">${l}${n !== '' ? ` <span class="count">${n}</span>` : ''}</button>`).join('')}</div>
      <section class="card proj-panel" id="panel-companies" role="tabpanel" aria-labelledby="tab-companies">${companies()}</section>
      <section class="card proj-panel" id="panel-members" role="tabpanel" aria-labelledby="tab-members">${members()}</section>
      <section class="card proj-panel" id="panel-activities" role="tabpanel" aria-labelledby="tab-activities">${activities()}</section>
      <section class="card proj-panel" id="panel-history" role="tabpanel" aria-labelledby="tab-history">${changes()}</section>`;
    drawIcons(page);
    showTab(tab);
    $('#edit')?.addEventListener('click', edit);
    $('#close')?.addEventListener('click', closeProject);
    $('#delete')?.addEventListener('click', deleteProject);
  }

  function showTab(next) {
    tab = tabIds.includes(next) ? next : 'companies';
    $$('.proj-tabs .tab', page).forEach(b => b.setAttribute('aria-selected', String(b.dataset.tab === tab)));
    $$('.proj-panel', page).forEach(panel => { panel.hidden = panel.id !== 'panel-' + tab; });
    history.replaceState(null, '', location.pathname + location.search + (tab === 'companies' ? '' : '#' + tab));
  }

  const head = (text, button = '') => `<div class="proj-panel-head"><span>${text}</span>${button}</div>`;

  function companies() {
    const can = d.can.change && d.companies.length > 1;
    return head(`<b>${plural(d.companies.length, 'company', 'companies')}</b> · members without their own territory get temporary access to each one`,
        d.can.change ? '<button type="button" class="button secondary small-btn" id="add-companies"><span data-icon="plus"></span>Add companies</button>' : '')
      + `<div class="table-wrap"><table class="data proj-inner"><thead><tr><th>Company</th><th>BRN</th><th>Salesperson · territory</th><th class="open-col"><span class="sr-only">Remove</span></th></tr></thead><tbody>
      ${d.companies.map(c => `<tr><td><a href="company.aspx?id=${c.id}"><b>${esc(c.name)}</b></a>${c.active ? '' : ' <span class="pill warn">Inactive</span>'}</td><td>${esc(c.brn)}</td>
        <td>${c.salesperson ? esc(c.salesperson) + ` <span class="muted">· ${esc(c.territory)}</span>` : '<span class="muted">No salesperson</span>'}</td>
        <td class="open-col">${can ? `<button type="button" class="text-button danger" data-remove-company="${c.id}" aria-label="Remove ${esc(c.name)} from the project">Remove</button>` : ''}</td></tr>`).join('')}
      </tbody></table></div>`;
  }

  function members() {
    return head(`<b>${plural(d.members.length, 'member', 'members')}</b> · the owner, ${esc(d.project.ownerName)}, is Project Manager and needs no temporary territory`,
        d.can.members ? '<button type="button" class="button secondary small-btn" id="add-member"><span data-icon="plus"></span>Add member</button>' : '')
      + `<div class="table-wrap"><table class="data proj-inner"><thead><tr><th>Name</th><th>Role</th><th>Temporary territory</th><th class="open-col"><span class="sr-only">Remove</span></th></tr></thead><tbody>
      ${d.members.map(m => `<tr><td><b>${esc(m.name)}</b></td><td>${esc(m.roleLabel)}</td>
        <td>${m.isOwner ? '<span class="muted">Project Manager: none needed</span>' : m.reached.length ? `<span class="act-status done">${plural(m.reached.length, 'company', 'companies')}</span> <span class="muted small">${esc(m.reached.join(', '))}</span>`
          : `<span class="muted">${d.project.open ? 'Has own territory' : 'None (project not open)'}</span>`}</td>
        <td class="open-col">${d.can.members && !m.isOwner ? `<button type="button" class="text-button danger" data-remove-member="${m.id}" aria-label="Remove ${esc(m.name)} from the project">Remove</button>` : ''}</td></tr>`).join('')}
      </tbody></table></div><p class="muted small proj-note">Temporary territories are taken away when the project closes or ends. Projects past their end date close automatically each morning.</p>`;
  }

  function activities() {
    return head(`<b>${plural(d.activities.length, 'activity', 'activities')}</b> linked to this project · newest first · links stay if the project closes`)
      + (d.activities.length ? `<div class="table-wrap"><table class="data proj-inner"><thead><tr><th>Start date</th><th>Action</th><th>Remarks</th><th>Company</th><th>Owner</th><th>Status</th></tr></thead><tbody>
      ${d.activities.map(a => `<tr><td class="nowrap">${esc(date(a.date))}</td><td>${esc(a.action)}</td><td><button type="button" class="act-remarks" data-activity="${a.id}">${esc(a.remarks || a.action)}</button></td>
        <td>${a.companyId ? `<a href="company.aspx?id=${a.companyId}#activities">${esc(a.company)}</a>` : ''}</td><td>${esc(a.owner)}</td><td>${window.ACT.statusTag(a.status)}</td></tr>`).join('')}
      </tbody></table></div>` : '<p class="muted">No activities are linked to this project. Link one from its Project field in the activity form.</p>');
  }

  function changes() {
    return head('<b>Changes</b> · newest first · those made in MyCRMRecords (changes made in MyCRM are not listed)')
      + (d.history.length ? `<div class="table-wrap"><table class="data proj-inner"><thead><tr><th>When</th><th>Change</th><th>By</th></tr></thead><tbody>
      ${d.history.map(h => `<tr><td class="nowrap">${esc(dateTime(h.at))}</td><td>${esc(h.what)}</td><td>${esc(h.by)}</td></tr>`).join('')}</tbody></table></div>`
        : '<p class="muted">No changes were made to this project in MyCRMRecords yet.</p>');
  }

  function dialog(title, body, action) {
    const el = document.createElement('dialog');
    el.className = 'act-panel';
    el.innerHTML = `<div class="act-panel-in"><div class="act-panel-head"><button type="button" class="icon-button" data-close aria-label="Cancel"><span data-icon="close"></span></button>
      <div class="act-panel-title"><h2>${esc(title)}</h2></div><div class="act-panel-action"><button type="button" class="button primary" data-ok>${esc(action)}</button></div></div>
      <div class="notice error" data-notice hidden></div><div class="act-panel-body">${body}</div><div class="act-panel-foot" data-foot></div></div>`;
    document.body.append(el);
    drawIcons(el);
    $('[data-close]', el).addEventListener('click', () => el.close());
    el.addEventListener('close', () => el.remove());
    el.showModal();
    return el;
  }
  const refused = (el, answer) => { const n = $('[data-notice]', el); n.hidden = false; n.textContent = answer.result.message; showErrors(el, answer.result.errors || []); };

  function edit() {
    const p = d.project;
    const el = dialog('Edit project', `<div class="form-grid two">
      <label class="field wide"><span class="required">Project name</span><input data-field="name" maxlength="250" value="${esc(p.name)}"><span class="field-error"></span></label>
      <label class="field"><span>Status</span><select data-field="status">${d.statuses.map(s => `<option value="${esc(s.code)}"${s.code === p.status ? ' selected' : ''}>${esc(s.label)}</option>`).join('')}</select></label>
      <label class="field"><span>Owner</span><input value="${esc(p.ownerName)}" disabled></label>
      <label class="field"><span>Start date</span><input type="date" data-field="startDate" value="${esc(p.startDate)}"><span class="field-error"></span></label>
      <label class="field"><span class="required">End date</span><input type="date" data-field="endDate" value="${esc(p.endDate)}"><span class="field-error"></span></label>
      <label class="field wide"><span>Remarks</span><textarea data-field="remarks" rows="3" maxlength="2000">${esc(p.remarks)}</textarea><span class="field-error"></span></label></div>
      <p class="muted small">Use Close project to end it early. A later end date keeps the members' temporary access until then.</p>`, 'Save to MyCRM');
    $('[data-ok]', el).addEventListener('click', async () => {
      const v = n => $(`[data-field=${n}]`, el).value;
      const answer = await api('POST', 'projects/' + id, {name: v('name'), status: v('status'), startDate: v('startDate'), endDate: v('endDate'), remarks: v('remarks')});
      if (!answer.result.ok) { refused(el, answer); return; }
      toast(answer.result.message);
      el.close();
      load();
    });
  }

  function addCompanies() {
    const picked = new Map();
    let found = [];
    const el = dialog('Add companies', `<label class="search-box proj-co-search"><span data-icon="search"></span><span class="sr-only">Find companies</span><input type="search" data-q placeholder="Company, BRN or website" autocomplete="off"></label>
      <div class="proj-pick" data-pick><p class="muted small">Type at least 2 letters of a company, BRN or website.</p></div><p class="muted small" data-summary>Tick the companies to add.</p>`, 'Add companies');
    drawIcons(el);
    const draw = () => {
      $('[data-pick]', el).innerHTML = found.length ? found.map(c => `<label class="proj-pick-row"><input type="checkbox" data-co="${c.id}"${picked.has(c.id) ? ' checked' : ''}${d.companies.some(x => x.id === c.id) ? ' disabled' : ''}>
        <span class="proj-pick-name">${esc(c.name)}</span><span class="muted small">${d.companies.some(x => x.id === c.id) ? 'in the project' : esc([c.territory, c.salesperson || 'no salesperson'].filter(Boolean).join(' · '))}</span></label>`).join('')
        : '<p class="muted small">No company matches.</p>';
      const temp = d.members.filter(m => !m.isOwner).length;
      $('[data-summary]', el).textContent = picked.size ? `${plural(picked.size, 'company', 'companies')} chosen. ${plural(temp, 'member gets', 'members get')} temporary access to ${picked.size === 1 ? 'it' : 'them'} unless they own ${picked.size === 1 ? 'it' : 'them'}.` : 'Tick the companies to add.';
      $('[data-ok]', el).textContent = picked.size ? `Add ${plural(picked.size, 'company', 'companies')}` : 'Add companies';
    };
    let t = 0;
    $('[data-q]', el).addEventListener('input', event => {
      clearTimeout(t);
      t = setTimeout(async () => {
        const q = event.target.value.trim();
        if (q.length < 2) return;
        const answer = await api('GET', 'projects/companies?' + new URLSearchParams({q}));
        found = answer.result.ok ? answer.companies : [];
        draw();
      }, 300);
    });
    el.addEventListener('change', event => {
      const box = event.target.closest('[data-co]');
      if (!box) return;
      const c = found.find(x => x.id === Number(box.dataset.co));
      if (box.checked) picked.set(c.id, c); else picked.delete(c.id);
      draw();
    });
    $('[data-ok]', el).addEventListener('click', async () => {
      if (!picked.size) return;
      const answer = await api('POST', `projects/${id}/companies`, {companies: [...picked.keys()]});
      if (!answer.result.ok) { refused(el, answer); return; }
      toast(answer.result.message);
      el.close();
      load();
    });
    $('[data-q]', el).focus();
  }

  function addMember() {
    const el = dialog('Add member', `<div class="form-grid two">
      <label class="field"><span class="required">Person</span><select data-field="login"><option value="">- Select a person -</option>${d.candidates.filter(c => !d.members.some(m => m.login.toLowerCase() === c.login.toLowerCase()))
        .map(c => `<option value="${esc(c.login)}">${esc(c.name)}</option>`).join('')}</select><small class="muted">Only the project owner can add members.</small><span class="field-error"></span></label>
      <label class="field"><span class="required">Role in this project</span><select data-field="role">${d.roles.map(r => `<option value="${esc(r.code)}"${r.code === 'SalespersonCaretaker' ? ' selected' : ''}>${esc(r.label)}</option>`).join('')}</select>
        <small class="muted">Caretakers and account owners show as the company's caretaker.</small><span class="field-error"></span></label></div>
      <div class="notice proj-info" data-info role="status">Choose a person to see whether they get temporary access.</div>`, 'Add member');
    $('[data-field=login]', el).addEventListener('change', async event => {
      const login = event.target.value, info = $('[data-info]', el);
      if (!login) { info.textContent = 'Choose a person to see whether they get temporary access.'; return; }
      const answer = await api('GET', `projects/${id}/reach?` + new URLSearchParams({login}));
      if (!answer.result.ok) { info.textContent = answer.result.message; return; }
      const name = event.target.selectedOptions[0].textContent;
      info.textContent = !d.project.open ? `${name} gets no temporary access: the project is not open.`
        : answer.temporary === 0 ? `${name} already owns ${answer.companies === 1 ? 'this company' : 'these companies'} through their own territory, so no temporary territory is needed.`
        : `${name} doesn't own ${answer.temporary === answer.companies ? (answer.companies === 1 ? 'this company' : 'these companies') : plural(answer.temporary, 'of the companies', 'of the companies')}, so a temporary territory gives them access while the project is open. It is taken away when the project closes or ends.`;
    });
    $('[data-ok]', el).addEventListener('click', async () => {
      const answer = await api('POST', `projects/${id}/members`, {login: $('[data-field=login]', el).value, role: $('[data-field=role]', el).value});
      if (!answer.result.ok) { refused(el, answer); return; }
      toast(answer.result.message);
      el.close();
      tab = 'members';
      load();
    });
  }

  async function closeProject() {
    if (!await ask({title: `Close ${d.project.name}?`, ok: 'Close project', danger: true,
      text: `${plural(d.losing, 'member loses', 'members lose')} their temporary access to ${plural(d.companyCount, 'company', 'companies')}; their activity links stay. You can't undo this.`})) return;
    const answer = await api('POST', `projects/${id}/close`, {});
    toast(answer.result.message, !answer.result.ok);
    if (answer.result.ok) load();
  }

  async function deleteProject() {
    if (!await ask({title: 'Are you sure you want to delete the project?', ok: 'Delete project', danger: true,
      text: `${d.project.name} disappears from Projects. ${d.losing ? plural(d.losing, 'member loses', 'members lose') + ` their temporary access to ${plural(d.companyCount, 'company', 'companies')}. ` : ''}Activities stay on each company. You can't undo this.`})) return;
    const answer = await api('POST', `projects/${id}/delete`, {});
    toast(answer.result.message, !answer.result.ok);
    if (answer.result.ok) location.href = 'projects.aspx';
  }

  page.addEventListener('click', async event => {
    const t = event.target.closest('[data-tab]');
    if (t) { showTab(t.dataset.tab); return; }
    if (event.target.closest('#add-companies')) { addCompanies(); return; }
    if (event.target.closest('#add-member')) { addMember(); return; }
    const activity = event.target.closest('[data-activity]');
    if (activity) { window.ACT.open(Number(activity.dataset.activity), load); return; }
    const rc = event.target.closest('[data-remove-company]');
    if (rc) {
      const c = d.companies.find(x => x.id === Number(rc.dataset.removeCompany));
      const temp = d.members.filter(m => m.reached.includes(c.name)).length;
      if (!await ask({title: 'Are you sure you want to remove the company?', ok: 'Remove company', danger: true,
        text: `${c.name} leaves this project. ${plural(temp, 'member loses', 'members lose')} temporary access to it; nothing else changes.`})) return;
      const answer = await api('POST', `projects/${id}/companies/${c.id}/remove`, {});
      toast(answer.result.message, !answer.result.ok);
      if (answer.result.ok) load();
      return;
    }
    const rm = event.target.closest('[data-remove-member]');
    if (rm) {
      const m = d.members.find(x => x.id === Number(rm.dataset.removeMember));
      if (!await ask({title: `Remove ${m.name} from the project?`, ok: 'Remove member', danger: true,
        text: m.reached.length ? `Their temporary territory is taken away, so they lose access to ${plural(m.reached.length, 'company', 'companies')}. Their activity links stay.` : 'They keep their own territory; only their project role goes.'})) return;
      const answer = await api('POST', `projects/${id}/members/${m.id}/remove`, {});
      toast(answer.result.message, !answer.result.ok);
      if (answer.result.ok) load();
    }
  });

  load();
})();
