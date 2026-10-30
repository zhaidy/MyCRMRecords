(() => {
  'use strict';
  // files.aspx: MyFiles' files (the PDFs MyFile keeps: costing sheets, request forms, FSAs, leasing papers, DOs…), read
  // only. The person sees the files of the companies they may open and the files of no company (every file for the roles
  // that open every company), 50 at a time, newest first, each with its company's territory. The search and filters (the
  // file type, the territory: the person's and their team's, or every one for the roles that open every company, and the
  // day uploaded from) apply as they are typed or chosen and
  // are kept for next time. On a computer the chosen file shows beside the list; ticked files download as one zip. On a
  // phone each file is a card that opens its PDF on the whole screen.
  const {$, $$, esc, api, drawIcons, pager, date} = window.WH;
  const page = $('#page');
  const blank = {q: '', type: '', since: '', territory: ''};
  const saved = (() => { try { return JSON.parse(localStorage.getItem('files-filter') || '{}'); } catch { return {}; } })();
  const filter = {...blank, ...Object.fromEntries(Object.entries(saved).filter(([k]) => k in blank)), skip: 0};
  // From a company's page (company=<id>&name=<name>): only that company's files, not kept for next time.
  const linked = new URLSearchParams(location.search);
  const company = {id: /^\d+$/.test(linked.get('company') || '') ? linked.get('company') : '', name: linked.get('name') || ''};
  if (company.id) Object.assign(filter, blank, {company: company.id});
  const everyCompany = () => { delete filter.company; company.id = ''; history.replaceState(null, '', location.pathname); };
  const wide = window.matchMedia('(min-width: 901px)');
  let data = null, timer = 0, previewId = 0;
  const ticked = new Map();

  // Uploaded: the day from which files are listed (in the browser's own days, as the dates are written).
  const day = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  const sinceChoices = () => {
    const now = new Date();
    return [['', 'Any time'], [day(new Date(now.getFullYear(), now.getMonth(), now.getDate() - 30)), 'Last 30 days'],
      [day(new Date(now.getFullYear() - 1, now.getMonth(), now.getDate())), 'Last 12 months'], [day(new Date(now.getFullYear(), 0, 1)), 'This year']];
  };
  // A saved "since" is kept by its name, so "Last 30 days" moves with the days.
  const sinceName = saved.sinceName || '';
  if (!company.id && sinceName) filter.since = (sinceChoices().find(c => c[1] === sinceName) || ['', ''])[0];

  const query = () => new URLSearchParams(Object.entries(filter).filter(([k, v]) => v !== '' && !(k === 'skip' && v === 0)).map(([k, v]) => [k, String(v)]));
  const viewUrl = id => `file.ashx/files/${id}?view=1`;
  const downloadUrl = id => `file.ashx/files/${id}`;
  const typeColor = {CostingSheet: '#0a58f0', RequestForm: '#0d9488', FSA: '#7c3aed', Leasing: '#c2410c', DO: '#475569', Others: '#8e8e96'};
  const tag = r => r.type ? `<span class="tag file-type" style="--tag:${typeColor[r.typeLabel] || '#8e8e96'}">${esc(r.type)}</span>` : '';
  const size = bytes => bytes >= 1048576 ? (bytes / 1048576).toFixed(1) + ' MB' : bytes >= 1024 ? Math.round(bytes / 1024) + ' KB' : bytes + ' B';
  const number = r => r.numbers.length ? `${r.numbers[0].kind} ${r.numbers[0].number}` : '';
  const numbers = r => r.numbers.map(n => `${n.kind} ${n.number}`).join(', ');

  async function load() {
    if (!filter.company) {
      const sinceLabel = (sinceChoices().find(c => c[0] === filter.since) || ['', ''])[1];
      try { localStorage.setItem('files-filter', JSON.stringify({q: filter.q, type: filter.type, territory: filter.territory, sinceName: filter.since ? sinceLabel : ''})); } catch { /* not kept */ }
    }
    const answer = await api('GET', 'files?' + query());
    $('#loading').hidden = true;
    page.hidden = false;
    // A territory kept from last time that is no longer the person's: every territory again.
    if (filter.territory && answer.result.ok && !(answer.territories || []).some(t => String(t.id) === String(filter.territory))) { filter.territory = ''; return load(); }
    if (!$('#rows')) shell(answer.types || [], answer.territories || [], answer.limited);
    if (!answer.result.ok) {
      $('#rows').innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3></div>`;
      $('#cards').innerHTML = '';
      $('#count').textContent = '';
      $('#pager').hidden = true;
      $('#preview').hidden = true;
      return;
    }
    data = answer;
    render();
  }

  function shell(types, territories, limited) {
    page.innerHTML = `
      <div class="page-heading list-heading"><div class="list-title"><h1>Files</h1><p id="count"></p></div>
        <div class="heading-actions" id="selection" hidden>
          <div class="action-capsule files-selected" role="group" aria-label="Selected files"><span id="selected-count"></span><span class="capsule-divider" aria-hidden="true"></span>
            <button type="button" class="capsule-button" id="clear">Clear</button></div>
          <a class="button primary" id="zip" href="#"><span data-icon="download"></span>Download as zip</a></div></div>
      <div class="toolbar files-toolbar" role="search">
        <label class="search-box"><span data-icon="search"></span><span class="sr-only">Search files</span>
          <input id="q" type="search" placeholder="Invoice, service or contract number, company, file name" value="${esc(filter.q)}" aria-label="Search files"></label>
        <div class="tabs file-types" role="group" aria-label="File type">${[{label: '', name: 'All'}, ...types].map(t =>
          `<button type="button" class="tab" data-type="${esc(t.label)}" aria-pressed="${String(filter.type === t.label)}">${esc(t.name)}</button>`).join('')}</div>
        ${territories.length && !company.id ? `<label class="filter-capsule"><span>Territory</span><select id="territory" aria-label="Territory">
          <option value="">${limited ? 'All mine' : 'All'}</option>${territories.map(t => `<option value="${t.id}"${String(t.id) === String(filter.territory) ? ' selected' : ''}>${esc(t.title)}</option>`).join('')}</select></label>` : ''}
        <label class="filter-capsule"><span>Uploaded</span><select id="since" aria-label="Uploaded">${sinceChoices().map(([value, label]) =>
          `<option value="${value}"${value === filter.since ? ' selected' : ''}>${esc(label)}</option>`).join('')}</select></label>
      </div>
      ${company.id ? `<p class="notice info" id="company-only" style="margin:0 0 14px">Only the files of <b>${esc(company.name || 'company ' + company.id)}</b>. <button type="button" class="text-button" id="every-company">Show every company</button></p>` : ''}
      <div class="files-layout">
        <section class="files-list list-card" aria-label="Files list"><div id="rows"></div><div id="cards" class="file-cards"></div><div class="pager" id="pager" hidden></div></section>
        <aside class="card file-preview" id="preview" aria-label="File preview" hidden></aside>
      </div>`;
    drawIcons(page);
    const later = () => { clearTimeout(timer); filter.skip = 0; timer = setTimeout(load, 400); };
    $('#q').addEventListener('input', event => { filter.q = event.target.value; later(); });
    $('#since').addEventListener('change', event => { filter.since = event.target.value; filter.skip = 0; load(); });
    $('#territory')?.addEventListener('change', event => { filter.territory = event.target.value; filter.skip = 0; load(); });
    $('#every-company')?.addEventListener('click', () => { everyCompany(); filter.skip = 0; page.innerHTML = ''; load(); });
    $('#clear').addEventListener('click', () => { ticked.clear(); render(); });
  }

  function render() {
    const filtered = !!filter.company || !!(filter.q || filter.type || filter.since || filter.territory);
    $('#count').innerHTML = `<b>${data.total.toLocaleString('en-SG')} file${data.total === 1 ? '' : 's'}</b>${filtered ? ' match' : ''} · newest first`
      + (data.limited ? ' · of the companies you may open, and files of no company' : '');
    $$('.file-types .tab', page).forEach(b => b.setAttribute('aria-pressed', String(b.dataset.type === filter.type)));
    const rows = data.rows;
    const all = rows.length > 0 && rows.every(r => ticked.has(r.id));
    $('#rows').innerHTML = rows.length ? `<div class="list-scroll"><table class="data files-table"><thead><tr>
        <th class="tick"><input type="checkbox" id="tick-all" aria-label="Select all files on this page"${all ? ' checked' : ''}></th>
        <th class="file-name">File</th><th class="file-type-col">Type</th><th>Company</th><th class="file-territory">Territory</th><th>Number / BRN</th><th class="file-date">Uploaded</th><th class="num file-size">Size</th><th class="file-open"></th></tr></thead><tbody>
      ${rows.map(r => `<tr class="${r.id === previewId ? 'open' : ''}" data-id="${r.id}">
        <td class="tick"><input type="checkbox" data-tick="${r.id}" aria-label="Select ${esc(r.fileName)}"${ticked.has(r.id) ? ' checked' : ''}></td>
        <td class="file-name"><button type="button" class="link-button" data-preview="${r.id}" title="${esc(r.fileName)}">${esc(r.fileName || 'File ' + r.id)}</button></td>
        <td class="file-type-col">${tag(r)}</td>
        <td title="${esc(r.company)}">${r.companyId ? `<a href="company.aspx?id=${r.companyId}">${esc(r.company)}</a>` : esc(r.company) || '<span class="muted">No company</span>'}</td>
        <td class="file-territory muted" title="${esc(r.territory)}">${esc(r.territory) || '–'}</td>
        <td title="${esc([numbers(r), r.brn ? 'BRN ' + r.brn : ''].filter(Boolean).join(' · '))}">${esc(r.numbers.length ? r.numbers[0].number : (r.brn ? 'BRN ' + r.brn : ''))}</td>
        <td class="file-date">${esc(date(r.uploadedAt))}</td>
        <td class="num file-size">${esc(size(r.size))}</td>
        <td class="file-open"><span class="row-first"><button type="button" class="icon-button small" data-preview="${r.id}" title="Show ${esc(r.fileName)}" aria-label="Show ${esc(r.fileName)}"><span data-icon="eye"></span></button>
          <a class="icon-button small" href="${downloadUrl(r.id)}" title="Download ${esc(r.fileName)}" aria-label="Download ${esc(r.fileName)}"><span data-icon="download"></span></a></span></td></tr>`).join('')}
      </tbody></table></div>`
      : `<div class="empty"><h3>No files to show</h3><p class="muted">${filtered ? 'No files match. Try another number or file type.' : 'There are no files you may see.'}</p></div>`;
    // On a phone: a card per file, opening its PDF on the whole screen.
    $('#cards').innerHTML = rows.map(r => `<a class="file-card" href="${viewUrl(r.id)}" target="_blank" rel="noopener" aria-label="Open ${esc(r.fileName)}">
        <span class="file-card-body"><span class="file-card-top"><b>${esc(r.fileName || 'File ' + r.id)}</b>${tag(r)}</span>
          <span class="file-card-line">${esc([number(r) || (r.brn ? 'BRN ' + r.brn : ''), size(r.size)].filter(Boolean).join(' · '))}</span>
          <span class="file-card-line strong">${esc(r.company) || 'No company'}${r.territory ? `<span class="muted"> · ${esc(r.territory)}</span>` : ''}</span>
          <span class="file-card-line">${esc(date(r.uploadedAt))}${r.uploadedBy ? ' · ' + esc(r.uploadedBy) : ''}</span></span>
        <span data-icon="arrow-right" class="section-chevron"></span></a>`).join('');
    drawIcons($('#rows'));
    drawIcons($('#cards'));
    pager($('#pager'), data.total, data.skip, data.take);
    selection();
    if (!rows.some(r => r.id === previewId)) previewId = wide.matches && rows.length ? rows[0].id : 0;
    preview();
  }

  function selection() {
    const ids = [...ticked.keys()];
    $('#selection').hidden = ids.length === 0;
    $('#selected-count').textContent = `${ids.length} selected`;
    $('#zip').href = 'file.ashx/files/zip?' + new URLSearchParams({ids: ids.join(',')});
    $('#zip').classList.toggle('disabled', ids.length > 50);
    $('#zip').title = ids.length > 50 ? 'At most 50 files at a time' : '';
  }

  /** The chosen file beside the list (on a computer): its PDF in the page, its facts, Open full size and Download. */
  function preview() {
    const box = $('#preview');
    const r = data && data.rows.find(x => x.id === previewId);
    $$('#rows tr[data-id]').forEach(tr => tr.classList.toggle('open', Number(tr.dataset.id) === previewId));
    if (!r || !wide.matches) { box.hidden = true; box.innerHTML = ''; return; }
    box.hidden = false;
    if (box.dataset.id === String(r.id)) return;
    box.dataset.id = String(r.id);
    box.innerHTML = `<div class="file-preview-head"><h2 title="${esc(r.fileName)}">${esc(r.fileName || 'File ' + r.id)}</h2>${tag(r)}</div>
      <iframe class="file-frame" src="${viewUrl(r.id)}" title="${esc(r.fileName)}"></iframe>
      <dl class="facts-list file-facts">
        ${r.numbers.map(n => `<dt>${esc(n.kind)}</dt><dd>${esc(n.number)}</dd>`).join('')}
        <dt>Company</dt><dd>${r.companyId ? `<a class="text-button" href="company.aspx?id=${r.companyId}">${esc(r.company)}</a>` : esc(r.company) || 'No company'}</dd>
        ${r.brn ? `<dt>BRN</dt><dd>${esc(r.brn)}</dd>` : ''}
        <dt>Uploaded</dt><dd>${esc(date(r.uploadedAt))}${r.uploadedBy ? ' by ' + esc(r.uploadedBy) : ''} · ${esc(size(r.size))}</dd>
      </dl>
      <div class="action-buttons"><a class="button secondary small-btn" href="${viewUrl(r.id)}" target="_blank" rel="noopener"><span data-icon="arrow-up-right"></span>Open full size</a>
        <a class="button primary small-btn" href="${downloadUrl(r.id)}"><span data-icon="download"></span>Download</a></div>`;
    drawIcons(box);
  }

  page.addEventListener('click', event => {
    const type = event.target.closest('[data-type]');
    if (type) { filter.type = type.dataset.type; filter.skip = 0; load(); return; }
    const show = event.target.closest('[data-preview]');
    if (show) {
      previewId = Number(show.dataset.preview);
      if (wide.matches) preview(); else window.open(viewUrl(previewId), '_blank', 'noopener');
      return;
    }
    if (event.target.closest('#zip.disabled')) { event.preventDefault(); return; }
    const move = event.target.closest('[data-page]');
    if (move && data) { filter.skip = Math.max(0, data.skip + Number(move.dataset.page) * data.take); load(); window.scrollTo({top: 0}); }
  });
  page.addEventListener('change', event => {
    if (event.target.id === 'tick-all') {
      data.rows.forEach(r => { if (event.target.checked) ticked.set(r.id, true); else ticked.delete(r.id); });
      render();
      return;
    }
    const box = event.target.closest('[data-tick]');
    if (box) {
      const id = Number(box.dataset.tick);
      if (box.checked) ticked.set(id, true); else ticked.delete(id);
      const all = $('#tick-all');
      if (all) all.checked = data.rows.every(r => ticked.has(r.id));
      selection();
    }
  });
  wide.addEventListener('change', () => { if (data) { if (wide.matches && !previewId && data.rows.length) previewId = data.rows[0].id; preview(); } });

  load();
})();
