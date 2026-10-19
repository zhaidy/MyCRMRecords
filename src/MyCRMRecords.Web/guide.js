(() => {
  'use strict';
  // The user guide: docs/user-guide.md, which guide.aspx writes into the page (#guide-source). On guide.aspx it is the
  // whole page, with its contents and a search box; on every other page the ? button in the top bar opens it in a panel
  // beside the page, at the section its link names (guide.aspx#low-stock), without leaving the page.
  // The Markdown is the plain subset the guide uses: headings, paragraphs, lists, tables, quotes, bold, italics, code
  // and links. Everything is escaped, so the guide can never add HTML of its own.
  const $ = (s, root = document) => root.querySelector(s);
  const $$ = (s, root = document) => [...root.querySelectorAll(s)];
  const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  // The icons are common.js's (window.WH), loaded before this script on every page.
  const icon = name => window.WH.icon(name);

  // ---- Markdown ------------------------------------------------------------
  // A heading's id is the one GitHub gives it, so guide.aspx#low-stock opens the same section here and on GitHub.
  // UserGuideTests checks the links in the pages with the same rule.
  const slug = text => text.trim().toLowerCase().replace(/[^\p{L}\p{M}\p{N}\p{Pc}\- ]/gu, '').replace(/ /g, '-');
  const plain = text => text.replace(/\[([^\]]*)\]\([^)]*\)/g, '$1').replace(/[*`]/g, '');
  // Links may only go to a part of the guide, a MyCRMRecords page, a website or an email address.
  const safeUrl = url => /^(#|https?:|mailto:)/i.test(url) || !/^[a-z][a-z0-9+.-]*:/i.test(url) ? url : null;

  function inline(text) {
    const kept = [];
    const keep = html => { kept.push(html); return `\u0000${kept.length - 1}\u0000`; };
    let s = text.replace(/\\([\\`*_[\]|#>-])/g, (_, c) => keep(esc(c)))
      .replace(/`([^`]+)`/g, (_, code) => keep(`<code>${esc(code)}</code>`));
    s = esc(s)
      .replace(/\[([^\]]+)\]\(([^)\s]+)\)/g, (match, label, url) => { const href = safeUrl(url); return href ? `<a href="${href}">${label}</a>` : label; })
      .replace(/\*\*(?=\S)(.+?)\*\*/g, '<strong>$1</strong>')
      .replace(/(^|[^\w*])\*(?=\S)(.+?)\*(?![\w*])/g, '$1<em>$2</em>')
      .replace(/(^|[^\w])_(?=\S)(.+?)_(?!\w)/g, '$1<em>$2</em>');
    return s.replace(/\u0000(\d+)\u0000/g, (_, i) => kept[Number(i)]);
  }

  const indentOf = line => line.match(/^ */)[0].length;
  const listItem = /^( *)([-*+]|\d{1,9}[.)]) +(.*)$/;
  const tableRow = /^ *\|/;
  const tableRule = /^ *\|? *:?-{3,}:? *(\| *:?-{3,}:? *)*\|? *$/;
  const rule = /^ {0,3}([-*_])( *\1){2,} *$/;
  const fence = /^ {0,3}```/;
  const heading = /^ {0,3}(#{1,6}) +(.*?)(?: +#+)? *$/;
  // Lines that end a paragraph without a blank line before them.
  const startsBlock = line => heading.test(line) || fence.test(line) || rule.test(line) || /^ {0,3}>/.test(line) ||
    /^ {0,3}([-*+]|1[.)]) +\S/.test(line) || tableRow.test(line);
  const cells = line => line.trim().replace(/^\|/, '').replace(/(^|[^\\])\|$/, '$1').split(/(?<!\\)\|/).map(c => c.trim());

  // Blocks of Markdown as HTML. headings collects every heading (with its id) for the contents and the sections; ids
  // are the ids given so far, with the prefix the panel puts before them (so they never clash with the page's own).
  function blocks(lines, headings, ids) {
    let html = '', i = 0;
    while (i < lines.length) {
      const line = lines[i];
      if (!line.trim()) { i++; continue; }
      let m;
      if ((m = line.match(heading))) {
        const level = m[1].length, text = m[2], base = slug(plain(text));
        let id = base;
        for (let n = 1; ids.has(id); n++) id = `${base}-${n}`;
        ids.add(id);
        headings.push({level, id, text: plain(text)});
        // The page and the panel show their own title, so the guide's title (#) is not repeated.
        if (level > 1) html += `<h${level} id="${esc(ids.prefix + id)}">${inline(text)}</h${level}>`;
        i++;
      } else if (fence.test(line)) {
        const code = [];
        for (i++; i < lines.length && !fence.test(lines[i]); i++) code.push(lines[i]);
        html += `<pre><code>${esc(code.join('\n'))}</code></pre>`;
        i++;
      } else if (rule.test(line)) {
        html += '<hr>'; i++;
      } else if (/^ {0,3}>/.test(line)) {
        const quote = [];
        for (; i < lines.length && lines[i].trim() && (/^ {0,3}>/.test(lines[i]) || !startsBlock(lines[i])); i++) quote.push(lines[i].replace(/^ {0,3}> ?/, ''));
        html += `<blockquote>${blocks(quote, headings, ids)}</blockquote>`;
      } else if (tableRow.test(line) && i + 1 < lines.length && tableRule.test(lines[i + 1])) {
        const head = cells(line), align = cells(lines[i + 1]).map(c => c.endsWith(':') ? (c.startsWith(':') ? 'center' : 'right') : '');
        const cell = (tag, text, n) => `<${tag}${align[n] ? ` style="text-align:${align[n]}"` : ''}>${inline(text)}</${tag}>`;
        let rows = '';
        for (i += 2; i < lines.length && tableRow.test(lines[i]); i++) rows += `<tr>${head.map((_, n) => cell('td', cells(lines[i])[n] ?? '', n)).join('')}</tr>`;
        html += `<div class="guide-table"><table><thead><tr>${head.map((text, n) => cell('th', text, n)).join('')}</tr></thead><tbody>${rows}</tbody></table></div>`;
      } else if ((m = line.match(listItem))) {
        i = list(lines, i, headings, ids, out => { html += out; });
      } else {
        const text = [line.trim()];
        for (i++; i < lines.length && lines[i].trim() && !startsBlock(lines[i]); i++) text.push(lines[i].trim());
        html += `<p>${inline(text.join(' '))}</p>`;
      }
    }
    return html;
  }

  // A list from line i: its items, each with the lines indented under it (more paragraphs, or a list inside it).
  function list(lines, i, headings, ids, write) {
    const first = lines[i].match(listItem), base = first[1].length, ordered = /\d/.test(first[2]), items = [];
    let current = null, contentIndent = 0;
    while (i < lines.length) {
      const line = lines[i], m = line.match(listItem);
      if (!line.trim()) {
        let next = i + 1;
        while (next < lines.length && !lines[next].trim()) next++;
        if (next >= lines.length) break;
        const after = lines[next], am = after.match(listItem);
        if (am && am[1].length === base && /\d/.test(am[2]) === ordered) { i = next; continue; }
        if (current && indentOf(after) >= contentIndent) { current.push(''); i = next; continue; }
        break;
      }
      if (m && m[1].length < base + 2) {
        if (/\d/.test(m[2]) !== ordered || m[1].length < base) break;
        current = [m[3]]; items.push(current);
        contentIndent = m[1].length + m[2].length + 1;
        i++; continue;
      }
      // Anything else under the item belongs to it, unless it starts a block of its own at the list's level.
      if (indentOf(line) < contentIndent && startsBlock(line)) break;
      current.push(line.slice(Math.min(indentOf(line), contentIndent)));
      i++;
    }
    const start = ordered && Number(first[2].slice(0, -1)) !== 1 ? ` start="${Number(first[2].slice(0, -1))}"` : '';
    const body = items.map(item => {
      const inner = blocks(item, headings, ids);
      // A list of single paragraphs stays compact.
      return `<li>${/^<p>((?!<p>)[\s\S])*<\/p>$/.test(inner) ? inner.slice(3, -4) : inner}</li>`;
    }).join('');
    write(ordered ? `<ol${start}>${body}</ol>` : `<ul>${body}</ul>`);
    return i;
  }

  // The guide as HTML: every ## heading opens a section, and every ### heading a part of it, so a search can show only
  // the parts that match.
  function render(markdown, prefix = '') {
    const headings = [], ids = Object.assign(new Set(), {prefix});
    const lines = markdown.replace(/\r\n?/g, '\n').split('\n');
    const parts = [];
    let part = {level: 0, lines: []};
    let inFence = false;
    for (const line of lines) {
      if (fence.test(line)) inFence = !inFence;
      const m = !inFence && line.match(heading);
      if (m && (m[1].length === 2 || m[1].length === 3)) { parts.push(part); part = {level: m[1].length, lines: []}; }
      part.lines.push(line);
    }
    parts.push(part);
    let html = '', open = false;
    for (const p of parts) {
      const inner = blocks(p.lines, headings, ids);
      if (p.level === 2 || (p.level === 0 && inner)) {
        if (open) html += '</section>';
        html += `<section class="guide-section"><div class="guide-part guide-intro">${inner}</div>`;
        open = true;
      } else if (p.level === 3) {
        if (!open) { html += '<section class="guide-section">'; open = true; }
        html += `<div class="guide-part">${inner}</div>`;
      }
    }
    if (open) html += '</section>';
    return {html, headings};
  }

  // ---- Search --------------------------------------------------------------
  const termsOf = query => query.toLowerCase().split(/\s+/).filter(t => t.length > 1);
  const matches = (el, terms) => { const text = el.textContent.toLowerCase(); return terms.every(t => text.includes(t)); };
  // Shows the sections and parts that hold every word searched for (a section whose own text matches shows whole), and
  // marks the words. Returns how many parts hold them.
  function filter(article, query) {
    const terms = termsOf(query);
    let shown = 0;
    $$('.guide-section', article).forEach(section => {
      const parts = $$('.guide-part', section), found = parts.map(part => !terms.length || matches(part, terms));
      // The section's opening part (its heading) always shows with a part that matches.
      parts.forEach((part, n) => { part.hidden = !(found[0] || found[n] || n === 0); });
      section.hidden = !found.some(Boolean);
      shown += found.filter(Boolean).length;
    });
    if (terms.length) mark(article, terms);
    return shown;
  }
  function mark(article, terms) {
    const pattern = new RegExp(terms.map(t => t.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|'), 'gi');
    const walker = document.createTreeWalker(article, NodeFilter.SHOW_TEXT, {acceptNode: node =>
      node.parentElement.closest('[hidden]') || !node.nodeValue.trim() ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT});
    const nodes = [];
    while (walker.nextNode()) nodes.push(walker.currentNode);
    nodes.forEach(node => {
      const text = node.nodeValue;
      pattern.lastIndex = 0;
      if (!pattern.test(text)) return;
      const fragment = document.createDocumentFragment();
      let last = 0;
      text.replace(pattern, (found, at) => {
        fragment.append(text.slice(last, at));
        const el = document.createElement('mark'); el.textContent = found; fragment.append(el);
        last = at + found.length;
      });
      fragment.append(text.slice(last));
      node.replaceWith(fragment);
    });
  }
  const searchNote = (query, shown) => !termsOf(query).length ? '' : shown
    ? `${shown} ${shown === 1 ? 'part' : 'parts'} of the guide ${shown === 1 ? 'mentions' : 'mention'} “${query.trim()}”.`
    : `Nothing in the guide mentions “${query.trim()}”. Try another word.`;

  // Searches as the person types: the guide is drawn again each time, so earlier marks go away.
  function searchable(input, note, article, html, after) {
    let timer;
    const run = () => {
      article.innerHTML = html;
      const shown = filter(article, input.value);
      note.textContent = searchNote(input.value, shown);
      note.hidden = !note.textContent;
      after?.(input.value);
    };
    input.addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(run, 120); });
    input.addEventListener('keydown', event => { if (event.key === 'Escape' && input.value) { event.stopPropagation(); input.value = ''; run(); } });
    return {clear() { if (input.value) { input.value = ''; run(); } }};
  }

  // ---- guide.aspx ----------------------------------------------------------
  const page = $('#guide-page');
  if (page) {
    const article = $('#guide-body'), toc = $('#guide-toc'), contents = $('#guide-contents'), input = $('#guide-search');
    const {html, headings} = render($('#guide-source').textContent);
    article.innerHTML = html;
    const tocItems = headings.filter(h => h.level === 2 || h.level === 3);
    toc.innerHTML = `<ol>${tocItems.map(h => `<li class="toc-${h.level}"><a href="#${esc(h.id)}">${esc(h.text)}</a></li>`).join('')}</ol>`;
    const narrow = matchMedia('(max-width: 900px)');
    if (narrow.matches) contents.open = false;

    // The contents follow the search: only the sections and parts shown are listed.
    const search = searchable(input, $('#guide-search-status'), article, html, () => {
      $$('a', toc).forEach(a => { const target = document.getElementById(a.hash.slice(1)); a.parentElement.hidden = !target || !!target.closest('[hidden]'); });
      highlight();
    });

    // The section being read is highlighted in the contents.
    function highlight() {
      let current = null;
      for (const h of $$('h2, h3', article)) {
        if (h.closest('[hidden]')) continue;
        if (h.getBoundingClientRect().top > 140) break;
        current = h.id;
      }
      current ??= $$('h2, h3', article).find(h => !h.closest('[hidden]'))?.id;
      $$('a', toc).forEach(a => {
        const on = a.hash === `#${current}`;
        on ? a.setAttribute('aria-current', 'true') : a.removeAttribute('aria-current');
        // Keeps it in sight when the contents scroll on their own (never the page itself).
        if (on) { const box = toc.getBoundingClientRect(), at = a.getBoundingClientRect(); if (at.top < box.top || at.bottom > box.bottom) toc.scrollTop += at.top - box.top - box.height / 2; }
      });
    }
    let frame = 0;
    addEventListener('scroll', () => { cancelAnimationFrame(frame); frame = requestAnimationFrame(highlight); }, {passive: true});

    // Opening a section (from the contents, a link in the guide, or the address) clears a search that hides it.
    function go(id, smooth) {
      let target = document.getElementById(id);
      if (target?.closest('[hidden]')) { search.clear(); target = document.getElementById(id); }
      if (!target) return;
      target.scrollIntoView({behavior: smooth ? 'smooth' : 'auto', block: 'start'});
      if (narrow.matches) contents.open = false;
      highlight();
    }
    document.addEventListener('click', event => {
      const link = event.target.closest('a[href^="#"]');
      if (!link || !(toc.contains(link) || article.contains(link)) || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey) return;
      event.preventDefault();
      history.pushState(null, '', link.hash);
      go(link.hash.slice(1), true);
    });
    addEventListener('hashchange', () => go(decodeURIComponent(location.hash.slice(1))));
    // The ? button of this page goes back to the top.
    $('#guide-button')?.addEventListener('click', event => { event.preventDefault(); history.pushState(null, '', location.pathname); scrollTo({top: 0, behavior: 'smooth'}); });
    document.addEventListener('keydown', event => {
      if (event.key === '/' && !['INPUT', 'TEXTAREA', 'SELECT'].includes(document.activeElement.tagName)) { event.preventDefault(); input.focus(); }
    });
    if (location.hash.length > 1) go(decodeURIComponent(location.hash.slice(1)));
    highlight();
    return;
  }

  // ---- The panel, on every other page --------------------------------------
  const button = $('#guide-button');
  if (!button) return;
  let panel = null, guide = null, search = null;
  // The panel's headings have ids of their own, so they never clash with the page's.
  const PREFIX = 'guide-';
  const find = id => id ? document.getElementById(PREFIX + id) : null;

  // The guide comes from guide.aspx, once per page.
  async function load() {
    if (guide) return guide;
    const response = await fetch('guide.aspx', {cache: 'no-store'});
    const source = response.ok && new DOMParser().parseFromString(await response.text(), 'text/html').getElementById('guide-source');
    if (!source) throw new Error(response.redirected ? 'Please sign in again to read the guide.' : 'The guide could not be loaded.');
    return (guide = render(source.textContent, PREFIX));
  }

  function create() {
    panel = document.createElement('aside');
    panel.className = 'guide-panel';
    panel.id = 'guide-panel';
    panel.hidden = true;
    panel.tabIndex = -1;
    panel.setAttribute('aria-labelledby', 'guide-panel-title');
    panel.innerHTML = `<div class="guide-panel-head"><h2 id="guide-panel-title">User guide</h2><a class="button secondary small-btn" id="guide-panel-page" href="guide.aspx">Open as a page${icon('arrow-up-right')}</a><button type="button" class="icon-button" id="guide-panel-close" aria-label="Close the user guide" title="Close">${icon('close')}</button></div>
      <div class="search-box">${icon('search')}<input type="search" id="guide-panel-search" placeholder="Search the guide…" aria-label="Search the guide"></div>
      <p class="guide-search-status" id="guide-panel-status" aria-live="polite" hidden></p>
      <div class="guide-panel-body" id="guide-panel-body"><article class="guide" id="guide-panel-article"><p class="guide-loading">Loading the guide…</p></article></div>`;
    document.body.append(panel);
    button.setAttribute('aria-controls', panel.id);
    $('#guide-panel-close').addEventListener('click', () => close(true));
    // Links to another part of the guide scroll the panel; they never change this page's address (#tab matters on
    // the admin page).
    panel.addEventListener('click', event => {
      const link = event.target.closest('a[href^="#"]');
      if (!link || !panel.contains(link)) return;
      event.preventDefault();
      show(decodeURIComponent(link.hash.slice(1)));
    });
    panel.addEventListener('keydown', event => { if (event.key === 'Escape') { event.stopPropagation(); close(true); } });
  }

  // Scrolls the panel to a section (its id in the guide), clearing a search that hides it.
  function show(id) {
    const body = $('#guide-panel-body');
    let target = find(id);
    if (target?.closest('[hidden]')) { search.clear(); target = find(id); }
    body.scrollTop = target ? target.getBoundingClientRect().top - body.getBoundingClientRect().top + body.scrollTop - 4 : 0;
    $('#guide-panel-page').href = 'guide.aspx' + (target ? '#' + id : '');
  }

  async function open() {
    if (!panel) create();
    panel.hidden = false;
    button.setAttribute('aria-expanded', 'true');
    panel.focus();
    const article = $('#guide-panel-article'), id = decodeURIComponent(new URL(button.href, location.href).hash.slice(1));
    try {
      const {html} = await load();
      if (!search) {
        article.innerHTML = html;
        search = searchable($('#guide-panel-search'), $('#guide-panel-status'), article, html);
      }
      show(id);
    } catch (error) {
      article.innerHTML = `<p class="guide-loading">${esc(error.message)} <a href="${esc(button.getAttribute('href'))}">Open the guide as a page</a></p>`;
    }
  }
  function close(focus) {
    if (!panel || panel.hidden) return;
    panel.hidden = true;
    button.setAttribute('aria-expanded', 'false');
    if (focus) button.focus();
  }

  button.setAttribute('aria-expanded', 'false');
  // A plain click opens the panel; a click with Ctrl or the middle button still opens the guide as a page.
  button.addEventListener('click', event => {
    if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    panel && !panel.hidden ? close(false) : open();
  });
})();
