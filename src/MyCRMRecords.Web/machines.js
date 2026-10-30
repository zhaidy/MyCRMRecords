(() => {
  'use strict';
  // machines.aspx: Advance's machines, read only. The person sees the machines of the companies they may open (their
  // territories', their team's, their MyCRM projects') and of the customers they have temporary access to; the roles that
  // open every company see every machine once they search. 50 at a time, by customer. The search and filters apply as
  // they are typed or chosen and are kept for next time. On a phone each machine is a card.
  const {$, $$, esc, api, drawIcons, pager, coverageTag, coverageText, machineUrl} = window.WH;
  const page = $('#page');
  const blank = {q: '', coverage: '', finance: ''};
  const saved = (() => { try { return JSON.parse(localStorage.getItem('machines-filter') || '{}'); } catch { return {}; } })();
  const filter = {...blank, ...Object.fromEntries(Object.entries(saved).filter(([k]) => k in blank)), skip: 0};
  let data = null, timer = 0;

  const query = () => new URLSearchParams(Object.entries(filter).filter(([k, v]) => v !== '' && !(k === 'skip' && v === 0)).map(([k, v]) => [k, String(v)]));
  const companyCell = r => r.companyId ? `<a href="company.aspx?id=${r.companyId}#machines">${esc(r.customer)}</a>` : esc(r.customer);

  async function load() {
    try { localStorage.setItem('machines-filter', JSON.stringify({q: filter.q, coverage: filter.coverage, finance: filter.finance})); } catch { /* not kept */ }
    const answer = await api('GET', 'machines?' + query());
    $('#loading').hidden = true;
    page.hidden = false;
    if (!$('#rows')) shell(answer);
    if (!answer.result.ok) {
      $('#rows').innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3></div>`;
      $('#cards').innerHTML = '';
      $('#count').textContent = '';
      $('#pager').hidden = true;
      return;
    }
    data = answer;
    render();
  }

  function shell(answer) {
    const coverages = answer.coverages || [];
    const finances = answer.financeTypes || [];
    page.innerHTML = `
      <div class="page-heading"><div><h1>Machines</h1><p id="count"></p></div>
        <div class="heading-actions">${answer.manageAccess ? '<a class="button secondary" href="machineaccess.aspx"><span data-icon="shield"></span>Machine access</a>' : ''}</div></div>
      <div class="toolbar files-toolbar machines-toolbar" role="search">
        <label class="search-box"><span data-icon="search"></span><span class="sr-only">Search machines</span>
          <input id="q" type="search" placeholder="Serial number, model, company or BRN (at least 3 characters)" value="${esc(filter.q)}" aria-label="Search machines"></label>
        <div class="tabs coverage-filter" role="group" aria-label="Coverage">${[{code: '', name: 'All'}, ...coverages].map(c =>
          `<button type="button" class="tab" data-coverage="${esc(c.code)}" aria-pressed="${String(filter.coverage === c.code)}">${esc(c.name)}</button>`).join('')}</div>
        <label class="since"><span class="sr-only">Finance type</span><select id="finance" aria-label="Finance type">
          ${[{code: '', name: 'any'}, ...finances, {code: 'none', name: 'none'}].map(f =>
            `<option value="${esc(f.code)}"${filter.finance === f.code ? ' selected' : ''}>Finance type: ${esc(f.name)}</option>`).join('')}</select></label>
      </div>
      <section aria-label="Machines list"><div id="rows"></div><div id="cards" class="file-cards"></div><div class="pager" id="pager" hidden></div></section>`;
    drawIcons(page);
    $('#q').addEventListener('input', event => { filter.q = event.target.value; clearTimeout(timer); filter.skip = 0; timer = setTimeout(load, 400); });
    $('#finance').addEventListener('change', event => { filter.finance = event.target.value; filter.skip = 0; load(); });
  }

  function render() {
    const filtered = !!(filter.q.trim().length >= data.searchFrom || filter.coverage || filter.finance);
    $('#count').innerHTML = data.needsSearch
      ? 'Type at least 3 characters of a serial number, model, company or BRN.'
      : `<b>${data.total.toLocaleString('en-SG')} machine${data.total === 1 ? '' : 's'}</b>${filtered ? ' match' : ''} · from Advance`
        + (data.limited ? ' · of the companies you may open, your projects and your temporary access' : '');
    $$('.coverage-filter .tab', page).forEach(b => b.setAttribute('aria-pressed', String(b.dataset.coverage === filter.coverage)));
    const rows = data.rows;
    $('#rows').innerHTML = rows.length ? `<div class="table-wrap"><table class="data machines-table"><thead><tr>
        <th>Machine</th><th>Model</th><th>Company</th><th>Access via</th><th>Coverage</th><th>Finance type</th><th class="open-col"><span class="sr-only">Open</span></th></tr></thead><tbody>
      ${rows.map(r => `<tr>
        <td class="nowrap"><a class="serial" href="${machineUrl(r)}">${esc(r.serial)}</a></td>
        <td class="nowrap muted">${esc(r.model)}</td>
        <td class="machine-company" title="${esc(r.customer)}">${companyCell(r)}</td>
        <td class="nowrap muted">${esc(r.via)}</td>
        <td><span class="coverage-cell">${coverageTag(r)}<span class="muted small">${esc(coverageText(r))}</span></span></td>
        <td class="nowrap">${esc(r.finance) || '<span class="muted">–</span>'}</td>
        <td class="open-col"><a class="icon-button small" href="${machineUrl(r)}" aria-label="Open ${esc(r.serial)}" title="Open"><span data-icon="arrow-right"></span></a></td></tr>`).join('')}
      </tbody></table></div>`
      : `<div class="empty"><h3>${data.needsSearch ? 'Search for a machine' : 'No machines to show'}</h3><p class="muted">${data.needsSearch
        ? 'You may see every machine: type at least 3 characters of its serial number, model, company or BRN.'
        : filtered ? 'No machines match. Try another serial number or coverage.' : 'There are no machines of the companies you may see.'}</p></div>`;
    // On a phone: a card per machine.
    $('#cards').innerHTML = rows.map(r => `<a class="file-card" href="${machineUrl(r)}" aria-label="Open ${esc(r.serial)}">
        <span class="file-card-body"><span class="file-card-top"><b>${esc(r.serial)} <span class="muted small">${esc(r.model)}</span></b></span>
          <span class="file-card-line strong">${esc(r.customer)}</span>
          <span class="file-card-line">${coverageTag(r)} ${esc([r.finance, coverageText(r)].filter(Boolean).join(' · '))}</span></span>
        <span data-icon="arrow-right" class="section-chevron"></span></a>`).join('');
    drawIcons($('#rows'));
    drawIcons($('#cards'));
    pager($('#pager'), data.total, data.skip, data.take);
  }

  page.addEventListener('click', event => {
    const coverage = event.target.closest('[data-coverage]');
    if (coverage) { filter.coverage = coverage.dataset.coverage; filter.skip = 0; load(); return; }
    const move = event.target.closest('[data-page]');
    if (move && data) { filter.skip = Math.max(0, data.skip + Number(move.dataset.page) * data.take); load(); window.scrollTo({top: 0}); }
  });

  load();
})();
