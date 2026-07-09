(function (global) {
  'use strict';

  const getByPath = (obj, path) => {
    if (!obj || !path) return undefined;
    const parts = path.split('.');
    let cur = obj;
    for (const p of parts) {
      if (cur == null) return undefined;
      cur = cur[p];
    }
    return cur;
  };

  const setByPath = (obj, path, value) => {
    const parts = path.split('.');
    let cur = obj;
    for (let i = 0; i < parts.length - 1; i++) {
      const p = parts[i];
      if (cur[p] == null || typeof cur[p] !== 'object') cur[p] = {};
      cur = cur[p];
    }
    cur[parts[parts.length - 1]] = value;
  };

  const escapeHtml = (str) => {
    if (str == null) return '';
    return String(str)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  };

  const deepClone = (obj) => JSON.parse(JSON.stringify(obj ?? {}));

  /** STJ used to serialize JObject as [] — ignore scalar arrays from stale/corrupt API payloads. */
  const normalizeRaw = (raw, field) => {
    if (raw == null) return raw;
    if (!Array.isArray(raw)) return raw;
    if (field.type === 'stringList' || field.type === 'json') return raw;
    return field.type === 'bool' ? false : field.type === 'int' ? 0 : '';
  };

  const stringListToText = (val) => {
    if (!Array.isArray(val)) return '';
    return val.join('\n');
  };

  const textToStringList = (text) =>
    String(text || '')
      .split('\n')
      .map((s) => s.trim())
      .filter(Boolean);

  const parseFieldValue = (field, el) => {
    if (!el) return undefined;
    switch (field.type) {
      case 'bool':
        return el.checked;
      case 'int': {
        const n = parseInt(el.value, 10);
        return Number.isFinite(n) ? n : 0;
      }
      case 'stringList':
        return textToStringList(el.value);
      case 'json':
        try {
          const parsed = JSON.parse(el.value || 'null');
          return parsed;
        } catch (_) {
          return el.dataset.lastValid ? JSON.parse(el.dataset.lastValid) : null;
        }
      case 'select':
        if (field.key === 'tracksmod') {
          const n = parseInt(el.value, 10);
          return Number.isFinite(n) ? n : 0;
        }
        return el.value;
      case 'string':
      case 'password':
      default:
        return el.value;
    }
  };

  const renderField = (field, data, prefix) => {
    const path = prefix ? `${prefix}.${field.key}` : field.key;
    const id = `cfg-${path.replace(/[^a-zA-Z0-9_-]/g, '_')}`;
    const raw = normalizeRaw(getByPath(data, path), field);
    const wrap = document.createElement('div');
    wrap.className = 'col-md-6 col-lg-4 mb-3 jr-settings-field';
    if (field.sensitive) wrap.classList.add('jr-settings-field--secret');
    wrap.dataset.fieldPath = path;

    let controlHtml = '';
    const desc = field.description
      ? `<div class="form-text">${escapeHtml(field.description)}</div>`
      : '';

    if (field.type === 'bool') {
      controlHtml =
        `<div class="form-check form-switch jr-settings-switch mt-1">` +
        `<input class="form-check-input" type="checkbox" role="switch" id="${id}" ${raw ? 'checked' : ''}>` +
        `<label class="form-check-label" for="${id}">${escapeHtml(field.label)}</label>` +
        `</div>${desc}`;
      wrap.className = 'col-md-6 col-lg-4 mb-3 jr-settings-field jr-settings-field--bool d-flex flex-column justify-content-end';
      wrap.innerHTML = controlHtml;
      return wrap;
    }

    let inputValue = raw;
    if (field.type === 'stringList') inputValue = stringListToText(raw);
    if (field.type === 'json') {
      inputValue = raw != null ? JSON.stringify(raw, null, 2) : (field.key.endsWith('.categories') ? '{}' : '[]');
      wrap.className = 'col-12 mb-3 jr-settings-field jr-settings-field--json';
    }

    let inputHtml = '';
    if (field.type === 'stringList' || field.type === 'json') {
      const rows = field.type === 'json' ? 6 : 4;
      inputHtml =
        `<textarea class="form-control form-control-sm font-monospace" id="${id}" rows="${rows}"` +
        ` spellcheck="false">${escapeHtml(inputValue ?? '')}</textarea>`;
    } else if (field.type === 'select') {
      const opts = (field.enumValues || []).map((v) => {
        const sel = String(raw ?? '') === String(v) ? ' selected' : '';
        return `<option value="${escapeHtml(v)}"${sel}>${escapeHtml(v)}</option>`;
      }).join('');
      inputHtml = `<select class="form-select form-select-sm" id="${id}">${opts}</select>`;
    } else if (field.type === 'int') {
      const min = field.min != null ? ` min="${field.min}"` : '';
      const max = field.max != null ? ` max="${field.max}"` : '';
      const numVal = raw != null && raw !== '' ? raw : '';
      inputHtml =
        `<input type="number" class="form-control form-control-sm" id="${id}"` +
        ` value="${escapeHtml(numVal)}"${min}${max}>`;
    } else if (field.type === 'password') {
      inputHtml =
        `<input type="text" class="form-control form-control-sm font-monospace" id="${id}"` +
        ` value="${escapeHtml(inputValue ?? '')}" autocomplete="off">`;
    } else {
      inputHtml =
        `<input type="text" class="form-control form-control-sm" id="${id}"` +
        ` value="${escapeHtml(raw ?? '')}">`;
    }

    wrap.innerHTML =
      `<label class="form-label jr-label mb-1" for="${id}">${escapeHtml(field.label)}</label>` +
      inputHtml + desc;
    return wrap;
  };

  const renderCheckboxList = (field, data) => {
    const path = field.key;
    const raw = getByPath(data, path);
    const selected = new Set(Array.isArray(raw) ? raw : []);
    const wrap = document.createElement('div');
    wrap.className = 'col-12 mb-3 jr-settings-field jr-settings-field--checkboxes';
    if (path === 'synctrackers') wrap.classList.add('jr-settings-field--sync-trackers');
    if (path === 'disable_trackers') wrap.classList.add('jr-settings-field--disable-trackers');
    wrap.dataset.fieldPath = path;

    const items = (field.enumValues || []).map((slug) => {
      const id = `cfg-${path}-${slug}`;
      const checked = selected.has(slug) ? ' checked' : '';
      return (
        `<div class="col-6 col-md-4 col-lg-3">` +
        `<label class="form-check jr-settings-checkbox-tile">` +
        `<input class="form-check-input" type="checkbox" id="${id}" value="${escapeHtml(slug)}"${checked}>` +
        `<span class="form-check-label">${escapeHtml(slug)}</span>` +
        `</label></div>`
      );
    }).join('');

    wrap.innerHTML =
      `<div class="jr-settings-checkbox-head">` +
      `<label class="form-label jr-label mb-0">${escapeHtml(field.label)}</label>` +
      `<span class="jr-settings-checkbox-count small text-secondary">${selected.size} выбрано</span>` +
      `</div>` +
      (field.description ? `<div class="form-text mb-2">${escapeHtml(field.description)}</div>` : '') +
      `<div class="row g-2 jr-settings-checkbox-grid">${items}</div>`;

    wrap.addEventListener('change', () => {
      const count = wrap.querySelectorAll('input[type="checkbox"]:checked').length;
      const countEl = wrap.querySelector('.jr-settings-checkbox-count');
      if (countEl) countEl.textContent = `${count} выбрано`;
    });

    return wrap;
  };

  const collectCheckboxList = (container, path) => {
    const values = [];
    container.querySelectorAll(`[data-field-path="${path}"] input[type="checkbox"]:checked`).forEach((el) => {
      values.push(el.value);
    });
    return values.sort((a, b) => a.localeCompare(b, 'ru'));
  };

  const groupIcons = {
    server: 'bi-hdd-network',
    api: 'bi-plug',
    sync: 'bi-arrow-repeat',
    logging: 'bi-journal-text',
    tracks: 'bi-soundwave',
    fdb: 'bi-database',
    evercache: 'bi-lightning-charge',
    search: 'bi-search',
    proxy: 'bi-shield'
  };

  const resolveActiveTab = (schema, activeTabId) => {
    if (!activeTabId || !schema?.groups) return null;
    const valid = new Set(schema.groups.map((g) => (g.id === 'trackers' ? 'tab-trackers' : `tab-${g.id}`)));
    return valid.has(activeTabId) ? activeTabId : null;
  };

  const buildForm = (schema, data, rootEl, options = {}) => {
    if (!rootEl || !schema?.groups) return;
    rootEl.innerHTML = '';

    const activeTabId = resolveActiveTab(schema, options.activeTabId);

    const navWrap = document.createElement('div');
    navWrap.className = 'jr-settings-nav-wrap';
    const nav = document.createElement('div');
    nav.className = 'nav nav-pills jr-settings-nav';
    nav.setAttribute('role', 'tablist');
    navWrap.appendChild(nav);

    const content = document.createElement('div');
    content.className = 'tab-content jr-settings-tab-content';

    schema.groups.forEach((group, idx) => {
      const tabId = group.id === 'trackers' ? 'tab-trackers' : `tab-${group.id}`;
      const isActive = activeTabId ? tabId === activeTabId : idx === 0;

      if (group.id === 'trackers') {
        nav.insertAdjacentHTML('beforeend',
          `<button class="nav-link${isActive ? ' active' : ''}" id="${tabId}-btn" data-bs-toggle="pill"` +
          ` data-bs-target="#${tabId}" type="button" role="tab"><i class="bi bi-collection me-1" aria-hidden="true"></i>${escapeHtml(group.title)}</button>`);

        const pane = document.createElement('div');
        pane.className = `tab-pane fade jr-settings-section${isActive ? ' show active' : ''}`;
        pane.id = tabId;
        pane.setAttribute('role', 'tabpanel');

        const toolbar = document.createElement('div');
        toolbar.className = 'jr-settings-trackers-toolbar';
        toolbar.innerHTML =
          `<div class="jr-search-wrap jr-settings-tracker-search">` +
          `<i class="bi bi-search jr-search-icon" aria-hidden="true"></i>` +
          `<input type="search" class="form-control form-control-sm jr-search-input" id="trackerSearchInput"` +
          ` placeholder="Поиск трекера…" autocomplete="off" aria-label="Поиск трекера">` +
          `</div>` +
          `<span class="small text-secondary jr-settings-trackers-count"></span>`;
        pane.appendChild(toolbar);

        const accordion = document.createElement('div');
        accordion.className = 'accordion jr-settings-trackers';
        accordion.id = 'trackersAccordion';

        (group.trackers || []).forEach((tracker) => {
          const collapseId = `tracker-${tracker.id}`;
          const host = getByPath(data, `${tracker.id}.host`);
          const logOn = getByPath(data, `${tracker.id}.log`);
          const item = document.createElement('div');
          item.className = 'accordion-item jr-settings-tracker-item';
          item.dataset.trackerName = tracker.title.toLowerCase();
          item.dataset.trackerHost = (host || '').toLowerCase();
          item.innerHTML =
            `<h3 class="accordion-header">` +
            `<button class="accordion-button collapsed" type="button"` +
            ` data-bs-toggle="collapse" data-bs-target="#${collapseId}" aria-expanded="false">` +
            `<span class="jr-settings-tracker-title">${escapeHtml(tracker.title)}</span>` +
            (host ? `<span class="jr-settings-tracker-host">${escapeHtml(host)}</span>` : '') +
            (logOn ? `<span class="jr-settings-tracker-badge"><i class="bi bi-journal-text" aria-hidden="true"></i> log</span>` : '') +
            `</button></h3>` +
            `<div id="${collapseId}" class="accordion-collapse collapse"` +
            ` data-bs-parent="#trackersAccordion">` +
            `<div class="accordion-body"><div class="row g-3 fields-row"></div></div></div>`;

          const row = item.querySelector('.fields-row');
          (tracker.fields || []).forEach((field) => {
            row.appendChild(renderField(field, data, tracker.id));
          });
          accordion.appendChild(item);
        });

        const countEl = toolbar.querySelector('.jr-settings-trackers-count');
        const updateCount = () => {
          const visible = accordion.querySelectorAll('.jr-settings-tracker-item:not(.jr-settings-tracker-item--hidden)').length;
          const total = group.trackers?.length || 0;
          countEl.textContent = visible === total ? `${total} трекеров` : `${visible} из ${total}`;
        };
        updateCount();

        const searchInput = toolbar.querySelector('#trackerSearchInput');
        searchInput?.addEventListener('input', () => {
          const q = searchInput.value.trim().toLowerCase();
          accordion.querySelectorAll('.jr-settings-tracker-item').forEach((el) => {
            const match = !q || el.dataset.trackerName.includes(q) || el.dataset.trackerHost.includes(q);
            el.classList.toggle('jr-settings-tracker-item--hidden', !match);
          });
          updateCount();
        });

        pane.appendChild(accordion);
        content.appendChild(pane);
        return;
      }

      const icon = groupIcons[group.id] || 'bi-sliders';
      nav.insertAdjacentHTML('beforeend',
        `<button class="nav-link${isActive ? ' active' : ''}" id="${tabId}-btn" data-bs-toggle="pill"` +
        ` data-bs-target="#${tabId}" type="button" role="tab"><i class="bi ${icon} me-1" aria-hidden="true"></i>${escapeHtml(group.title)}</button>`);

      const pane = document.createElement('div');
      pane.className = `tab-pane fade jr-settings-section${isActive ? ' show active' : ''}`;
      pane.id = tabId;
      pane.setAttribute('role', 'tabpanel');

      if (group.description) {
        const intro = document.createElement('p');
        intro.className = 'jr-settings-section-desc';
        intro.textContent = group.description;
        pane.appendChild(intro);
      }

      const card = document.createElement('div');
      card.className = 'jr-settings-fields-card';
      const row = document.createElement('div');
      row.className = 'row g-3 fields-row';

      (group.fields || []).forEach((field) => {
        if (field.type === 'stringList' && field.enumValues?.length && (field.key === 'synctrackers' || field.key === 'disable_trackers')) {
          row.appendChild(renderCheckboxList(field, data));
        } else {
          row.appendChild(renderField(field, data, ''));
        }
      });

      card.appendChild(row);
      pane.appendChild(card);
      content.appendChild(pane);
    });

    rootEl.appendChild(navWrap);
    rootEl.appendChild(content);
  };

  const collectFormData = (schema, baseData, rootEl) => {
    const result = deepClone(baseData);

    // Repair nested objects if a scalar was corrupted to [] (legacy STJ bug).
    const ensureObjectPath = (obj, path) => {
      const parts = path.split('.');
      if (parts.length < 2) return;
      let cur = obj;
      for (let i = 0; i < parts.length - 1; i++) {
        const p = parts[i];
        if (cur[p] == null || typeof cur[p] !== 'object' || Array.isArray(cur[p])) {
          cur[p] = {};
        }
        cur = cur[p];
      }
    };

    const applyField = (path, field, el) => {
      let val = parseFieldValue(field, el);
      if (path.includes('.')) ensureObjectPath(result, path);
      setByPath(result, path, val);
    };

    rootEl.querySelectorAll('[data-field-path]').forEach((wrap) => {
      const path = wrap.dataset.fieldPath;
      if (path === 'synctrackers' || path === 'disable_trackers') {
        setByPath(result, path, collectCheckboxList(rootEl, path));
        return;
      }
      const input = wrap.querySelector('input:not([type="checkbox"]), select, textarea') ||
        wrap.querySelector('input[type="checkbox"]');
      if (!input) return;
      const fieldMeta = findFieldMeta(schema, path);
      applyField(path, fieldMeta || { type: input.type === 'checkbox' ? 'bool' : 'string' }, input);
    });

    return result;
  };

  const findFieldMeta = (schema, path) => {
    if (!schema?.groups) return null;
    for (const group of schema.groups) {
      if (group.id === 'trackers') {
        const [trackerName, ...rest] = path.split('.');
        const tracker = (group.trackers || []).find((t) => t.id === trackerName);
        if (tracker && rest.length) {
          return (tracker.fields || []).find((f) => f.key === rest.join('.')) || null;
        }
      } else {
        const field = (group.fields || []).find((f) => f.key === path);
        if (field) return field;
      }
    }
    return null;
  };

  global.JacredSettingsForm = Object.freeze({
    buildForm,
    collectFormData,
    deepClone,
    getByPath
  });
})(window);
