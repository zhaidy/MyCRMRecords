(() => {
  'use strict';
  // costingsheet.aspx?id=: one costing sheet as MyOrders' summary showed it (header, attachments, products with their
  // items, billing, variable costs, trade-ins, meter charges), read only, with its history from the dates MyOrders kept
  // and the facts its summary did not show. Print opens print.aspx, MyOrders' costing sheet PDF as a page.
  const {$, esc, api, drawIcons, dateTime, date, param} = window.WH;
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
  const empty = message => `<p class="muted">${esc(message)}</p>`;

  function header(c) {
    const specialists = c.specialists.length
      ? c.specialists.map(s => `<span class="pill${s.pending ? ' warn' : ''}" title="${s.pending ? 'Pending' : 'Done'}">${esc(s.role)}: ${esc(s.name)}${s.pending ? ' (pending)' : ''}</span>`).join(' ')
      : '<span class="muted">-</span>';
    return card('Costing sheet', facts([
      ['Reference', c.reference ? '# ' + esc(c.reference) : '<span class="muted">-</span>'],
      ['Salesman', text(c.salesman)],
      ['Costing Sheet Status', text(c.status)],
      ['Specialists', specialists],
      ['Created At', when(c.createdAt)],
      ['Updated At', when(c.updatedAt)],
      ['Recall At', when(c.recallAt)],
      // The company opens its page here (MyCRM's companies are kept in MyCRMRecords).
      ['Company', c.companyId ? `<a href="company.aspx?id=${c.companyId}"><b>${text(c.company)}</b></a>` : `<b>${text(c.company)}</b>`],
      ['Billing Address', text(c.address)],
      ['Contact', text(c.contact)],
      ['Customer No', text(c.customerNo)],
      ['Customer Type', text(c.customerType)],
      ['Is ZBA', yesNo(c.isZba)],
      ['Is Contract Extension', yesNo(c.isContractExtension)],
      ['Is Fixed Term', yesNo(c.isFixedTerm)],
      ['Is Vital Pricing', yesNo(c.isVitalPricing)],
      ['GMA Pricing', yesNo(c.gmaPricing)],
      ['Transferred On', when(c.transferredOn)],
      ['Original Salesman', text(c.originalSalesman)],
      ['Event Tag', text(c.eventTag)],
    ]));
  }

  function history(c) {
    const f = c.facts;
    const steps = c.history.length ? `<div class="table-wrap"><table class="data"><thead><tr><th>Step</th><th>When</th><th>By</th><th>Detail</th></tr></thead><tbody>
      ${c.history.map(s => `<tr><td><b>${esc(s.step)}</b></td><td class="nowrap">${when(s.at)}</td><td>${text(s.by)}</td><td>${text(s.detail)}</td></tr>`).join('')}
      </tbody></table></div>` : empty('MyOrders kept no dates for this costing sheet.');
    return card('History', steps + facts([
      ['Order No', text(f.orderNo)],
      ['Invoice No', text(f.invoiceNo)],
      f.invoiceDates ? ['Invoice Dates', text(f.invoiceDates)] : null,
      ['OPO Checked', yesNo(f.isOpoChecked)],
      ['Credit Terms', text(f.creditTerms)],
      ['Total Approved Price', dollars(f.totalApprovedPrice)],
    ]), '<span class="muted small">From the dates and names MyOrders kept on the costing sheet</span>');
  }

  function attachments(c) {
    if (!c.attachments.length) return card('Attachments', empty('No attachments to show.'));
    const rows = c.attachments.map(a => `<tr>
      <td class="nowrap"><a class="icon-button small" href="file.ashx/attachment?costingSheet=${c.id}&amp;id=${a.id}" title="Download ${esc(a.fileName)}" aria-label="Download ${esc(a.fileName)}"><span data-icon="download"></span></a></td>
      <td class="wide-text"><b>${esc(a.fileName)}</b><span class="sub">${esc(size(a.size))}${a.remarks ? ' · ' + esc(a.remarks) : ''}${a.isComplex ? ' · Specialists\' attachment' : ''}</span></td>
      <td>${text(a.uploadedBy)}<span class="sub">${a.uploadedOn ? esc(date(a.uploadedOn)) : ''}</span></td></tr>`).join('');
    return card('Attachments', `<div class="table-wrap"><table class="data"><thead><tr><th></th><th>File</th><th>Uploaded</th></tr></thead><tbody>${rows}</tbody></table></div>`);
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
        `<tr class="total"><td colspan="3" class="num muted">Street price</td><td class="num"><b>${money(item.totalStp)}</b></td><td></td></tr>`,
      ].join('');
      const flags = [item.isSubscription ? '<span class="pill">Is Subscription</span>' : '', item.oaaPricing ? '<span class="pill">OAA Pricing</span>' : ''].join(' ');
      return `<div class="item-block"><div class="item-head"><h3>${esc(item.productName)}</h3>${flags}<span class="grow"></span><span>Achieved Price <b>${dollars(item.achievedPrice)}</b></span></div>
        <div class="table-wrap"><table class="data"><thead><tr><th>Name</th><th class="num">Quantity</th><th class="num">Street Price (Unit)</th><th class="num">Street Price (Total)</th><th class="num">Achieved Price</th></tr></thead>
        <tbody>${lines}</tbody></table></div></div>`;
    }).join('');
    const m = c.margins;
    return card('Product', (items || empty('No items to show.')) + facts([
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
    return card('Billing', facts([
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
    return card('Variable Costs', variables + `<p class="muted small" style="margin:8px 0 0">Total variable cost: ${dollars(c.facts.totalVariableCost)}</p>`) + card('Trade In(s)', tradeIns);
  }

  function meters(c) {
    if (!c.items.length) return card('Meter Charges', empty('No items to show.'));
    const cents = value => money(value, 4) + ' cents';
    return card('Meter Charges', c.items.map(item => {
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
    return card('More details', facts([
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
    ]), '<span class="muted small">Kept on the costing sheet</span>');
  }

  function render(c) {
    document.title = `Costing sheet ${c.id} · MyCRMRecords`;
    page.innerHTML = `
      <div class="page-heading"><div><p class="muted small" style="margin:0"><a href="costingsheets.aspx"><span data-icon="arrow-left"></span> Costing sheets</a></p>
        <h1>Costing Sheet # ${c.id} <span class="pill">${esc(c.status)}</span></h1><p>${esc(c.company)}${c.salesman ? ' · ' + esc(c.salesman) : ''}</p></div>
        <div class="heading-actions">${c.canPrint ? `<a class="button primary" href="print.aspx?id=${c.id}" target="_blank" rel="noopener"><span data-icon="printer"></span>Print</a>` : ''}</div></div>
      ${c.isActive ? '' : `<div class="notice error">This costing sheet was deleted in MyOrders${c.deletedAt ? ' on ' + esc(dateTime(c.deletedAt)) : ''}${c.deletedBy ? ' by ' + esc(c.deletedBy) : ''}. It is not in the list.</div>`}
      ${differences(c)}
      <div class="detail-grid"><div>${header(c)}${product(c)}${meters(c)}</div><div>${history(c)}${attachments(c)}${billing(c)}${costs(c)}${more(c)}</div></div>`;
    drawIcons(page);
  }

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
