(() => {
  'use strict';
  // statistics.aspx: a financial year's figures (MyCRM's Statistics): the completed feedbacks' monthly averages against
  // their service targets, how many were closed within 10 business days, the feedbacks by category and by division, and
  // the report of the months chosen (Excel). Charts are drawn at the width they are shown at; each column or bar has a
  // tooltip on pointing or tabbing to it, and a click lists the feedbacks behind it.
  const {$, $$, esc, api, toast, date, drawIcons} = window.WH;
  const page = $('#page');
  let data = null, year = 0, selected = null;
  const tip = document.createElement('div');
  tip.className = 'chart-tip';
  tip.hidden = true;
  document.body.append(tip);

  async function load() {
    const answer = await api('GET', 'statistics' + (year ? '?year=' + year : ''));
    $('#loading').hidden = true;
    page.hidden = false;
    if (!answer.result.ok) { page.innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3></div>`; return; }
    data = answer;
    year = data.year;
    selected = null;
    render();
  }

  const byId = () => new Map(data.feedbacks.map(f => [f.id, f]));
  const average = (list, key) => list.length ? Math.round(list.reduce((s, f) => s + f[key], 0) / list.length) : 0;

  function render() {
    const all = data.feedbacks;
    const within = all.filter(f => f.closeCaseDays <= data.targets.close).length;
    page.innerHTML = `
      <div class="page-heading"><div><h1>Statistics</h1><p>The feedbacks completed in the financial year, by the month they were received, against their service targets.</p></div>
        <div class="heading-actions"><label class="field" style="min-width:150px"><span>Financial year</span><select id="year">
          ${data.years.map(y => `<option value="${y.year}"${y.year === data.year ? ' selected' : ''}>${esc(y.label)}</option>`).join('')}</select></label></div></div>
      <div class="tiles" style="margin-bottom:16px">
        <div class="tile"><div class="tile-label">Completed</div><div class="big-number">${all.length}</div></div>
        <div class="tile"><div class="tile-label">Acknowledged in (average)</div><div class="big-number">${average(all, 'acknowledgedHours')} h</div><div class="muted small">target ${data.targets.acknowledge} hours</div></div>
        <div class="tile"><div class="tile-label">Resolution started in</div><div class="big-number">${average(all, 'startResolutionDays')} d</div><div class="muted small">target ${data.targets.start} business days</div></div>
        <div class="tile"><div class="tile-label">Closed in</div><div class="big-number">${average(all, 'closeCaseDays')} d</div><div class="muted small">target ${data.targets.close} business days</div></div>
        <div class="tile"><div class="tile-label">Closed within ${data.targets.close} days</div><div class="big-number">${all.length ? Math.round(within * 100 / all.length) : 0}%</div><div class="muted small">target ${data.targets.within}%</div></div>
      </div>
      <div class="charts">
        <section class="card chart"><h2>Average of the 24hr acknowledgement for the month</h2><div class="plot-box" data-chart="acknowledgedHours"></div></section>
        <section class="card chart"><h2>Average of 2 days resolution for the month</h2><div class="plot-box" data-chart="startResolutionDays"></div></section>
        <section class="card chart"><h2>Average of 10 days close case for the month</h2><div class="plot-box" data-chart="closeCaseDays"></div></section>
        <section class="card chart"><h2>Resolved within the service standard for the month</h2><div class="plot-box" data-chart="within"></div></section>
        <section class="card chart"><h2>Feedback category</h2><div data-bars="categories"></div></section>
        <section class="card chart"><h2>Division involved in feedback</h2><div data-bars="divisions"></div></section>
      </div>
      <section class="card" id="picked" hidden style="margin-top:16px"></section>
      <section class="card" style="margin-top:16px"><div class="card-heading"><h2>Report</h2>
          <button class="button primary" id="download"><span data-icon="excel"></span>Download</button></div>
        <p class="muted">The feedbacks received in the months ticked, whatever their status: the person and division in charge, the company, the timings, the description, root cause and corrective action.</p>
        <div id="months" style="display:flex;flex-wrap:wrap;gap:6px 16px;margin-top:10px">${data.reportMonths.map(m => `<label class="check"><input type="checkbox" value="${m.month}"${m.month === data.thisMonth ? ' checked' : ''}>${esc(m.label)}</label>`).join('')}</div>
        <p class="muted small" id="report-count" style="margin-top:10px"></p></section>`;
    drawIcons(page);
    $('#year').addEventListener('change', event => { year = Number(event.target.value); load(); });
    $('#months').addEventListener('change', reportCount);
    $('#download').addEventListener('click', () => {
      const months = chosenMonths();
      if (!months.length) { toast('Please select month', true); return; }
      location.href = 'file.ashx/report?months=' + encodeURIComponent(months.join(','));
    });
    reportCount();
    draw();
  }

  const chosenMonths = () => $$('#months input:checked').map(b => b.value);
  async function reportCount() {
    const months = chosenMonths();
    if (!months.length) { $('#report-count').textContent = 'Tick the months to report.'; return; }
    const answer = await api('GET', 'statistics/report?months=' + encodeURIComponent(months.join(',')));
    $('#report-count').textContent = answer.result.ok ? `${answer.count} feedback${answer.count === 1 ? '' : 's'} received in those months.` : answer.result.message;
  }

  // ---- charts ----
  const SVG = 'http://www.w3.org/2000/svg';
  const short = label => label.slice(0, 3);
  /** A column with its top corners rounded (4px), from the baseline. */
  const column = (x, y, w, h, r = 4) => h <= 0 ? '' : h < r ? `M${x},${y + h}V${y}H${x + w}V${y + h}Z`
    : `M${x},${y + h}V${y + r}Q${x},${y} ${x + r},${y}H${x + w - r}Q${x + w},${y} ${x + w},${y + r}V${y + h}Z`;
  /** An axis to a little above the value, in 3 to 5 whole steps of 1, 2 or 5 (×10ⁿ): 0, 10, 20, 30, 40, 50. */
  const niceScale = value => {
    const raw = Math.max(1, value * 1.1) / 4, mag = Math.pow(10, Math.floor(Math.log10(raw)));
    const step = Math.max(1, [1, 2, 5, 10].map(m => m * mag).find(x => x >= raw));
    return {max: Math.ceil(Math.max(1, value * 1.1) / step) * step, step};
  };

  function draw() {
    $$('[data-chart]').forEach(box => box.dataset.chart === 'within' ? stacked(box) : averages(box, box.dataset.chart));
    $$('[data-bars]').forEach(box => bars(box, data[box.dataset.bars]));
  }

  function frame(box, height) {
    const width = Math.max(260, box.clientWidth);
    return {width, height, left: 38, right: 10, top: 18, bottom: 26};
  }

  function grid(f, scale, unit) {
    let out = '';
    for (let value = 0; value <= scale.max; value += scale.step) {
      const y = f.top + (f.height - f.top - f.bottom) * (1 - value / scale.max);
      out += `<line x1="${f.left}" x2="${f.width - f.right}" y1="${y}" y2="${y}" class="grid-line"/><text x="${f.left - 6}" y="${y + 4}" text-anchor="end">${value}${unit}</text>`;
    }
    return out;
  }

  /** A month's average against its target: within it in blue, over it in red with "over" under its value. */
  function averages(box, key) {
    const target = {acknowledgedHours: data.targets.acknowledge, startResolutionDays: data.targets.start, closeCaseDays: data.targets.close}[key];
    const unit = key === 'acknowledgedHours' ? ' h' : ' d';
    const f = frame(box, 230);
    const months = data.months;
    const scale = niceScale(Math.max(target, ...months.map(m => m[key]))), max = scale.max;
    const plotH = f.height - f.top - f.bottom, slot = (f.width - f.left - f.right) / months.length, barW = Math.max(6, Math.min(34, slot - 6));
    const y = value => f.top + plotH * (1 - value / max);
    const labels = slot >= 26;
    let marks = '';
    months.forEach((m, i) => {
      const x = f.left + i * slot + (slot - barW) / 2, value = m[key], over = value > target;
      marks += `<g class="mark" tabindex="0" role="button" data-month="${i}" data-key="${key}" aria-label="${esc(m.label)}: ${value}${unit}, ${m.ids.length} feedbacks${over ? ', over the target' : ''}">
        <rect x="${f.left + i * slot}" y="${f.top}" width="${slot}" height="${plotH}" class="hit"/>
        ${m.ids.length ? `<path d="${column(x, y(value), barW, plotH - (y(value) - f.top))}" class="${over ? 'bar-over' : 'bar-ok'}"/>` : ''}
        ${labels && m.ids.length ? `<text x="${x + barW / 2}" y="${y(value) - 5}" text-anchor="middle" class="value">${value}</text>` : ''}
        <text x="${x + barW / 2}" y="${f.height - 8}" text-anchor="middle">${slot >= 30 || i % 2 === 0 ? short(m.label) : ''}</text></g>`;
    });
    const ty = y(target);
    box.innerHTML = `<svg class="plot" viewBox="0 0 ${f.width} ${f.height}" width="${f.width}" height="${f.height}" role="img" aria-label="Average${unit === ' h' ? ' hours' : ' business days'} by month">
      ${grid(f, scale, '')}${marks}<line x1="${f.left}" x2="${f.width - f.right}" y1="${ty}" y2="${ty}" class="target-line"/>
      <text x="${f.width - f.right}" y="${ty - 5}" text-anchor="end" class="target-label">target ${target}${unit}</text></svg>
      <div class="legend"><span><i class="swatch-ok"></i>Within the target</span><span><i class="swatch-over"></i>Over the target</span><span><i class="swatch-target"></i>Target</span></div>`;
  }

  /** Each month's completed feedbacks closed within 10 business days and over, as shares of 100%; the target is 95%. */
  function stacked(box) {
    const f = frame(box, 230);
    const months = data.months;
    const plotH = f.height - f.top - f.bottom, slot = (f.width - f.left - f.right) / months.length, barW = Math.max(6, Math.min(34, slot - 6));
    const y = share => f.top + plotH * (1 - share);
    let marks = '';
    months.forEach((m, i) => {
      const x = f.left + i * slot + (slot - barW) / 2, total = m.within.length + m.over.length;
      const share = total ? m.within.length / total : 0;
      const okTop = y(share), overH = total ? okTop - f.top - (m.over.length && m.within.length ? 2 : 0) : 0;
      marks += `<g class="mark" tabindex="0" role="button" data-month="${i}" data-key="within" aria-label="${esc(m.label)}: ${m.within.length} within ${data.targets.close} days, ${m.over.length} over">
        <rect x="${f.left + i * slot}" y="${f.top}" width="${slot}" height="${plotH}" class="hit"/>
        ${m.within.length ? `<path d="${column(x, okTop, barW, f.top + plotH - okTop, m.over.length ? 0 : 4)}" class="bar-ok"/>` : ''}
        ${m.over.length ? `<path d="${column(x, f.top, barW, overH)}" class="bar-over"/>` : ''}
        ${slot >= 30 && total ? `<text x="${x + barW / 2}" y="${f.top - 5}" text-anchor="middle" class="value">${Math.round(share * 100)}%</text>` : ''}
        <text x="${x + barW / 2}" y="${f.height - 8}" text-anchor="middle">${slot >= 30 || i % 2 === 0 ? short(m.label) : ''}</text></g>`;
    });
    const ty = y(data.targets.within / 100);
    let axis = '';
    for (let i = 0; i <= 4; i++) {
      const yy = f.top + plotH * (1 - i / 4);
      axis += `<line x1="${f.left}" x2="${f.width - f.right}" y1="${yy}" y2="${yy}" class="grid-line"/><text x="${f.left - 6}" y="${yy + 4}" text-anchor="end">${i * 25}%</text>`;
    }
    box.innerHTML = `<svg class="plot" viewBox="0 0 ${f.width} ${f.height}" width="${f.width}" height="${f.height}" role="img" aria-label="Share closed within ${data.targets.close} business days by month">
      ${axis}${marks}<line x1="${f.left}" x2="${f.width - f.right}" y1="${ty}" y2="${ty}" class="target-line"/>
      <text x="${f.width - f.right}" y="${ty + 13}" text-anchor="end" class="target-label">target ${data.targets.within}%</text></svg>
      <div class="legend"><span><i class="swatch-ok"></i>Within ${data.targets.close} business days</span><span><i class="swatch-over"></i>Over ${data.targets.close}</span><span><i class="swatch-target"></i>Target</span></div>`;
  }

  /** One bar per category or division, by how many feedbacks (one colour: identity is the label). */
  function bars(box, slices) {
    if (!slices.length) { box.innerHTML = '<p class="muted">No completed feedbacks in this year.</p>'; return; }
    const max = Math.max(...slices.map(s => s.ids.length));
    const total = slices.reduce((sum, s) => sum + s.ids.length, 0);
    box.innerHTML = `<div class="hbars">${slices.map((s, i) => `<button type="button" class="hbar bar-row" data-slice="${box.dataset.bars}" data-index="${i}" aria-label="${esc(s.label)}: ${s.ids.length} feedbacks">
      <span class="bar-label">${esc(s.label)}</span><span class="bar"><i style="width:${Math.max(2, s.ids.length * 100 / max)}%"></i></span>
      <span class="num">${s.ids.length} <span class="muted small">· ${Math.round(s.ids.length * 100 / total)}%</span></span></button>`).join('')}</div>`;
  }

  // ---- tooltips and the feedbacks behind a mark ----
  function behind(target) {
    if (target.dataset.slice) {
      const slice = data[target.dataset.slice][Number(target.dataset.index)];
      return {title: slice.label, ids: slice.ids, lines: [`${slice.ids.length} feedback${slice.ids.length === 1 ? '' : 's'}`]};
    }
    const m = data.months[Number(target.dataset.month)], key = target.dataset.key;
    if (key === 'within') return {title: m.label, ids: [...m.within, ...m.over], lines: [`${m.within.length} within ${data.targets.close} business days`, `${m.over.length} over`]};
    const unit = key === 'acknowledgedHours' ? 'hours' : 'business days';
    return {title: m.label, ids: m.ids, lines: [`Average ${m[key]} ${unit}`, `${m.ids.length} completed feedback${m.ids.length === 1 ? '' : 's'}`]};
  }

  function showTip(target, x, y) {
    const b = behind(target);
    tip.innerHTML = `<div class="tip-title">${esc(b.title)}</div>${b.lines.map(l => `<div>${esc(l)}</div>`).join('')}${b.ids.length ? '<div class="muted small">Click to list them</div>' : ''}`;
    tip.hidden = false;
    const r = tip.getBoundingClientRect();
    tip.style.left = Math.max(8, Math.min(x + 14, innerWidth - r.width - 8)) + 'px';
    tip.style.top = Math.max(8, Math.min(y + 14, innerHeight - r.height - 8)) + 'px';
  }

  function pick(target) {
    const b = behind(target);
    const known = byId();
    const rows = b.ids.map(i => known.get(i)).filter(Boolean);
    const box = $('#picked');
    box.hidden = false;
    box.innerHTML = `<div class="card-heading"><h2>${esc(b.title)}: ${rows.length} feedback${rows.length === 1 ? '' : 's'}</h2>
        <button class="icon-button" id="unpick" aria-label="Close the list"><span data-icon="close"></span></button></div>
      ${rows.length ? `<div class="table-wrap" style="margin-top:10px"><table class="data"><thead><tr><th>No.</th><th>Feedback</th><th>In charge</th><th class="num">Acknowledged</th><th class="num">Started</th><th class="num">Closed</th></tr></thead><tbody>
        ${rows.map(f => `<tr><td><a class="button secondary small-btn" href="feedback.aspx?id=${f.id}">${f.id}</a></td>
          <td class="wide-text"><b>${esc(f.companyName || 'Feedback')}</b><span class="sub">${esc(f.statusLabel)} · received ${esc(date(f.dateReceived))}</span></td>
          <td>${esc(f.inCharge)}<span class="sub">${esc(f.division)}</span></td>
          <td class="num">${f.acknowledgedHours} h</td><td class="num">${f.startResolutionDays} d</td><td class="num">${f.closeCaseDays} d</td></tr>`).join('')}</tbody></table></div>` : '<p class="muted">None.</p>'}`;
    drawIcons(box);
    $('#unpick').addEventListener('click', () => { box.hidden = true; });
    box.scrollIntoView({behavior: 'smooth', block: 'nearest'});
  }

  const markOf = el => el.closest?.('.mark, .bar-row');
  page.addEventListener('pointermove', event => { const m = markOf(event.target); if (m) showTip(m, event.clientX, event.clientY); else tip.hidden = true; });
  page.addEventListener('pointerleave', () => { tip.hidden = true; });
  page.addEventListener('focusin', event => { const m = markOf(event.target); if (m) { const r = m.getBoundingClientRect(); showTip(m, r.left + r.width / 2, r.top); } });
  page.addEventListener('focusout', () => { tip.hidden = true; });
  page.addEventListener('click', event => { const m = markOf(event.target); if (m) { tip.hidden = true; pick(m); } });
  page.addEventListener('keydown', event => { const m = markOf(event.target); if (m && (event.key === 'Enter' || event.key === ' ') && m.tagName !== 'BUTTON') { event.preventDefault(); pick(m); } });
  let resizeTimer = 0;
  addEventListener('resize', () => { clearTimeout(resizeTimer); resizeTimer = setTimeout(() => { if (data) draw(); }, 150); });
  load();
})();
