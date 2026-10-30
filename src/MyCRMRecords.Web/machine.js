(() => {
  'use strict';
  // machine.aspx?serial=&item=: a machine of Advance, read only: its customer, contract, warranty and finance type, the
  // copies billed per month (the last 6 or 12 months), its service calls, toner orders and meter readings, and for a
  // machine on time and materials its last contract's rates. Only for those who see the machine's company (or have
  // temporary access to it).
  const {$, $$, esc, api, drawIcons, date, number, param, coverageTag, coverageText} = window.WH;
  const page = $('#page');
  const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  let d = null, span = 6, tab = 'service';

  async function load() {
    const serial = param('serial') || '';
    const answer = await api('GET', 'machines/detail?' + new URLSearchParams({serial, item: param('item') || ''}));
    $('#loading').hidden = true;
    page.hidden = false;
    if (!answer.result.ok) {
      page.innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3><p><a class="button secondary" href="machines.aspx">Back to the machines</a></p></div>`;
      return;
    }
    d = answer;
    render();
  }

  const fact = (label, value) => `<div><div class="fact-label">${esc(label)}</div><div>${value || '<span class="muted">–</span>'}</div></div>`;
  const dim = value => value && value.slice(0, 10) < new Date().toISOString().slice(0, 10);
  const when = value => value ? `<span class="${dim(value) ? 'muted' : ''}">${esc(date(value))}</span>` : '';

  function render() {
    const m = d.machine;
    document.title = m.serial + ' · MyCRMRecords';
    const company = m.companyId ? `<a href="company.aspx?id=${m.companyId}#machines">${esc(m.customer)}</a>` : esc(m.customer);
    const warranty = m.warrantyEnd ? `${dim(m.warrantyEnd) ? 'Ended' : 'Ends'} ${esc(date(m.warrantyEnd))}` : '';
    page.innerHTML = `
      <div class="page-heading"><div><a class="crumb" href="machines.aspx"><span data-icon="arrow-left"></span>Machines</a>
          <h1 class="machine-title">${esc(m.serial)} ${coverageTag(m)}</h1>
          <p>${esc([m.model, m.description !== m.model ? m.description : ''].filter(Boolean).join(' · '))} · ${company} · ${esc(m.via)}</p></div>
        <div class="heading-actions">${d.lastContract ? '<button type="button" class="button secondary" id="last-contract"><span data-icon="history"></span>Last contract</button>' : ''}</div></div>
      <section class="card"><div class="facts machine-facts">
        ${fact('Installed', esc(date(d.installed)))}
        ${fact('Contract', m.contractNo ? `${esc(m.contractNo)} <span class="muted small">${esc(m.contractType)}</span>` : '')}
        ${fact('Contract period', m.contractStart || m.contractEnd ? `${when(m.contractStart)} – ${when(m.contractEnd)}` : '')}
        ${fact('Finance type', m.finance ? `${esc(m.finance)} <span class="muted small">${esc(m.financeCode)}</span>` : '')}
        ${fact('Warranty', warranty)}
        ${fact('Coverage', esc(coverageText(m)) || esc(m.coverageName))}
        ${fact('Contact', esc([d.contact, d.phone].filter(Boolean).join(' · ')))}
        ${fact('Location', esc(d.location))}
        ${fact('Address', esc(d.address))}
        ${fact('BRN', esc(m.brn))}
        ${fact('Customer no.', esc(m.customerNo))}
        ${m.coverage === 'TM' ? fact('Last service / invoice', esc([m.lastService, m.lastInvoice].filter(Boolean).join(' / '))) : fact('Item', esc(m.item))}
      </div></section>
      <section class="card"><div class="card-heading chart-heading"><h2>Copies per month</h2>
          <span class="legend"><span><i style="background:var(--teal)"></i>Black &amp; white</span><span><i style="background:var(--coral)"></i>Colour</span></span>
          <div class="tabs span-switch" role="group" aria-label="Months shown">
            <button type="button" class="tab" data-span="6" aria-pressed="true">6 months</button><button type="button" class="tab" data-span="12" aria-pressed="false">12 months</button></div></div>
        <div class="chart machine-chart" id="chart"></div></section>
      <div class="tabs machine-tabs" role="tablist" aria-label="Machine history">
        <button type="button" class="tab" role="tab" data-tab="service">Service history <span class="count">${d.services.length}</span></button>
        <button type="button" class="tab" role="tab" data-tab="toner">Toner supply <span class="count">${d.toner.length}</span></button>
        <button type="button" class="tab" role="tab" data-tab="meters">Meter readings <span class="count">${d.meters.length}</span></button></div>
      <section class="card" id="history"></section>`;
    drawIcons(page);
    chart();
    history();
    $('#last-contract')?.addEventListener('click', lastContract);
  }

  /** The copies of each month (the invoices' dates), the last 6 or 12 months: black and white in teal, colour in coral. */
  function chart() {
    $$('.span-switch .tab', page).forEach(b => b.setAttribute('aria-pressed', String(Number(b.dataset.span) === span)));
    const now = new Date();
    const buckets = [];
    for (let i = span - 1; i >= 0; i--) {
      const at = new Date(now.getFullYear(), now.getMonth() - i, 1);
      buckets.push({key: `${at.getFullYear()}-${String(at.getMonth() + 1).padStart(2, '0')}`, label: `${months[at.getMonth()]} ${String(at.getFullYear()).slice(2)}`, bw: 0, colour: 0});
    }
    for (const r of d.meters) {
      const b = buckets.find(x => x.key === (r.invoiceDate || '').slice(0, 7));
      if (b) { b.bw += r.blackWhite; b.colour += r.colour; }
    }
    const max = Math.max(1, ...buckets.map(b => Math.max(b.bw, b.colour)));
    if (!d.meters.length) { $('#chart').innerHTML = '<p class="muted small">Advance has no billed copies of this machine in the last 12 months.</p>'; return; }
    const w = Math.max(320, Math.round($('#chart').clientWidth || 1000)), h = 200, top = 12, bottom = 30, slot = w / buckets.length, bar = Math.max(4, Math.min(34, slot / 4));
    const y = v => top + (h - top - bottom) * (1 - v / max);
    $('#chart').innerHTML = `<svg viewBox="0 0 ${w} ${h}" role="img" aria-label="Copies per month">
      <line x1="0" x2="${w}" y1="${h - bottom}" y2="${h - bottom}" stroke="var(--line)"/>
      ${buckets.map((b, i) => {
        const x = i * slot + slot / 2;
        return `<g><title>${esc(b.label)}: ${number(b.bw)} black & white, ${number(b.colour)} colour</title>
          <rect x="${x - bar - 3}" y="${y(b.bw)}" width="${bar}" height="${h - bottom - y(b.bw)}" rx="6" fill="var(--teal)"/>
          <rect x="${x + 3}" y="${y(b.colour)}" width="${bar}" height="${h - bottom - y(b.colour)}" rx="6" fill="var(--coral)"/>
          <text x="${x}" y="${h - 8}" text-anchor="middle">${esc(b.label)}</text></g>`;
      }).join('')}</svg>`;
  }

  const reading = v => v === null || v === undefined ? '<span class="muted">–</span>' : number(v);
  function history() {
    $$('.machine-tabs .tab', page).forEach(b => b.setAttribute('aria-selected', String(b.dataset.tab === tab)));
    const box = $('#history');
    const table = (head, rows, none) => rows.length
      ? `<div class="table-wrap"><table class="data"><thead><tr>${head.map(h => `<th${h.num ? ' class="num"' : ''}>${esc(h.label || h)}</th>`).join('')}</tr></thead><tbody>${rows.join('')}</tbody></table></div>`
      : `<p class="muted">${none}</p>`;
    if (tab === 'service')
      box.innerHTML = table(['Service no.', 'Service type', 'Technician', {label: 'B&W reading', num: true}, {label: 'Colour reading', num: true}, 'Entered', 'Completed'],
        d.services.map(s => `<tr><td class="nowrap">${esc(s.serviceNo)}</td><td><b>${esc(s.serviceType)}</b> <span class="muted small">${esc(s.group)}</span></td>
          <td>${esc(s.technician)}</td><td class="num">${reading(s.blackWhite)}</td><td class="num">${reading(s.colour)}</td>
          <td class="nowrap">${esc(date(s.entered))} ${esc(s.enteredTime)}</td><td class="nowrap">${esc(date(s.completed))} ${esc(s.completedTime)}</td></tr>`),
        'No service calls done in the last 12 months.');
    else if (tab === 'toner')
      box.innerHTML = table(['Service no.', 'Ordered', 'Invoice', 'Invoiced'],
        d.toner.map(t => `<tr><td class="nowrap">${esc(t.serviceNo)}</td><td class="nowrap">${esc(date(t.entered))}</td><td>${esc(t.invoiceNo)}</td><td class="nowrap">${esc(date(t.invoiceDate))}</td></tr>`),
        'No toner orders in the last 12 months.');
    else
      box.innerHTML = table(['Invoice', 'Invoiced', {label: 'Black & white copies', num: true}, {label: 'Colour copies', num: true}],
        d.meters.map(r => `<tr><td>${esc(r.invoiceNo)}</td><td class="nowrap">${esc(date(r.invoiceDate))}</td><td class="num">${number(r.blackWhite)}</td><td class="num">${number(r.colour)}</td></tr>`),
        'No billed meter readings in the last 12 months.');
    drawIcons(box);
  }

  /** A T&M machine's last contract: its rates per meter type (Advance's daily_TM). */
  function lastContract() {
    const c = d.lastContract;
    const money = v => '$' + Number(v).toLocaleString('en-SG', {minimumFractionDigits: 2, maximumFractionDigits: 4});
    const name = t => /^b/i.test(t) ? 'Black & white' : /^c/i.test(t) ? 'Colour' : t;
    const dialog = document.createElement('dialog');
    dialog.innerHTML = `<div class="modal last-contract"><div class="modal-head"><div><h2>Last contract · ${esc(d.machine.serial)}</h2>
        <p class="muted small">${esc([c.contractNo, c.start || c.end ? `${date(c.start)} – ${date(c.end)}` : ''].filter(Boolean).join(' · '))}</p></div></div>
      ${c.rates.map(r => `<h3 class="rate-head">${esc(name(r.meterType))} <span class="muted small">${esc(r.meterType)}</span></h3>
        <div class="facts rate-facts">${fact('Min bill', esc(money(r.minBill)))}${fact('Min copies', esc(number(r.minCopies)))}${fact('Min rate', esc(money(r.minRate)))}${fact('Thereafter', esc(money(r.thereafter)))}</div>`).join('')}
      <div class="modal-footer"><button type="button" class="button primary" value="done">Done</button></div></div>`;
    document.body.append(dialog);
    const close = () => { dialog.close(); dialog.remove(); };
    $('button[value=done]', dialog).addEventListener('click', close);
    dialog.addEventListener('cancel', event => { event.preventDefault(); close(); });
    dialog.showModal();
  }

  page.addEventListener('click', event => {
    const s = event.target.closest('[data-span]');
    if (s) { span = Number(s.dataset.span); chart(); return; }
    const t = event.target.closest('[data-tab]');
    if (t) { tab = t.dataset.tab; history(); }
  });

  load();
})();
