(() => {
  'use strict';
  // users.aspx: the roles people have (MyCRM's Admin, BE and HR; the administrators), finding people in Active
  // Directory, and "Sign in as" (administrators see MyCRMRecords as someone else does).
  const {$, $$, esc, api, toast, ask, dateTime, drawIcons} = window.WH;
  const page = $('#page');
  let data = null, filter = '', found = [], searchTimer = 0;

  async function load() {
    const answer = await api('GET', 'users');
    $('#loading').hidden = true;
    if (!answer.result.ok) { page.hidden = false; page.innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3></div>`; return; }
    data = answer;
    render();
  }

  const roleChecks = (login, roles) => `<div class="role-checks">${data.roles.map(r => {
    const locked = r.name === 'Admin' && (!data.canGiveAdmin || login.toLowerCase() === data.me.toLowerCase() && roles.includes('Admin'));
    return `<label title="${esc(r.description)}"><input type="checkbox" value="${esc(r.name)}"${roles.includes(r.name) ? ' checked' : ''}${locked ? ' disabled' : ''}>${esc(r.label)}</label>`;
  }).join('')}</div>`;

  function person(p, fromDirectory) {
    return `<div class="person" data-login="${esc(p.login)}" data-name="${esc(p.displayName)}" data-email="${esc(p.email)}">
      <div class="who"><b>${esc(p.displayName || p.login)}</b><small>${esc(p.login)}${p.email ? ' · ' + esc(p.email) : ''}</small>
        ${p.lastSeenAt ? `<small>Last opened ${esc(dateTime(p.lastSeenAt))}</small>` : ''}</div>
      ${roleChecks(p.login, p.roles || [])}
      <div class="heading-actions"><button class="button primary small-btn" data-save>Save</button>
        ${fromDirectory || p.login.toLowerCase() === data.me.toLowerCase() ? '' : '<button class="button secondary small-btn" data-impersonate>Sign in as</button>'}</div></div>`;
  }

  function render() {
    page.hidden = false;
    const term = filter.toLowerCase();
    const users = data.users.filter(u => !term || [u.displayName, u.login, u.email, u.roles.join(' ')].join(' ').toLowerCase().includes(term));
    const withRoles = users.filter(u => u.roles.length), others = users.filter(u => !u.roles.length);
    page.innerHTML = `
      <div class="page-heading"><div><h1>User roles</h1><p>Everyone signed in can open MyCRMRecords, create feedbacks and work on those they are in charge of, as in MyCRM. The roles below are MyCRM's (its Admin is Feedback Admin here); Administrator is MyCRMRecords's own.</p></div></div>
      <div class="stack">
        <section class="card"><h2>Find a person</h2><p class="muted small">Search Active Directory by name or login, then tick their roles and save.</p>
          <label class="search-box" style="margin-top:12px"><span data-icon="search"></span><input id="directory" type="search" placeholder="Name or login, e.g. Jamie or jlee" autocomplete="off"></label>
          <div class="people-list" id="found">${found.map(p => person({...p, roles: data.users.find(u => u.login.toLowerCase() === p.login.toLowerCase())?.roles || []}, true)).join('')}</div></section>
        <section class="card"><div class="card-heading"><h2>People with a role</h2><span class="muted small">${withRoles.length}</span></div>
          <div class="toolbar"><label class="search-box"><span data-icon="search"></span><input id="filter" type="search" placeholder="Filter by name, login or role" value="${esc(filter)}"></label></div>
          <div class="people-list">${withRoles.map(u => person(u, false)).join('') || '<p class="muted">Nobody.</p>'}</div></section>
        <section class="card"><details><summary class="text-button">Everyone else who has opened MyCRMRecords (${others.length})</summary>
          <div class="people-list">${others.map(u => person(u, false)).join('')}</div></details></section>
        <section class="card"><h2>The roles</h2><dl class="facts-list">${data.roles.map(r => `<dt>${esc(r.label)}</dt><dd>${esc(r.description)}</dd>`).join('')}</dl></section>
      </div>`;
    drawIcons(page);
    $('#filter').addEventListener('input', event => { filter = event.target.value; const pos = event.target.selectionStart; render(); const f = $('#filter'); f.focus(); f.setSelectionRange(pos, pos); });
    $('#directory').addEventListener('input', event => {
      clearTimeout(searchTimer);
      const q = event.target.value.trim();
      searchTimer = setTimeout(async () => {
        if (q.length < 2) { found = []; $('#found').innerHTML = ''; return; }
        const answer = await api('GET', 'users/search?q=' + encodeURIComponent(q));
        if (!answer.result.ok) { toast(answer.result.message, true); return; }
        found = answer.people;
        $('#found').innerHTML = found.length ? found.map(p => person({...p, roles: data.users.find(u => u.login.toLowerCase() === p.login.toLowerCase())?.roles || []}, true)).join('')
          : '<p class="muted">Nobody found.</p>';
      }, 300);
    });
  }

  page.addEventListener('click', async event => {
    const row = event.target.closest('.person');
    if (!row) return;
    if (event.target.closest('[data-save]')) {
      const roles = $$('input[type=checkbox]:checked', row).map(input => input.value);
      const answer = await api('POST', 'users/roles', {login: row.dataset.login, displayName: row.dataset.name, email: row.dataset.email, roles});
      toast(answer.result.message, !answer.result.ok);
      if (answer.result.ok) { found = []; load(); }
    }
    if (event.target.closest('[data-impersonate]')) {
      if (!await ask({title: `Sign in as ${row.dataset.name || row.dataset.login}?`, text: 'You see MyCRMRecords as they do, and can act for them; everything is recorded as done by you on their behalf. Sign out to return to your own account.', ok: 'Sign in as'})) return;
      const answer = await api('POST', 'users/impersonate', {login: row.dataset.login});
      if (!answer.result.ok) { toast(answer.result.message, true); return; }
      location.href = './';
    }
  });
  load();
})();
