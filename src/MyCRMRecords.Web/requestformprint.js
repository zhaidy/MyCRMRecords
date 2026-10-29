(() => {
  'use strict';
  // requestformprint.aspx?id=: MyOrders' request form PDF as a page to print or save as PDF from the browser
  // ("RequestForm #<Id>" as the file name). Every type but Collection prints MyOrders' delivery form: a part for each
  // delivery, 25 lines a page, as MyOrders printed it last (its Print rebuilt the delivery form each time; MyCRMRecords
  // shows the one MyOrders kept and builds none). A Collection prints the collection form. The texts are MyOrders'; the
  // values come from api.ashx/requestforms/<id>/print. Printing records nothing (MyOrders wrote the time it was printed).
  const {$, esc, api, param} = window.WH;
  const id = Number(param('id') || 0);
  const t = value => esc(value ?? '');
  const tick = '<svg class="tick" viewBox="0 0 24 24" width="16" height="16" aria-label="Yes"><path d="m4 12 5 5L20 6" fill="none" stroke="#2e9e4f" stroke-width="3"/></svg>';
  const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  const parts = value => /^(\d{4})-(\d{2})-(\d{2})(?:T(\d{2}):(\d{2}))?/.exec(value || '');
  /** dd/MM/yyyy */
  const ddmmyyyy = value => { const m = parts(value); return m ? `${m[3]}/${m[2]}/${m[1]}` : ''; };
  /** dd MMM yyyy */
  const ddMMMyyyy = value => { const m = parts(value); return m ? `${m[3]} ${months[Number(m[2]) - 1]} ${m[1]}` : ''; };
  /** MyOrders' FormatDateTime_: dd/MM/yyyy, with HH:mm when the time is not midnight. */
  const dayOrTime = value => { const m = parts(value); return !m ? '' : `${m[3]}/${m[2]}/${m[1]}` + (m[4] && (m[4] !== '00' || m[5] !== '00') ? ` ${m[4]}:${m[5]}` : ''); };
  const qr = text => window.WHQR ? window.WHQR.svg(String(text), 120) : '';

  /** MyOrders' RequestFormHeader, at the top of every page. */
  function header(p) {
    const title = p.isCollection ? 'COLLECTION FORM' : 'REQUEST FORM for - Request Form/' + t(p.type);
    return `<div class="myHeader"><img src="ricoh-logo.png" alt="RICOH">
      <div class="middle"><div class="ref"><span class="Underline">${t(p.reference)}-${p.id}-${(p.agilityRunningNumber || 0) - 1}</span> OPO / Warehouse / Scv / IT</div>
        <div class="fakeSelect">${title} (Updated ${esc(ddMMMyyyy(p.updatedOn))})</div></div>
      <img src="restricted.png" alt="RESTRICTED. Do not access if you are not sure of your access rights. [RSP ISMS]"></div>`;
  }

  const widths = ['15%', '5%', '8%', '30%', '15%', '10%', '10%', '7%'];
  const row = (cells, cls) => `<table class="${cls}"><tbody><tr>${cells.map((c, i) => `<td style="width:${widths[i]}"${i === 0 || i === 3 || i === 4 ? ' class="break"' : ''}>${c}</td>`).join('')}</tr></tbody></table>`;

  /** One page of a delivery: header, customer and delivery tables, the item table padded to 25 rows, approval and QR code. */
  function deliveryPage(p, d, lines) {
    const salesman = p.salesman + (p.salesmanCode ? ' - ' + p.salesmanCode : ' - ');
    return `<div class="Page">${header(p)}
      <table class="table"><tbody>
        <tr><td class="label">Customer</td><td>${t(p.company)}</td><td class="label">A/C Number</td><td>${t(p.customerNo)}</td></tr>
        <tr><td class="label">Contact Person</td><td>${t(d.contact)}</td><td class="label">Contact Number</td><td>${t(d.phone)}</td></tr>
        <tr><td class="label">Salesman Name</td><td>${t(salesman)}</td><td class="label">Submission Date </td><td>${esc(ddmmyyyy(p.createdAt))}</td></tr>
      </tbody></table>
      <table class="table"><tbody>
        <tr><td>Delivery Address</td><td>${t(d.address)}<div>${t(d.countryPostalCode)}</div></td></tr>
        <tr><td>Delivery Date</td><td><b>${esc(ddmmyyyy(d.date))}</b></td></tr>
        <tr><td>No Lift</td><td>${d.noLift ? tick : ''}</td></tr>
        <tr><td>Remarks</td><td><b>${t(d.remarks)}</b></td></tr>
      </tbody></table>
      <div style="margin-top:10px">${row(['<b>Item Code</b>', '<b>Tick</b>', '<b>IsFSA</b>', '<b>Name</b>', '<b>Serial No.</b>', '<b>Quantity</b>', '<b>Allocated Qty</b>', '<b>Check</b>'], 'table')}
        ${lines.map(l => row([l.itemCode ? t(l.itemCode) : '&nbsp;', '', l.isFsa ? tick : '', t(l.name), t(l.serialNo), l.quantity > 0 ? l.quantity : '', l.allocatedQuantity, ''], 'table2')).join('')}
        ${Array.from({length: Math.max(0, 25 - lines.length)}, () => row(['&nbsp;', '', '', '', '', '', '', ''], 'table2')).join('')}</div>
      <div class="row" style="margin-top:20px;align-items:flex-start"><div style="flex:0 0 66%;display:flex;gap:8px"><div style="flex:1" class="Bold">APPROVED BY MANAGER</div><div style="flex:1" class="UnderLineBox"></div></div>
        <div style="flex:1;display:flex;justify-content:flex-end">${qr(p.id)}</div></div>
      ${p.declared ? `<div class="declare">${tick}<span>I declare the following: Area of visit by Ricoh’s delivery crew is not Covid-19 affected area. For delivery to customers who are individuals, there is no one in the premise under SHN</span></div>` : ''}
    </div>`;
  }

  function deliveryForm(p) {
    if (!p.deliveries.length)
      return `<div class="Page">${header(p)}<p class="message">MyOrders never printed this request form, so there is no delivery form to show. MyOrders made the delivery form when it printed, and MyCRMRecords only shows what MyOrders kept.</p></div>`;
    return p.deliveries.map(d => (d.pages.length ? d.pages : [[]]).map(lines => deliveryPage(p, d, lines)).join('')).join('');
  }

  const half = (label, value) => `<div class="half"><div class="label">${label}</div><div class="value">${value}</div></div>`;

  function collectionForm(p) {
    const c = p.collection;
    const salesman = p.salesman + (p.salesmanCode ? ' - ' + p.salesmanCode : ' - ');
    const approved = (name, on) => name ? `${t(name)} ${esc(dayOrTime(on))}` : '';
    return `<div class="Page">${header(p)}
      <table style="width:100%;margin-top:10px"><tbody>
        <tr><td><p class="heading2">Request for Collection</p></td><td rowspan="2" style="vertical-align:middle;text-align:right;width:130px">${qr(p.id)}</td></tr>
        <tr><td><p class="heading3">${t(p.reference)}</p></td></tr></tbody></table>
      <div class="italic" style="margin-top:10px">* required fields</div>
      <div class="borderBox"><div class="Bold">A. Type of Request</div><div style="text-align:center;font-size:14pt">${t(c.assetType)} (${t(c.collectionType)})</div></div>
      <div class="borderBox"><div class="Bold">B. Customer Infomation</div>
        <div class="row">${half('Customer Name *:', t(p.company))}${half('A/C No. *', t(p.customerNo))}</div>
        <div class="row">${half('Contact Person *', t(c.contact))}${half('Contact No. *', t(c.contactNo))}</div>
        <div class="row">${half('Secondary Contact', t(c.secondaryContact))}${half('Contact No.', t(c.secondaryContactNo))}</div>
        <div class="row"><div style="flex:0 0 16%">Special Instructions</div><div class="Bold" style="flex:1">${t(c.specialInstructions)}</div></div></div>
      <div class="borderBox"><div class="Bold">C. Collection Information</div>
        <div class="row">${half('Collection Address', t(c.address))}${half('Postal Code', t(c.postalCode))}</div>
        <div class="row">${half('Collection Date', esc(ddMMMyyyy(c.date)))}<div class="half"></div></div></div>
      <p class="heading2" style="margin-top:20px">${t(c.collectionType)} ${t(c.assetType)}</p>
      <div class="items" style="margin-top:20px">${c.items.length ? c.items.map((i, n) => `<div class="item"><div class="num">${n + 1}.</div><div>
        <div class="row" style="margin-top:0">${half('Model *', t(i.model))}${half('Serial No.', t(i.serial))}</div>
        <div class="row">${half('Quantity', i.quantity)}${half('Description', t(i.description))}</div>
        <div class="row">${half('Remark', t(i.remarks))}${half('Has Finisher', i.hasFinisher ? 'Yes' : 'No')}</div>
        <div class="row">${half('EDP Code', t(i.edpCode))}<div class="half"></div></div></div></div>`).join('') : '<p>No items to show...</p>'}</div>
      <div class="row"><div style="flex:0 0 33%">CREATED BY</div><div style="flex:1">${t(salesman)}</div></div>
      <div class="row"><div style="flex:0 0 33%" class="Bold">APPROVED BY MANAGER</div><div style="flex:1" class="UnderLineBox">${approved(c.approvedByManager, c.approvedByManagerOn)}</div></div>
      <div class="row"><div style="flex:0 0 33%" class="Bold">APPROVED BY HEAD</div><div style="flex:1" class="UnderLineBox">${approved(c.approvedByHead, c.approvedByHeadOn)}</div></div>
      <div class="row" style="margin-top:20px;align-items:flex-start">
        <div style="flex:0 0 calc(50% - 4px)"><div class="UnderLineBox" style="margin-top:60px"></div><div style="text-align:center">Authorised signature, stamp and date</div></div>
        <div style="flex:0 0 calc(50% - 4px)"><table class="toner"><tbody>
          <tr><td colspan="2" style="font-style:normal">Please indicate the no. of unused toner cartridge collected</td></tr>
          <tr><td style="width:30%">Qty</td><td style="width:70%">Customer's Signature &amp; Stamp</td></tr>
          <tr><td style="height:60px"></td><td></td></tr></tbody></table></div></div>
    </div>`;
  }

  $('#print').addEventListener('click', () => window.print());
  $('#back').href = 'requestform.aspx?id=' + id;
  document.title = `RequestForm #${id}`;

  (async () => {
    const answer = id ? await api('GET', `requestforms/${id}/print`) : {result: {ok: false, message: 'Open a request form from the list.'}};
    if (!answer.result.ok) { $('#doc').innerHTML = `<p class="message">${esc(answer.result.message)}</p>`; $('#print').disabled = true; return; }
    const p = answer.print;
    const printed = p.printedAt ? `<p class="message note">As MyOrders printed it on ${esc(dayOrTime(p.printedAt))}.</p>` : '';
    $('#doc').innerHTML = (p.isCollection ? '' : printed) + (p.isCollection ? collectionForm(p) : deliveryForm(p));
    if (!p.isCollection && !p.deliveries.length) $('#print').disabled = true;
  })();
})();
