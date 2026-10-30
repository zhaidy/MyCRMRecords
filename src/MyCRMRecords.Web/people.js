(() => {
  'use strict';
  // people.aspx (Sales Admins): MyCRM's Users screens. CRM users: every person with a CRM profile (or, with Show
  // everyone, everyone MyCRMRecords knows), each with their MyCRM role, teams and codes; Edit sets them (the teams a role
  // takes follow MyCRM: one team for Sales, PP Specialist and CLETrainer; the team named after them for the managers'
  // roles; one or none for CSST; any for the others). Teams: create, rename, switch active, see who is in each.
  // Laid out as the approved design: a large title with the count, the tabs People and Teams in one glass capsule, the
  // filters as glass capsules (search, Role, Show everyone), one line per person or team in a solid card, and the person
  // edited in a glass panel at the side. On a phone a card per person, the role in a sheet, the panel on the whole screen.
  const {$, $$, esc, api, toast, ask, drawIcons, pager, showErrors} = window.WH;
  const {dialog, refused} = window.CRM;
  const page = $('#page');
  const PAGE = 50;
  let tab = location.hash.slice(1) === 'teams' ? 'teams' : 'users';
  let lists = null, people = null, teams = null, q = '', role = '', everyone = false, skip = 0, timer = 0, teamCount = null;

  const plural = (n, one, many) => `${n.toLocaleString('en-SG')} ${n === 1 ? one : many}`;
  const roleLabel = code => { const r = (lists.roles || []).find(x => x.code === code); return r ? r.label : code; };

  function shell() {
    $('#loading').hidden = true;
    page.hidden = false;
    page.innerHTML = `<div class="page-heading users-heading"><div class="users-title"><h1>CRM users</h1><p id="count">&nbsp;</p></div>
        <div class="heading-actions" id="actions"></div></div>
      <div class="users-bar">
        <div class="tabs users-tabs" role="tablist" aria-label="CRM users sections" id="tabs"></div>
        <div class="users-filters" id="filters" role="search"></div></div>
      <div id="tab"></div>`;
    show();
  }

  function drawTabs() {
    $('#tabs').innerHTML = [['users', `People${people ? ' · ' + people.length.toLocaleString('en-SG') : ''}`], ['teams', `Teams${teamCount !== null ? ' · ' + teamCount : ''}`]].map(([key, label]) =>
      `<button type="button" class="tab" role="tab" data-tab="${key}" aria-selected="${key === tab}">${esc(label)}</button>`).join('');
  }

  function show() {
    drawTabs();
    history.replaceState(null, '', '#' + tab);
    if (tab === 'teams') loadTeams(); else usersTab();
  }

  // ---- CRM users ----
  function usersTab() {
    $('#actions').innerHTML = lists.directory ? `<div class="action-capsule" role="group" aria-label="People actions"><button type="button" class="capsule-button" id="find-person" title="Find someone in Active Directory">
      <span data-icon="search"></span><span class="action-label">Find someone in Active Directory</span></button></div>` : '';
    $('#filters').innerHTML = `
      <label class="search-box"><span data-icon="search"></span><input id="q" type="search" placeholder="Name, login, email, code or role" value="${esc(q)}" aria-label="Search people"></label>
      <label class="filter-capsule users-wide"><span>Role</span><select id="role"><option value="">All roles</option>${lists.roles.map(r => `<option value="${esc(r.code)}"${r.code === role ? ' selected' : ''}>${esc(r.label)}</option>`).join('')}</select></label>
      <button type="button" class="filter-capsule users-role-button" id="open-roles" aria-haspopup="dialog"><span>Role:</span><b id="role-label"></b><span data-icon="arrow-right" class="users-caret"></span></button>
      <label class="filter-capsule users-switch-capsule"><input type="checkbox" role="switch" class="users-switch" id="everyone"${everyone ? ' checked' : ''}><span>Show everyone</span></label>`;
    $('#tab').innerHTML = `<section class="users-list" aria-label="People"><div id="rows"><p class="loading users-pad">Loading…</p></div><div id="cards" class="file-cards"></div>
      <div class="pager" id="pager" hidden></div></section>`;
    drawIcons(page);
    $('#q').addEventListener('input', event => { clearTimeout(timer); q = event.target.value; timer = setTimeout(() => { skip = 0; loadPeople(); }, 350); });
    $('#role').addEventListener('change', event => { role = event.target.value; skip = 0; drawPeople(); });
    $('#open-roles').addEventListener('click', roleSheet);
    $('#everyone').addEventListener('change', event => { everyone = event.target.checked; skip = 0; loadPeople(); });
    $('#find-person')?.addEventListener('click', findPerson);
    loadPeople();
  }

  async function loadPeople() {
    const answer = await api('GET', 'crmusers?' + new URLSearchParams({q, all: everyone ? '1' : ''}));
    if (!$('#rows')) return;
    if (!answer.result.ok) { $('#rows').innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3></div>`; $('#cards').innerHTML = ''; return; }
    people = answer.people;
    drawTabs();
    drawPeople();
  }

  function drawPeople() {
    if (!people || !$('#rows')) return;
    const shown = people.filter(p => !role || (p.isCrmUser && p.role === role));
    skip = Math.min(skip, Math.max(0, Math.floor((shown.length - 1) / PAGE) * PAGE));
    const rows = shown.slice(skip, skip + PAGE);
    $('#count').innerHTML = `<b>${plural(shown.length, 'person', 'people')}</b>${role ? ' · ' + esc(roleLabel(role)) : ''}<span class="users-note"> · ${everyone ? "everyone, including people who aren't CRM users" : 'CRM users only'} · A to Z</span>`;
    $('#role-label').textContent = role ? roleLabel(role) : 'All';
    $('#role').value = role;
    const teamTags = p => p.teams.length ? p.teams.slice(0, 2).map(t => `<span class="users-tag" title="${esc(t)}">${esc(t)}</span>`).join('')
      + (p.teams.length > 2 ? `<span class="users-more" title="${esc(p.teams.slice(2).join(', '))}">+${p.teams.length - 2}</span>` : '') : '<span class="muted">—</span>';
    const rolePill = p => p.isCrmUser ? `<span class="users-role">${esc(p.roleLabel)}</span>` : '<span class="users-not">Not a CRM user</span>';
    const empty = `<div class="empty"><h3>Nobody found</h3><p class="muted">${q || role ? `No one matches.${everyone ? '' : ' Try another name, or switch on Show everyone.'}` : 'No CRM user yet.'}</p></div>`;
    $('#rows').innerHTML = rows.length ? `<table class="data users-table"><thead><tr><th class="u-name">Name</th><th class="u-role">Role</th><th>Teams</th><th class="u-code">Salesman code</th>
        <th class="u-code">Territory code</th><th class="num u-terr">Territories</th><th class="u-edit"><span class="sr-only">Edit</span></th></tr></thead><tbody>
      ${rows.map(p => `<tr class="${p.isCrmUser ? '' : 'inactive'}" data-row="${esc(p.login)}">
        <td class="u-name" title="${esc(p.name)} · ${esc(p.login)}${p.email ? ' · ' + esc(p.email) : ''}"><b>${esc(p.name)}</b><span class="users-login"> · ${esc(p.login)}</span></td>
        <td class="u-role">${rolePill(p)}</td><td class="u-teams">${teamTags(p)}</td>
        <td class="u-code">${esc(p.salesmanCode) || '<span class="muted">—</span>'}</td><td class="u-code">${esc(p.territoryCode) || '<span class="muted">—</span>'}</td>
        <td class="num u-terr">${p.territories || '<span class="muted">—</span>'}</td>
        <td class="u-edit"><button type="button" class="button secondary small-btn" data-person="${esc(p.login)}" aria-label="${p.isCrmUser ? 'Edit' : 'Add as CRM user:'} ${esc(p.name)}">${p.isCrmUser ? 'Edit' : 'Add'}</button></td></tr>`).join('')}</tbody></table>`
      : empty;
    $('#cards').innerHTML = rows.map(p => `<button type="button" class="file-card users-card" data-person="${esc(p.login)}" aria-label="Edit ${esc(p.name)}">
        <span class="file-card-body"><span class="file-card-top"><b>${esc(p.name)}</b>${rolePill(p)}</span>
          ${p.teams.length ? `<span class="users-card-tags">${teamTags(p)}</span>` : ''}
          <span class="file-card-line">${esc([p.login, p.salesmanCode ? 'Salesman ' + p.salesmanCode : '', p.territoryCode ? 'Territory code ' + p.territoryCode : '', p.territories ? plural(p.territories, 'territory', 'territories') : ''].filter(Boolean).join(' · '))}</span></span>
        <span data-icon="arrow-right" class="section-chevron"></span></button>`).join('') || empty;
    drawIcons($('#cards'));
    pager($('#pager'), shown.length, skip, PAGE);
  }

  /** The phone's role filter: a sheet listing the roles, with how many people of each are listed. */
  function roleSheet() {
    const sheet = document.createElement('dialog');
    sheet.className = 'companies-sheet';
    sheet.setAttribute('aria-labelledby', 'role-sheet-title');
    const count = code => people ? people.filter(p => !code || (p.isCrmUser && p.role === code)).length : '';
    sheet.innerHTML = `<form method="dialog" class="sheet-form"><span class="sheet-grabber" aria-hidden="true"></span>
      <div class="sheet-head"><button type="button" class="icon-button" value="cancel" aria-label="Close role list"><span data-icon="close"></span></button>
        <h2 id="role-sheet-title">Role</h2><span class="users-sheet-spacer" aria-hidden="true"></span></div>
      <div class="sheet-group users-sheet-list" role="radiogroup" aria-labelledby="role-sheet-title">${[{code: '', label: 'All roles'}].concat(lists.roles).map(r =>
        `<button type="button" role="radio" class="users-sheet-row" data-role="${esc(r.code)}" aria-checked="${r.code === role}"><span>${esc(r.label)}</span><span class="muted">${count(r.code)}</span><span data-icon="check" class="users-check"></span></button>`).join('')}</div>
      <label class="users-sheet-switch"><span>Show everyone (also people who are not CRM users)</span><input type="checkbox" role="switch" class="users-switch" id="sheet-everyone"${everyone ? ' checked' : ''}></label></form>`;
    document.body.append(sheet);
    drawIcons(sheet);
    $('button[value=cancel]', sheet).addEventListener('click', () => sheet.close());
    sheet.addEventListener('close', () => sheet.remove());
    sheet.addEventListener('click', event => {
      if (event.target === sheet) { sheet.close(); return; }
      const pick = event.target.closest('[data-role]');
      if (!pick) return;
      role = pick.dataset.role;
      skip = 0;
      sheet.close();
      drawPeople();
    });
    $('#sheet-everyone', sheet).addEventListener('change', event => {
      everyone = event.target.checked;
      $('#everyone').checked = everyone;
      skip = 0;
      sheet.close();
      loadPeople();
    });
    sheet.showModal();
  }

  /** Finds someone in Active Directory, adds them to the people MyCRMRecords knows, and opens their editor. */
  function findPerson() {
    const el = dialog('Find someone in Active Directory', `<label class="search-box" style="margin:10px 0"><span data-icon="search"></span><input data-q type="search" placeholder="Name or login, e.g. Jamie or jlee" autocomplete="off" aria-label="Name or login"></label>
      <div data-found class="users-found"></div>`);
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
        $('[data-found]', el).innerHTML = found.length ? found.map((p, i) => `<button type="button" class="users-sheet-row" data-found-person="${i}">
          <span><b>${esc(p.displayName)}</b><span class="muted small users-sub">${esc(p.login)} · ${esc(p.email)}</span></span></button>`).join('') : '<p class="muted users-pad">Nobody found.</p>';
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

  /** A person's MyCRM role, teams and details, in a glass panel at the side (the whole screen on a phone). */
  async function personDialog(login) {
    const answer = await api('GET', 'crmusers/person?' + new URLSearchParams({login}));
    if (!answer.result.ok) { toast(answer.result.message, true); return; }
    const p = answer.person;
    let teamList = answer.teams;
    let chosen = p.teams.slice();
    $$('tr[data-row]', page).forEach(tr => tr.classList.toggle('editing', tr.dataset.row === login));
    const row = (name, label, value, max, type = 'text') => `<label class="field"><span>${esc(label)}</span><input data-field="${name}" type="${type}" value="${esc(value ?? '')}" maxlength="${max}"><span class="field-error"></span></label>`;
    const el = document.createElement('dialog');
    el.className = 'users-panel';
    el.setAttribute('aria-label', `Edit ${p.name}`);
    el.innerHTML = `<form class="users-panel-form" novalidate>
      <div class="users-panel-head"><button type="button" class="icon-button" value="cancel" aria-label="Close"><span data-icon="close"></span></button>
        <div class="users-panel-title"><h2>${esc(p.name)}</h2><span>${esc(p.login)}${p.email ? ' · ' + esc(p.email) : ''}</span></div>
        <button type="submit" class="button primary" data-submit>${p.isCrmUser ? 'Save' : 'Add as CRM user'}</button></div>
      ${p.isCrmUser ? '' : '<span class="users-not users-panel-badge">Not a CRM user: pick a role and team to add them</span>'}
      <div class="users-panel-body">
        <div><div class="users-group"><label class="field"><span class="required">Role</span><select data-field="role"><option value="">Choose a role…</option>
          ${lists.roles.map(r => `<option value="${esc(r.code)}"${r.code === p.role ? ' selected' : ''}>${esc(r.label)}</option>`).join('')}</select><span class="field-error"></span></label></div>
          <p class="users-hint" data-role-hint></p></div>
        <fieldset class="field users-teams" data-field="teams"><legend class="users-label" data-teams-label>Team</legend><div data-teams></div><span class="field-error"></span>
          <div class="notice warn" data-no-team hidden></div></fieldset>
        <div><p class="users-label">Details</p><div class="users-group">
          ${row('designation', 'Designation', p.designation, 50)}${row('phoneNumber', 'Phone Number', p.phoneNumber, 20, 'tel')}
          ${row('salesmanCode', 'Salesman Code', p.salesmanCode, 50)}${row('territoryCode', 'Territory Code', p.territoryCode, 50)}</div></div>
        ${answer.territories.length ? `<div><p class="users-label">Territories</p><div class="users-group users-links">${answer.territories.map(t => `<a href="territories.aspx?id=${t.id}">${esc(t.title)}<span data-icon="arrow-right"></span></a>`).join('')}</div></div>` : ''}
        ${p.isCrmUser || answer.territories.length ? `<section class="users-remove" aria-label="Remove access"><h3>Remove access</h3>
          <p class="muted">Takes away their CRM access and territories. You will be asked to confirm.</p>
          <button type="button" class="button danger-btn" data-remove-person>Remove CRM user and territories</button></section>` : ''}
        <div class="notice error" data-notice hidden></div>
      </div></form>`;
    document.body.append(el);
    const close = () => el.close();
    $('button[value=cancel]', el).addEventListener('click', close);
    el.addEventListener('close', () => { el.remove(); $$('tr.editing', page).forEach(tr => tr.classList.remove('editing')); });
    el.addEventListener('click', event => { if (event.target === el) close(); });
    const roleSelect = $('[data-field=role]', el);
    const kind = r => !r ? 'none' : lists.oneTeamRoles.includes(r) ? 'one' : lists.managerRoles.includes(r) ? 'manager' : r === 'CSST' ? 'csst' : 'many';
    const hints = {none: '', one: 'This role belongs to one team.', manager: 'Managers look after their own team.', csst: 'One team, or none.', many: 'This role can work across several teams.'};
    const keep = () => {
      const radios = $$('[data-team-radio]', el), checks = $$('[data-team-check]', el);
      if (radios.length) { const on = radios.find(r => r.checked); chosen = on && on.value ? [Number(on.value)] : []; }
      else if (checks.length) chosen = checks.filter(c => c.checked).map(c => Number(c.value));
    };
    const option = (type, t, on) => `<label class="users-option${on ? ' on' : ''}"><input type="${type}" ${type === 'radio' ? 'name="person-team" data-team-radio' : 'data-team-check'} value="${t ? t.id : ''}"${on ? ' checked' : ''}><span>${esc(t ? t.name : 'None')}</span></label>`;
    const drawTeams = () => {
      const k = kind(roleSelect.value), box = $('[data-teams]', el), label = $('[data-teams-label]', el);
      $('[data-role-hint]', el).textContent = hints[k];
      label.className = 'users-label' + (k === 'one' || k === 'manager' ? ' required' : '');
      if (k === 'none') { label.textContent = 'Team'; box.innerHTML = '<p class="muted users-pad-s">Choose a role first.</p>'; warn(); return; }
      if (k === 'many') {
        label.textContent = 'Teams · pick any';
        box.innerHTML = `<div class="users-select-all"><button type="button" class="text-button" data-all-teams>Select All</button><button type="button" class="text-button" data-no-teams>Unselect All</button></div>
          <div class="users-options">${teamList.map(t => option('checkbox', t, chosen.includes(t.id))).join('') || '<span class="muted">No active team.</span>'}</div>`;
      } else {
        label.textContent = k === 'manager' ? 'Team' : 'Team · pick one';
        const offered = k === 'manager' ? teamList.filter(t => t.namedAfter || chosen.includes(t.id)) : teamList;
        box.innerHTML = `<div class="users-options">${k === 'csst' ? option('radio', null, !chosen.length) : ''}${offered.map(t => option('radio', t, chosen[0] === t.id)).join('')}</div>
          ${k === 'manager' ? `<div class="users-manager">${offered.length ? '' : `<span>Their team: <b>none yet</b>. No team named after them yet.</span>`}
            <span class="muted small">A manager is put in the team named after them (${esc(p.name)}).</span>
            ${offered.some(t => t.namedAfter) ? '' : '<button type="button" class="button secondary small-btn" data-create-team><span data-icon="plus"></span>Create team</button>'}</div>` : ''}`;
      }
      drawIcons(box);
      warn();
    };
    const warn = () => {
      keep();
      $$('.users-option', el).forEach(o => o.classList.toggle('on', $('input', o).checked));
      const k = kind(roleSelect.value), notice = $('[data-no-team]', el);
      notice.hidden = !((k === 'many') && chosen.length === 0);
      notice.textContent = `With no team, ${p.name} is no longer a CRM user (as in MyCRM): tick at least one team to keep them.`;
    };
    drawTeams();
    drawIcons(el);
    roleSelect.addEventListener('change', () => { keep(); drawTeams(); });
    el.addEventListener('change', event => { if (event.target.matches('[data-team-check],[data-team-radio]')) warn(); });
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
      showErrors(el, []);
      const v = name => $(`[data-field="${name}"]`, el).value;
      const result = await api('POST', 'crmusers/person?' + new URLSearchParams({login}), {
        role: roleSelect.value, teams: chosen, designation: v('designation'), phoneNumber: v('phoneNumber'), salesmanCode: v('salesmanCode'), territoryCode: v('territoryCode'),
      });
      if (!result.result.ok) { refused(el, result); return; }
      toast(result.result.message);
      el.close();
      loadPeople();
    });
    el.showModal();
    roleSelect.focus();
  }

  // ---- Teams ----
  async function loadTeams() {
    $('#actions').innerHTML = '<button type="button" class="button primary" id="new-team" title="New team"><span data-icon="plus"></span><span class="action-label">New team</span></button>';
    drawIcons($('#actions'));
    $('#new-team').addEventListener('click', () => teamDialog(null));
    $('#filters').innerHTML = '<label class="search-box"><span data-icon="search"></span><input id="team-q" type="search" placeholder="Team name or short name" aria-label="Search teams"></label>';
    drawIcons($('#filters'));
    $('#tab').innerHTML = '<section class="users-list"><p class="loading users-pad">Loading…</p></section>';
    const answer = await api('GET', 'teams');
    if (tab !== 'teams') return;
    if (!answer.result.ok) { $('#tab').innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3></div>`; return; }
    teams = answer.teams;
    teamCount = teams.length;
    drawTabs();
    $('#tab').innerHTML = '<section class="users-list" aria-label="Teams"><div id="team-rows"></div><div id="team-cards" class="file-cards"></div></section>';
    $('#team-q').addEventListener('input', drawTeamRows);
    drawTeamRows();
  }

  function drawTeamRows() {
    const term = ($('#team-q').value || '').trim().toLowerCase();
    const rows = teams.filter(t => !term || `${t.name} ${t.shortName}`.toLowerCase().includes(term));
    $('#count').innerHTML = `<b>${plural(rows.length, 'team', 'teams')}</b><span class="users-note"> · Teams are named after their manager. They are never deleted: an inactive team is no longer offered.</span>`;
    const toggle = t => `<label class="users-active"><input type="checkbox" role="switch" class="users-switch" data-active="${t.id}"${t.isActive ? ' checked' : ''} aria-label="${esc(t.name)} active"><span>${t.isActive ? 'On' : 'Off'}</span></label>`;
    const empty = '<div class="empty"><h3>Nothing to show</h3><p class="muted">No team matches. Try another name, or create a new team.</p></div>';
    $('#team-rows').innerHTML = rows.length ? `<table class="data users-table"><thead><tr><th class="u-name">Name</th><th>Short name</th><th class="u-active">Active</th><th class="num u-terr">People</th><th class="u-edit u-edit-2"><span class="sr-only">Edit</span></th></tr></thead><tbody>
      ${rows.map(t => `<tr class="${t.isActive ? '' : 'inactive'}"><td class="u-name" title="${esc(t.name)}"><b>${esc(t.name)}</b></td><td>${esc(t.shortName) || '<span class="muted">—</span>'}</td>
        <td class="u-active">${toggle(t)}</td>
        <td class="num u-terr"><button type="button" class="link-button users-members" data-members="${t.id}" aria-label="People in ${esc(t.name)}">${t.members}</button></td>
        <td class="u-edit u-edit-2"><span class="users-row-actions"><button type="button" class="button secondary small-btn" data-members="${t.id}">People</button><button type="button" class="button secondary small-btn" data-team="${t.id}" aria-label="Edit team ${esc(t.name)}">Edit</button></span></td></tr>`).join('')}</tbody></table>`
      : empty;
    $('#team-cards').innerHTML = rows.map(t => `<div class="file-card users-team-card${t.isActive ? '' : ' inactive'}">
        <button type="button" class="users-team-open" data-team="${t.id}" aria-label="Edit team ${esc(t.name)}"><span class="file-card-body"><span class="file-card-top"><b>${esc(t.name)}</b></span>
          <span class="file-card-line">${esc([t.shortName, plural(t.members, 'person', 'people')].filter(Boolean).join(' · '))}</span></span></button>
        <button type="button" class="text-button" data-members="${t.id}">People</button>${toggle(t)}</div>`).join('') || empty;
  }

  function teamDialog(t) {
    const el = dialog(t ? `Edit team ${t.name}` : 'New team', `<div class="users-group">
        <label class="field"><span class="required">Name</span><input data-field="name" type="text" maxlength="255" value="${esc(t ? t.name : '')}" placeholder="e.g. North"><span class="field-error"></span></label>
        <label class="field"><span>Short Name</span><input data-field="shortName" type="text" maxlength="50" value="${esc(t ? t.shortName : '')}" placeholder="e.g. NTH"><span class="field-error"></span></label>
        <label class="field users-switch-row"><span>Active <span class="muted">· offered in team lists</span></span><input type="checkbox" role="switch" class="users-switch" data-field="isActive"${!t || t.isActive ? ' checked' : ''}></label></div>
      ${t ? '' : '<p class="users-hint">Put people in it from their Edit panel.</p>'}`);
    $('[data-submit]', el).textContent = t ? 'Save' : 'Create team';
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
    const el = dialog(t.name, `<p class="muted">Users assigned to this team.</p>${answer.members.length ? `<ul class="put-companies users-members-list">${answer.members.map(m => `<li><b>${esc(m.name)}</b><span class="users-role">${esc(m.role)}</span></li>`).join('')}</ul>` : '<p class="muted">Nobody.</p>'}`);
    $('[data-submit]', el).hidden = true;
    $('button[value=cancel]', el).setAttribute('aria-label', 'Close');
  }

  page.addEventListener('click', event => {
    const move = event.target.closest('[data-page]');
    if (move && people) { skip = Math.max(0, skip + Number(move.dataset.page) * PAGE); drawPeople(); window.scrollTo({top: 0}); return; }
    const t = event.target.closest('[data-tab]');
    if (t) { if (tab !== t.dataset.tab) { tab = t.dataset.tab; show(); } return; }
    const person = event.target.closest('[data-person]');
    if (person) { personDialog(person.dataset.person); return; }
    const team = event.target.closest('[data-team]');
    if (team) { teamDialog(teams.find(x => x.id === Number(team.dataset.team))); return; }
    const members = event.target.closest('[data-members]');
    if (members) { membersDialog(teams.find(x => x.id === Number(members.dataset.members))); return; }
    // A person's line opens them (the Edit button is the way for the keyboard).
    const row = event.target.closest('tr[data-row]');
    if (row && !event.target.closest('a, button, input, label, select') && !(window.getSelection && String(window.getSelection()))) personDialog(row.dataset.row);
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
