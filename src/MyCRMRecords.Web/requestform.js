(() => {
  'use strict';
  // requestform.aspx?id=: one request form as MyOrders' summary showed it (header, declaration, attachments, the
  // differences with its costing sheet, items with their street prices, and the deliveries, or for a collection the
  // collection items), read only, with its history from the dates MyOrders kept. Print opens requestformprint.aspx,
  // MyOrders' delivery form or collection form as a page. Laid out as the approved design, as a costing sheet: the key
  // details under the title, the sections as tabs (Items and Delivery, or Collection items; More details) with History
  // and Attachments beside them; on a phone every section is a card that opens and closes.
  const {$, $$, esc, api, drawIcons, dateTime, date, param, crumbs} = window.WH;
  const page = $('#page');
  const id = Number(param('id') || 0);
  const money = (value, digits = 2) => Number(value || 0).toLocaleString('en-SG', {minimumFractionDigits: digits, maximumFractionDigits: digits});
  const dollars = value => '$' + money(value);
  const yesNo = value => value ? '<span class="yes">Yes</span>' : '<span class="muted">No</span>';
  const text = value => value === null || value === undefined || value === '' ? '<span class="muted">-</span>' : esc(value);
  const when = value => value ? esc(dateTime(value)) : '<span class="muted">-</span>';
  const day = value => value ? esc(date(value)) : '<span class="muted">-</span>';
  const size = bytes => bytes >= 1048576 ? (bytes / 1048576).toFixed(1) + ' MB' : bytes >= 1024 ? Math.round(bytes / 1024) + ' KB' : bytes + ' bytes';
  const facts = pairs => `<dl class="facts">${pairs.filter(Boolean).map(([label, value]) => `<div><dt>${esc(label)}</dt><dd>${value}</dd></div>`).join('')}</dl>`;
  const card = (title, body, extra = '') => `<section class="card"><div class="card-heading"><h2>${esc(title)}</h2>${extra}</div>${body}</section>`;
  const empty = message => `<p class="muted">${esc(message)}</p>`;
  /** A section: a tab's panel on a computer (or a box beside them), a card that opens and closes on a phone. */
  const section = (key, title, summary, body, note = '') => `<section class="cs-section" data-sec="${key}" aria-labelledby="head-${key}">
    <h2 class="cs-section-head" id="head-${key}"><button type="button" class="cs-toggle" aria-expanded="false" aria-controls="body-${key}"><span class="cs-section-title">${esc(title)}</span>
      <span class="cs-summary">${summary}</span><span data-icon="arrow-right" class="cs-chevron"></span></button></h2>
    <div class="cs-section-body" id="body-${key}">${note ? `<p class="muted small cs-note">${note}</p>` : ''}${body}</div></section>`;
  const table = (head, rows) => `<div class="table-wrap"><table class="data"><thead><tr>${head}</tr></thead><tbody>${rows}</tbody></table></div>`;
  const sheetLink = f => f.canOpenCostingSheet ? `<a href="costingsheet.aspx?id=${f.costingSheetId}"># ${esc(f.costingSheetId)}</a>` : '# ' + esc(f.costingSheetId);

  /** The key details under the title: everything MyOrders' summary showed at its top. */
  function details(f) {
    const specialists = f.specialists.length
      ? f.specialists.map(s => `<span class="pill${s.pending ? ' warn' : ''}" title="${s.pending ? 'Pending' : 'Done'}">${esc(s.role)}: ${esc(s.name)}${s.pending ? ' (pending)' : ''}</span>`).join(' ')
      : '<span class="muted">-</span>';
    return `<section class="card cs-details" aria-label="Request form details">${facts([
      ['Contact', text(f.contact)],
      ['Phone', f.phones.length ? f.phones.map(esc).join('<br>') : '<span class="muted">-</span>'],
      ['Customer No', text(f.customerNo)],
      ['Customer Type', text(f.customerType)],
      ['Billing Address', text(f.address)],
      ['Costing Sheet', f.costingSheetId ? sheetLink(f) : '<span class="muted">-</span>'],
      ['Specialists', specialists],
      ['Created At', when(f.createdAt)],
      ['Updated At', when(f.updatedAt)],
      ['Recall At', when(f.recallAt)],
      ['Event Tag', text(f.eventTag)],
    ])}<p class="small rf-declaration${f.declared ? '' : ' muted'}">${f.declared ? '<span class="yes">Declared</span> ' : 'Not declared: '}I declare the following: Area of visit by Ricoh’s delivery crew is not Covid-19 affected area. For delivery to customers who are individuals, there is no one in the premise under SHN</p></section>`;
  }

  function history(f) {
    const x = f.facts;
    const steps = f.history.length ? `<ol class="cs-timeline">${f.history.map((s, i) => `<li${i === f.history.length - 1 ? ' class="last"' : ''}><span class="cs-dot" aria-hidden="true"></span>
        <span class="cs-step"><span><b>${esc(s.step)}</b>${s.by ? ` · ${esc(s.by)}` : ''}</span><span class="cs-when">${when(s.at)}</span></span></li>`).join('')}</ol>`
      : empty('MyOrders kept no dates for this request form.');
    const last = f.history.length ? f.history[f.history.length - 1] : null;
    return section('history', 'History', last ? `${esc(last.step)}${last.at ? ' ' + esc(date(last.at)) : ''}` : '', steps + facts([
      ['Sales Order Number', text(x.salesOrderNumber)],
      x.backorderedSalesOrderNumber ? ['Backordered SO Number', text(x.backorderedSalesOrderNumber)] : null,
      ['Picking Slip Number', text(x.pickingSlipNumber)],
      ['Invoice Number', text(x.invoiceNumber)],
      x.warehouse ? ['Warehouse', text(x.warehouse)] : null,
      ['OPO Checked', yesNo(x.isOpoChecked)],
    ]), 'From the dates and names MyOrders kept on the request form');
  }

  function attachments(f) {
    const body = f.attachments.length ? `<ul class="cs-files">${f.attachments.map(a => `<li><span class="cs-file"><b>${esc(a.fileName)}</b>
        <span class="muted small">${esc([size(a.size), a.uploadedBy, a.uploadedOn ? date(a.uploadedOn) : '', a.isComplex ? 'Specialists\' attachment' : ''].filter(Boolean).join(' · '))}</span>
        ${a.remarks ? `<span class="muted small">${esc(a.remarks)}</span>` : ''}</span>
      <a class="button secondary small-btn" href="file.ashx/requestform-attachment?requestForm=${f.id}&amp;id=${a.id}" aria-label="Download ${esc(a.fileName)}"><span data-icon="download"></span>Download</a></li>`).join('')}</ul>`
      : empty('No attachments to show.');
    return section('attachments', 'Attachments', String(f.attachments.length), body);
  }

  function differences(f) {
    if (!f.costingSheetId || !f.differences.length) return '';
    return `<div class="notice warn">There are differences between Costing Sheet ${sheetLink(f)} and Request Form.</div>` + card('Costing sheet and request form',
      table('<th>EDP Code</th><th>Description</th><th class="num">Costing Sheet Quantity</th><th class="num">Request Form Quantity</th>',
        f.differences.map(d => `<tr><td class="nowrap">${esc(d.edpCode)}</td><td>${esc(d.description)}</td><td class="num">${d.costingSheetQuantity}</td><td class="num">${d.requestFormQuantity}</td></tr>`).join('')));
  }

  function items(f) {
    const blocks = f.items.map(item => {
      const lines = [
        `<tr><td><b>${esc(item.productName)}</b></td><td class="num">${item.quantity}</td><td class="num">${money(item.unitPrice)}</td><td class="num">${money(item.quantity * item.unitPrice)}</td></tr>`,
        ...item.subItems.map(s => `<tr><td>${esc(s.productName)}</td><td class="num">${s.quantity}</td><td class="num">${money(s.unitPrice)}</td><td class="num">${money(s.quantity * s.unitPrice)}</td></tr>`),
        ...item.complexItems.map(x => `<tr><td>${esc(x.productName)}</td><td class="num">${x.quantity}</td><td class="num">${money(0)}</td><td class="num">${money(0)}</td></tr>`),
        `<tr class="total"><td colspan="3" class="num muted">Street price</td><td class="num" data-label=""><b>${money(item.totalStp)}</b></td></tr>`,
      ].join('');
      return `<div class="item-block"><div class="item-head"><h3>${esc(item.productName)}</h3></div>
        ${table('<th>Name</th><th class="num">Quantity</th><th class="num">Street Price (Unit)</th><th class="num">Street Price (Total)</th>', lines)}</div>`;
    }).join('');
    return section('items', 'Items', `${f.items.length} · ${dollars(f.totalStp)}`, (blocks || empty('No items to show.')) + facts([['Request Total Street Price', `<b>${dollars(f.totalStp)}</b>`]]));
  }

  function delivery(f) {
    if (!f.items.length) return section('delivery', 'Delivery', '0', empty('No items to show.'));
    const dates = f.items.map(item => item.date).filter(Boolean).sort();
    return section('delivery', 'Delivery', `${f.items.length}${dates.length ? ' · ' + esc(date(dates[0])) : ''}`, table('<th>Product</th><th>Delivery</th><th>Model / Serial</th>', f.items.map(item => {
      const parts = [...item.subItems.map(s => `${esc(s.productName)} x ${s.quantity}`), ...item.complexItems.map(x => `${esc(x.productName)} x ${x.quantity}`),
        ...item.packetItems.map(p => `${esc(p.productName)} x ${p.quantity}`)];
      return `<tr><td class="wide-text"><b>${esc(item.productName)} x ${item.quantity}</b>${parts.length ? `<span class="sub">${parts.join(' · ')}</span>` : ''}
          ${item.isFsa || item.noLift ? `<span class="sub">${item.isFsa ? '<span class="pill">Is FSA</span> ' : ''}${item.noLift ? '<span class="pill">No Lift</span>' : ''}</span>` : ''}</td>
        <td class="wide-text"><b>${day(item.date)}</b><span class="sub">${text(item.address)}</span>${item.contact || item.phone ? `<span class="sub">${esc([item.contact, item.phone].filter(Boolean).join(' · '))}</span>` : ''}
          ${item.remarks ? `<span class="sub">${esc(item.remarks)}</span>` : ''}</td>
        <td>${text(item.model)}<span class="sub">${esc(item.serial)}</span></td></tr>`;
    }).join('')));
  }

  function collection(f) {
    const first = f.items[0];
    if (!first) return section('collection', 'Collection items', '0', empty('No items to show.'));
    return section('collection', 'Collection items', `${f.items.length}${first.date ? ' · ' + esc(date(first.date)) : ''}`, facts([
      ['Collection Type', text(first.collectionType)],
      ['Contract No', text(first.contractNo)],
      ['Reason Of Termination', text(first.reasonOfTermination)],
      ['Removal Fee (Per Unit Only For Leasing)', dollars(first.removalFee)],
      ['Invoice No', text(first.invoiceNo)],
      ['Balance To Invoice', dollars(first.balanceToInvoice)],
      ['Rollover Amount', dollars(first.rolloverAmount)],
      ['Asset Type', text(first.assetType)],
      ['Brand', text(first.brand)],
      ['Collection Date', day(first.date)],
      ['Address', text(first.address)],
      ['Postal Code', text(first.postalCode)],
      ['Contact', text(first.contact)],
      ['Phone', text(first.phone)],
      ['Secondary Contact', text(first.secondaryContact)],
      ['Secondary Contact No.', text(first.secondaryContactNo)],
      ['No Lift', yesNo(first.noLift)],
      ['Call Before Collection', yesNo(first.callBeforeCollection)],
      ['Special Instructions', text(first.specialInstructions)],
    ]) + table('<th>Model</th><th>Serial</th><th>EDPCode</th><th class="num">Quantity</th><th>Has Finisher</th>', f.items.map(i => `<tr>
      <td><b>${text(i.model)}</b><span class="sub">${esc([i.brand, i.description].filter(Boolean).join(' · '))}</span>${i.remarks ? `<span class="sub">${esc(i.remarks)}</span>` : ''}</td>
      <td>${text(i.serial)}</td><td class="nowrap">${text(i.edpCode)}</td><td class="num">${i.quantity}</td><td>${yesNo(i.hasFinisher)}</td></tr>`).join('')));
  }

  function more(f) {
    const x = f.facts;
    return section('more', 'More details', f.printedAt ? 'Printed ' + esc(date(f.printedAt)) : '', facts([
      ['Proposal', x.proposalId ? '# ' + esc(x.proposalId) : '<span class="muted">-</span>'],
      ['Option', x.optionId ? '# ' + esc(x.optionId) : '<span class="muted">-</span>'],
      ['Previous Request Form', x.previousRequestFormId ? `<a href="requestform.aspx?id=${x.previousRequestFormId}"># ${esc(x.previousRequestFormId)}</a>` : '<span class="muted">-</span>'],
      ['Printed At', when(f.printedAt)],
      ['Is EPP', yesNo(x.isEpp)],
      ['Recon Billed', yesNo(x.isReconBilled)],
    ]), 'Kept on the request form');
  }

  let tab = '';

  function render(f) {
    document.title = `Request form ${f.id} · MyCRMRecords`;
    const tone = /invoiced/i.test(f.status) && !/pending/i.test(f.status) ? 'done' : /recall|reject|cancel/i.test(f.status) ? 'warn' : /backorder/i.test(f.status) ? 'other' : '';
    const tabs = (f.isCollection ? [['collection', 'Collection items']] : [['items', 'Items'], ['delivery', 'Delivery']]).concat([['more', 'More details']]);
    if (!tabs.some(t => t[0] === tab)) tab = tabs[0][0];
    page.innerHTML = `
      <div class="page-heading cs-heading"><div class="cs-title">
          ${crumbs(`Request Form # ${f.id}`, [['Request forms', 'requestforms.aspx']])}
          <div class="cs-name"><h1>Request Form # ${f.id}</h1><span class="orders-status rf-type">${esc(f.type)}</span><span class="orders-status ${tone}">${esc(f.status)}</span></div>
          <p>${f.reference ? `Ref # ${esc(f.reference)} · ` : ''}${f.companyId ? `<a href="company.aspx?id=${f.companyId}">${esc(f.company)}</a>` : esc(f.company)}${f.salesman ? ' · ' + esc(f.salesman) : ''}</p>
          <p class="muted small cs-readonly">Read only, from MyOrders.</p></div>
        <div class="heading-actions">${f.canPrint ? `<a class="button primary" href="requestformprint.aspx?id=${f.id}" target="_blank" rel="noopener"><span data-icon="printer"></span>Print ${f.isCollection ? 'collection' : 'delivery'} form</a>` : ''}</div></div>
      ${f.isActive ? '' : `<div class="notice error">This request form was deleted in MyOrders${f.deletedAt ? ' on ' + esc(dateTime(f.deletedAt)) : ''}${f.deletedBy ? ' by ' + esc(f.deletedBy) : ''}. It is not in the list.</div>`}
      ${details(f)}
      ${differences(f)}
      <div class="tabs cs-tabs" role="tablist" aria-label="Request form sections">${tabs.map(([key, label]) => `<button type="button" class="tab" role="tab" data-tab="${key}" aria-controls="body-${key}" aria-selected="${tab === key}">${esc(label)}</button>`).join('')}</div>
      <div class="cs-layout"><div class="cs-main">${f.isCollection ? collection(f) : items(f) + delivery(f)}${more(f)}</div><aside class="cs-aside">${history(f)}${attachments(f)}</aside></div>`;
    drawIcons(page);
    showTab();
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
    const answer = id ? await api('GET', 'requestforms/' + id) : {result: {ok: false, message: 'Open a request form from the list.'}};
    $('#loading').hidden = true;
    page.hidden = false;
    if (!answer.result.ok) {
      page.innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3><p><a class="button secondary" href="requestforms.aspx">Back to the request forms</a></p></div>`;
      return;
    }
    render(answer.requestForm);
  })();
})();
