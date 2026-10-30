(() => {
  'use strict';
  // companymachines.aspx?company=: a company's machines in Advance (read only, every one of its BRN) grouped by coverage
  // as MyMachines showed them: on contract (by contract), warranty, time and materials (with the last contract's rates on
  // the machine's page), contract expired and no contract. Each group folds; Collapse all folds them all. Only for those
  // who may see the company's machines, as its Machines tab.
  const {$, $$, esc, api, drawIcons, date, param, crumbs, coverageTag, machineUrl} = window.WH;
  const page = $('#page');
  const id = Number(param('company') || 0);
  const folded = new Set();
  let d = null;

  const chevron = '<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M6 15l6-6 6 6"/></svg>';
  const foldIcon = path => `<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="${path}"/></svg>`;
  const none = '<span class="muted">–</span>';
  const day = v => v ? esc(date(v)) : none;
  const period = (a, b) => a || b ? `${a ? esc(date(a)) : '…'} – ${b ? esc(date(b)) : '…'}` : none;
  const serial = r => `<a class="serial" href="${machineUrl(r)}"><b>${esc(r.serial)}</b></a>`;
  const finance = r => r.finance ? esc(r.finance) : none;

  async function load() {
    const answer = await api('GET', 'machines?' + new URLSearchParams({company: String(id), all: '1'}));
    $('#loading').hidden = true;
    page.hidden = false;
    if (!answer.result.ok) {
      page.innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3><p><a class="button secondary" href="${id ? `company.aspx?id=${id}#machines` : './'}">Back to the company</a></p></div>`;
      return;
    }
    d = answer;
    render();
  }

  /** The groups, in MyMachines' order, each with its columns and how a row fills them. */
  function groups(rows) {
    const of = code => rows.filter(r => r.coverage === code);
    const contracts = [];
    for (const r of of('Contract')) {
      let c = contracts.find(x => x.no === r.contractNo);
      if (!c) contracts.push(c = {no: r.contractNo, type: r.contractType, start: r.contractStart, end: r.contractEnd, rows: []});
      c.rows.push(r);
    }
    return [
      {id: 'contract', title: 'On contract', rows: of('Contract'), sub: n => `${n} machine${n === 1 ? '' : 's'} · ${contracts.length} contract${contracts.length === 1 ? '' : 's'}`,
        head: ['Machine', 'Model', 'Finance type', 'Description', 'Warranty'], contracts,
        cells: r => [serial(r), esc(r.model), finance(r), esc(r.description !== r.model ? r.description : '') || none, r.warrantyEnd ? period(r.warrantyStart, r.warrantyEnd) : none]},
      {id: 'warranty', title: 'Warranty', rows: of('Warranty'),
        head: ['Machine', 'Model', 'Finance type', 'Warranty', 'Description'],
        cells: r => [serial(r), esc(r.model), finance(r), period(r.warrantyStart, r.warrantyEnd), esc(r.description !== r.model ? r.description : '') || none]},
      {id: 'tm', title: 'Time & material', rows: of('TM'),
        head: ['Machine', 'Model', 'Finance type', 'Last service', 'Last invoice', 'Last contract'],
        cells: r => [serial(r), esc(r.model), finance(r), esc(r.lastService) || none, esc(r.lastInvoice) || none,
          r.lastContract ? `<span class="cm-last">${esc(r.lastContract)} <a href="${machineUrl(r)}#last-contract">Rates</a></span>` : none]},
      {id: 'expired', title: 'Contract expired', rows: of('Expired'),
        head: ['Machine', 'Model', 'Finance type', 'Contract', 'Ended'],
        cells: r => [serial(r), esc(r.model), finance(r), `<span class="cm-contract">${coverageTag(r)}${r.contractNo ? ` ${esc(r.contractNo)} <span class="muted small">${esc(r.contractType)}</span>` : ''}</span>`,
          day(r.contractEnd || r.warrantyEnd)]},
      {id: 'none', title: 'No contract', rows: of('None'),
        head: ['Machine', 'Model', 'Finance type', 'Description', 'Item'],
        cells: r => [serial(r), esc(r.model), finance(r), esc(r.description !== r.model ? r.description : '') || none, esc(r.item) || none]},
    ].filter(g => g.rows.length);
  }

  function table(g) {
    const row = r => `<tr>${g.cells(r).map((c, i) => `<td${i === 0 ? ' class="nowrap"' : ''} data-label="${esc(g.head[i])}">${c}</td>`).join('')}</tr>`;
    const body = g.contracts
      ? g.contracts.map(c => `<tr class="cm-contract-row"><th colspan="${g.head.length}" scope="rowgroup"><b>${esc(c.no || 'No contract number')}</b>
          <span class="muted">${esc([c.type, c.start || c.end ? `${date(c.start)} – ${date(c.end)}` : '', `${c.rows.length} machine${c.rows.length === 1 ? '' : 's'}`].filter(Boolean).join(' · '))}</span></th></tr>
          ${c.rows.map(row).join('')}`).join('')
      : g.rows.map(row).join('');
    return `<div class="table-wrap"><table class="data cm-table"><thead><tr>${g.head.map(h => `<th>${esc(h)}</th>`).join('')}</tr></thead><tbody>${body}</tbody></table></div>`;
  }

  function render() {
    const c = d.company || {id, name: 'Company', brn: ''};
    document.title = `Machines by coverage · ${c.name} · MyCRMRecords`;
    const rows = d.rows;
    const list = groups(rows);
    const count = code => rows.filter(r => r.coverage === code).length;
    const parts = [[count('Contract'), 'on contract'], [count('Warranty'), 'warranty'], [count('TM'), 'T&M'], [count('Expired'), 'expired'], [count('None'), 'no contract']]
      .filter(p => p[0]).map(p => `${p[0]} ${p[1]}`);
    const allFolded = list.length > 0 && list.every(g => folded.has(g.id));
    page.innerHTML = `
      <div class="page-heading"><div>${crumbs('Machines by coverage', [['Companies', './'], [c.name, `company.aspx?id=${c.id}`]])}
          <h1>Machines by coverage</h1>
          <p><b>${esc(c.name)}</b>${c.brn ? ` · BRN ${esc(c.brn)}` : ''}${c.customerNo ? ` · Customer no. ${esc(c.customerNo)}` : ''} · from Advance</p></div>
        <div class="heading-actions"><div class="action-capsule" role="group" aria-label="Page actions">
          <a class="capsule-button" href="usage.aspx?company=${c.id}"><span data-icon="chart"></span><span class="action-label">Usage &amp; history</span></a>
          ${list.length ? `<button type="button" class="capsule-button" id="fold-all">${allFolded ? foldIcon('M7 4l5 5 5-5M7 10l5 5 5-5M7 16l5 5 5-5') : foldIcon('M7 14l5-5 5 5M7 20l5-5 5 5M7 8l5-5 5 5')}<span class="action-label">${allFolded ? 'Expand all' : 'Collapse all'}</span></button>` : ''}</div></div></div>
      <p class="cm-summary">${rows.length ? `<b>${rows.length} machine${rows.length === 1 ? '' : 's'}</b> · ${esc(parts.join(' · '))}` : ''}</p>
      ${rows.length ? list.map(g => {
        const open = !folded.has(g.id);
        return `<section class="card cm-group${open ? '' : ' folded'}" aria-labelledby="cm-${g.id}">
          <div class="card-heading cm-head"><h2 id="cm-${g.id}">${esc(g.title)} <span class="muted cm-sub">${esc(g.sub ? g.sub(g.rows.length) : `${g.rows.length} machine${g.rows.length === 1 ? '' : 's'}`)}</span></h2>
            <button type="button" class="icon-button small cm-fold" data-fold="${g.id}" aria-expanded="${open}" aria-controls="cm-body-${g.id}" aria-label="${open ? 'Collapse' : 'Expand'} ${esc(g.title)}">${chevron}</button></div>
          <div id="cm-body-${g.id}"${open ? '' : ' hidden'}>${table(g)}</div></section>`;
      }).join('')
      : `<div class="empty"><h3>No machines</h3><p class="muted">${c.brn ? 'Advance has no machines with this BRN.' : 'The company has no BRN: its machines are found by BRN.'}</p></div>`}`;
    drawIcons(page);
  }

  page.addEventListener('click', event => {
    const fold = event.target.closest('[data-fold]');
    if (fold) { if (folded.has(fold.dataset.fold)) folded.delete(fold.dataset.fold); else folded.add(fold.dataset.fold); render(); $(`[data-fold="${fold.dataset.fold}"]`).focus(); return; }
    if (event.target.closest('#fold-all')) {
      const ids = groups(d.rows).map(g => g.id);
      const all = ids.every(x => folded.has(x));
      ids.forEach(x => all ? folded.delete(x) : folded.add(x));
      render();
      $('#fold-all').focus();
    }
  });

  if (!id) {
    $('#loading').hidden = true;
    page.hidden = false;
    page.innerHTML = '<div class="empty"><h3>Open a company first: its machines by coverage are on its Machines tab.</h3><p><a class="button secondary" href="./">Back to the companies</a></p></div>';
  } else load();
})();
