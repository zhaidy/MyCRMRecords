(() => {
  'use strict';
  // requestform.aspx?id=: one request form as MyOrders' summary showed it (header, declaration, attachments, the
  // differences with its costing sheet, items with their street prices, and the deliveries, or for a collection the
  // collection items), read only, with its history from the dates MyOrders kept. Print opens requestformprint.aspx,
  // MyOrders' delivery form or collection form as a page.
  const {$, esc, api, drawIcons, dateTime, date, param} = window.WH;
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
  const table = (head, rows) => `<div class="table-wrap"><table class="data"><thead><tr>${head}</tr></thead><tbody>${rows}</tbody></table></div>`;
  const sheetLink = f => f.canOpenCostingSheet ? `<a href="costingsheet.aspx?id=${f.costingSheetId}"># ${esc(f.costingSheetId)}</a>` : '# ' + esc(f.costingSheetId);

  function header(f) {
    const specialists = f.specialists.length
      ? f.specialists.map(s => `<span class="pill${s.pending ? ' warn' : ''}" title="${s.pending ? 'Pending' : 'Done'}">${esc(s.role)}: ${esc(s.name)}${s.pending ? ' (pending)' : ''}</span>`).join(' ')
      : '<span class="muted">-</span>';
    return card('Request form', facts([
      ['Request Form Type', text(f.type)],
      ['Reference', f.reference ? '# ' + esc(f.reference) : '<span class="muted">-</span>'],
      ['Salesman', text(f.salesman)],
      ['Request Form Status', text(f.status)],
      ['Specialists', specialists],
      ['Created At', when(f.createdAt)],
      ['Updated At', when(f.updatedAt)],
      ['Recall At', when(f.recallAt)],
      // The company opens its page here (MyCRM's companies are kept in MyCRMRecords).
      ['Company', f.companyId ? `<a href="company.aspx?id=${f.companyId}"><b>${text(f.company)}</b></a>` : `<b>${text(f.company)}</b>`],
      ['Billing Address', text(f.address)],
      ['Contact', text(f.contact)],
      ['Phone', f.phones.length ? f.phones.map(esc).join('<br>') : '<span class="muted">-</span>'],
      ['Customer No', text(f.customerNo)],
      ['Customer Type', text(f.customerType)],
      ['Event Tag', text(f.eventTag)],
      ['Costing Sheet', f.costingSheetId ? sheetLink(f) : '<span class="muted">-</span>'],
    ]) + `<p class="small${f.declared ? '' : ' muted'}" style="margin:10px 0 0">${f.declared ? '<span class="yes">Declared</span> ' : 'Not declared: '}I declare the following: Area of visit by Ricoh’s delivery crew is not Covid-19 affected area. For delivery to customers who are individuals, there is no one in the premise under SHN</p>`);
  }

  function history(f) {
    const x = f.facts;
    const steps = f.history.length ? table('<th>Step</th><th>When</th><th>By</th>',
      f.history.map(s => `<tr><td><b>${esc(s.step)}</b></td><td class="nowrap">${when(s.at)}</td><td>${text(s.by)}</td></tr>`).join(''))
      : empty('MyOrders kept no dates for this request form.');
    return card('History', steps + facts([
      ['Sales Order Number', text(x.salesOrderNumber)],
      x.backorderedSalesOrderNumber ? ['Backordered SO Number', text(x.backorderedSalesOrderNumber)] : null,
      ['Picking Slip Number', text(x.pickingSlipNumber)],
      ['Invoice Number', text(x.invoiceNumber)],
      x.warehouse ? ['Warehouse', text(x.warehouse)] : null,
      ['OPO Checked', yesNo(x.isOpoChecked)],
    ]), '<span class="muted small">From the dates and names MyOrders kept on the request form</span>');
  }

  function attachments(f) {
    if (!f.attachments.length) return card('Attachments', empty('No attachments to show.'));
    return card('Attachments', table('<th></th><th>File</th><th>Uploaded</th>', f.attachments.map(a => `<tr>
      <td class="nowrap"><a class="icon-button small" href="file.ashx/requestform-attachment?requestForm=${f.id}&amp;id=${a.id}" title="Download ${esc(a.fileName)}" aria-label="Download ${esc(a.fileName)}"><span data-icon="download"></span></a></td>
      <td class="wide-text"><b>${esc(a.fileName)}</b><span class="sub">${esc(size(a.size))}${a.remarks ? ' · ' + esc(a.remarks) : ''}${a.isComplex ? ' · Specialists\' attachment' : ''}</span></td>
      <td>${text(a.uploadedBy)}<span class="sub">${a.uploadedOn ? esc(date(a.uploadedOn)) : ''}</span></td></tr>`).join('')));
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
        `<tr class="total"><td colspan="3" class="num muted">Street price</td><td class="num"><b>${money(item.totalStp)}</b></td></tr>`,
      ].join('');
      return `<div class="item-block"><div class="item-head"><h3>${esc(item.productName)}</h3></div>
        ${table('<th>Name</th><th class="num">Quantity</th><th class="num">Street Price (Unit)</th><th class="num">Street Price (Total)</th>', lines)}</div>`;
    }).join('');
    return card('Items', (blocks || empty('No items to show.')) + facts([['Request Total Street Price', `<b>${dollars(f.totalStp)}</b>`]]));
  }

  function delivery(f) {
    if (!f.items.length) return card('Delivery', empty('No items to show.'));
    return card('Delivery', table('<th>Product</th><th>Delivery</th><th>Model / Serial</th>', f.items.map(item => {
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
    if (!first) return card('Collection Items', empty('No items to show.'));
    return card('Collection Items', facts([
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
    return card('More details', facts([
      ['Proposal', x.proposalId ? '# ' + esc(x.proposalId) : '<span class="muted">-</span>'],
      ['Option', x.optionId ? '# ' + esc(x.optionId) : '<span class="muted">-</span>'],
      ['Previous Request Form', x.previousRequestFormId ? `<a href="requestform.aspx?id=${x.previousRequestFormId}"># ${esc(x.previousRequestFormId)}</a>` : '<span class="muted">-</span>'],
      ['Printed At', when(f.printedAt)],
      ['Is EPP', yesNo(x.isEpp)],
      ['Recon Billed', yesNo(x.isReconBilled)],
    ]), '<span class="muted small">Kept on the request form</span>');
  }

  function render(f) {
    document.title = `Request form ${f.id} · MyCRMRecords`;
    page.innerHTML = `
      <div class="page-heading"><div><p class="muted small" style="margin:0"><a href="requestforms.aspx"><span data-icon="arrow-left"></span> Request forms</a></p>
        <h1>Request Form # ${f.id} <span class="pill">${esc(f.status)}</span></h1><p>${esc(f.type)} · ${esc(f.company)}${f.salesman ? ' · ' + esc(f.salesman) : ''}</p></div>
        <div class="heading-actions">${f.canPrint ? `<a class="button primary" href="requestformprint.aspx?id=${f.id}" target="_blank" rel="noopener"><span data-icon="printer"></span>Print</a>` : ''}</div></div>
      ${f.isActive ? '' : `<div class="notice error">This request form was deleted in MyOrders${f.deletedAt ? ' on ' + esc(dateTime(f.deletedAt)) : ''}${f.deletedBy ? ' by ' + esc(f.deletedBy) : ''}. It is not in the list.</div>`}
      ${differences(f)}
      <div class="detail-grid"><div>${header(f)}${f.isCollection ? collection(f) : items(f) + delivery(f)}</div><div>${history(f)}${attachments(f)}${more(f)}</div></div>`;
    drawIcons(page);
  }

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
