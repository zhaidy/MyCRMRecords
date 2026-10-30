(() => {
  'use strict';
  // costingsheet.aspx?id=: one costing sheet as MyOrders' summary showed it (header, attachments, products with their
  // items, billing, variable costs, trade-ins, meter charges), read only, with its history from the dates MyOrders kept
  // and the facts its summary did not show. Print opens print.aspx, MyOrders' costing sheet PDF as a page (New CS or
  // Old CS, chosen beside it). Laid out as the approved design: the key details under the title, the sections as tabs
  // (Product, Meter charges, Billing, Variable costs & trade-ins, More details) with History and Attachments beside
  // them; on a phone every section is a card that opens and closes.
  const {$, $$, esc, api, drawIcons, dateTime, date, param, crumbs} = window.WH;
  const page = $('#page');
  const id = Number(param('id') || 0);
  const money = (value, digits = 2) => Number(value || 0).toLocaleString('en-SG', {minimumFractionDigits: digits, maximumFractionDigits: digits});
  const dollars = (value, digits = 2) => '$' + money(value, digits);
  const yesNo = value => value ? '<span class="yes">Yes</span>' : '<span class="muted">No</span>';
  const text = value => value === null || value === undefined || value === '' ? '<span class="muted">-</span>' : esc(value);
  const when = value => value ? esc(dateTime(value)) : '<span class="muted">-</span>';
  const size = bytes => bytes >= 1048576 ? (bytes / 1048576).toFixed(1) + ' MB' : bytes >= 1024 ? Math.round(bytes / 1024) + ' KB' : bytes + ' bytes';

  /** A list of label: value pairs (values already HTML). */
  const facts = pairs => `<dl class="facts">${pairs.filter(Boolean).map(([label, value]) => `<div><dt>${esc(label)}</dt><dd>${value}</dd></div>`).join('')}</dl>`;
  const card = (title, body, extra = '') => `<section class="card"><div class="card-heading"><h2>${esc(title)}</h2>${extra}</div>${body}</section>`;
  /** A section: a tab's panel on a computer (or a box beside them), a card that opens and closes on a phone. */
  const section = (key, title, summary, body, note = '') => `<section class="cs-section" data-sec="${key}" aria-labelledby="head-${key}">
    <h2 class="cs-section-head" id="head-${key}"><button type="button" class="cs-toggle" aria-expanded="false" aria-controls="body-${key}"><span class="cs-section-title">${esc(title)}</span>
      <span class="cs-summary">${summary}</span><span data-icon="arrow-right" class="cs-chevron"></span></button></h2>
    <div class="cs-section-body" id="body-${key}">${note ? `<p class="muted small cs-note">${note}</p>` : ''}${body}</div></section>`;
  const empty = message => `<p class="muted">${esc(message)}</p>`;

  /** The key details under the title: everything MyOrders' summary showed at its top. */
  function details(c) {
    const specialists = c.specialists.length
      ? c.specialists.map(s => `<span class="pill${s.pending ? ' warn' : ''}" title="${s.pending ? 'Pending' : 'Done'}">${esc(s.role)}: ${esc(s.name)}${s.pending ? ' (pending)' : ''}</span>`).join(' ')
      : '<span class="muted">-</span>';
    const terms = [['Is ZBA', c.isZba], ['Is Contract Extension', c.isContractExtension], ['Is Fixed Term', c.isFixedTerm], ['Is Vital Pricing', c.isVitalPricing], ['GMA Pricing', c.gmaPricing]];
    return `<section class="card cs-details" aria-label="Costing sheet details">${facts([
      ['Contact', text(c.contact)],
      ['Customer No', text(c.customerNo)],
      ['Customer Type', text(c.customerType)],
      ['Billing Address', text(c.address)],
      ['Request Form', c.requestFormId ? `<a href="requestform.aspx?id=${c.requestFormId}"># ${esc(c.requestFormId)}</a>` : '<span class="muted">-</span>'],
      ['Specialists', specialists],
      ['Created At', when(c.createdAt)],
      ['Updated At', when(c.updatedAt)],
      ['Recall At', when(c.recallAt)],
      ['Transferred On', when(c.transferredOn)],
      ['Original Salesman', text(c.originalSalesman)],
      ['Event Tag', text(c.eventTag)],
    ])}<div class="cs-terms" aria-label="Terms">${terms.map(([label, on]) => `<span class="cs-term${on ? ' on' : ''}">${esc(label)}: ${on ? 'Yes' : 'No'}</span>`).join('')}</div></section>`;
  }

  function history(c) {
    const f = c.facts;
    const steps = c.history.length ? `<ol class="cs-timeline">${c.history.map((s, i) => `<li${i === c.history.length - 1 ? ' class="last"' : ''}><span class="cs-dot" aria-hidden="true"></span>
        <span class="cs-step"><span><b>${esc(s.step)}</b>${s.by ? ` · ${esc(s.by)}` : ''}</span><span class="cs-when">${when(s.at)}</span>${s.detail ? `<span class="muted small">${esc(s.detail)}</span>` : ''}</span></li>`).join('')}</ol>`
      : empty('MyOrders kept no dates for this costing sheet.');
    const last = c.history.length ? c.history[c.history.length - 1] : null;
    return section('history', 'History', last ? `${esc(last.step)}${last.at ? ' ' + esc(date(last.at)) : ''}` : '', steps + facts([
      ['Order No', text(f.orderNo)],
      ['Invoice No', text(f.invoiceNo)],
      f.invoiceDates ? ['Invoice Dates', text(f.invoiceDates)] : null,
      ['OPO Checked', yesNo(f.isOpoChecked)],
      ['Credit Terms', text(f.creditTerms)],
      ['Total Approved Price', dollars(f.totalApprovedPrice)],
    ]), 'From the dates and names MyOrders kept on the costing sheet');
  }

  function attachments(c) {
    const body = c.attachments.length ? `<ul class="cs-files">${c.attachments.map(a => `<li><span class="cs-file"><b>${esc(a.fileName)}</b>
        <span class="muted small">${esc([size(a.size), a.uploadedBy, a.uploadedOn ? date(a.uploadedOn) : '', a.isComplex ? 'Specialists\' attachment' : ''].filter(Boolean).join(' · '))}</span>
        ${a.remarks ? `<span class="muted small">${esc(a.remarks)}</span>` : ''}</span>
      <a class="button secondary small-btn" href="file.ashx/attachment?costingSheet=${c.id}&amp;id=${a.id}" aria-label="Download ${esc(a.fileName)}"><span data-icon="download"></span>Download</a></li>`).join('')}</ul>`
      : empty('No attachments to show.');
    return section('attachments', 'Attachments', String(c.attachments.length), body);
  }

  function differences(c) {
    if (!c.requestFormId || !c.differences.length) return '';
    return `<div class="notice warn">There are differences between Costing Sheet and Request Form <a href="requestform.aspx?id=${c.requestFormId}"># ${esc(c.requestFormId)}</a>.</div>` + card('Costing sheet and request form', `<div class="table-wrap"><table class="data">
      <thead><tr><th>EDP Code</th><th>Description</th><th class="num">Costing Sheet</th><th class="num">Request Form</th></tr></thead><tbody>
      ${c.differences.map(d => `<tr><td class="nowrap">${esc(d.edpCode)}</td><td>${esc(d.description)}</td><td class="num">${d.costingSheetQuantity}</td><td class="num">${d.requestFormQuantity}</td></tr>`).join('')}
      </tbody></table></div>`);
  }

  function product(c) {
    const items = c.items.map(item => {
      const lines = [
        `<tr><td><b>${esc(item.productName)}</b>${item.model || item.serial ? `<span class="sub">${esc([item.model, item.serial].filter(Boolean).join(' · '))}</span>` : ''}</td><td class="num">${item.quantity}</td><td class="num">${money(item.unitPrice)}</td><td class="num">${money(item.quantity * item.unitPrice)}</td><td></td></tr>`,
        ...item.subItems.map(s => `<tr><td>${esc(s.productName)}</td><td class="num">${s.quantity}</td><td class="num">${money(s.unitPrice)}</td><td class="num">${money(s.quantity * s.unitPrice)}</td><td></td></tr>`),
        ...item.complexItems.map(x => `<tr><td>${esc(x.productName)}${x.mpgCode ? ` (${esc(x.mpgCode)})` : ''}</td><td class="num">${x.quantity}</td>
          <td class="num">${x.hasStp ? money(x.stp) : '<span class="muted">Not Available</span>'}</td><td class="num">${x.hasStp ? money(x.stp * x.quantity) : '<span class="muted">Not Available</span>'}</td><td class="num">${money(x.achievedPrice)}</td></tr>`),
        `<tr class="total"><td colspan="3" class="num muted">Street price</td><td class="num" data-label=""><b>${money(item.totalStp)}</b></td><td data-label=""></td></tr>`,
      ].join('');
      const flags = [item.isSubscription ? '<span class="pill">Is Subscription</span>' : '', item.oaaPricing ? '<span class="pill">OAA Pricing</span>' : ''].join(' ');
      return `<div class="item-block"><div class="item-head"><h3>${esc(item.productName)}</h3>${flags}<span class="grow"></span><span>Achieved Price <b>${dollars(item.achievedPrice)}</b></span></div>
        <div class="table-wrap"><table class="data"><thead><tr><th>Name</th><th class="num">Quantity</th><th class="num">Street Price (Unit)</th><th class="num">Street Price (Total)</th><th class="num">Achieved Price</th></tr></thead>
        <tbody>${lines}</tbody></table></div></div>`;
    }).join('');
    const m = c.margins;
    return section('product', 'Product', `${c.items.length} · ${dollars(c.totalAchievedPrice)}`, (items || empty('No items to show.')) + facts([
      ['Request Total Street Price', dollars(c.totalStp)],
      ['Total Achieved Price', `<b>${dollars(c.totalAchievedPrice)}</b>`],
      ['Commissionable Revenue', `<b>${dollars(c.commissionableRevenue)}</b>`],
      m ? ['Percentage Above Manager Price', money(m.percentageAboveManagerPrice) + ' %'] : null,
      m ? ['Percentage Above Landed', money(m.percentageAboveLanded) + ' %'] : null,
      m ? ['Deal Margin', money(m.dealMargin) + ' %'] : null,
      m ? ['Estimated Commission', dollars(m.estimatedCommission)] : null,
      m ? ['Confirmed Commission', dollars(m.confirmedCommission)] : null,
    ]));
  }

  function billing(c) {
    const b = c.billing;
    return section('billing', 'Billing', text(b.financeType), facts([
      ['Is PPP', yesNo(b.isPpp)],
      ['Finance Type', text(b.financeType)],
      ['Finance/Outright Sales Amount', dollars(b.financeOutrightAmount)],
      b.isFinancing ? ['Months', text(b.months)] : null,
      b.isFinancing ? ['Interest Rates', money(b.interestRate) + ' %'] : null,
      b.isFinancing ? ['Down Payment', dollars(b.downPayment)] : null,
      b.isFinancing ? ['Final Payment', `${money(b.finalPercentage)} % · ${dollars(b.finalPayment)}`] : null,
      b.isFinancing ? [b.monthlyLabel, dollars(b.monthlyLease)] : null,
      b.showsPppRates ? ['PPP Internal B/W Rate ($)', money(b.pppInternalBwRate, 6)] : null,
      b.showsPppRates ? ['PPP Internal Colour Rate ($)', money(b.pppInternalColourRate, 6)] : null,
      b.showsPppRates ? ['PPP Customer B/W Rate ($)', money(b.pppCustomerBwRate, 6)] : null,
      b.showsPppRates ? ['PPP Customer Colour Rate ($)', money(b.pppCustomerColourRate, 6)] : null,
      b.showsPppRates ? ['PPP Monthly B/W Volume', text(b.pppMonthlyBwVolume)] : null,
      b.showsPppRates ? ['PPP Monthly Colour Volume', text(b.pppMonthlyColourVolume)] : null,
      b.isExternalFinancing ? ['Bill To', text(b.billTo)] : null,
      b.isFinancing ? ['Lease Agreement Number', text(b.leaseAgreementNumber)] : null,
      ['Transaction Type', text(b.transactionType)],
      ['Trade In To', text(b.tradeInTo)],
      b.isExternalFinancing ? null : ['Billing Address', text(b.billingAddress)],
      ['Remarks', b.remarks ? `<span class="pre">${esc(b.remarks)}</span>` : '<span class="muted">-</span>'],
    ]));
  }

  function costs(c) {
    const variables = c.variables.length ? `<div class="table-wrap"><table class="data"><thead><tr><th>Variable Cost Item</th><th class="num">Cost</th><th>Remarks</th></tr></thead><tbody>
      ${c.variables.map(v => `<tr><td>${esc(v.name)}</td><td class="num nowrap">${money(v.cost)}</td><td>${text(v.remarks)}</td></tr>`).join('')}</tbody></table></div>` : empty('No items to show.');
    const tradeIns = c.tradeIns.length ? `<div class="table-wrap"><table class="data"><thead><tr><th>Trade In Item</th><th class="num">Amount</th><th>Type</th></tr></thead><tbody>
      ${c.tradeIns.map(t => `<tr><td class="wide-text"><b>${text(t.item)}</b><span class="sub">${esc([t.manufacturer, t.model, t.serialNo ? 'Serial No. ' + t.serialNo : ''].filter(Boolean).join(' · '))}</span>${t.remarks ? `<span class="sub">${esc(t.remarks)}</span>` : ''}</td>
        <td class="num nowrap">${money(t.amount)}</td><td>${esc(t.type)}</td></tr>`).join('')}</tbody></table></div>` : empty('No items to show.');
    return section('costs', 'Variable costs & trade-ins', String(c.variables.length + c.tradeIns.length), `<h3 class="cs-sub">Variable Costs</h3>${variables}
      <p class="muted small" style="margin:8px 0 0">Total variable cost: ${dollars(c.facts.totalVariableCost)}</p><h3 class="cs-sub">Trade In(s)</h3>${tradeIns}`);
  }

  function meters(c) {
    if (!c.items.length) return section('meter', 'Meter charges', '0', empty('No items to show.'));
    const cents = value => money(value, 4) + ' cents';
    const below = c.items.filter(item => item.meter && item.meter.belowGmCpc).length;
    return section('meter', 'Meter charges', `${c.items.length}${below ? ` · ${below} below GM CPC` : ''}`, c.items.map(item => {
      const parts = [...item.subItems.map(s => `${esc(s.productName)} x ${s.quantity}`), ...item.complexItems.map(x => `${esc(x.productName)} x ${x.quantity}`),
        ...item.packetItems.map(p => `${esc(p.productName)} x ${p.quantity}`)];
      const m = item.meter;
      let meter = '';
      if (m) {
        meter = facts([['Under FSA', yesNo(m.isFsa)], ['Below GM CPC', yesNo(m.belowGmCpc)]]) + (m.isFsa ? `<div class="table-wrap"><table class="data meter"><thead><tr><th></th><th>Black &amp; White</th>${m.blackAndWhiteOnly ? '' : '<th>Colour</th>'}</tr></thead><tbody>
          <tr><td><b>Initial</b></td><td>${m.initialBwCopies} copies @ ${cents(m.initialBwPrice)} each</td>${m.blackAndWhiteOnly ? '' : `<td>${m.initialClrCopies} copies @ ${cents(m.initialClrPrice)} each</td>`}</tr>
          <tr><td><b>Monthly</b><span class="sub">For ${m.monthlyMonths} Months</span></td><td>${m.monthlyBwCopies} copies @ ${cents(m.monthlyBwPrice)} each</td>${m.blackAndWhiteOnly ? '' : `<td>${m.monthlyClrCopies} copies @ ${cents(m.monthlyClrPrice)} each</td>`}</tr>
          <tr><td><b>Thereafter</b></td><td>${cents(m.thereafterBwPrice)} per copy</td>${m.blackAndWhiteOnly ? '' : `<td>${cents(m.thereafterClrPrice)} per copy</td>`}</tr>
          </tbody></table></div>` : '<p class="muted">Not available</p>');
      }
      return `<div class="item-block"><div class="item-head"><h3 class="green">${esc(item.productName)} x ${item.quantity}</h3>${item.isSubscription ? '<span class="pill">Is Subscription</span>' : ''}</div>
        ${parts.length ? `<p class="small muted" style="margin:0 0 8px">${parts.join(' · ')}</p>` : ''}${meter}</div>`;
    }).join(''));
  }

  function more(c) {
    const f = c.facts;
    return section('more', 'More details', f.isPoCompleted ? 'PO completed' : '', facts([
      ['Proposal', f.proposalId ? '# ' + esc(f.proposalId) : '<span class="muted">-</span>'],
      ['Option', f.optionId ? '# ' + esc(f.optionId) : '<span class="muted">-</span>'],
      ['Request Form', c.requestFormId ? `<a href="requestform.aspx?id=${c.requestFormId}"># ${esc(c.requestFormId)}</a>` : '<span class="muted">-</span>'],
      ['Is EPP', yesNo(f.isEpp)],
      ['Is Novation', yesNo(f.isNovation)],
      ['PO Completed', yesNo(f.isPoCompleted)],
      ['Receipt Completed', yesNo(f.isReceiptCompleted)],
      ['Contact Name', text(f.contactName)],
      ['Phone', text(f.phone)],
      ['Email', text(f.email)],
      ['Fax', text(f.fax)],
      ['Deliver To', text(f.deliverTo)],
    ]), 'Kept on the costing sheet');
  }

  const tabs = [['product', 'Product'], ['meter', 'Meter charges'], ['billing', 'Billing'], ['costs', 'Variable costs & trade-ins'], ['more', 'More details']];
  let tab = 'product';

  function render(c) {
    document.title = `Costing sheet ${c.id} · MyCRMRecords`;
    const tone = /invoiced/i.test(c.status) && !/pending/i.test(c.status) ? 'done' : /recall|reject|cancel/i.test(c.status) ? 'warn' : '';
    let format = 'new';
    try { format = localStorage.getItem('costing-sheet-format') === 'old' ? 'old' : 'new'; } catch { /* the newer layout */ }
    page.innerHTML = `
      <div class="page-heading cs-heading"><div class="cs-title">
          ${crumbs(`Costing Sheet # ${c.id}`, [['Costing sheets', 'costingsheets.aspx']])}
          <div class="cs-name"><h1>Costing Sheet # ${c.id}</h1><span class="orders-status ${tone}">${esc(c.status)}</span></div>
          <p>${c.reference ? `Ref # ${esc(c.reference)} · ` : ''}${c.companyId ? `<a href="company.aspx?id=${c.companyId}">${esc(c.company)}</a>` : esc(c.company)}${c.salesman ? ' · ' + esc(c.salesman) : ''}</p>
          <p class="muted small cs-readonly">Read only, from MyOrders; printing here doesn't change MyOrders' Printed At.</p></div>
        <div class="heading-actions">${c.canPrint ? `<label class="filter-capsule cs-format"><span>Layout</span><select id="format" aria-label="Print layout"><option value="new"${format === 'new' ? ' selected' : ''}>New CS</option><option value="old"${format === 'old' ? ' selected' : ''}>Old CS</option></select></label>
          <a class="button primary" id="print" href="print.aspx?id=${c.id}${format === 'old' ? '&amp;format=old' : ''}" target="_blank" rel="noopener"><span data-icon="printer"></span>Print</a>` : ''}</div></div>
      ${c.isActive ? '' : `<div class="notice error">This costing sheet was deleted in MyOrders${c.deletedAt ? ' on ' + esc(dateTime(c.deletedAt)) : ''}${c.deletedBy ? ' by ' + esc(c.deletedBy) : ''}. It is not in the list.</div>`}
      ${details(c)}
      ${differences(c)}
      <div class="tabs cs-tabs" role="tablist" aria-label="Costing sheet sections">${tabs.map(([key, label]) => `<button type="button" class="tab" role="tab" data-tab="${key}" aria-controls="body-${key}" aria-selected="${tab === key}">${esc(label)}</button>`).join('')}</div>
      <div class="cs-layout"><div class="cs-main">${product(c)}${meters(c)}${billing(c)}${costs(c)}${more(c)}</div><aside class="cs-aside">${history(c)}${attachments(c)}</aside></div>`;
    drawIcons(page);
    showTab();
    $('#format')?.addEventListener('change', event => {
      const old = event.target.value === 'old';
      try { localStorage.setItem('costing-sheet-format', old ? 'old' : 'new'); } catch { /* not kept */ }
      $('#print').href = `print.aspx?id=${c.id}${old ? '&format=old' : ''}`;
    });
  }

  function showTab() {
    $$('.cs-main .cs-section', page).forEach(s => s.classList.toggle('on', s.dataset.sec === tab));
    $$('[data-tab]', page).forEach(b => b.setAttribute('aria-selected', String(b.dataset.tab === tab)));
  }

  page.addEventListener('click', event => {
    const t = event.target.closest('[data-tab]');
    if (t) { tab = t.dataset.tab; showTab(); return; }
    // On a phone each section opens and closes.
    const toggle = event.target.closest('.cs-toggle');
    if (toggle) {
      const open = toggle.getAttribute('aria-expanded') !== 'true';
      toggle.setAttribute('aria-expanded', String(open));
      toggle.closest('.cs-section').classList.toggle('open', open);
    }
  });

  (async () => {
    const answer = id ? await api('GET', 'costingsheets/' + id) : {result: {ok: false, message: 'Open a costing sheet from the list.'}};
    $('#loading').hidden = true;
    page.hidden = false;
    if (!answer.result.ok) {
      page.innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3><p><a class="button secondary" href="costingsheets.aspx">Back to the costing sheets</a></p></div>`;
      return;
    }
    render(answer.costingSheet);
  })();
})();
