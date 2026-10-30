(() => {
  'use strict';
  // projects.aspx: MyCRM's projects the person or their team are in (MyCRM's Projects screen): open, closed or all,
  // searched by name, remarks or company. Managers, Admins, Sales Admins and MD create one in two steps (details, then
  // companies); members are added on the project's page. On a phone a card per project.
  const {$, $$, esc, api, toast, drawIcons, pager, date, showErrors} = window.WH;
  const page = $('#page');
  const filter = {show: 'open', q: '', skip: 0};
  try { const saved = JSON.parse(localStorage.getItem('projects-filter') || '{}'); if (['open', 'closed', 'all'].includes(saved.show)) filter.show = saved.show; } catch { /* none */ }
  let data = null, timer = 0;

  async function load() {
    try { localStorage.setItem('projects-filter', JSON.stringify({show: filter.show})); } catch { /* not kept */ }
    const answer = await api('GET', 'projects?' + new URLSearchParams({show: filter.show, q: filter.q, skip: String(filter.skip)}));
    $('#loading').hidden = true;
    page.hidden = false;
    if (!$('#rows')) shell(answer);
    if (!answer.result.ok) { $('#rows').innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3></div>`; $('#cards').innerHTML = ''; return; }
    data = answer;
    render();
  }

  function shell(answer) {
    page.innerHTML = `
      <div class="page-heading companies-heading"><div class="companies-title"><h1>Projects</h1>
          <p>Projects give people temporary access to companies, for example to caretake an account. Saved in MyCRM.</p></div>
        <div class="heading-actions">${answer.canCreate ? '<button type="button" class="button primary phone-fab" id="create" title="Create project"><span data-icon="plus"></span><span class="action-label">Create project</span></button>' : ''}</div></div>
      <div class="companies-filters" role="search"><div class="companies-filter-row">
        <label class="search-box"><span data-icon="search"></span><span class="sr-only">Search projects</span>
          <input id="q" type="search" placeholder="Project name, remarks or company" aria-label="Search projects"></label>
        <div class="tabs proj-show" role="group" aria-label="Project status">${[['open', 'Open'], ['closed', 'Closed'], ['all', 'All']].map(([v, l]) =>
          `<button type="button" class="tab" data-show="${v}" aria-pressed="false">${l}</button>`).join('')}</div>
        <p class="companies-show-note proj-count" id="count"></p></div></div>
      <section aria-label="Projects list" class="companies-list"><div id="rows"></div><div id="cards" class="file-cards"></div><div class="pager" id="pager" hidden></div>
        <p class="proj-foot muted small">Projects past their end date close automatically each morning.</p></section>`;
    drawIcons(page);
    $('#q').addEventListener('input', event => { filter.q = event.target.value; filter.skip = 0; clearTimeout(timer); timer = setTimeout(load, 350); });
    $('#create')?.addEventListener('click', () => create(answer));
  }

  function render() {
    $$('[data-show]', page).forEach(b => b.setAttribute('aria-pressed', String(b.dataset.show === filter.show)));
    $('#count').innerHTML = `<b>${data.total} project${data.total === 1 ? '' : 's'}</b> · you or your team are in · newest first`;
    const rows = data.rows;
    const companies = r => { const n = r.companyCount ?? r.companies.length; return r.companies.length ? `${esc(r.companies[0])}${n > 1 ? ` <span class="muted">+${(n - 1).toLocaleString()} more</span>` : ''}` : '<span class="muted">–</span>'; };
    $('#rows').innerHTML = rows.length ? `<table class="data companies-table proj-table"><thead><tr><th class="proj-name">Project name</th><th>Status</th><th class="proj-cos">Companies</th>
        <th class="proj-members">Members</th><th>Owner</th><th class="proj-date">End date</th><th class="proj-updated">Updated</th><th class="proj-closed">Closed</th></tr></thead><tbody>
      ${rows.map(r => `<tr data-href="project.aspx?id=${r.id}"${r.closed ? ' class="closed"' : ''}><td class="co-name"><a href="project.aspx?id=${r.id}">${esc(r.name)}</a></td><td>${esc(r.status)}</td>
        <td>${companies(r)}</td><td>${r.members}${r.caretaker ? ` <span class="muted">· ${esc(r.caretaker)}</span>` : ''}</td><td>${esc(r.owner)}</td>
        <td class="nowrap">${esc(date(r.endDate))}</td><td><span class="nowrap">${esc(date(r.updatedDate))}</span>${r.updatedBy ? ` <span class="muted">· ${esc(r.updatedBy)}</span>` : ''}</td>
        <td>${r.closed ? '<span class="act-status pending">Closed</span>' : '<span class="muted">–</span>'}</td></tr>`).join('')}</tbody></table>`
      : `<div class="empty"><h3>No projects</h3><p class="muted">No projects match. Try another name or company, or choose All.</p></div>`;
    $('#cards').innerHTML = rows.map(r => `<a class="file-card" href="project.aspx?id=${r.id}"><span class="file-card-body"><span class="file-card-top"><b>${esc(r.name)}</b></span>
        <span class="file-card-line">${esc(r.status)} · ends ${esc(date(r.endDate))}${r.closed ? ' · Closed' : ''}</span>
        <span class="file-card-line">${esc(r.companies[0] || '')}${r.companies.length > 1 ? ` +${r.companies.length - 1}` : ''} · ${r.members} member${r.members === 1 ? '' : 's'}</span></span>
        <span data-icon="arrow-right" class="section-chevron"></span></a>`).join('');
    drawIcons($('#rows'));
    drawIcons($('#cards'));
    pager($('#pager'), data.total, data.skip, data.take);
  }

  /** Create project: step 1 the details, step 2 the companies (at least one); nothing is saved before step 2. */
  function create(answer) {
    const el = document.createElement('dialog');
    el.className = 'act-panel wide';
    const tomorrow = (() => { const d = new Date(); d.setMonth(d.getMonth() + 3); return d.toISOString().slice(0, 10); })();
    const form = {name: '', status: answer.statuses[0].code, startDate: answer.today, endDate: tomorrow, remarks: ''};
    const picked = new Map();
    let step = 1, found = [];
    el.innerHTML = `<div class="act-panel-in"><div class="act-panel-head"><button type="button" class="icon-button" data-close aria-label="Cancel"><span data-icon="close"></span></button>
      <div class="act-panel-title"><h2>Create project</h2><span class="muted small" data-step></span></div><div class="act-panel-action"></div></div>
      <div class="notice error" data-notice hidden></div><div class="act-panel-body" data-body></div><div class="act-panel-foot" data-foot></div></div>`;
    document.body.append(el);
    $('[data-close]', el).addEventListener('click', () => el.close());
    el.addEventListener('close', () => el.remove());
    const notice = text => { const n = $('[data-notice]', el); n.hidden = !text; n.textContent = text || ''; };
    const draw = () => {
      $('[data-step]', el).textContent = step === 1 ? 'Step 1 of 2 · Details' : 'Step 2 of 2 · Companies';
      if (step === 1) {
        $('[data-body]', el).innerHTML = `<div class="act-card"><div class="form-grid two">
          <label class="field wide"><span class="required">Project name</span><input data-field="name" maxlength="250" value="${esc(form.name)}" placeholder="e.g. Caretake — Harbourline accounts"><span class="field-error"></span></label>
          <label class="field"><span>Status</span><select data-field="status">${answer.statuses.map(s => `<option value="${esc(s.code)}"${s.code === form.status ? ' selected' : ''}>${esc(s.label)}</option>`).join('')}</select></label>
          <label class="field"><span>Owner</span><input value="You (Project Manager)" disabled></label>
          <label class="field"><span>Start date</span><input type="date" data-field="startDate" value="${esc(form.startDate)}"><span class="field-error"></span></label>
          <label class="field"><span class="required">End date</span><input type="date" data-field="endDate" value="${esc(form.endDate)}"><small class="muted">Must be after today. The project closes itself the morning it ends.</small><span class="field-error"></span></label>
          <label class="field wide"><span>Remarks</span><textarea data-field="remarks" rows="3" maxlength="2000" placeholder="Why this project exists, e.g. covering accounts while the salesperson is on leave">${esc(form.remarks)}</textarea></label>
        </div></div><p class="proj-who"><b>Who gets access</b> · Members without their own territory for a company get temporary access to it while the project is open.</p>`;
        $('[data-foot]', el).innerHTML = '<span class="muted small">Nothing is saved yet.</span><button type="button" class="button primary" data-next>Next: add companies</button>';
      } else {
        $('[data-body]', el).innerHTML = `<label class="search-box proj-co-search"><span data-icon="search"></span><span class="sr-only">Find companies</span><input type="search" data-co-q placeholder="Company, BRN or website" autocomplete="off"></label>
          <div class="proj-pick" data-pick></div><div class="proj-picked" data-picked></div>`;
        $('[data-foot]', el).innerHTML = '<button type="button" class="button secondary" data-back>Back</button><span class="grow"></span><button type="button" class="button primary" data-create>Create project in MyCRM</button>';
        drawPick();
        let t = 0;
        $('[data-co-q]', el).addEventListener('input', event => {
          clearTimeout(t);
          t = setTimeout(async () => {
            const q = event.target.value.trim();
            const result = q.length < 2 ? {result: {ok: true}, companies: []} : await api('GET', 'projects/companies?' + new URLSearchParams({q}));
            found = result.result.ok ? result.companies : [];
            drawPick(q.length < 2);
          }, 300);
        });
        $('[data-co-q]', el).focus();
      }
      drawIcons(el);
    };
    const drawPick = (short = true) => {
      $('[data-pick]', el).innerHTML = found.length ? found.map(c => `<label class="proj-pick-row"><input type="checkbox" data-co="${c.id}"${picked.has(c.id) ? ' checked' : ''}>
          <span class="proj-pick-name">${esc(c.name)}</span><span class="muted small">${esc([c.territory, c.salesperson || 'no salesperson'].filter(Boolean).join(' · '))}</span></label>`).join('')
        : `<p class="muted small">${short ? 'Type at least 2 letters of a company, BRN or website.' : 'No company matches.'}</p>`;
      $('[data-picked]', el).innerHTML = `<b>${picked.size} compan${picked.size === 1 ? 'y' : 'ies'} chosen</b>${[...picked.values()].map(c => `<span class="act-chip">${esc(c.name)}<button type="button" data-unpick="${c.id}" aria-label="Remove ${esc(c.name)}"><span data-icon="close"></span></button></span>`).join('')}
        <span class="muted small"> · Add members on the project's page once it is created.</span>`;
      drawIcons($('[data-picked]', el));
    };
    el.addEventListener('change', event => {
      const box = event.target.closest('[data-co]');
      if (!box) return;
      const id = Number(box.dataset.co), c = found.find(x => x.id === id);
      if (box.checked) picked.set(id, c); else picked.delete(id);
      drawPick(false);
    });
    el.addEventListener('click', async event => {
      if (event.target.closest('[data-unpick]')) { picked.delete(Number(event.target.closest('[data-unpick]').dataset.unpick)); drawPick(false); return; }
      if (event.target.closest('[data-next]')) {
        for (const k of ['name', 'status', 'startDate', 'endDate', 'remarks']) form[k] = $(`[data-field=${k}]`, el).value;
        const errors = [];
        if (!form.name.trim()) errors.push({field: 'name', message: 'Enter the project\'s name.'});
        if (!form.endDate || form.endDate <= answer.today) errors.push({field: 'endDate', message: 'Project End Date must not be less than the current date: choose a day after today.'});
        showErrors(el, errors);
        if (errors.length) return;
        notice('');
        step = 2;
        draw();
        return;
      }
      if (event.target.closest('[data-back]')) { step = 1; notice(''); draw(); return; }
      if (event.target.closest('[data-create]')) {
        if (!picked.size) { notice('You must have a Company Associated with this Project: choose at least one company.'); return; }
        const result = await api('POST', 'projects/0', {...form, companies: [...picked.keys()]});
        if (!result.result.ok) {
          notice(result.result.message);
          if ((result.result.errors || []).some(e => e.field !== 'companies')) { step = 1; draw(); showErrors(el, result.result.errors); }
          return;
        }
        toast(result.result.message);
        el.close();
        location.href = 'project.aspx?id=' + result.result.id;
      }
    });
    draw();
    el.showModal();
    $('[data-field=name]', el).focus();
  }

  page.addEventListener('click', event => {
    const show = event.target.closest('[data-show]');
    if (show) { filter.show = show.dataset.show; filter.skip = 0; load(); return; }
    const move = event.target.closest('[data-page]');
    if (move && data) { filter.skip = Math.max(0, data.skip + Number(move.dataset.page) * data.take); load(); window.scrollTo({top: 0}); return; }
    const row = event.target.closest('tr[data-href]');
    if (row && !event.target.closest('a')) location.href = row.dataset.href;
  });

  load();
})();
