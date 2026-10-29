(() => {
  'use strict';
  // print.aspx?id=&format=new|old: MyOrders' costing sheet PDF as a page to print or save as PDF from the browser
  // ("CostingSheet #<Id>" as the file name). "New CS" is MyOrders' newer one-page layout; "Old CS" its first layout:
  // item pages (3 items a page), meter pages (6 a page), the financing page with the internal financing calculation
  // and verification, the service credit pages when the items' rates differ, and the specialists' page. The texts are
  // MyOrders'; the values come from api.ashx/costingsheets/<id>/print. Printing records nothing (MyOrders wrote the
  // time it was printed; MyCRMRecords only reads).
  const {$, $$, esc, api, param} = window.WH;
  const id = Number(param('id') || 0);
  let format = param('format') === 'old' ? 'old' : 'new';
  let data = null;

  // OutSystems' FormatCurrency / FormatDecimal with "$", "." and ",".
  const dec = (value, digits = 2) => Number(value || 0).toLocaleString('en-US', {minimumFractionDigits: digits, maximumFractionDigits: digits});
  const cur = (value, digits = 2) => (Number(value || 0) < 0 ? '-$' : '$') + dec(Math.abs(Number(value || 0)), digits);
  const day = value => (/^(\d{4}-\d{2}-\d{2})/.exec(value || '') || [])[1] || '';
  const dayTime = value => { const m = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/.exec(value || ''); return m ? `${m[3]}-${m[2]}-${m[1]} ${m[4]}:${m[5]}` : ''; };
  const t = value => esc(value ?? '');
  const tick = '<svg viewBox="0 0 24 24" width="18" height="18" aria-label="Yes"><path d="m4 12 5 5L20 6" fill="none" stroke="#2e9e4f" stroke-width="3"/></svg>';
  const chunks = (list, size) => { const out = []; for (let i = 0; i < Math.max(list.length, 1); i += size) out.push(list.slice(i, i + size)); return out; };

  // ---------------------------------------------------------------- shared pieces

  function header(p) {
    const s = p.sheet;
    return `<div class="header-logos"><img src="ricoh-logo.png" alt="RICOH">
      ${p.isVitalCase ? '<div class="flag"><span>Vital Pricing</span></div>' : '<img src="restricted.png" alt="RESTRICTED. Do not access if you are not sure of your access rights. [RSP ISMS]">'}</div>
      <div class="Bold mt5">RICOH (SINGAPORE) PTE LTD</div><div>Ref # ${t(s.reference)}</div>`;
  }

  /** MyOrders' MeterTable for some items. */
  function meters(p, items) {
    const price = value => 'S' + cur(Number(value || 0) / 100, 4);
    const monthly = value => p.isEpp ? 'S$0.0000*' : price(value);
    return items.map(i => `<table class="table meter"><tbody>
      <tr><td colspan="7" class="BlackContainer" style="text-align:left;border:1px solid black;">Model Name: ${t(i.productName)}</td></tr>
      <tr><td rowspan="2">Initial</td><td>${i.initialBwCopies}</td><td>B/W</td><td rowspan="2">copies at S$</td><td>${price(i.initialBwPrice)}</td><td></td><td></td></tr>
      <tr><td>${i.initialClrCopies}</td><td>Color</td><td>${price(i.initialClrPrice)}</td><td></td><td></td></tr>
      <tr><td colspan="7" class="grey">Or</td></tr>
      <tr><td rowspan="2">Monthly</td><td>${i.monthlyBwCopies}</td><td>B/W</td><td rowspan="2">copies for</td><td rowspan="2">${i.monthlyMonths}</td><td rowspan="2">months at</td><td>${monthly(i.monthlyBwPrice)}</td></tr>
      <tr><td>${i.monthlyClrCopies}</td><td>Color</td><td>${monthly(i.monthlyClrPrice)}</td></tr>
      <tr><td></td><td></td><td></td><td></td><td></td><td>Bill to</td><td></td></tr>
      <tr><td rowspan="2">Thereafter</td><td>B/W</td><td>${dec(i.thereafterBwPrice, 4)}</td><td>cents per copy</td><td></td><td></td><td></td></tr>
      <tr><td>Color</td><td>${dec(i.thereafterClrPrice, 4)}</td><td>cents per copy</td><td></td><td></td><td></td></tr>
      </tbody></table>`).join('') + (p.isEpp ? '<div class="epp">*EPP rates</div>' : '');
  }

  /** A box of the internal financing calculation: a black title bar, then values with their captions and operators. */
  const box = (title, parts) => `<div class="calc"><div class="BlackContainer">${t(title)}</div><div class="formula">${parts.map(part =>
    Array.isArray(part) ? `<div><div class="value">${part[0]}</div><div class="caption">${t(part[1])}</div></div>` : `<div class="op">${part}</div>`).join('')}</div></div>`;

  function financeBoxes(p) {
    const f = p.finance;
    const differ = p.ratesDiffer;
    const credit = (title, copies, rate, months, total) => differ
      ? `<div class="calc"><div class="BlackContainer">${t(title)}</div><div style="padding:20px;text-align:center;">Rates are different, please refer to the next page.</div></div>`
      : box(title, [[dec(copies, 0), 'No. of copies FOC Monthly'], 'X', [dec(rate, 4), 'rates'], 'X', [dec(months, 0), '# of months'], '=', [cur(total), 'Total']]);
    return box('Deal Interest (f.k.a Machine Interest)', [[cur(f.products), 'Products'], 'X', [dec(f.productsRate, 3) + '%', 'rates'], 'X', [dec(f.noOfYear, 3), '# of years'], '=', [cur(f.dealInterest), 'Total']])
      + box('Final Payment Interest', [[cur(f.finalPayment), 'Final Payment'], 'X', [dec(f.productsRate, 3) + '% x 2', 'rates'], 'X', [dec(f.noOfYear, 3), '# of years'], '=', [cur(f.finalPaymentInterest), 'Total']])
      + credit('Service Credit (f.k.a. Software Service Credit) (Monthly FOC B/W Copies)', f.bwNoOfCopies, f.bwRate, f.bwNoOfMonth, f.bwTotal)
      // MyOrders shows the B/W months in the colour box too.
      + credit('Service Credit (f.k.a. Software Service Credit) (Monthly FOC Color Copies)', f.colNoOfCopies, f.colRate, f.bwNoOfMonth, f.colTotal)
      + box('Total Lease Payable', [[cur(f.machineInterest), 'Machine Interest'], '+', [cur(f.serviceCredit), 'Service Credit'], '+', [cur(f.productsWithFinalPayment), 'Products with Final Payment'], '=', [cur(f.leasePayable), 'Lease Payable']])
      + box('Amount of Installment', ['(', [cur(f.leasePayable), 'Lease Payable'], '-', [cur(f.finalPayment), 'Final Payment'], ') /', [String(f.noOfInstal), 'No. of Instal.'], '=', [cur(f.perMonth), 'per month']]);
  }

  // ---------------------------------------------------------------- New CS

  function newLayout(p) {
    const s = p.sheet, b = s.billing;
    const detail = (label, value) => `<div class="label">${t(label)}</div><div>${value}</div>`;
    const lines = p.lines.map(l => `<tr class="${l.isMain ? 'main' : ''}"><td class="num">${t(l.seq)}</td><td>${t(l.line)}</td><td>${t(l.edpCode)}</td><td>${t(l.productType)}</td>
      <td>${t(l.items)}${l.isSubscription ? '<div>(Is Subscription)</div>' : ''}</td><td></td><td></td><td class="num">${l.isMain ? dec(l.qty, 0) : ''}</td><td></td>
      <td class="num nowrap">${l.isMain ? cur(l.totalStp) : ''}</td><td class="num nowrap">${l.isMain ? cur(l.totalAchieved) : ''}</td><td class="num nowrap">${l.isMain ? cur(l.palRelease) : ''}</td></tr>`).join('');
    const totalStp = p.lines.filter(l => l.isMain).reduce((sum, l) => sum + Number(l.totalStp || 0), 0);
    const totalAchieved = p.lines.filter(l => l.isMain).reduce((sum, l) => sum + Number(l.totalAchieved || 0), 0);
    const variables = p.variables.length ? `<table class="lines"><thead><tr><th>Name</th><th class="num">Cost</th><th>Trade In Item</th><th>Manufacturer</th><th>Model</th><th>Serial No.</th><th>Remarks</th></tr></thead><tbody>
      ${p.variables.map(v => `<tr><td>${t(v.name)}</td><td class="num nowrap">${cur(v.cost)}</td><td>${t(v.tradeInItem)}</td><td>${t(v.manufacturer)}</td><td>${t(v.model)}</td><td>${t(v.serialNo)}</td><td>${t(v.remarks)}</td></tr>`).join('')}</tbody></table>`
      : '<p>No items to show...</p>';
    const flags = [p.isVitalCase ? 'Vital Pricing' : '', p.isZba ? 'ZBA' : '', p.isNovation ? 'Novation' : ''].filter(Boolean).map(f => `<div class="flag"><span>${f}</span></div> `).join('');
    return `<div class="Page new"><div class="box effect1">
      <h1>Costing Sheet #${s.id}</h1>
      <div class="details">
        ${detail('Costing Sheet Status', t(s.status))}${detail('Created At', t(dayTime(s.createdAt)))}${detail('Updated At', t(dayTime(s.updatedAt)))}
        ${detail('Recall At', t(dayTime(s.recallAt)))}${detail('Salesman', t(p.salesmanName) + (p.salesmanCode ? ` (${t(p.salesmanCode)})` : ''))}
        ${detail('Manager', t(p.managerName))}${detail('Specialist', p.specialistNames.map(t).join('<br>'))}${detail('Customer Code', t(s.customerNo))}
        ${detail('BRN/UEN', t(p.brn))}${detail('Company', t(s.company))}${detail('Billing Address', t(s.address))}${detail('Contact', t(p.contact))}
        ${detail('Phone / Mobile', t(p.contactPhone))}${detail('Fax', '')}${detail('Email', '')}${detail('Account Type', t(s.customerType))}
        ${detail('Global MA Type', '')}${detail('Global MA RU#', '')}${detail('Finance Type', t(b.financeType))}${detail('Credit Term', t(p.companyCreditTerms))}
        ${detail('Transaction Type', t(b.transactionType))}
      </div>
      ${flags ? `<div class="mt10">${flags}</div>` : ''}
      <h3>Items</h3>
      <table class="lines"><thead><tr><th class="num">Seq</th><th>Line</th><th>EDP Code</th><th>Product Type</th><th>Items</th><th>Promo Code</th><th>Pricing Level</th><th class="num">Qty</th><th>Transaction Type</th><th class="num">Total Street Price</th><th class="num">Total Achieve Price</th><th class="num">PAL Release</th></tr></thead>
        <tbody>${lines || '<tr><td colspan="12">No items to show...</td></tr>'}<tr class="total"><td colspan="9"></td><td class="num nowrap">${cur(totalStp)}</td><td class="num nowrap">${cur(totalAchieved)}</td><td></td></tr></tbody></table>
      <h3>Variables</h3>${variables}
      <h3>Meter Charges</h3>${p.items.length ? meters(p, p.items) : '<p>No items to show...</p>'}
      <h3>Summary</h3>
      ${p.isInternalFinancing ? financeBoxes(p) : ''}
      <table class="lines mt10"><thead><tr><th>Internal Lease Agt No.</th><th class="num">Finance/Outright Sales Amount</th><th class="num">Interest Rate %</th><th class="num">No. of Installation</th><th class="num">Monthly</th><th class="num">Final Payment</th><th>External Lease Bill to</th><th class="num">Down Payment</th></tr></thead>
        <tbody><tr><td>${t(p.invoiceNo)}</td><td class="num nowrap">${cur(b.financeOutrightAmount)}</td><td class="num">${dec(b.interestRate)}</td><td class="num">${b.months}</td><td class="num">${dec(b.monthlyLease)}</td><td class="num">${dec(b.finalPayment)}</td><td>${t(b.billTo)}</td><td class="num">${dec(b.downPayment)}</td></tr></tbody></table>
      <h3>Others</h3>
      <div class="approvals">
        <div></div><div class="label">Submitted by: <br>Salesperson</div><div class="label">Approved by: <br>Sales Manager</div><div class="label">Verified by: <br>Sales Admin</div><div class="label">Approved by: <br>Sales Head</div>
        <div class="label">Name</div><div>${t(p.salesmanName)}</div><div>${t(p.managerName)}</div><div></div><div>${t(p.headName)}</div>
        <div class="label">Date</div><div>${t(dayTime(p.createdAt))}</div><div>${t(dayTime(p.approvedAt))}</div><div></div><div>${t(dayTime(p.approvedAt))}</div>
        <div class="label">Remarks (Rejected)</div><div></div><div></div><div></div><div></div>
      </div>
    </div></div>`;
  }

  // ---------------------------------------------------------------- Old CS

  function itemTable(p, items) {
    const cells = render => items.map(i => `<td>${i ? render(i) : ''}</td>`).join('');
    const rows = [
      ['', () => ''],
      ['Machine Model Configuration', i => t(i.productName)],
      ['(if different from Price List)', i => t(i.subItemsText) + (i.hasComplex ? `${i.subItemsText ? '<br>' : ''}Please refer to the last page` : '')],
      ['QTY (Machine)', i => dec(i.quantity, 0)],
      ['Achieved Price (per unit)', i => `<span class="nowrap">${cur(i.quantity === 0 ? 0 : i.achievedPrice / i.quantity)}</span>`],
      ['Street Price (per unit)', i => `<span class="nowrap">${cur(i.quantity === 0 ? 0 : i.totalStp / i.quantity)}</span>`],
      ['Commission Rate %', () => '0.0%'],
      ['Add: Above Floor Amount', i => i.achievedPrice - i.totalStp > 0.001 ? cur(i.achievedPrice - i.totalStp) : ''],
      ['Profit Sharing Rate %', () => ''],
      ['TDV Commission', () => ''],
      ['Fixed Amount', () => ''],
      ['Add:', () => ''],
      ['<b>TOTAL ACHIEVED PRICE</b>', i => `<span class="nowrap">${cur(i.achievedPrice)}</span>`],
    ];
    return `<table class="myTable mt10"><tbody>${rows.map(([label, render]) => `<tr><td>${label}</td>${cells(render)}</tr>`).join('')}</tbody></table>`;
  }

  function variablesTable(p) {
    const s = p.sheet;
    const byName = Object.fromEntries(p.variableRows.map(r => [r.label, r]));
    const cost = r => r ? r.costs.map(c => Number(c) !== 0 ? cur(c) : '').join('<br>') : '';
    const remarks = r => r ? r.remarks.map(t).join('<br>') : '';
    const has = r => r && (r.costs.some(c => Number(c) !== 0) || r.remarks.some(x => x));
    const subtotal = Number(p.subtotalBeforeTradeIn) !== 0 ? cur(p.subtotalBeforeTradeIn) : '';
    const rows = [
      ['<b>TOTAL AMOUNT</b>', `<span class="nowrap">${cur(s.totalAchievedPrice)}</span>`, '', true],
      ...['Rollover / Refinancing', 'Advertisement & Promotion', 'Accessories', 'Free Prints', 'Service Dept', 'Accruals', 'Others'].map(n => [t(n), cost(byName[n]), remarks(byName[n]), has(byName[n])]),
      ['Subtotal (before Trade-In): ', subtotal, '', subtotal !== ''],
      ...['R2R Trade In', 'O2R Trade In'].map(n => [t(n), cost(byName[n]), remarks(byName[n]), has(byName[n])]),
      ['Other S/R', '', '', true],
    ];
    // With long sub items MyOrders lists only the rows that have something.
    const shown = p.isLong ? rows.filter(r => r[3]) : rows;
    return `<table class="myTable variables mt10"><tbody>${shown.map(([label, c, r]) => `<tr><td>${label}</td><td>${c}</td><td>${r}</td></tr>`).join('')}</tbody></table>`;
  }

  function particulars(p) {
    const s = p.sheet;
    return `<div class="mt10"><div class="Bold">PARTICULARS</div>
      <div class="row"><div class="c3">Customer:</div><div class="c3 UnderLineBox">${t(s.company)}</div><div class="c3 right">Ref # </div><div class="c3 UnderLineBox FakeLink">${t(p.referenceWithId)}</div></div>
      <div class="row"><div class="c3"></div><div class="c3 right">c/o</div><div class="c3 right">Invoice/Order #</div><div class="c3"></div></div>
      <div class="row"><div class="c3">Holding Company:</div><div class="c3 UnderLineBox"></div><div class="c3 right">Leasing Agmt #</div><div class="c3 UnderLineBox"></div></div>
      <div class="row"><div class="c4">Contact: ${t(p.contact)}</div><div class="c1 right">Tel:</div><div class="c2 UnderLineBox"></div><div class="c1 right">DID:</div><div class="c2 UnderLineBox"></div><div class="c1 right">Fax:</div><div class="c1 UnderLineBox"></div></div>
    </div>`;
  }

  function totals(p) {
    const s = p.sheet, b = s.billing;
    return `<div class="mt10">
      <div class="row"><div class="c3 Bold">TOTAL INVOICE VALUE S$</div><div class="c3 UnderLineBox">${cur(Number(s.totalAchievedPrice) + Number(p.totalVariableCost))}</div><div class="c2">Specialist:</div><div class="c4 UnderLineBox">${p.specialistNames.map(t).join(' ')}</div></div>
      <div class="row"><div class="c3 Bold">TYPE OF TRANSACTION:</div><div class="c3">${t(b.transactionType)}</div><div class="c3 Bold">TRADE IN TO:</div><div class="c3">${t(b.tradeInTo)}</div></div>
      <div class="row"><div class="c3 Bold">INDUSTRY TYPE:</div><div class="c3"></div><div class="c3"></div><div class="c3"></div></div>
      <div class="row"><div class="c3 Bold">SALES CONCLUDED BY:</div><div class="c3">${t(p.salesmanName)} - ${t(p.salesmanCode)}</div><div class="c3 Bold">TERRITORY HOLDER:</div><div class="c3 UnderLineBox"></div></div>
    </div>`;
  }

  function financing(p) {
    const s = p.sheet, b = s.billing;
    return `<div class="Bold mt10">FINANCING</div>
      <div class="row"><div class="c2 BorderBox nowrap">${p.isOutrightSale ? cur(b.financeOutrightAmount) : ''}</div><div class="c2 Bold">Cash/Cheque</div><div class="c2 BorderBox"></div><div class="c2 Bold">Credit Term</div><div class="c4 UnderLineBox"></div></div>
      <div class="row"><div class="c2 BorderBox">${p.isExternalFinancing ? tick : ''}</div><div class="c2 Bold">External Finance</div><div class="c2 Bold">Leasing Co:</div><div class="c6"></div></div>
      <div class="row"><div class="c2 Bold">Products (f.k.a. Hardware)</div><div class="c4 UnderLineBox center">${cur(s.totalAchievedPrice)}</div><div class="c2 Bold">Bill to</div><div class="c4 UnderLineBox center">${t(p.billToName)}</div></div>
      <div class="row"><div class="c2 BorderBox">${p.isInternalFinancing ? tick : ''}</div><div class="c2 Bold">Internal Finance</div><div class="c2 BorderBox">${p.isExternalFinancing ? tick : ''}</div><div class="c2 Bold">Pure Leasing</div><div class="c2"></div><div class="c2 Bold">Rental machines</div></div>
      <div class="row"><div class="c2 BorderBox">${b.months}</div><div class="c2 Bold">Months Installment @</div><div class="c2 BorderBox">${dec(b.interestRate, 3)}%</div><div class="c2 Bold">% Interest Rate p.a.</div><div class="c2 center">Down Payment</div><div class="c2 UnderLineBox">${cur(b.downPayment)}</div></div>
      ${b.remarks ? `<div class="row mt10"><div class="c2 Bold">Remarks</div><div class="c10" style="white-space:pre-wrap">${t(b.remarks)}</div></div>` : ''}`;
  }

  function verification(p) {
    const manager = p.managerName || p.headName;
    const line = (label, name, date) => `<div class="row"><div class="c3">${t(label)}</div><div class="c3 UnderLineBox center Bold">${name}</div><div class="c3 UnderLineBox"></div><div class="c3 UnderLineBox center Bold">${date}</div></div>`;
    return `<div class="verification"><div class="Bold mt10">VERIFICATION</div>
      <div class="row center Bold"><div class="c3"></div><div class="c3">Name</div><div class="c3">Signature</div><div class="c3">Date</div></div>
      ${line('Sales Person', t(p.salesmanName), t(day(p.createdAt)))}
      ${line('Sales Manager', t(manager), t(day(p.approvedAt)))}
      ${line('Fin & Admin', '', '')}${line('Remarks', '', '')}</div>`;
  }

  function serviceCredit(items) {
    return items.map(i => `<div class="GrayContainer mt10">Model Name: ${t(i.productName)}</div>`
      + box('Service Credit (f.k.a. Software Service Credit) (Monthly FOC B/W Copies)', [[dec(i.monthlyBwCopies, 0), 'No. of copies FOC Monthly'], 'X', [dec(i.monthlyBwPrice / 100, 4), 'rates'], 'X', [dec(i.monthlyMonths, 0), '# of months'], '=', [cur(i.monthlyBwCopies * (i.monthlyBwPrice / 100) * i.monthlyMonths), 'Total']])
      + box('Service Credit (f.k.a. Software Service Credit) (Monthly FOC Color Copies)', [[dec(i.monthlyClrCopies, 0), 'No. of copies FOC Monthly'], 'X', [dec(i.monthlyClrPrice / 100, 4), 'rates'], 'X', [dec(i.monthlyMonths, 0), '# of months'], '=', [cur(i.monthlyClrCopies * (i.monthlyClrPrice / 100) * i.monthlyMonths), 'Total']])).join('');
  }

  function specialists(p) {
    const complex = p.complexGroups.length ? `<table class="parts"><tbody>${p.complexGroups.map(g => `<tr><td style="width:30%;text-align:center">${t(g.productName)}</td><td style="width:70%">
      ${g.parts.length ? `<table class="BlackContainer" style="width:100%"><tr><td style="width:35%">Product Name</td><td style="width:30%">EDP Code</td><td style="width:15%">Quantity</td><td style="width:20%">Allocated Price</td></tr></table>` : ''}
      ${g.parts.map(x => `<table class="table" style="width:100%"><tr><td style="width:35%">${t(x.productName)}</td><td style="width:30%">${t(x.edpCode)}</td><td style="width:15%">${x.quantity}</td><td style="width:20%" class="nowrap">${cur(x.allocatedPrice)}</td></tr></table>`).join('')}</td></tr>`).join('')}</tbody></table>` : '';
    const packets = p.packets.length ? `<table class="parts"><tbody>${p.packets.map(k => `<tr><td style="width:30%;text-align:center">${t(k.productName)}</td><td style="width:15%;text-align:center" class="nowrap">${cur(k.achievedPrice)}</td><td style="width:70%">
      ${k.parts.length ? `<table class="BlackContainer" style="width:100%"><tr><td style="width:55%">Product Name</td><td style="width:30%">EDP Code</td><td style="width:15%">Quantity</td></tr></table>` : ''}
      ${k.parts.map(x => `<table class="table" style="width:100%"><tr><td style="width:55%">${t(x.productName)}</td><td style="width:30%">${t(x.edpCode)}</td><td style="width:15%">${x.quantity}</td></tr></table>`).join('')}</td></tr>`).join('')}</tbody></table>` : '';
    return complex + packets;
  }

  function oldLayout(p) {
    const pages = [];
    const itemPages = chunks(p.items, 3);
    itemPages.forEach((items, index) => {
      const padded = items.concat(Array(3 - items.length).fill(null));
      pages.push(header(p) + (index === 0 ? particulars(p) : '') + (p.items.length ? itemTable(p, padded) : '') + (index === 0 ? variablesTable(p) + totals(p) : ''));
    });
    if (p.items.length) chunks(p.items, 6).forEach(items => pages.push(header(p) + '<div class="Bold mt10">Meter Charges</div>' + meters(p, items)));
    pages.push(header(p) + financing(p) + (p.isInternalFinancing ? financeBoxes(p) : '') + verification(p));
    if (p.ratesDiffer) chunks(p.items, 6).forEach(items => pages.push(header(p) + '<div class="Bold mt10">Service Credit</div>' + serviceCredit(items)));
    if (p.complexGroups.length || p.packets.length) pages.push(header(p) + '<div class="Bold mt10">Specialists Solutions, Services, PPG Breakdown, Packets</div>' + specialists(p));
    return pages.map(page => `<div class="Page">${page}</div>`).join('');
  }

  // ---------------------------------------------------------------- page

  function draw() {
    $$('[data-format]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.format === format)));
    $('#doc').innerHTML = format === 'old' ? oldLayout(data.print) : newLayout(data.print);
    history.replaceState(null, '', `print.aspx?id=${id}&format=${format}`);
  }

  $$('[data-format]').forEach(button => button.addEventListener('click', () => { format = button.dataset.format; if (data) draw(); }));
  $('#print').addEventListener('click', () => window.print());
  $('#back').href = 'costingsheet.aspx?id=' + id;
  document.title = `CostingSheet #${id}`;

  (async () => {
    const answer = id ? await api('GET', `costingsheets/${id}/print`) : {result: {ok: false, message: 'Open a costing sheet from the list.'}};
    if (!answer.result.ok) { $('#doc').innerHTML = `<p class="message">${esc(answer.result.message)}</p>`; $('#print').disabled = true; return; }
    data = answer;
    draw();
  })();
})();
