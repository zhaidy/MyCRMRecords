(() => {
  'use strict';
  // deployment.aspx: which build the website runs, what the server's deployment task last found, and the change log.
  // Reads api.ashx/admin/deployment (administrators only).
  const {$, esc, api} = window.WH;
  const page = $('#page');
  const STATES = {current: 'Up to date', deploying: 'Deploying', ready: 'Update ready', waiting: 'Update waiting', failed: 'Not updated'};
  const when = iso => { const d = new Date(iso); return isNaN(d) ? '' : d.toLocaleString('en-SG', {dateStyle: 'medium', timeStyle: 'short'}); };
  const short = revision => esc((revision || '').slice(0, 7));

  async function load() {
    const d = await api('GET', 'admin/deployment');
    $('#loading').hidden = true;
    page.hidden = false;
    if (d.result && d.result.ok === false) { page.innerHTML = `<div class="empty"><h3>${esc(d.result.message)}</h3></div>`; return; }
    const {installed, status, changeLog} = d;
    const repository = changeLog && /^[\w.-]+\/[\w.-]+$/.test(changeLog.repository || '') ? changeLog.repository : null;
    const commit = revision => repository && /^[a-f0-9]{40}$/i.test(revision || '')
      ? `<a class="po-link" href="https://github.com/${repository}/commit/${revision}" target="_blank" rel="noopener">${short(revision)}</a>` : `<code>${short(revision) || 'unknown'}</code>`;
    const stale = status?.checkedUtc && (Date.now() - new Date(status.checkedUtc)) / 60000 > 10;
    const entries = changeLog?.entries || [];
    page.innerHTML = `<div class="page-heading"><div><h1>Deployment</h1><p>GitHub builds every change merged into master; the server's scheduled task installs the build.</p></div></div>
      <div class="stack">
        <section class="card"><div class="card-heading"><h2>Website</h2><span class="status ${status?.state === 'current' ? 'completed' : status?.state === 'failed' ? 'rejected' : 'approval'}">${esc(STATES[status?.state] || 'Unknown')}</span></div>
          <p class="muted">${esc(status?.message || 'The server has not reported a deployment check yet. The task "MyCRMRecords - Deploy master" writes it every minute.')}</p>
          ${stale ? `<div class="notice warn">The server has not checked for updates since ${esc(when(status.checkedUtc))}. Check that the scheduled task "MyCRMRecords - Deploy master" is enabled, and see C:\\ProgramData\\MyCRMRecordsDeploy\\deploy.log.</div>` : ''}
          <dl class="facts-list">
            <dt>Running</dt><dd>${installed ? `${Number.isInteger(changeLog?.version) ? `Version ${changeLog.version} · ` : ''}${commit(installed.revision)}${installed.buildNumber ? ` · build #${esc(installed.buildNumber)}` : ''}${installed.builtUtc ? ` · built ${esc(when(installed.builtUtc))}` : ''}` : 'Unknown (Content/deployment-version.txt is missing)'}</dd>
            ${status?.latestRevision ? `<dt>Latest master</dt><dd>${commit(status.latestRevision)}</dd>` : ''}
            ${status?.checkedUtc ? `<dt>Last checked</dt><dd>${esc(when(status.checkedUtc))}</dd>` : ''}
          </dl></section>
        <section class="card"><h2>Change log</h2>
          ${entries.length ? `<ul class="timeline" style="margin-top:12px">${entries.map(e => `<li><b>${Number.isInteger(e.version) ? `Version ${e.version}: ` : ''}${esc(e.title || '(no title)')}</b>
            <span class="when">${esc(when(e.date))}${e.author ? ' · ' + esc(e.author) : ''} · ${commit(e.revision)}${repository && Number.isInteger(e.pullRequest) ? ` · <a class="po-link" href="https://github.com/${repository}/pull/${e.pullRequest}" target="_blank" rel="noopener">#${e.pullRequest}</a>` : ''}</span>
            ${String(e.description || '').split(/\n\s*\n/).filter(p => p.trim()).map(p => `<span class="remarks">${esc(p.trim())}</span>`).join('')}</li>`).join('')}</ul>`
            : '<p class="muted">No change log in this build (bin/changelog.json is written by the GitHub build).</p>'}</section>
      </div>`;
  }
  load();
})();
