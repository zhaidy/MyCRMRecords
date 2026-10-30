(() => {
  'use strict';
  // machineaccess.aspx (Sales Admins): MyMachines' temporary access, which lets someone see a company's machines without
  // the company being in their territories. Listed from MyMachines' TempAccess; given (a row added) and removed (the row
  // marked, never deleted) only once an administrator lets MyCRMRecords change MyMachines' temporary access.
  const {$, esc, api, drawIcons, date, toast, ask} = window.WH;
  const page = $('#page');
  let d = null, chosen = null, timer = 0;

  async function load() {
    const answer = await api('GET', 'machineaccess');
    $('#loading').hidden = true;
    page.hidden = false;
    if (!answer.result.ok) {
      page.innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3><p><a class="button secondary" href="machines.aspx">Back to the machines</a></p></div>`;
      return;
    }
    d = answer;
    render();
  }

  const rule = () => `<section class="card rule-card"><h2>Who can see a company's machines <span class="muted small">· the same people who can open the company, and temporary access</span></h2>
      <ol class="rule-steps">
        <li><b>Territory</b><span class="muted">The company is in one of their territories, or a territory of someone in their team.</span></li>
        <li><b>Project</b><span class="muted">They are in a MyCRM project (or CSST territory) with the company. The roles that open every company see every machine.</span></li>
        <li><b>Temporary access</b><span class="muted">Given on this page for one company: its machines by BRN or by Advance's customer name. It stays until it is removed.</span></li>
      </ol></section>`;

  function render() {
    const rows = d.rows;
    page.innerHTML = `
      <div class="page-heading"><div><a class="crumb" href="machines.aspx"><span data-icon="arrow-left"></span>Machines</a><h1>Machine access</h1>
        <p>Sales Admins · temporary access to a company's machines, kept in MyMachines</p></div></div>
      ${rule()}
      ${d.canWrite ? '' : `<div class="notice warn" style="margin:16px 0 0">${esc(d.writesOff)}</div>`}
      <div class="access-layout">
        <section class="card give-card"><h2>Give temporary access</h2>
          <form id="give" class="stack" novalidate>
            <label class="field"><span>User</span><select id="person"${d.canWrite ? '' : ' disabled'}><option value="">Choose someone…</option>
              ${d.people.map(p => `<option value="${esc(p.login)}">${esc(p.name || p.login)} (${esc(p.login)})</option>`).join('')}</select></label>
            <label class="field"><span>Company</span><input id="company-q" type="search" placeholder="Company name or BRN" autocomplete="off"${d.canWrite ? '' : ' disabled'}></label>
            <div id="choices" class="company-choices" hidden></div>
            <p class="muted small" id="chosen">${chosen ? `Chosen: <b>${esc(chosen.name)}</b> · BRN ${esc(chosen.brn)}` : 'Type at least 2 characters and choose the company.'}</p>
            <div class="action-buttons"><button class="button primary" type="submit"${d.canWrite ? '' : ' disabled'}><span data-icon="plus"></span>Give access</button></div>
          </form></section>
        <section class="card list-card"><h2>${rows.length} ${rows.length === 1 ? 'person' : 'people'} with temporary access</h2>
          ${rows.length ? `<div class="table-wrap machines-wide"><table class="data"><thead><tr><th>User</th><th>Company</th><th>BRN</th><th>Given</th>${d.canWrite ? '<th class="open-col"><span class="sr-only">Remove</span></th>' : ''}</tr></thead><tbody>
            ${rows.map(r => `<tr><td><b>${esc(r.name)}</b><span class="sub muted small" style="display:block">${esc(r.login)}</span></td><td>${esc(r.customer)}</td>
              <td class="nowrap">${esc(r.brn)}</td><td class="nowrap">${esc(date(r.given))}</td>
              ${d.canWrite ? `<td class="open-col"><button type="button" class="icon-button small" data-remove="${r.id}" aria-label="Remove ${esc(r.name)}'s access to ${esc(r.customer)}" title="Remove"><span data-icon="close"></span></button></td>` : ''}</tr>`).join('')}
            </tbody></table></div>
          <div class="file-cards access-cards">${rows.map(r => `<div class="file-card"><span class="file-card-body"><span class="file-card-top"><b>${esc(r.name)}</b></span>
              <span class="file-card-line strong">${esc(r.customer)}</span><span class="file-card-line">BRN ${esc(r.brn)} · given ${esc(date(r.given))}</span></span>
              ${d.canWrite ? `<button type="button" class="icon-button small" data-remove="${r.id}" aria-label="Remove ${esc(r.name)}'s access to ${esc(r.customer)}"><span data-icon="close"></span></button>` : ''}</div>`).join('')}</div>` : '<p class="muted">Nobody has temporary access.</p>'}</section>
      </div>`;
    drawIcons(page);
    $('#company-q').addEventListener('input', event => { clearTimeout(timer); timer = setTimeout(() => search(event.target.value.trim()), 300); });
    $('#give').addEventListener('submit', give);
  }

  async function search(q) {
    const box = $('#choices');
    if (q.length < 2) { box.hidden = true; return; }
    const answer = await api('GET', 'companies?' + new URLSearchParams({q}));
    const rows = answer.result.ok ? answer.rows.slice(0, 8) : [];
    box.hidden = false;
    box.innerHTML = rows.length ? rows.map(c => `<button type="button" class="company-choice" data-company="${c.id}" data-name="${esc(c.name)}" data-brn="${esc(c.brn)}">
      <b>${esc(c.name)}</b><span class="muted small">${c.brn ? 'BRN ' + esc(c.brn) : 'No BRN'}</span></button>`).join('') : '<p class="muted small">No company found.</p>';
  }

  async function give(event) {
    event.preventDefault();
    const login = $('#person').value;
    if (!login || !chosen) { toast('Choose the user and the company.', true); return; }
    const answer = await api('POST', 'machineaccess', {login, companyId: chosen.id});
    toast(answer.result.message, !answer.result.ok);
    if (answer.result.ok) { chosen = null; load(); }
  }

  page.addEventListener('click', async event => {
    const choice = event.target.closest('[data-company]');
    if (choice) {
      chosen = {id: Number(choice.dataset.company), name: choice.dataset.name, brn: choice.dataset.brn};
      $('#choices').hidden = true;
      $('#company-q').value = chosen.name;
      $('#chosen').innerHTML = `Chosen: <b>${esc(chosen.name)}</b> · BRN ${esc(chosen.brn) || '–'}`;
      return;
    }
    const remove = event.target.closest('[data-remove]');
    if (remove) {
      const row = d.rows.find(r => r.id === Number(remove.dataset.remove));
      if (!await ask({title: 'Remove temporary access?', text: `${row.name} will no longer see the machines of ${row.customer}. MyMachines keeps the row, marked as removed.`, ok: 'Remove', danger: true})) return;
      const answer = await api('POST', `machineaccess/${row.id}/remove`, {});
      toast(answer.result.message, !answer.result.ok);
      if (answer.result.ok) load();
    }
  });

  load();
})();
