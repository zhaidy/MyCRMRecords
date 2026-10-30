(() => {
  'use strict';
  // usage.aspx?company=: a company's Usage & history, read only from Advance. Usage trends: the black and white or colour
  // copies billed on all its machines per month over the range chosen, each month beside the same month a year before,
  // as a chart and a table. Yearly history: the copies per year and how many machines were billed. Export to Excel
  // downloads both (file.ashx/usage). Only for those who may see the company's machines. The meter and range chosen are
  // kept in this browser for next time.
  const {$, $$, esc, api, drawIcons, number, param, crumbs} = window.WH;
  const page = $('#page');
  const id = Number(param('company') || 0);
  const names = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  const ranges = ['6', '12', '24', '48', '96'];
  const saved = (() => { try { return JSON.parse(localStorage.getItem('usage-view') || '{}'); } catch { return {}; } })();
  const state = {
    view: location.hash === '#yearly' ? 'yearly' : 'trend',
    meter: saved.meter === 'colour' ? 'colour' : 'bw',
    range: ranges.includes(saved.range) ? saved.range : '12',
    from: '', to: '',
  };
  let company = null, machines = 0, months = null, years = null, failure = '';

  const monthKey = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
  const thisMonth = () => { const now = new Date(); return new Date(now.getFullYear(), now.getMonth(), 1); };
  const monthName = (m, withYear) => names[m.month - 1] + (withYear ? ' ' + m.year : '');
  const keep = () => { try { localStorage.setItem('usage-view', JSON.stringify({meter: state.meter, range: state.range})); } catch { /* not kept */ } };

  /** The span asked for: the last N months, or the months chosen (with the year before each to compare). */
  function span() {
    if (state.range === 'custom' && state.from && state.to) {
      const [fy, fm] = state.from.split('-').map(Number);
      const from = new Date(fy - 1, fm - 1, 1);
      return {from: monthKey(from), to: state.to};
    }
    return {months: String(Number(state.range === 'custom' ? '12' : state.range) + 12)};
  }
  const exportQuery = () => {
    if (state.range === 'custom' && state.from && state.to) return new URLSearchParams({company: String(id), from: state.from, to: state.to});
    return new URLSearchParams({company: String(id), months: state.range === 'custom' ? '12' : state.range});
  };

  async function loadMonths() {
    const answer = await api('GET', 'machines/copies?' + new URLSearchParams({company: String(id), ...span()}));
    if (!answer.result.ok) { failure = answer.result.message; return false; }
    company = answer.company;
    machines = answer.machines;
    months = answer.months;
    return true;
  }

  async function loadYears() {
    const answer = await api('GET', 'machines/years?' + new URLSearchParams({company: String(id), years: '6'}));
    if (!answer.result.ok) { failure = answer.result.message; return false; }
    company = answer.company;
    machines = answer.machines;
    years = answer.years;
    return true;
  }

  async function load() {
    const ok = state.view === 'yearly' ? await loadYears() : await loadMonths();
    $('#loading').hidden = true;
    page.hidden = false;
    if (!ok) {
      page.innerHTML = `<div class="empty"><h3>${esc(failure)}</h3><p><a class="button secondary" href="${id ? `company.aspx?id=${id}` : './'}">Back to the company</a></p></div>`;
      return;
    }
    shell();
    render();
  }

  /** The heading, the view tabs and the controls: drawn once, then only the content below them changes. */
  function shell() {
    document.title = `Usage & history · ${company.name} · MyCRMRecords`;
    page.innerHTML = `
      <div class="page-heading usage-heading"><div>${crumbs('Usage & history', [['Companies', './'], [company.name, `company.aspx?id=${company.id}`]])}
          <h1>Usage &amp; history</h1>
          <p id="usage-sub"></p></div>
        <div class="heading-actions"><div class="action-capsule" role="group" aria-label="Page actions">
          <a class="capsule-button" href="companymachines.aspx?company=${company.id}"><span data-icon="printer"></span><span class="action-label">Grouped by coverage</span></a>
          <a class="capsule-button" id="export" href="#"><span data-icon="excel"></span><span class="action-label">Export to Excel</span></a></div></div></div>
      <div class="usage-views"><div class="tabs" role="tablist" aria-label="View">
        <button type="button" class="tab" role="tab" data-view="trend" aria-controls="usage-body">Usage trends</button>
        <button type="button" class="tab" role="tab" data-view="yearly" aria-controls="usage-body">Yearly history</button></div></div>
      <div class="usage-controls" id="usage-controls">
        <div class="tabs" role="group" aria-label="Meter">
          <button type="button" class="tab" data-meter="bw">Black &amp; white</button>
          <button type="button" class="tab" data-meter="colour">Colour</button></div>
        <label class="filter-capsule"><span>Range</span><select id="range" aria-label="Range">
          ${ranges.map(r => `<option value="${r}"${state.range === r ? ' selected' : ''}>${r} months</option>`).join('')}
          <option value="custom"${state.range === 'custom' ? ' selected' : ''}>Choose dates…</option></select></label>
        <span class="usage-dates" id="usage-dates"${state.range === 'custom' ? '' : ' hidden'}>
          <label class="filter-capsule"><span>From</span><input type="month" id="from" value="${esc(state.from)}" max="${monthKey(thisMonth())}" aria-label="From month"></label>
          <label class="filter-capsule"><span>To</span><input type="month" id="to" value="${esc(state.to)}" max="${monthKey(thisMonth())}" aria-label="To month"></label></span>
        <span class="muted usage-span" id="usage-span"></span></div>
      <div id="usage-body" role="tabpanel"></div>`;
    drawIcons(page);
    $('#range').addEventListener('change', event => {
      state.range = event.target.value;
      $('#usage-dates').hidden = state.range !== 'custom';
      if (state.range === 'custom') {
        if (!state.from || !state.to) {
          const end = thisMonth();
          state.to = monthKey(end);
          state.from = monthKey(new Date(end.getFullYear(), end.getMonth() - 11, 1));
          $('#from').value = state.from;
          $('#to').value = state.to;
        }
      } else keep();
      refreshMonths();
    });
    for (const which of ['from', 'to'])
      $('#' + which).addEventListener('change', event => {
        if (!event.target.value) return;
        state[which] = event.target.value;
        if (state.from > state.to) { state[which === 'from' ? 'to' : 'from'] = event.target.value; $('#from').value = state.from; $('#to').value = state.to; }
        refreshMonths();
      });
  }

  async function refreshMonths() {
    $('#usage-body').setAttribute('aria-busy', 'true');
    const ok = await loadMonths();
    $('#usage-body').removeAttribute('aria-busy');
    if (!ok) { $('#usage-body').innerHTML = `<div class="empty"><h3>${esc(failure)}</h3></div>`; return; }
    render();
  }

  function render() {
    $$('[data-view]', page).forEach(b => b.setAttribute('aria-selected', String(b.dataset.view === state.view)));
    $$('[data-meter]', page).forEach(b => b.setAttribute('aria-pressed', String(b.dataset.meter === state.meter)));
    $('#usage-controls').hidden = state.view !== 'trend';
    $('#export').href = 'file.ashx/usage?' + exportQuery();
    $('#usage-sub').innerHTML = !company.brn ? 'The company has no BRN: its machines are found by BRN.'
      : machines ? `All ${machines} machine${machines === 1 ? '' : 's'} of <b>${esc(company.name)}</b> in Advance · BRN ${esc(company.brn)}${company.customerNo ? ` · Customer no. ${esc(company.customerNo)}` : ''}`
      : `Advance has no machines with the BRN ${esc(company.brn)}.`;
    if (state.view === 'yearly') yearly(); else trend();
  }

  /** The months chosen, each beside the same month a year before: a chart of pairs of bars and the table of the numbers. */
  function trend() {
    const body = $('#usage-body');
    if (!months) { body.innerHTML = '<p class="muted">Loading…</p>'; refreshMonths(); return; }
    const key = state.meter === 'bw' ? 'blackWhite' : 'colour';
    const rows = months.slice(12).map((m, i) => ({m, now: m[key], before: months[i][key]}));
    if (!rows.length) { body.innerHTML = '<p class="muted">No months to show.</p>'; return; }
    const first = rows[0].m, last = rows[rows.length - 1].m;
    $('#usage-span').textContent = `${monthName(first, true)} – ${monthName(last, true)}, compared with the year before`;
    const many = rows.length > 12;
    const every = Math.ceil(rows.length / 12);
    const top = Math.max(1, ...rows.map(r => Math.max(r.now, r.before)));
    const pct = v => v > 0 ? Math.max(1.5, v / top * 100) : 0;
    const change = r => {
      if (!r.before) return r.now ? {text: 'New', cls: 'up'} : {text: '–', cls: ''};
      const c = Math.round((r.now - r.before) / r.before * 100);
      return c > 0 ? {text: `▲ +${c}%`, cls: 'up'} : c < 0 ? {text: `▼ ${c}%`, cls: 'down'} : {text: '0%', cls: ''};
    };
    const title = (state.meter === 'bw' ? 'Black & white' : 'Colour') + ' copies per month';
    const total = k => rows.reduce((s, r) => s + r[k], 0);
    const label = `${title}, ${monthName(first, true)} to ${monthName(last, true)}: ${number(total('now'))} in all against ${number(total('before'))} the year before`;
    body.innerHTML = `
      <section class="card usage-chart-card ${state.meter === 'bw' ? 'meter-bw' : 'meter-colour'}" aria-labelledby="usage-chart-title">
        <div class="card-heading"><h2 id="usage-chart-title">${esc(title)}</h2>
          <span class="legend usage-legend"><span><i class="this"></i>This year</span><span><i class="before"></i>Year before</span></span></div>
        <div class="usage-chart${many ? ' many' : ''}" role="img" aria-label="${esc(label)}">${rows.map((r, i) => `<div class="usage-month" title="${esc(`${monthName(r.m, true)}: ${number(r.now)}, a year before ${number(r.before)}`)}">
          <div class="usage-bars"><span class="before" style="height:${pct(r.before)}%"></span><span class="this" style="height:${pct(r.now)}%"></span></div>
          <span class="usage-label">${i % every === 0 ? esc(many ? `${names[r.m.month - 1]} ${String(r.m.year).slice(2)}` : names[r.m.month - 1]) : ''}</span></div>`).join('')}</div>
        <p class="muted small usage-note">Billed copies from Advance, each machine's of its contract. In all: <b>${number(total('now'))}</b>, the year before <b>${number(total('before'))}</b>.</p>
      </section>
      <section class="card usage-table-card" aria-label="Copies by month"><div class="table-wrap"><table class="data usage-table"><thead><tr>
        <th>Month</th><th class="num">This year</th><th class="num">Year before</th><th class="num">Change</th></tr></thead><tbody>
        ${rows.map(r => { const c = change(r); return `<tr><td class="muted nowrap">${esc(monthName(r.m, true))}</td><td class="num"><b>${number(r.now)}</b></td>
          <td class="num">${number(r.before)}</td><td class="num nowrap usage-change ${c.cls}">${esc(c.text)}</td></tr>`; }).join('')}
        </tbody></table></div></section>`;
  }

  /** The copies of each year, this one so far, with how many machines were billed: a table, on a phone a card per year. */
  function yearly() {
    const body = $('#usage-body');
    if (!years) { body.innerHTML = '<p class="muted">Loading…</p>'; loadYears().then(ok => { if (ok) render(); else body.innerHTML = `<div class="empty"><h3>${esc(failure)}</h3></div>`; }); return; }
    const shown = years.slice().reverse();
    const thisYear = new Date().getFullYear();
    const change = (now, before) => !before ? '' : (c => c > 0 ? `<span class="usage-change up">▲ +${c}%</span>` : c < 0 ? `<span class="usage-change down">▼ ${c}%</span>` : '')(Math.round((now - before) / before * 100));
    const before = y => years.find(x => x.year === y.year - 1);
    body.innerHTML = `
      <section class="card usage-years" aria-labelledby="usage-years-title">
        <div class="card-heading usage-years-head"><h2 id="usage-years-title">Copies and machines, by year</h2>
          <p class="muted small">The copies billed each year on the company's machines in Advance and how many of them were billed; ${thisYear} so far.</p></div>
        <div class="table-wrap usage-years-wide"><table class="data usage-table"><thead><tr><th>Year</th><th class="num">Machines billed</th>
          <th class="num">Black &amp; white</th><th class="num">Colour</th><th class="num">Total</th></tr></thead><tbody>
          ${shown.map(y => { const b = before(y); return `<tr><td><b>${y.year}</b>${y.year === thisYear ? ' <span class="muted small">so far</span>' : ''}</td><td class="num">${number(y.machines)}</td>
            <td class="num">${number(y.blackWhite)} ${b && y.year !== thisYear ? change(y.blackWhite, b.blackWhite) : ''}</td>
            <td class="num">${number(y.colour)} ${b && y.year !== thisYear ? change(y.colour, b.colour) : ''}</td><td class="num"><b>${number(y.blackWhite + y.colour)}</b></td></tr>`; }).join('')}
          </tbody></table></div></section>
      <div class="usage-year-cards">${shown.map(y => `<section class="card usage-year-card" aria-label="${y.year}"><h2>${y.year}${y.year === thisYear ? ' <span class="muted small">so far</span>' : ''}</h2>
        <dl><div><dt>Machines</dt><dd>${number(y.machines)}</dd></div><div><dt>Black &amp; white</dt><dd>${number(y.blackWhite)}</dd></div>
          <div><dt>Colour</dt><dd>${number(y.colour)}</dd></div></dl></section>`).join('')}</div>`;
  }

  page.addEventListener('click', event => {
    const view = event.target.closest('[data-view]');
    if (view) {
      state.view = view.dataset.view;
      history.replaceState(null, '', state.view === 'yearly' ? '#yearly' : location.pathname + location.search);
      render();
      return;
    }
    const meter = event.target.closest('[data-meter]');
    if (meter) { state.meter = meter.dataset.meter; keep(); render(); }
  });

  if (!id) {
    $('#loading').hidden = true;
    page.hidden = false;
    page.innerHTML = '<div class="empty"><h3>Open a company first: its Usage &amp; history is on its Overview and Machines tab.</h3><p><a class="button secondary" href="./">Back to the companies</a></p></div>';
  } else load();
})();
