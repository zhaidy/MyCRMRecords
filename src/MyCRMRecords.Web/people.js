(() => {
  'use strict';
  // people.aspx (Sales Admins): MyCRM's Users screens. CRM users: every person with a CRM profile (or, with Show
  // everyone, everyone MyCRMRecords knows), each with their MyCRM role, teams and codes; Edit sets them (the teams a role
  // takes follow MyCRM: one team for Sales, PP Specialist and CLETrainer; the team named after them for the managers'
  // roles; one or none for CSST; any for the others). Teams: create, rename, switch active, see who is in each.
  const {$, $$, esc, api, toast, ask, drawIcons} = window.WH;
  const {dialog, refused, text} = window.CRM;
  const page = $('#page');
  const TABS = [['users', 'CRM users'], ['teams', 'Teams']];
  let tab = location.hash.slice(1) === 'teams' ? 'teams' : 'users';
  let lists = null, people = null, teams = null, q = '', everyone = false, timer = 0;

  function shell() {
    $('#loading').hidden = true;
    page.hidden = false;
    page.innerHTML = `<div class="page-heading"><div><h1>CRM users</h1><p>People's MyCRM role and teams decide what they see and do in MyCRMRecords and in MyCRM.</p></div>
        <div class="heading-actions" id="actions"></div></div>
      <div class="page-tabs" role="tablist">${TABS.map(([key, label]) => `<button class="page-tab" role="tab" data-tab="${key}" aria-selected="${key === tab}">${esc(label)}</button>`).join('')}</div>
      <div id="tab"></div>`;
    show();
  }

  function show() {
    $$('.page-tab').forEach(b => b.setAttribute('aria-selected', String(b.dataset.tab === tab)));
    history.replaceState(null, '', '#' + tab);
    if (tab === 'teams') loadTeams(); else usersTab();
  }

  // ---- CRM users ----
  function usersTab() {
    $('#actions').innerHTML = lists.directory ? '<button class="button secondary" id="find-person"><span data-icon="search"></span>Find someone in Active Directory</button>' : '';
    $('#tab').innerHTML = `<section class="card"><div class="filters">
        <label class="search-box"><span data-icon="search"></span><input id="q" type="search" placeholder="Name, login, email, salesman or territory code, or role" value="${esc(q)}" aria-label="Search people"></label>
        <label class="check"><input type="checkbox" id="everyone"${everyone ? ' checked' : ''}>Show everyone (also people who are not CRM users)</label></div>
      <p class="muted small" id="count" style="margin:10px 0 0"></p><div id="rows"><p class="loading">Loading…</p></div></section>`;
    drawIcons(page);
    $('#q').addEventListener('input', event => { clearTimeout(timer); q = event.target.value; timer = setTimeout(loadPeople, 350); });
    $('#everyone').addEventListener('change', event => { everyone = event.target.checked; loadPeople(); });
    $('#find-person')?.addEventListener('click', findPerson);
    loadPeople();
  }

  async function loadPeople() {
    const answer = await api('GET', 'crmusers?' + new URLSearchParams({q, all: everyone ? '1' : ''}));
    if (!answer.result.ok) { $('#rows').innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3></div>`; return; }
    people = answer.people;
    const shown = people.slice(0, 300);
    $('#count').textContent = `${people.length} ${people.length === 1 ? 'person' : 'people'}${people.length > shown.length ? `; the first ${shown.length} are shown: search to find others` : ''}.`;
    $('#rows').innerHTML = shown.length ? `<div class="table-wrap" style="margin-top:10px"><table class="data"><thead><tr><th></th><th>Person</th><th>Role</th><th>Teams</th><th>Codes</th><th class="num">Territories</th></tr></thead><tbody>
      ${shown.map(p => `<tr class="${p.isCrmUser ? '' : 'inactive'}"><td class="nowrap"><button type="button" class="button secondary small-btn" data-person="${esc(p.login)}"><span data-icon="edit"></span>Edit</button></td>
        <td class="wide-text"><b>${esc(p.name)}</b><span class="sub">${esc(p.login)}${p.email ? ' · ' + esc(p.email) : ''}</span></td>
        <td>${p.isCrmUser ? esc(p.roleLabel) : '<span class="muted">Not a CRM user</span>'}</td>
        <td class="small">${p.teams.map(esc).join('<br>') || '<span class="muted">-</span>'}</td>
        <td class="small">${p.salesmanCode ? 'Salesman ' + esc(p.salesmanCode) : ''}${p.territoryCode ? `<span class="sub">Territory code ${esc(p.territoryCode)}</span>` : ''}</td>
        <td class="num">${p.territories || ''}</td></tr>`).join('')}</tbody></table></div>`
      : `<div class="empty" style="margin-top:10px"><h3>Nobody found</h3><p class="muted">${q ? 'Nobody matches the search.' : 'No CRM user yet.'}</p></div>`;
    drawIcons($('#rows'));
  }

  /** Finds someone in Active Directory, adds them to the people MyCRMRecords knows, and opens their editor. */
  function findPerson() {
    const el = dialog('Find someone in Active Directory', `<label class="search-box" style="margin:10px 0"><span data-icon="search"></span><input data-q type="search" placeholder="Name or login, e.g. Jamie or jlee" autocomplete="off" aria-label="Name or login"></label>
      <div data-found style="max-height:320px;overflow:auto"></div>`);
    $('[data-submit]', el).hidden = true;
    let found = [];
    $('[data-q]', el).addEventListener('input', event => {
      clearTimeout(timer);
      const term = event.target.value.trim();
      if (term.length < 2) { $('[data-found]', el).innerHTML = ''; return; }
      timer = setTimeout(async () => {
        const answer = await api('GET', 'people/search?' + new URLSearchParams({q: term}));
        if (!answer.result.ok) { $('[data-found]', el).innerHTML = `<p class="no">${esc(answer.result.message)}</p>`; return; }
        found = answer.people;
        $('[data-found]', el).innerHTML = found.length ? found.map((p, i) => `<button type="button" class="person" data-found-person="${i}" style="display:block;width:100%;text-align:left;padding:8px 10px;border:0;background:none;border-bottom:1px solid var(--border-soft)">
          <b>${esc(p.displayName)}</b><span class="muted small" style="display:block">${esc(p.login)} · ${esc(p.email)}</span></button>`).join('') : '<p class="muted">Nobody found.</p>';
      }, 350);
    });
    $('[data-found]', el).addEventListener('click', async event => {
      const pick = event.target.closest('[data-found-person]');
      if (!pick) return;
      const p = found[Number(pick.dataset.foundPerson)];
      const answer = await api('POST', 'people', {login: p.login, displayName: p.displayName, email: p.email});
      if (!answer.result.ok) { refused(el, answer); return; }
      el.close();
      personDialog(p.login);
    });
    $('[data-q]', el).focus();
  }

  /** A person's MyCRM role, teams and details. */
  async function personDialog(login) {
    const answer = await api('GET', 'crmusers/person?' + new URLSearchParams({login}));
    if (!answer.result.ok) { toast(answer.result.message, true); return; }
    const p = answer.person;
    let teamList = answer.teams;
    let chosen = p.teams.slice();
    const footer = p.isCrmUser || answer.territories.length ? '<button type="button" class="button danger-btn" data-remove-person>Remove CRM user and territories</button>' : '';
    const el = dialog(p.name, `<p class="muted" style="margin-top:-6px">${esc(p.login)}${p.email ? ' · ' + esc(p.email) : ''}${p.isCrmUser ? '' : ' · not a CRM user yet'}</p>
      <div class="form-grid two">
        <label class="field wide"><span class="required">Role</span><select data-field="role"><option value="">Choose a role…</option>
          ${lists.roles.map(r => `<option value="${esc(r.code)}"${r.code === p.role ? ' selected' : ''}>${esc(r.label)}</option>`).join('')}</select><span class="field-error"></span></label>
        <div class="field wide" data-field="teams"><span data-teams-label>Team</span><div data-teams></div><span class="field-error"></span></div>
        <div class="notice warn wide" data-no-team hidden></div>
        ${text('designation', 'Designation', p.designation, {max: 50})}${text('phoneNumber', 'Phone Number', p.phoneNumber, {max: 20})}
        ${text('salesmanCode', 'Salesman Code', p.salesmanCode, {max: 50})}${text('territoryCode', 'Territory Code', p.territoryCode, {max: 50})}
        ${answer.territories.length ? `<div class="wide"><div class="fact-label">Territories</div>${answer.territories.map(t => `<a href="territories.aspx?id=${t.id}" style="margin-right:12px">${esc(t.title)}</a>`).join('')}</div>` : ''}
      </div>`, {wide: true, footer});
    const roleSelect = $('[data-field=role]', el);
    const kind = role => !role ? 'none' : lists.oneTeamRoles.includes(role) ? 'one' : lists.managerRoles.includes(role) ? 'manager' : role === 'CSST' ? 'csst' : 'many';
    const keep = () => {
      const single = $('[data-team-select]', el);
      if (single) chosen = single.value ? [Number(single.value)] : [];
      else if ($$('[data-team-check]', el).length) chosen = $$('[data-team-check]', el).filter(c => c.checked).map(c => Number(c.value));
    };
    const drawTeams = () => {
      const k = kind(roleSelect.value), box = $('[data-teams]', el), label = $('[data-teams-label]', el);
      label.className = k === 'one' || k === 'manager' ? 'required' : '';
      if (k === 'none') { box.innerHTML = '<p class="muted small" style="margin:4px 0 0">Choose a role first.</p>'; warn(); return; }
      if (k === 'many') {
        label.textContent = 'Teams';
        box.innerHTML = `<div class="row-first" style="margin:4px 0 6px"><button type="button" class="text-button" data-all-teams>Select All</button><button type="button" class="text-button" data-no-teams>Unselect All</button></div>
          <div class="role-checks" style="display:flex;flex-wrap:wrap;gap:6px 18px">${teamList.map(t => `<label class="check"><input type="checkbox" data-team-check value="${t.id}"${chosen.includes(t.id) ? ' checked' : ''}>${esc(t.name)}</label>`).join('') || '<span class="muted">No active team.</span>'}</div>`;
      } else {
        label.textContent = 'Team';
        const offered = k === 'manager' ? teamList.filter(t => t.namedAfter || chosen.includes(t.id)) : teamList;
        box.innerHTML = `<span style="display:flex;gap:6px;flex-wrap:wrap"><select data-team-select style="flex:1 1 240px"><option value="">-</option>
            ${offered.map(t => `<option value="${t.id}"${chosen[0] === t.id ? ' selected' : ''}>${esc(t.name)}</option>`).join('')}</select>
          ${k === 'manager' ? '<button type="button" class="button secondary small-btn" data-create-team><span data-icon="plus"></span>Create team</button>' : ''}</span>
          ${k === 'manager' ? `<small class="muted">A manager is put in the team named after them (${esc(p.name)}).</small>` : k === 'csst' ? '<small class="muted">One team, or none.</small>' : ''}`;
      }
      drawIcons(box);
      warn();
    };
    const warn = () => {
      keep();
      const k = kind(roleSelect.value), notice = $('[data-no-team]', el);
      notice.hidden = !((k === 'many') && chosen.length === 0);
      notice.textContent = `With no team, ${p.name} is no longer a CRM user (as in MyCRM): tick at least one team to keep them.`;
    };
    drawTeams();
    roleSelect.addEventListener('change', () => { keep(); drawTeams(); });
    el.addEventListener('change', event => { if (event.target.matches('[data-team-check],[data-team-select]')) warn(); });
    el.addEventListener('click', async event => {
      if (event.target.closest('[data-all-teams]')) { $$('[data-team-check]', el).forEach(c => { c.checked = true; }); warn(); return; }
      if (event.target.closest('[data-no-teams]')) { $$('[data-team-check]', el).forEach(c => { c.checked = false; }); warn(); return; }
      if (event.target.closest('[data-create-team]')) {
        if (!await ask({title: `Create the team ${p.name}?`, text: 'A manager is put in the team named after them.', ok: 'Create team'})) return;
        const result = await api('POST', 'crmusers/person/team?' + new URLSearchParams({login}), {});
        toast(result.result.message, !result.result.ok);
        if (!result.result.ok) return;
        const again = await api('GET', 'crmusers/person?' + new URLSearchParams({login}));
        if (again.result.ok) teamList = again.teams;
        chosen = [result.result.id];
        drawTeams();
        return;
      }
      if (event.target.closest('[data-remove-person]')) {
        if (!await ask({title: `Remove ${p.name} as a CRM user?`, text: `Their CRM user records become inactive and their ${answer.territories.length} territor${answer.territories.length === 1 ? 'y is' : 'ies are'} taken away with their companies (all kept, marked as taken away).`, ok: 'Remove', danger: true})) return;
        const result = await api('POST', 'crmusers/person/remove?' + new URLSearchParams({login}), {});
        toast(result.result.message, !result.result.ok);
        if (result.result.ok) { el.close(); loadPeople(); }
      }
    });
    $('form', el).addEventListener('submit', async event => {
      event.preventDefault();
      keep();
      const v = name => $(`[data-field="${name}"]`, el).value;
      const result = await api('POST', 'crmusers/person?' + new URLSearchParams({login}), {
        role: roleSelect.value, teams: chosen, designation: v('designation'), phoneNumber: v('phoneNumber'), salesmanCode: v('salesmanCode'), territoryCode: v('territoryCode'),
      });
      if (!result.result.ok) { refused(el, result); return; }
      toast(result.result.message);
      el.close();
      loadPeople();
    });
    roleSelect.focus();
  }

  // ---- Teams ----
  async function loadTeams() {
    $('#actions').innerHTML = '<button class="button primary" id="new-team"><span data-icon="plus"></span>New team</button>';
    drawIcons($('#actions'));
    $('#new-team').addEventListener('click', () => teamDialog(null));
    $('#tab').innerHTML = '<section class="card"><p class="loading">Loading…</p></section>';
    const answer = await api('GET', 'teams');
    if (!answer.result.ok) { $('#tab').innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3></div>`; return; }
    teams = answer.teams;
    $('#tab').innerHTML = `<section class="card"><p class="muted">Teams are named after their manager. Teams are never deleted: an inactive team is no longer offered.</p>
      <label class="search-box" style="margin-top:10px"><span data-icon="search"></span><input id="team-q" type="search" placeholder="Name or short name" aria-label="Search teams"></label>
      <div id="team-rows"></div></section>`;
    drawIcons($('#tab'));
    $('#team-q').addEventListener('input', drawTeamRows);
    drawTeamRows();
  }

  function drawTeamRows() {
    const term = ($('#team-q').value || '').trim().toLowerCase();
    const rows = teams.filter(t => !term || `${t.name} ${t.shortName}`.toLowerCase().includes(term));
    $('#team-rows').innerHTML = rows.length ? `<div class="table-wrap" style="margin-top:10px"><table class="data"><thead><tr><th></th><th>Team</th><th class="num">People</th><th>Active</th></tr></thead><tbody>
      ${rows.map(t => `<tr class="${t.isActive ? '' : 'inactive'}"><td class="nowrap"><div class="row-first"><button type="button" class="button secondary small-btn" data-team="${t.id}"><span data-icon="edit"></span>Edit</button>
          <button type="button" class="button secondary small-btn" data-members="${t.id}"><span data-icon="users"></span>People</button></div></td>
        <td class="wide-text"><b>${esc(t.name)}</b>${t.shortName ? `<span class="sub">${esc(t.shortName)}</span>` : ''}</td>
        <td class="num">${t.members}</td>
        <td><label class="check"><input type="checkbox" data-active="${t.id}"${t.isActive ? ' checked' : ''}>Active</label></td></tr>`).join('')}</tbody></table></div>`
      : '<p class="muted" style="margin-top:10px">No team matches.</p>';
    drawIcons($('#team-rows'));
  }

  function teamDialog(t) {
    const el = dialog(t ? `Team ${t.name}` : 'New team', `<div class="form-grid two">${text('name', 'Name', t ? t.name : '', {required: true, max: 255, cls: 'wide'})}
        ${text('shortName', 'Short Name', t ? t.shortName : '', {max: 50})}<label class="check"><input type="checkbox" data-field="isActive"${!t || t.isActive ? ' checked' : ''}>Active</label></div>`);
    $('form', el).addEventListener('submit', async event => {
      event.preventDefault();
      const result = await api('POST', 'teams/' + (t ? t.id : 0), {name: $('[data-field=name]', el).value, shortName: $('[data-field=shortName]', el).value, isActive: $('[data-field=isActive]', el).checked});
      if (!result.result.ok) { refused(el, result); return; }
      toast(result.result.message);
      el.close();
      loadTeams();
    });
    $('[data-field=name]', el).focus();
  }

  async function membersDialog(t) {
    const answer = await api('GET', 'teams/' + t.id);
    if (!answer.result.ok) { toast(answer.result.message, true); return; }
    const el = dialog(t.name, `<p class="muted">Users assigned to this team.</p>${answer.members.length ? `<ul class="check-lines">${answer.members.map(m => `<li><b>${esc(m.name)}</b> <span class="pill">${esc(m.role)}</span></li>`).join('')}</ul>` : '<p class="muted">Nobody.</p>'}`);
    $('[data-submit]', el).hidden = true;
    $('button[value=cancel]', el).textContent = 'Close';
  }

  page.addEventListener('click', event => {
    const t = event.target.closest('[data-tab]');
    if (t) { tab = t.dataset.tab; show(); return; }
    const person = event.target.closest('[data-person]');
    if (person) { personDialog(person.dataset.person); return; }
    const team = event.target.closest('[data-team]');
    if (team) { teamDialog(teams.find(x => x.id === Number(team.dataset.team))); return; }
    const members = event.target.closest('[data-members]');
    if (members) membersDialog(teams.find(x => x.id === Number(members.dataset.members)));
  });
  page.addEventListener('change', async event => {
    const active = event.target.closest('[data-active]');
    if (!active) return;
    const t = teams.find(x => x.id === Number(active.dataset.active));
    const result = await api('POST', 'teams/' + t.id, {name: t.name, shortName: t.shortName, isActive: active.checked});
    toast(result.result.message, !result.result.ok);
    if (result.result.ok) t.isActive = active.checked; else active.checked = !active.checked;
    drawTeamRows();
  });

  (async () => {
    const answer = await api('GET', 'lists');
    $('#loading').hidden = true;
    if (!answer.result.ok) { page.hidden = false; page.innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3></div>`; return; }
    lists = answer;
    shell();
  })();
})();
