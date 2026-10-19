(() => {
  'use strict';
  // audit.aspx: every change made through MyCRMRecords and every sign-in, newest first. Administrators only.
  const {$, esc, api, dateTime} = window.WH;
  const page = $('#page');
  let skip = 0, search = '', failedOnly = false, timer = 0;

  function shell() {
    $('#loading').hidden = true;
    page.hidden = false;
    page.innerHTML = `<div class="page-heading"><div><h1>Audit log</h1><p>Who did what, when, from where, and how it went.</p></div></div>
      <div class="toolbar"><label class="search-box"><span data-icon="search"></span><input id="search" type="search" placeholder="Search person, action, message" aria-label="Search"></label>
        <label class="check"><input type="checkbox" id="failed">Only what did not succeed</label></div>
      <div id="rows"><div class="loading">Loading…</div></div><div class="pager" id="pager" hidden></div>`;
    window.WH.drawIcons(page);
    $('#search').addEventListener('input', event => { clearTimeout(timer); timer = setTimeout(() => { search = event.target.value.trim(); skip = 0; load(); }, 300); });
    $('#failed').addEventListener('change', event => { failedOnly = event.target.checked; skip = 0; load(); });
    $('#pager').addEventListener('click', event => {
      const button = event.target.closest('[data-page]');
      if (!button) return;
      skip = Math.max(0, skip + Number(button.dataset.page) * 100);
      load();
    });
    load();
  }

  async function load() {
    const answer = await api('GET', 'admin/audit?' + new URLSearchParams({q: search, failed: failedOnly ? '1' : '', skip}));
    if (!answer.result.ok) { $('#rows').innerHTML = `<div class="empty"><h3>${esc(answer.result.message)}</h3></div>`; return; }
    $('#rows').innerHTML = `<div class="table-wrap"><table class="data"><thead><tr><th>When</th><th>Who</th><th>Action</th><th>Result</th><th>Details</th></tr></thead><tbody>
      ${answer.rows.map(e => `<tr><td class="nowrap small">${esc(dateTime(e.at))}</td>
        <td>${esc(e.displayName)}<span class="sub">${esc(e.login)}${e.impersonatorName ? ' · by ' + esc(e.impersonatorName) : ''} · ${esc(e.address)}</span></td>
        <td>${esc(e.action)}<span class="sub"><code>${esc(e.path)}</code></span></td>
        <td>${e.succeeded ? '<span class="yes">OK</span>' : '<span class="file-missing">Failed</span>'}<span class="sub">${esc(e.message)}</span></td>
        <td>${e.details ? `<details><summary class="text-button">Sent</summary><pre class="log">${esc(e.details)}</pre></details>` : ''}</td></tr>`).join('')
        || '<tr><td colspan="5" class="muted">Nothing to show.</td></tr>'}</tbody></table></div>`;
    const pager = $('#pager');
    pager.hidden = answer.total <= 100;
    pager.innerHTML = `<span>${skip + 1}–${Math.min(skip + 100, answer.total)} of ${answer.total}</span><span class="pager-buttons">
      <button class="button secondary small-btn" data-page="-1"${skip === 0 ? ' disabled' : ''}>Previous</button>
      <button class="button secondary small-btn" data-page="1"${skip + 100 >= answer.total ? ' disabled' : ''}>Next</button></span>`;
  }
  shell();
})();
