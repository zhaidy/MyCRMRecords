(() => {
  'use strict';
  // Shared by the Companies list and a company's page (window.CRM): the company form (MyCRM's CompanyDetail, Sales
  // Admins) with its group names, kept in the same dialog (add, rename, remove), and the dialog helpers of the CRM pages.
  const {$, $$, esc, api, toast, ask, showErrors, drawIcons, date, dialogHead} = window.WH;

  /** Options of a list ({code, label, active}): the active ones, and the current one even when no longer used. */
  function options(list, current, blank = '-') {
    const shown = list.filter(c => c.active !== false || c.code === current);
    return `<option value="">${esc(blank)}</option>${shown.map(c => `<option value="${esc(c.code)}"${c.code === current ? ' selected' : ''}>${esc(c.label)}</option>`).join('')}`;
  }

  /**
   * A dialog with its form (the design's glass dialog): a round close (×) on the left, the title in the middle and the
   * action on the right ([data-submit]: "Save" unless the page names it; `danger` makes it red), the form below, a
   * refused change's message ([data-notice]) and `footer` (e.g. a Delete button) under it. Returns the dialog element
   * (already open). Close with dialog.close(); Escape and the × close it too.
   */
  function dialog(title, body, {wide = false, footer = '', action = 'Save', danger = false} = {}) {
    const el = document.createElement('dialog');
    el.className = 'act-panel dlg' + (wide ? ' wide' : '');
    el.innerHTML = `<form class="act-panel-in" novalidate>${dialogHead(title, {closeLabel: 'Cancel',
        action: `<button type="submit" class="button ${danger ? 'danger-btn solid' : 'primary'}" data-submit>${esc(action)}</button>`})}
      <div class="act-panel-body">${body}
      <div class="notice error" data-notice hidden></div></div>
      ${footer ? `<div class="act-panel-foot">${footer}</div>` : ''}</form>`;
    document.body.append(el);
    $('button[value=cancel]', el).addEventListener('click', () => el.close());
    el.addEventListener('close', () => el.remove());
    drawIcons(el);
    el.showModal();
    // As before the close button came first: the first field has the focus.
    const first = $('.act-panel-body input:not([type=hidden]):not([disabled]),.act-panel-body select:not([disabled]),.act-panel-body textarea:not([disabled])', el);
    if (first) first.focus();
    return el;
  }

  /** Shows a refused change in a dialog: its message, and its fields marked. */
  function refused(el, answer) {
    const notice = $('[data-notice]', el);
    notice.hidden = false;
    notice.textContent = answer.result.message;
    if (notice.scrollIntoView) notice.scrollIntoView({block: 'nearest'});
    showErrors(el, answer.result.errors || []);
  }

  const text = (name, label, value, {required = false, max = 0, type = 'text', cls = '', hint = ''} = {}) =>
    `<label class="field ${cls}"><span class="${required ? 'required' : ''}">${esc(label)}</span><input data-field="${name}" type="${type}" value="${esc(value ?? '')}"${max ? ` maxlength="${max}"` : ''}>
      ${hint ? `<small class="muted">${esc(hint)}</small>` : ''}<span class="field-error"></span></label>`;

  /**
   * The company form (Sales Admins): a new company (company null) or a company's main fields. Resolves the saved
   * company's Id, or null when cancelled.
   */
  function companyForm(lists, company) {
    return new Promise(resolve => {
      const c = company || {isActive: true, name: '', brn: '', website: '', isChannelPartner: false, companyGroup: '', advanceCustomerType: '', companyGroupNameId: null,
        compAccountType: '', compOwnership: '', remarks: '', isCreatedByMsd: false};
      let groupNames = lists.groupNames.slice();
      let saved = null;
      const groupOptions = selected => `<option value="">-</option>${groupNames.map(g => `<option value="${g.id}"${String(g.id) === String(selected || '') ? ' selected' : ''}>${esc(g.groupName)}</option>`).join('')}
        ${selected && !groupNames.some(g => String(g.id) === String(selected)) ? `<option value="${esc(selected)}" selected>${esc(c.groupName || '#' + selected)}</option>` : ''}`;
      const el = dialog(company ? `Edit ${company.name}` : 'New company', `<div class="form-grid two">
          <div class="field wide" style="display:flex;flex-direction:row;flex-wrap:wrap;gap:8px 22px">
            <label class="check"><input type="checkbox" data-field="isActive"${c.isActive ? ' checked' : ''}>Is Active</label>
            <label class="check"><input type="checkbox" data-field="isHomeUser">Is HOME USER <span class="muted small">(the BRN may be left empty)</span></label>
            <label class="check"><input type="checkbox" data-field="isChannelPartner"${c.isChannelPartner ? ' checked' : ''}>Is Channel Partner</label>
            ${c.isCreatedByMsd ? '<span class="pill">Created by MSD</span>' : ''}</div>
          ${text('name', 'Company Name', c.name, {required: true, max: 250, cls: 'wide'})}
          ${text('brn', 'BRN', c.brn, {required: true, max: 50})}
          ${text('website', 'Website', c.website, {max: 100})}
          <label class="field"><span>Customer Type</span><select data-field="companyGroup">${options(lists.companyGroups, c.companyGroup)}</select><span class="field-error"></span></label>
          <label class="field"><span>Advance Customer Type</span><select data-field="advanceCustomerType">${options(lists.advanceCustomerTypes, c.advanceCustomerType)}</select><span class="field-error"></span></label>
          <div class="field wide"><span>Company Group Name</span><span style="display:flex;gap:6px;flex-wrap:wrap"><select data-field="companyGroupNameId" style="flex:1 1 240px">${groupOptions(c.companyGroupNameId)}</select>
            <button type="button" class="button secondary small-btn" data-group="add"><span data-icon="plus"></span>Add a group name</button>
            <button type="button" class="button secondary small-btn" data-group="rename"><span data-icon="edit"></span>Rename</button>
            <button type="button" class="icon-button small" data-group="remove" title="Remove the chosen group name" aria-label="Remove the chosen group name"><span data-icon="trash"></span></button></span>
            <span class="field-error"></span></div>
          <label class="field"><span>Account Type</span><select data-field="compAccountType">${options(lists.compAccountTypes, c.compAccountType)}</select><span class="field-error"></span></label>
          <label class="field"><span>Comp Ownership</span><select data-field="compOwnership">${options(lists.compOwnerships, c.compOwnership)}</select><span class="field-error"></span></label>
          <label class="field wide"><span>Remarks</span><textarea data-field="remarks" rows="3" maxlength="10000">${esc(c.remarks || '')}</textarea><span class="field-error"></span></label>
          ${company ? `<p class="muted small wide" style="margin:0">Created ${esc(date(company.createdDate))}${company.createdBy && company.createdBy.name ? ' by ' + esc(company.createdBy.name) : ''}${company.updatedDate ? ` · last updated ${esc(date(company.updatedDate))}${company.updatedBy && company.updatedBy.name ? ' by ' + esc(company.updatedBy.name) : ''}` : ''}</p>` : ''}
        </div>`, {wide: true});
      el.addEventListener('close', () => resolve(saved));
      const home = $('[data-field=isHomeUser]', el), brnLabel = $('[data-field=brn]', el).closest('.field').querySelector('span');
      home.addEventListener('change', () => brnLabel.classList.toggle('required', !home.checked));
      const groupSelect = $('[data-field=companyGroupNameId]', el);
      $$('[data-group]', el).forEach(button => button.addEventListener('click', async () => {
        const action = button.dataset.group, id = Number(groupSelect.value || 0), current = groupNames.find(g => g.id === id);
        if (action !== 'add' && !current) { toast('Choose a group name first.', true); return; }
        if (action === 'remove') {
          if (!await ask({title: `Remove the group name ${current.groupName}?`, text: 'It is no longer offered here (MyCRM keeps it). A group name a company still has cannot be removed.', ok: 'Remove', danger: true})) return;
          const answer = await api('POST', `groupnames/${id}/remove`, {});
          toast(answer.result.message, !answer.result.ok);
          if (!answer.result.ok) return;
          groupNames = groupNames.filter(g => g.id !== id);
          groupSelect.innerHTML = groupOptions('');
          return;
        }
        const name = await ask({title: action === 'add' ? 'Add a group name' : `Rename ${current.groupName}`, field: 'Group Name', required: true, value: action === 'add' ? '' : current.groupName, ok: 'Save'});
        if (name === null) return;
        const answer = await api('POST', `groupnames/${action === 'add' ? 0 : id}`, {groupName: name});
        toast(answer.result.message, !answer.result.ok);
        if (!answer.result.ok) return;
        const list = await api('GET', 'groupnames');
        if (list.result.ok) groupNames = list.groupNames;
        groupSelect.innerHTML = groupOptions(action === 'add' ? answer.result.id : id);
      }));
      $('form', el).addEventListener('submit', async event => {
        event.preventDefault();
        const value = name => $(`[data-field="${name}"]`, el);
        const body = {
          isActive: value('isActive').checked, isHomeUser: home.checked, isChannelPartner: value('isChannelPartner').checked, name: value('name').value,
          brn: value('brn').value, website: value('website').value, companyGroup: value('companyGroup').value, advanceCustomerType: value('advanceCustomerType').value,
          companyGroupNameId: groupSelect.value ? Number(groupSelect.value) : null, compAccountType: value('compAccountType').value, compOwnership: value('compOwnership').value,
          remarks: value('remarks').value,
        };
        const answer = await api('POST', 'companies/' + (company ? company.id : 0), body);
        if (!answer.result.ok) { refused(el, answer); return; }
        toast(answer.result.message);
        saved = answer.result.id;
        el.close();
      });
      $('[data-field=name]', el).focus();
    });
  }

  /** Asks for an Excel workbook and uploads it to file.ashx/<kind>; resolves the answer, or null when cancelled. */
  function uploadDialog(title, textHtml, kind, sample) {
    return new Promise(resolve => {
      let result = null;
      const el = dialog(title, `${textHtml}
        <p style="margin:10px 0"><a class="button secondary small-btn" href="file.ashx/sample?of=${esc(sample)}"><span data-icon="download"></span>Download a sample</a></p>
        <label class="field"><span class="required">Excel workbook (.xlsx)</span><input type="file" data-field="file" accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"><span class="field-error"></span></label>`);
      $('[data-submit]', el).textContent = 'Upload';
      el.addEventListener('close', () => resolve(result));
      $('form', el).addEventListener('submit', async event => {
        event.preventDefault();
        const file = $('[data-field=file]', el).files[0];
        if (!file) { showErrors(el, [{field: 'file', message: 'Choose the file.'}]); return; }
        $('[data-submit]', el).disabled = true;
        const answer = await window.WH.upload(kind, file);
        $('[data-submit]', el).disabled = false;
        if (answer.result && !answer.result.ok) { refused(el, answer); return; }
        result = answer;
        el.close();
      });
    });
  }

  window.CRM = {options, dialog, refused, text, companyForm, uploadDialog};
})();
