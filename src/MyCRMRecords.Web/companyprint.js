(() => {
  'use strict';
  // companyprint.aspx?id=: a company's summary on one A4 page, to print or save as PDF from the browser (the company
  // page's Print summary). Only what the company page shows: its details and salesperson, addresses, primary contacts
  // and the machines whose contract or warranty is running (Advance, by BRN). Printing records nothing.
  const {$, esc, api, date, number, param} = window.WH;
  const id = Number(param('id') || 0);
  const doc = $('#doc');
  const t = value => esc(value ?? '');
  const row = (label, value) => `<dt>${esc(label)}</dt><dd>${value || '–'}</dd>`;

  async function load() {
    if (!id) { doc.innerHTML = '<p class="message">No company chosen.</p>'; return; }
    const [answer, session] = await Promise.all([api('GET', 'companies/' + id), api('GET', 'session')]);
    if (!answer.result.ok) { doc.innerHTML = `<p class="message">${esc(answer.result.code === 'NOT_FOUND' ? 'That company is not there.' : answer.result.message)}</p>`; return; }
    const machines = await api('GET', 'machines?' + new URLSearchParams({company: String(id)}));
    const c = answer.company, owner = answer.owner;
    document.title = c.name + ' · Company summary';
    $('#back').href = 'company.aspx?id=' + id;
    const now = new Date();
    const pad = n => String(n).padStart(2, '0');
    const today = `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
    const printed = `${date(today + 'T00:00:00')}, ${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}`;
    const primary = answer.contacts.filter(p => p.isDefault);
    const addresses = answer.addresses.slice().sort((a, b) => Number(b.isDefault) - Number(a.isDefault));
    const machineRows = machines.result.ok ? machines.rows : [];
    doc.innerHTML = `<article class="sheet">
      <header><div><p class="grey">MyCRMRecords · Company summary</p><h1>${t(c.name)}${c.isActive ? '' : ' <span class="pr-tag">Inactive</span>'}</h1>
        <p class="grey">${[c.brn ? 'BRN ' + t(c.brn) : 'No BRN', c.customerNo ? 'Customer code ' + t(c.customerNo) : '', owner ? 'Territory ' + t(owner.territory) : ''].filter(Boolean).join(' · ')}</p></div>
        <p class="printed grey">Printed ${esc(printed)}${session.result.ok ? `<br>by ${t(session.user.name)}` : ''}</p></header>
      <div class="two">
        <section><h2 class="pr-h">Company details</h2><dl>
          ${row('A/C Type', t(c.compAccountTypeLabel))}${row('Customer Code', t(c.customerNo))}${row('Customer Type', t(c.companyGroupLabel))}
          ${row('Company Group', t(c.groupName))}${row('Industry', t(c.industry))}${row('No of Employees', number(c.noOfEmployees))}
          ${row('Website', t(c.website))}${row('Sales Remarks', t(c.salesRemarks))}</dl></section>
        <section><h2 class="pr-h">Salesperson</h2><dl>
          ${row('Salesperson', owner ? `<b>${t(owner.name || 'Vacant')}</b>, ${t(owner.territory)}` : 'No salesperson')}
          ${row('Caretake Remarks', answer.caretakes.map(t).join('\n'))}</dl></section>
      </div>
      <section><h2 class="pr-h">Addresses</h2>${addresses.length ? `<table class="pr-t"><colgroup><col style="width:80px"><col></colgroup><tbody>
        ${addresses.map(a => `<tr><td><span class="pr-tag">${t(a.typeLabel || '–')}</span></td><td>${t(a.text)}${a.isDefault ? ' (default)' : ''}</td></tr>`).join('')}</tbody></table>` : '<p class="none">No address.</p>'}</section>
      <section><h2 class="pr-h">Primary contacts</h2>${primary.length ? `<table class="pr-t"><colgroup><col style="width:30%"><col style="width:26%"><col style="width:22%"><col style="width:22%"></colgroup>
        <thead><tr><th>Name</th><th>Designation</th><th>Phone</th><th>Email</th></tr></thead><tbody>
        ${primary.map(p => `<tr><td><b>${t(p.name)}</b></td><td>${t([p.designation, p.department].filter(Boolean).join(' · '))}</td>
          <td>${p.phones.map(x => t(x.number)).join('<br>')}</td><td>${p.emails.map(x => t(x.address)).join('<br>')}</td></tr>`).join('')}</tbody></table>` : '<p class="none">No primary contact.</p>'}</section>
      <section><h2 class="pr-h">Machines <span>${machines.result.ok ? `${machineRows.length} with a contract or warranty running` : ''}</span></h2>
        ${!machines.result.ok ? `<p class="none">${esc(machines.result.message)}</p>` : machineRows.length ? `<table class="pr-t"><colgroup><col style="width:17%"><col style="width:15%"><col style="width:13%"><col style="width:18%"><col style="width:19%"><col style="width:18%"></colgroup>
        <thead><tr><th>Serial</th><th>Model</th><th>Coverage</th><th>Contract</th><th>Finance type</th><th>Ends</th></tr></thead><tbody>
        ${machineRows.map(m => `<tr><td>${t(m.serial)}</td><td>${t(m.model)}</td><td>${t(m.coverageName || m.coverage)}</td><td>${t(m.contractNo || '–')}</td><td>${t(m.finance || '–')}</td>
          <td>${m.contractEnd && m.contractEnd.slice(0, 10) >= today ? esc(date(m.contractEnd)) : m.warrantyEnd ? esc(date(m.warrantyEnd)) + ' (warranty)' : m.contractEnd ? esc(date(m.contractEnd)) : '–'}</td></tr>`).join('')}</tbody></table>`
          : '<p class="none">No machine of this BRN has a contract or warranty running.</p>'}</section>
      <footer><span>Print / Save as PDF from the company page</span><span>${t(c.name)}</span></footer>
    </article>`;
  }

  $('#print').addEventListener('click', () => print());
  load();
})();
