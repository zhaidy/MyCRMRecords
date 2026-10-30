(() => {
  'use strict';
  // Shared by every page: icons, the menu, talking to api.ashx, formatting, toasts and dialogs (window.WH).
  const $ = (s, root = document) => root.querySelector(s);
  const $$ = (s, root = document) => [...root.querySelectorAll(s)];
  const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'}[c]));

  const paths = {
    menu: '<path d="M4 6h16M4 12h16M4 18h16"/>',
    file: '<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5M9 13h6M9 17h4"/>',
    folder: '<path d="M3 7a2 2 0 0 1 2-2h4l2 2.5h8a2 2 0 0 1 2 2V17a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>',
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
    shield: '<path d="M12 3 4.5 6v5.5c0 4.6 3.2 8.4 7.5 9.5 4.3-1.1 7.5-4.9 7.5-9.5V6z"/><path d="m9 12 2 2 4-4"/>',
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
    briefcase: '<rect x="3" y="7" width="18" height="13" rx="2"/><path d="M9 7V5a2 2 0 0 1 2-2h2a2 2 0 0 1 2 2v2M3 13h18"/>',
    calendar: '<rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/>',
    'plus-square': '<rect x="4" y="4" width="16" height="16" rx="3"/><path d="M12 8v8M8 12h8"/>',
    'chevron-left': '<path d="M15 5l-7 7 7 7"/>',
    'chevron-right': '<path d="M9 6l6 6-6 6"/>',
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

  /* On a phone, More in the bottom bar opens the More screen over the page (AppPage.MoreSheet): the person, Install the
     app, the rest of the menu and Sign out. The ×, More again, Escape or the phone's Back close it. */
  const moreButton = $('#more-button'), moreSheet = $('#more-sheet');
  const moreOpen = () => !!moreSheet && !moreSheet.hidden;
  function setMore(open, {fromHistory = false} = {}) {
    if (!moreSheet || open === moreOpen()) return;
    moreSheet.hidden = !open;
    document.body.classList.toggle('more-open', open);
    // The sheet covers the page: what is under it cannot be reached with Tab (the bottom bar can).
    [...document.body.children].forEach(el => { if (el !== moreSheet && !el.classList.contains('tabbar') && el.tagName !== 'SCRIPT' && el.tagName !== 'DIALOG') el.inert = open; });
    moreButton?.setAttribute('aria-expanded', open);
    if (open) {
      moreSheet.scrollTop = 0;
      moreSheet.classList.remove('scrolled');
      try { history.pushState({whMore: true}, ''); } catch { /* no history: the × still closes it */ }
      $('#more-close', moreSheet)?.focus();
    } else {
      if (!fromHistory && history.state && history.state.whMore) history.back();
      moreButton?.focus();
    }
  }
  moreButton?.addEventListener('click', () => setMore(!moreOpen()));
  $('#more-close')?.addEventListener('click', () => setMore(false));
  addEventListener('popstate', () => { if (moreOpen()) setMore(false, {fromHistory: true}); });
  moreSheet?.addEventListener('scroll', () => moreSheet.classList.toggle('scrolled', moreSheet.scrollTop > 36), {passive: true});
  document.addEventListener('keydown', event => {
    if (!moreOpen()) return;
    if (event.key === 'Escape') { event.preventDefault(); setMore(false); }
  });
  // Grown wider than a phone (a tablet turned), the menu is in the top bar again.
  matchMedia('(min-width: 901px)').addEventListener?.('change', event => { if (event.matches) setMore(false); });
  const closeNavGroups = target => $$('.nav-group[open]').forEach(group => { if (!group.contains(target)) group.open = false; });
  document.addEventListener('click', event => closeNavGroups(event.target));
  document.addEventListener('keydown', event => { if (event.key === 'Escape') closeNavGroups(null); });

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
    // A glass message at the bottom: a check (or ! for a refusal), the words and OK, which closes it sooner.
    if (!el) {
      el = document.createElement('div'); el.id = 'toast'; el.className = 'toast'; el.setAttribute('role', 'status'); el.setAttribute('aria-live', 'polite');
      el.innerHTML = '<span class="toast-icon" aria-hidden="true"></span><span class="toast-text"></span><button type="button" class="toast-ok">OK</button>';
      $('.toast-ok', el).addEventListener('click', () => { clearTimeout(toastTimer); el.classList.remove('show'); });
      document.body.append(el);
    }
    $('.toast-text', el).textContent = message;
    $('.toast-icon', el).innerHTML = error ? '<b>!</b>' : icon('check');
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

  /**
   * A dialog's glass head (the design's): a round close (×) on the left, the title in the middle and the action (an
   * accent button, red for what takes something away) on the right. Used by WH.ask, CRM.dialog and the pages' dialogs.
   */
  const dialogHead = (title, {sub = '', action = '', closeLabel = 'Close', id = ''} = {}) =>
    `<div class="act-panel-head"><button type="button" class="icon-button dlg-close" value="cancel" data-close aria-label="${esc(closeLabel)}"><span data-icon="close"></span></button>
      <div class="act-panel-title"><h2${id ? ` id="${esc(id)}"` : ''}>${esc(title)}</h2>${sub ? `<span class="muted small" data-sub>${esc(sub)}</span>` : ''}</div>
      <div class="act-panel-action">${action}</div></div>`;

  /** A dialog asking to confirm; resolves true or false. With a field, resolves with its text (or null). */
  let asked = 0;
  function ask({title, text = '', ok = 'OK', danger = false, field = null, required = false, value = ''}) {
    return new Promise(resolve => {
      const dialog = document.createElement('dialog'), titleId = 'ask-title-' + (++asked);
      dialog.className = 'act-panel dlg dlg-ask';
      dialog.setAttribute('aria-labelledby', titleId);
      dialog.innerHTML = `<form method="dialog" class="act-panel-in" novalidate>${dialogHead(title, {id: titleId, closeLabel: 'Cancel',
          action: `<button type="submit" class="button ${danger ? 'danger-btn solid' : 'primary'}" value="ok">${esc(ok)}</button>`})}
        <div class="act-panel-body">${text ? `<p class="dlg-card">${esc(text)}</p>` : ''}
        ${field ? `<div class="dlg-card"><label class="field"><span class="${required ? 'required' : ''}">${esc(field)}</span><textarea rows="3" maxlength="1000">${esc(value)}</textarea><span class="field-error"></span></label></div>` : ''}</div></form>`;
      document.body.append(dialog);
      drawIcons(dialog);
      const input = $('textarea', dialog);
      const finish = answer => { dialog.close(); dialog.remove(); resolve(answer); };
      $('button[value=cancel]', dialog).addEventListener('click', () => finish(field ? null : false));
      dialog.addEventListener('cancel', event => { event.preventDefault(); finish(field ? null : false); });
      $('form', dialog).addEventListener('submit', event => {
        event.preventDefault();
        if (field && required && !input.value.trim()) { $('.field-error', dialog).textContent = 'Please fill this in.'; input.closest('.field').classList.add('invalid'); input.focus(); return; }
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

  /** A machine's coverage (machines.js, machine.js, company.js): its tag, and what it says in a few words. */
  const coverageColors = {Contract: '#0a58f0', Warranty: '#0d9488', TM: '#7c3aed', Expired: '#d9443a', None: '#8e8e96'};
  const coverageTag = m => `<span class="tag coverage" style="--tag:${coverageColors[m.coverage] || '#8e8e96'}">${esc(m.coverageName || m.coverage)}</span>`;
  function coverageText(m) {
    switch (m.coverage) {
      case 'Contract': return [m.contractNo, m.contractEnd ? 'ends ' + date(m.contractEnd) : ''].filter(Boolean).join(' · ');
      case 'Warranty': return m.warrantyEnd ? 'Ends ' + date(m.warrantyEnd) : '';
      case 'TM': return m.lastInvoice ? 'Last invoice ' + m.lastInvoice : m.lastService ? 'Last service ' + m.lastService : '';
      case 'Expired': return [m.contractNo, (m.contractEnd || m.warrantyEnd) ? 'ended ' + date(m.contractEnd || m.warrantyEnd) : ''].filter(Boolean).join(' · ');
      default: return '';
    }
  }
  const machineUrl = m => 'machine.aspx?' + new URLSearchParams(m.item ? {serial: m.serial, item: m.item} : {serial: m.serial});

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
  /** The brand mark of the top bar (an svg), for the install card. */
  const Brand = () => { const mark = $('.brand svg'); return mark ? mark.outerHTML : icon('phone-download'); };
  async function showInstall() {
    if (onPhone() && !isIos() && await installHere()) return;
    const phone = onPhone();
    const dialog = document.createElement('dialog');
    dialog.className = 'install-dialog act-panel dlg';
    dialog.setAttribute('aria-labelledby', 'install-title');
    const steps = isIos()
      ? `<ol class="install-steps"><li>Tap <span data-icon="share"></span> <strong>Share</strong> at the bottom of Safari.</li><li>Tap <span data-icon="plus-square"></span> <strong>Add to Home Screen</strong>, then <strong>Add</strong>.</li></ol>`
      : `<ol class="install-steps"><li>Open the browser's menu <strong>⋮</strong>.</li><li>Tap <strong>Install app</strong> (or <strong>Add to Home screen</strong>).</li></ol>`;
    dialog.innerHTML = `<form method="dialog" class="act-panel-in install-modal">
      ${dialogHead('Install MyCRMRecords', {id: 'install-title', action: installPrompt && !phone ? '<button type="button" class="button primary" value="here">Install on this computer</button>' : ''})}
      <div class="act-panel-body">
      ${phone ? `<div class="dlg-card install-card"><div class="install-app"><span class="install-app-icon">${Brand()}</span><span><b>MyCRMRecords</b><span class="small muted">${esc(appAddress())}</span></span></div>
          <ul class="install-points"><li>An icon on your home screen</li><li>Opens full screen, without the browser bar</li><li>Same sign-in and data as the website</li></ul>${steps}</div>`
        : `<div class="dlg-card install-qr-row"><div class="install-qr" aria-label="QR code of MyCRMRecords' address"></div><div>
          <p><strong>On your phone:</strong> scan the code with the camera, sign in, then install MyCRMRecords from the page that opens (Install app).</p>
          <p class="small muted">${esc(appAddress())}</p><p class="small muted">On the office network or the VPN.</p></div></div>`}
    </div></form>`;
    document.body.append(dialog);
    drawIcons(dialog);
    const close = () => { dialog.close(); dialog.remove(); };
    $('button[value=cancel]', dialog).addEventListener('click', close);
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
  const moreInstall = $('#more-install');
  if (moreInstall) {
    moreInstall.hidden = false;
    const showInstalled = () => {
      $('[data-install-title]', moreInstall).textContent = 'Installed';
      $('[data-install-text]', moreInstall).textContent = 'MyCRMRecords is on your home screen.';
      $('#more-install-button', moreInstall).hidden = true;
    };
    if (installed()) showInstalled();
    addEventListener('appinstalled', showInstalled);
    $('#more-install-button', moreInstall).addEventListener('click', showInstall);
  }
  if ('serviceWorker' in navigator && location.protocol !== 'file:') navigator.serviceWorker.register('sw.js').catch(() => {});

  /* Breadcrumb: the way the person came to this page, in this browser tab (sessionStorage; only page names and
     addresses, no data). A menu page starts the trail again; a page opened directly gets its usual parents. */
  const listPages = {'': 'Companies', 'default.aspx': 'Companies', 'territories.aspx': 'Territories', 'people.aspx': 'CRM users',
    'costingsheets.aspx': 'Costing sheets', 'requestforms.aspx': 'Request forms', 'files.aspx': 'Files', 'machines.aspx': 'Machines',
    'activities.aspx': 'Activities', 'projects.aspx': 'Projects', 'prospects.aspx': 'Prospects'};
  const pageKey = url => { const page = url.pathname.split('/').pop(); return (page === 'default.aspx' ? '' : page) + url.search; };
  const hereKey = pageKey(location);
  const readTrail = () => { try { return JSON.parse(sessionStorage.getItem('wh-trail') || '[]'); } catch { return []; } };
  const saveTrail = trail => { try { sessionStorage.setItem('wh-trail', JSON.stringify(trail.slice(-7))); } catch { /* no storage: the usual parents */ } };
  (() => {
    const page = location.pathname.split('/').pop();
    if (location.search.indexOf('id=') < 0 && Object.prototype.hasOwnProperty.call(listPages, page === 'default.aspx' ? '' : page))
      saveTrail([{label: listPages[page === 'default.aspx' ? '' : page], href: hereKey || './'}]);
  })();
  /** The breadcrumb of a record's page: the trail that led here (or `parents`, [[label, href]...]), then `title`. */
  function crumbs(title, parents = []) {
    let trail = readTrail();
    const at = trail.findIndex(t => t.key === hereKey || t.href === hereKey);
    let ref = null;
    try { ref = document.referrer ? new URL(document.referrer) : null; } catch { ref = null; }
    const from = ref && ref.origin === location.origin ? pageKey(ref) : null;
    if (at >= 0) trail = trail.slice(0, at);
    else {
      // A record's page by its address; a menu page by its page alone (its filters can change its address).
      const path = key => (key || '').split('?')[0].split('#')[0].replace(/^\.\/$/, '');
      const prev = from === null ? -1 : trail.findIndex(t => t.key !== undefined ? t.key === from : path(t.href) === path(from));
      trail = prev >= 0 ? trail.slice(0, prev + 1) : [];
    }
    if (!trail.length) trail = parents.map(([label, href]) => ({label, href, key: href === './' ? '' : href}));
    saveTrail(trail.concat([{label: title, href: (hereKey || './') + location.hash, key: hereKey}]));
    // The tab open on this page (company.js keeps it in the address) is where its crumb leads back to.
    if (!crumbs.watching) crumbs.watching = addEventListener('hashchange', () => {
      const now = readTrail();
      const mine = now.findIndex(t => t.key === hereKey);
      if (mine >= 0) { now[mine].href = (hereKey || './') + location.hash; saveTrail(now); }
    }) || true;
    const links = trail.map(t => `<a href="${esc(t.href)}">${esc(t.label)}</a><span class="crumb-sep" aria-hidden="true">›</span>`).join('');
    const back = trail.length ? trail[trail.length - 1] : null;
    return `<nav class="crumbs" aria-label="Breadcrumb">${back ? `<a class="crumb-back" href="${esc(back.href)}" aria-label="Back to ${esc(back.label)}"><span data-icon="arrow-left"></span>${esc(back.label)}</a>` : ''}`
      + `<span class="crumb-trail">${links}<span class="crumb-here" aria-current="page">${esc(title)}</span></span></nav>`;
  }

  /* The top bar while scrolling (the design's Liquid Glass bar): scrolling down folds the menu to the page you are on
     and shows the page's title beside it; scrolling up opens the menu again. A band blurs what passes under the bar. */
  (() => {
    const bar = $('.appbar');
    if (!bar) return;
    let lastY = scrollY, ticking = false;
    const title = document.createElement('span');
    title.className = 'appbar-title';
    title.setAttribute('aria-hidden', 'true');
    $('.appbar-right', bar)?.before(title);
    const update = () => {
      ticking = false;
      const y = scrollY, body = document.body;
      if (y > lastY + 4 && y > 90) body.classList.add('bar-compact');
      else if (y < lastY - 4 || y < 30) body.classList.remove('bar-compact');
      body.classList.toggle('bar-scrolled', y > 8);
      // On a phone the page's title shows in the bar once its large title has scrolled away.
      body.classList.toggle('bar-titled', y > 60);
      lastY = y;
      if ((body.classList.contains('bar-compact') || body.classList.contains('bar-titled')) && !title.textContent) title.textContent = pageTitle();
    };
    const pageTitle = () => {
      const h1 = $$('main h1').find(h => h.getClientRects().length);
      return h1 ? h1.textContent.trim() : '';
    };
    addEventListener('scroll', () => { if (!ticking) { ticking = true; requestAnimationFrame(update); } }, {passive: true});
    /* What the page draws after loading: its title (read again), the breadcrumb's step back (on a phone, the round back
       button at the left of the bar, AppPage: #appbar-back) and a create button that floats beside the bottom bar on a
       phone (.phone-fab: body.has-fab makes room for it). */
    const back = $('#appbar-back', bar);
    let syncing = false;
    const sync = () => {
      syncing = false;
      title.textContent = '';
      const body = document.body;
      if (body.classList.contains('bar-compact') || body.classList.contains('bar-titled')) title.textContent = pageTitle();
      // A page showing one of its sections on its own (a company's, .in-section) goes back to the page first.
      const section = $('main .in-section .section-back');
      const crumb = section || $('main .crumb-back');
      if (back) {
        back.hidden = !crumb;
        body.classList.toggle('has-back', !!crumb);
        if (crumb) {
          back.href = section ? '#' : crumb.getAttribute('href');
          back.setAttribute('aria-label', section ? 'Back to ' + section.textContent.trim() : crumb.getAttribute('aria-label') || 'Back');
        }
      }
      body.classList.toggle('has-fab', $$('main .phone-fab').some(b => !b.hidden && b.getClientRects().length > 0));
    };
    back?.addEventListener('click', event => {
      const section = $('main .in-section .section-back');
      if (section) { event.preventDefault(); section.click(); }
    });
    const schedule = () => { if (!syncing) { syncing = true; requestAnimationFrame(sync); } };
    const main = document.querySelector('main');
    if (main) new MutationObserver(schedule).observe(main, {childList: true, subtree: true, attributes: true, attributeFilter: ['class', 'hidden']});
    schedule();
    // The person's menu: name, login and Sign out behind the avatar.
    const avatar = $('#avatar-button', bar), card = $('#avatar-menu', bar);
    if (avatar && card) {
      const set = open => { card.hidden = !open; avatar.setAttribute('aria-expanded', open); };
      avatar.addEventListener('click', event => { event.stopPropagation(); set(card.hidden); });
      document.addEventListener('click', event => { if (!card.contains(event.target)) set(false); });
      document.addEventListener('keydown', event => { if (event.key === 'Escape') set(false); });
    }
  })();

  window.WH = {tag, $, $$, esc, icon, drawIcons, api, toast, number, date, dateTime, dateInput, statusBadge, ask, dialogHead, showErrors, param, pager, change, upload,
    coverageTag, coverageText, machineUrl, crumbs};
})();
