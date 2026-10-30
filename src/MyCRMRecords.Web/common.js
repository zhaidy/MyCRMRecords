(() => {
  'use strict';
  // Shared by every page: icons, the menu, talking to api.ashx, formatting, toasts and dialogs (window.WH).
  const $ = (s, root = document) => root.querySelector(s);
  const $$ = (s, root = document) => [...root.querySelectorAll(s)];
  const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'}[c]));

  const paths = {
    menu: '<path d="M4 6h16M4 12h16M4 18h16"/>',
    file: '<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5M9 13h6M9 17h4"/>',
    truck: '<path d="M3 6h11v10H3zM14 9h4l3 3v4h-7"/><circle cx="7" cy="17.5" r="1.8"/><circle cx="17" cy="17.5" r="1.8"/>',
    matrix: '<rect x="3" y="3" width="18" height="18" rx="2"/><path d="M3 9h18M3 15h18M9 3v18"/>',
    chart: '<path d="M4 20V10M10 20V4M16 20v-7M22 20H2"/>',
    eye: '<path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3"/>',
    ban: '<circle cx="12" cy="12" r="9"/><path d="m5.6 5.6 12.8 12.8"/>',
    tag: '<path d="M20.6 13.4 13.4 20.6a2 2 0 0 1-2.8 0L3 13V3h10l7.6 7.6a2 2 0 0 1 0 2.8Z"/><circle cx="7.5" cy="7.5" r="1.5"/>',
    users: '<circle cx="9" cy="8" r="4"/><path d="M2 21a7 7 0 0 1 14 0M16 3.5a4 4 0 0 1 0 9M22 21a7 7 0 0 0-4-6.3"/>',
    settings: '<path d="M4 6h9M17 6h3M4 12h3M11 12h9M4 18h11M19 18h1"/><circle cx="15" cy="6" r="2"/><circle cx="9" cy="12" r="2"/><circle cx="17" cy="18" r="2"/>',
    key: '<circle cx="8" cy="15" r="4"/><path d="m10.8 12.2 9.2-9.2m-3 3 3 3m-6 0 2 2"/>',
    database: '<ellipse cx="12" cy="5" rx="8" ry="3"/><path d="M4 5v14c0 1.7 3.6 3 8 3s8-1.3 8-3V5M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3"/>',
    history: '<path d="M3 11a9 9 0 1 1 2.6 7M3 4v7h7m2-4v5l3 2"/>',
    audit: '<path d="M9 4h6v3H9z"/><path d="M9 5.5H6a1 1 0 0 0-1 1V20a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V6.5a1 1 0 0 0-1-1h-3M8.5 12h7M8.5 16h5"/>',
    help: '<circle cx="12" cy="12" r="9"/><path d="M9.5 9a2.5 2.5 0 1 1 3.5 2.3c-.6.3-1 .9-1 1.6V14m0 3h.01"/>',
    logout: '<path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4m7 14 5-5-5-5m5 5H9"/>',
    plus: '<path d="M12 5v14M5 12h14"/>',
    download: '<path d="M12 3v12m-5-5 5 5 5-5M4 19h16"/>',
    upload: '<path d="M12 16V4m-5 5 5-5 5 5M4 20h16"/>',
    printer: '<path d="M7 9V3h10v6M7 18H5a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/><path d="M7 14h10v7H7z"/>',
    search: '<circle cx="10.5" cy="10.5" r="6.5"/><path d="m16 16 4 4"/>',
    check: '<path d="m5 12 5 5L20 7"/>',
    close: '<path d="M6 6l12 12M18 6 6 18"/>',
    trash: '<path d="M4 7h16M10 11v6M14 11v6M5 7l1 13a1 1 0 0 0 1 1h10a1 1 0 0 0 1-1l1-13M9 7V4h6v3"/>',
    'arrow-left': '<path d="M20 12H4m6-6-6 6 6 6"/>',
    'arrow-up-right': '<path d="M7 17 17 7M8 7h9v9"/>',
    'arrow-right': '<path d="M4 12h16m-6-6 6 6-6 6"/>',
    edit: '<path d="M4 20h4L19 9l-4-4L4 16z"/><path d="m13 7 4 4"/>',
    send: '<path d="m4 12 16-8-6 16-3-7z"/><path d="m11 13 9-9"/>',
    undo: '<path d="M9 14 4 9l5-5"/><path d="M4 9h11a5 5 0 0 1 0 10h-3"/>',
    clock: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
    paperclip: '<path d="m21 12-8.5 8.5a5 5 0 0 1-7-7L14 5a3.3 3.3 0 0 1 4.7 4.7L10 18.4a1.7 1.7 0 0 1-2.4-2.4L15.5 8"/>',
    refresh: '<path d="M20 7a9 9 0 0 0-15-2L2 8m0-5v5h5m-3 9a9 9 0 0 0 15 2l3-3m0 5v-5h-5"/>',
    lock: '<rect x="4" y="11" width="16" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/>',
    excel: '<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5M9 12l4 5m0-5-4 5"/>',
    zip: '<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5M11 5h1M11 8h1M11 11h1M10.5 14h2v3h-2z"/>',
    money: '<rect x="2" y="6" width="20" height="12" rx="2"/><circle cx="12" cy="12" r="2.5"/><path d="M6 12h.01M18 12h.01"/>',
    'phone-download': '<rect x="6" y="2" width="12" height="20" rx="2.5"/><path d="M12 7v7m-3-3 3 3 3-3"/>',
    share: '<path d="M12 3v12M8 7l4-4 4 4"/><path d="M6 11H5a1 1 0 0 0-1 1v8a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-8a1 1 0 0 0-1-1h-1"/>',
    'plus-square': '<rect x="4" y="4" width="16" height="16" rx="3"/><path d="M12 8v8M8 12h8"/>',
  };
  const icon = name => `<svg viewBox="0 0 24 24" aria-hidden="true">${paths[name] || paths.file}</svg>`;
  // On a phone a table of rows is shown as a card per row (app.css): each cell is labelled with its column's heading.
  const labelCells = root => $$('table.data', root).forEach(table => {
    const headings = $$('thead th', table).map(th => th.textContent.trim());
    $$('tbody tr', table).forEach(tr => [...tr.children].forEach((td, i) => { if (!td.hasAttribute('data-label') && (td.colSpan || 1) === 1) td.setAttribute('data-label', headings[i] || ''); }));
  });
  const drawIcons = (root = document) => {
    $$('[data-icon]', root).forEach(el => { if (!el.firstElementChild) el.innerHTML = icon(el.dataset.icon); });
    labelCells(root);
  };
  drawIcons();

  // Phones and tablets hide the menu links; the menu button in the top bar slides them in over the page.
  const menuButton = $('#menu-button');
  const setMenu = open => { document.body.classList.toggle('menu-open', open); menuButton?.setAttribute('aria-expanded', open); };
  menuButton?.addEventListener('click', () => setMenu(!document.body.classList.contains('menu-open')));
  // On a phone, More in the bottom bar opens the same menu, as a sheet from the bottom (app.css).
  const moreButton = $('#more-button');
  moreButton?.addEventListener('click', () => { const open = !document.body.classList.contains('menu-open'); setMenu(open); moreButton.setAttribute('aria-expanded', open); });
  document.addEventListener('keydown', event => { if (event.key === 'Escape') moreButton?.setAttribute('aria-expanded', 'false'); });
  $('#menu-backdrop')?.addEventListener('click', () => moreButton?.setAttribute('aria-expanded', 'false'));
  $('#menu-backdrop')?.addEventListener('click', () => setMenu(false));
  const closeNavGroups = target => $$('.nav-group[open]').forEach(group => { if (!group.contains(target)) group.open = false; });
  document.addEventListener('click', event => closeNavGroups(event.target));
  document.addEventListener('keydown', event => { if (event.key === 'Escape') { setMenu(false); closeNavGroups(null); } });

  /** Calls api.ashx; resolves with the JSON answer (its result says whether a change was made); sends people to sign in when needed. */
  async function api(method, path, body) {
    const options = {method, headers: {Accept: 'application/json'}, credentials: 'same-origin', cache: 'no-store'};
    if (body !== undefined) { options.headers['Content-Type'] = 'application/json'; options.body = JSON.stringify(body); }
    let response;
    try { response = await fetch('api.ashx/' + path.replace(/^\//, ''), options); }
    catch { return {result: {ok: false, code: 'OFFLINE', message: 'MyCRMRecords could not be reached. Check your connection and try again.'}}; }
    if (response.status === 401) { location.href = 'login.aspx?ReturnUrl=' + encodeURIComponent(location.pathname + location.search); return new Promise(() => {}); }
    let json;
    try { json = await response.json(); }
    catch { json = {result: {ok: false, code: 'BAD_ANSWER', message: `Something went wrong (HTTP ${response.status}).`}}; }
    if (!json.result) json.result = {ok: response.ok, code: response.ok ? 'OK' : 'ERROR', message: ''};
    return json;
  }

  let toastTimer = 0;
  function toast(message, error = false) {
    let el = $('#toast');
    if (!el) { el = document.createElement('div'); el.id = 'toast'; el.className = 'toast'; el.setAttribute('role', 'status'); el.setAttribute('aria-live', 'polite'); document.body.append(el); }
    el.textContent = message;
    el.classList.toggle('error', !!error);
    el.classList.add('show');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => el.classList.remove('show'), error ? 7000 : 4000);
  }

  const numberFormat = new Intl.NumberFormat('en-SG', {maximumFractionDigits: 4});
  const number = value => value === null || value === undefined || value === '' ? '' : numberFormat.format(Number(value));
  const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  /** "16 Oct 2026" from the API's dates ("2026-10-16T00:00:00"), read as they are written (Singapore time). */
  function date(value) {
    const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(value || '');
    return m ? `${Number(m[3])} ${months[Number(m[2]) - 1]} ${m[1]}` : '';
  }
  function dateTime(value) {
    const m = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/.exec(value || '');
    return m ? `${date(value)}, ${m[4]}:${m[5]}` : date(value);
  }
  /** The input value (2026-10-16) of an API date. */
  const dateInput = value => (/^(\d{4}-\d{2}-\d{2})/.exec(value || '') || [])[1] || '';
  const statusBadge = (description, kind, big = false) => `<span class="status ${esc(kind || '')}${big ? ' big' : ''}">${esc(description)}</span>`;

  /** A dialog asking to confirm; resolves true or false. With a field, resolves with its text (or null). */
  function ask({title, text = '', ok = 'OK', danger = false, field = null, required = false, value = ''}) {
    return new Promise(resolve => {
      const dialog = document.createElement('dialog');
      dialog.innerHTML = `<form method="dialog" class="modal"><h2>${esc(title)}</h2>${text ? `<p>${esc(text)}</p>` : ''}
        ${field ? `<label class="field"><span class="${required ? 'required' : ''}">${esc(field)}</span><textarea rows="3" maxlength="1000">${esc(value)}</textarea><span class="field-error"></span></label>` : ''}
        <div class="modal-footer"><button type="button" class="button secondary" value="cancel">Cancel</button>
        <button type="submit" class="button ${danger ? 'danger-btn' : 'primary'}" value="ok">${esc(ok)}</button></div></form>`;
      document.body.append(dialog);
      const input = $('textarea', dialog);
      const finish = answer => { dialog.close(); dialog.remove(); resolve(answer); };
      $('button[value=cancel]', dialog).addEventListener('click', () => finish(field ? null : false));
      dialog.addEventListener('cancel', event => { event.preventDefault(); finish(field ? null : false); });
      $('form', dialog).addEventListener('submit', event => {
        event.preventDefault();
        if (field && required && !input.value.trim()) { $('.field-error', dialog).textContent = 'Please fill this in.'; input.focus(); return; }
        finish(field ? input.value.trim() : true);
      });
      dialog.showModal();
      (input || $('button[value=ok]', dialog)).focus();
    });
  }

  /** Marks the fields a change's errors name (data-field="server"), and clears the others. */
  function showErrors(root, errors) {
    $$('.field', root).forEach(field => { field.classList.remove('invalid'); const message = $('.field-error', field); if (message) message.textContent = ''; });
    $$('.cell-input.invalid', root).forEach(input => input.classList.remove('invalid'));
    let first = null;
    for (const error of errors || []) {
      const target = root.querySelector(`[data-field="${CSS.escape(error.field)}"]`);
      if (!target) continue;
      if (target.classList.contains('cell-input')) { target.classList.add('invalid'); target.title = error.message; }
      else {
        const field = target.closest('.field') || target;
        field.classList.add('invalid');
        const message = $('.field-error', field);
        if (message) message.textContent = error.message;
      }
      first ??= target;
    }
    first?.focus?.();
    return first;
  }

  /** A query string parameter. */
  const param = name => new URLSearchParams(location.search).get(name);

  /** The pager under a list: "1–50 of 120" with Previous and Next (data-page="-1" / "1"). */
  function pager(element, total, skip, take) {
    element.hidden = total <= take;
    element.innerHTML = `<span>${skip + 1}–${Math.min(skip + take, total)} of ${total}</span><span class="pager-buttons">
      <button class="button secondary small-btn" data-page="-1"${skip === 0 ? ' disabled' : ''}>Previous</button>
      <button class="button secondary small-btn" data-page="1"${skip + take >= total ? ' disabled' : ''}>Next</button></span>`;
  }

  /** Runs a change and reports it; resolves with the answer. */
  async function change(path, body, {reload} = {}) {
    const answer = await api('POST', path, body ?? {});
    toast(answer.result.message || (answer.result.ok ? 'Done.' : 'Something went wrong.'), !answer.result.ok);
    if (answer.result.ok && reload) await reload();
    return answer;
  }

  /** Uploads a file to file.ashx/<kind> (the header keeps other sites from doing it); resolves with the JSON answer. */
  async function upload(kind, file) {
    const data = new FormData();
    data.append('file', file, file.name);
    let response;
    try { response = await fetch('file.ashx/' + kind, {method: 'POST', body: data, credentials: 'same-origin', headers: {'X-MyCRMRecords': '1', Accept: 'application/json'}}); }
    catch { return {result: {ok: false, message: 'The file could not be uploaded. Check your connection and try again.'}}; }
    if (response.status === 401) { location.href = 'login.aspx?ReturnUrl=' + encodeURIComponent(location.pathname + location.search); return new Promise(() => {}); }
    try { return await response.json(); }
    catch { return {result: {ok: false, message: `The file could not be uploaded (HTTP ${response.status}).`}}; }
  }

  /** A coloured tag. */
  const tag = (label, color) => label ? `<span class="tag" style="--tag:${esc(color || '#7a8299')}">${esc(label)}</span>` : '';

  /* Install app: MyCRMRecords installs as an app (manifest.json, sw.js). On a phone the button installs it (Android and
     computers ask the browser; an iPhone shows the steps, as Safari has no button for it). On a computer it shows a QR
     code of MyCRMRecords' address to open on a phone and install there, and installs on the computer too when the
     browser can. Hidden once MyCRMRecords runs as the installed app. */
  const installed = () => matchMedia('(display-mode: standalone)').matches || navigator.standalone === true;
  const onPhone = () => /Android|iPhone|iPad|iPod/i.test(navigator.userAgent) || matchMedia('(max-width: 900px)').matches;
  const isIos = () => /iPhone|iPad|iPod/i.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
  const appAddress = () => new URL('./', location.href).href;
  let installPrompt = null;
  const installButton = $('#install-button');
  if (installButton && !installed()) installButton.hidden = false;
  addEventListener('beforeinstallprompt', event => { event.preventDefault(); installPrompt = event; });
  addEventListener('appinstalled', () => { installPrompt = null; if (installButton) installButton.hidden = true; toast('MyCRMRecords is installed.'); });
  async function installHere() {
    if (!installPrompt) return false;
    const prompt = installPrompt;
    installPrompt = null;
    await prompt.prompt();
    return true;
  }
  function loadQr() {
    if (window.WHQR) return Promise.resolve(window.WHQR);
    return new Promise((resolve, reject) => {
      const script = document.createElement('script');
      script.src = 'qrcode.js';
      script.onload = () => resolve(window.WHQR);
      script.onerror = reject;
      document.head.append(script);
    });
  }
  async function showInstall() {
    if (onPhone() && !isIos() && await installHere()) return;
    const phone = onPhone();
    const dialog = document.createElement('dialog');
    dialog.className = 'install-dialog';
    dialog.setAttribute('aria-labelledby', 'install-title');
    const steps = isIos()
      ? `<ol class="install-steps"><li>Tap <span data-icon="share"></span> <strong>Share</strong> at the bottom of Safari.</li><li>Tap <span data-icon="plus-square"></span> <strong>Add to Home Screen</strong>, then <strong>Add</strong>.</li></ol>`
      : `<ol class="install-steps"><li>Open the browser's menu <strong>⋮</strong>.</li><li>Tap <strong>Install app</strong> (or <strong>Add to Home screen</strong>).</li></ol>`;
    dialog.innerHTML = `<form method="dialog" class="modal install-modal">
      <div class="modal-head"><h2 id="install-title">Install MyCRMRecords</h2><button type="button" class="icon-button" value="close" aria-label="Close"><span data-icon="close"></span></button></div>
      ${phone ? `<p>Add MyCRMRecords to your home screen: it opens like an app, full screen, with the menu at the bottom.</p>${steps}`
        : `<div class="install-qr-row"><div class="install-qr" aria-label="QR code of MyCRMRecords' address"></div><div>
          <p><strong>On your phone:</strong> scan the code with the camera, sign in, then install MyCRMRecords from the page that opens (Install app).</p>
          <p class="small muted">${esc(appAddress())}</p><p class="small muted">On the office network or the VPN.</p></div></div>
          ${installPrompt ? `<div class="modal-footer"><button type="button" class="button primary" value="here"><span data-icon="download"></span>Install on this computer</button></div>` : ''}`}
    </form>`;
    document.body.append(dialog);
    drawIcons(dialog);
    const close = () => { dialog.close(); dialog.remove(); };
    $('button[value=close]', dialog).addEventListener('click', close);
    dialog.addEventListener('cancel', event => { event.preventDefault(); close(); });
    $('button[value=here]', dialog)?.addEventListener('click', async () => { close(); await installHere(); });
    dialog.addEventListener('click', event => { if (event.target === dialog) close(); });
    dialog.showModal();
    if (!phone) {
      try { const qr = await loadQr(); $('.install-qr', dialog).innerHTML = qr.svg(appAddress(), 200); }
      catch { $('.install-qr', dialog).textContent = 'The QR code could not be drawn.'; }
    }
  }
  installButton?.addEventListener('click', showInstall);
  if ('serviceWorker' in navigator && location.protocol !== 'file:') navigator.serviceWorker.register('sw.js').catch(() => {});

  window.WH = {tag, $, $$, esc, icon, drawIcons, api, toast, number, date, dateTime, dateInput, statusBadge, ask, showErrors, param, pager, change, upload};
})();
