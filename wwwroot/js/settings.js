(function () {
  'use strict';

  const {
    LS,
    initThemeToggle,
    showToast,
    withDevKeyHeaders,
    withApiKeyHeaders
  } = window.Jacred;

  const { buildForm, collectFormData, deepClone } = window.JacredSettingsForm;

  const API_URL = '/api/v1.0/config';
  const FETCH_TIMEOUT_MS = 15000;
  const LS_FORM_TAB = 'jacredSettingsFormTab';
  const LS_MODE_TAB = 'jacredSettingsMode';

  const els = {
    accessDenied: document.getElementById('accessDenied'),
    settingsPanel: document.getElementById('settingsPanel'),
    settingsLoading: document.getElementById('settingsLoading'),
    configFormRoot: document.getElementById('configFormRoot'),
    configEditor: document.getElementById('configEditor'),
    formatSelect: document.getElementById('formatSelect'),
    configMeta: document.getElementById('configMeta'),
    validationResult: document.getElementById('validationResult'),
    reloadBtn: document.getElementById('reloadBtn'),
    validateBtn: document.getElementById('validateBtn'),
    formatBtn: document.getElementById('formatBtn'),
    formatRawBtn: document.getElementById('formatRawBtn'),
    saveBtn: document.getElementById('saveBtn'),
    devKeyBtn: document.getElementById('devKeyBtn'),
    devKeyInput: document.getElementById('devKeyInput'),
    devKeyForm: document.getElementById('devKeyForm'),
    devKeyModalError: document.getElementById('devKeyModalError'),
    devKeyModalSave: document.getElementById('devKeyModalSave'),
    devKeyModal: document.getElementById('devKeyModal'),
    accessDeniedMessage: document.getElementById('accessDeniedMessage'),
    accessDeniedDevKeyBtn: document.getElementById('accessDeniedDevKeyBtn'),
    modeFormTab: document.getElementById('modeFormTab'),
    modeRawTab: document.getElementById('modeRawTab'),
    diffModal: document.getElementById('diffModal'),
    diffTableBody: document.getElementById('diffTableBody'),
    diffSummary: document.getElementById('diffSummary'),
    diffValidation: document.getElementById('diffValidation'),
    diffEmpty: document.getElementById('diffEmpty'),
    diffConfirmBtn: document.getElementById('diffConfirmBtn'),
    unsavedBadge: document.getElementById('unsavedBadge')
  };

  let schema = null;
  let baseData = null;
  let activeFormat = 'yaml';
  let dirty = false;
  let diffModalInstance = null;
  let pendingSavePayload = null;
  let devKeyModalInstance = null;

  const ACCESS_MESSAGES = {
    network: 'Редактирование конфигурации доступно только из локальной сети сервера. Откройте эту страницу по адресу сервера (например, http://127.0.0.1:9117/settings) или через VPN.',
    devkey: 'Требуется dev-ключ из init.yaml. Укажите его в настройках браузера — кнопка «Dev ключ» в шапке или ниже.',
    apikey: 'Требуется API-ключ. Задайте его на главной странице (кнопка «API ключ» в шапке), затем обновите настройки.'
  };

  const withConfigHeaders = (options = {}) => withApiKeyHeaders(withDevKeyHeaders(options));

  const isAuthError = (status) => status === 403 || status === 401;

  const authErrorKind = (status) => {
    if (status === 403) return 'network';
    if (status === 401) {
      return LS.get('dev_key') ? 'apikey' : 'devkey';
    }
    return 'network';
  };

  const setDirty = (value) => {
    dirty = value;
    els.unsavedBadge?.classList.toggle('d-none', !dirty);
    document.body.classList.toggle('jr-settings-dirty', dirty);
  };

  const isFormMode = () => els.modeFormTab?.classList.contains('active');

  const escapeHtml = (str) => {
    if (str == null) return '';
    return String(str)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  };

  const apiFetch = (url, options = {}) => {
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), FETCH_TIMEOUT_MS);
    return fetch(url, withConfigHeaders({ ...options, signal: controller.signal }))
      .then(async (response) => {
        clearTimeout(timeoutId);
        if (isAuthError(response.status)) {
          return { response, json: null, authError: authErrorKind(response.status) };
        }
        const ct = response.headers.get('content-type') || '';
        if (!ct.includes('application/json')) {
          const text = await response.text();
          throw new Error(response.ok ? 'Ответ сервера не JSON' : (text || `HTTP ${response.status}`));
        }
        const json = await response.json();
        return { response, json, authError: null };
      })
      .catch((err) => {
        clearTimeout(timeoutId);
        throw err;
      });
  };

  const handleAuthError = (kind, { promptDevKey = false } = {}) => {
    showAccessDenied(kind);
    if (promptDevKey && kind === 'devkey') openDevKeyModal();
  };

  const getPayload = () => {
    if (isFormMode()) {
      return { data: collectFormData(schema, baseData, els.configFormRoot) };
    }
    return {
      content: els.configEditor?.value || '',
      format: els.formatSelect?.value || activeFormat
    };
  };

  const getOutputFormat = () => els.formatSelect?.value || activeFormat;

  /** Always send structured data so save/diff use AppInit normalization and formatted output. */
  const normalizePayload = (payload) => {
    const format = payload.format || getOutputFormat();
    if (payload.data) {
      return Promise.resolve({ data: payload.data, format });
    }
    return apiFetch(`${API_URL}/parse`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify({ content: payload.content || '', format })
    }).then(({ response, json, authError }) => {
      if (authError) {
        handleAuthError(authError);
        throw new Error('Доступ запрещён');
      }
      if (!json.ok) throw new Error(json.error || 'Не удалось разобрать конфигурацию');
      return { data: json.data, format };
    });
  };

  const applyFormattedConfig = (json) => {
    if (json.data) baseData = deepClone(json.data);
    if (els.configEditor) els.configEditor.value = json.content || '';
    renderForm();
    setDirty(true);
  };

  const showRawTab = () => {
    if (!els.modeRawTab || typeof bootstrap === 'undefined') return;
    bootstrap.Tab.getOrCreateInstance(els.modeRawTab).show();
  };

  const formatConfig = (options = {}) => {
    const { switchToRaw = false } = options;
    setLoading(true);
    return normalizePayload(getPayload())
      .then((payload) => apiFetch(`${API_URL}/format`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify(payload)
      }))
      .then(({ response, json, authError }) => {
        if (authError) {
          handleAuthError(authError);
          return;
        }
        if (!json.ok) throw new Error(json.error || 'Ошибка форматирования');
        applyFormattedConfig(json);
        if (switchToRaw || isFormMode()) showRawTab();
        showToast('Конфигурация отформатирована', { type: 'success' });
      })
      .catch((err) => {
        showToast(err.message || 'Ошибка форматирования', { type: 'error' });
      })
      .finally(() => setLoading(false));
  };

  const setLoading = (state) => {
    const panelVisible = els.settingsPanel && !els.settingsPanel.classList.contains('d-none');
    if (els.settingsLoading) {
      els.settingsLoading.classList.toggle('d-none', !state || panelVisible);
    }
    els.settingsPanel?.classList.toggle('jr-settings-panel--loading', state && panelVisible);
    if (els.reloadBtn) els.reloadBtn.disabled = state;
    if (els.validateBtn) els.validateBtn.disabled = state;
    if (els.formatBtn) els.formatBtn.disabled = state;
    if (els.formatRawBtn) els.formatRawBtn.disabled = state;
    if (els.saveBtn) els.saveBtn.disabled = state;
  };

  const showAccessDenied = (kind = 'network') => {
    if (els.accessDeniedMessage) {
      els.accessDeniedMessage.textContent = ACCESS_MESSAGES[kind] || ACCESS_MESSAGES.network;
    }
    els.accessDeniedDevKeyBtn?.classList.toggle('d-none', kind !== 'devkey');
    els.devKeyBtn?.classList.toggle('jr-nav-needs-key', kind === 'devkey');
    els.accessDenied?.classList.remove('d-none');
    els.settingsPanel?.classList.add('d-none');
    els.settingsLoading?.classList.add('d-none');
  };

  const showEditor = () => {
    els.accessDenied?.classList.add('d-none');
    els.devKeyBtn?.classList.remove('jr-nav-needs-key');
    els.settingsPanel?.classList.remove('d-none');
    els.settingsLoading?.classList.add('d-none');
  };

  const formatDate = (iso) => {
    if (!iso) return '—';
    try { return new Date(iso).toLocaleString('ru-RU'); } catch (_) { return iso; }
  };

  const renderMeta = (data) => {
    if (!els.configMeta) return;
    const chips = [];
    if (data.path) {
      chips.push(
        `<span class="jr-settings-meta-chip"><i class="bi bi-file-earmark-code" aria-hidden="true"></i>` +
        `Файл <code>${escapeHtml(data.path)}</code></span>`
      );
    }
    if (data.format) {
      chips.push(
        `<span class="jr-settings-meta-chip"><i class="bi bi-braces" aria-hidden="true"></i>` +
        `${escapeHtml(String(data.format).toUpperCase())}</span>`
      );
    }
    if (data.lastModifiedUtc) {
      chips.push(
        `<span class="jr-settings-meta-chip"><i class="bi bi-clock" aria-hidden="true"></i>` +
        `${escapeHtml(formatDate(data.lastModifiedUtc))}</span>`
      );
    }
    els.configMeta.innerHTML = chips.join('');
  };

  const renderValidation = (result) => {
    if (!els.validationResult) return;
    if (!result) { els.validationResult.innerHTML = ''; return; }

    const errors = Array.isArray(result.errors) ? result.errors : [];
    const warnings = Array.isArray(result.warnings) ? result.warnings : [];

    if (result.error && errors.length === 0) {
      els.validationResult.innerHTML =
        `<div class="alert alert-danger mb-0 py-2 small">${escapeHtml(result.error)}</div>`;
      return;
    }

    if (errors.length) {
      const list = errors.map((e) => `<li>${escapeHtml(e)}</li>`).join('');
      els.validationResult.innerHTML =
        `<div class="alert alert-danger mb-0 py-2 small"><strong>Ошибки:</strong><ul class="mb-0 mt-1">${list}</ul></div>`;
      return;
    }

    if (result.ok && !warnings.length) {
      els.validationResult.innerHTML =
        '<div class="alert alert-success mb-0 py-2 small">Конфигурация корректна</div>';
      return;
    }

    const list = warnings.map((w) => `<li>${escapeHtml(w)}</li>`).join('');
    els.validationResult.innerHTML =
      `<div class="alert alert-warning mb-0 py-2 small"><strong>Есть предупреждения:</strong><ul class="mb-0 mt-1">${list}</ul></div>`;
  };

  const restoreModeTab = () => {
    if (LS.get(LS_MODE_TAB) !== 'raw' || !els.modeRawTab || typeof bootstrap === 'undefined') return;
    bootstrap.Tab.getOrCreateInstance(els.modeRawTab).show();
  };

  const renderForm = () => {
    if (!schema || !baseData || !els.configFormRoot) return;
    buildForm(schema, baseData, els.configFormRoot, { activeTabId: LS.get(LS_FORM_TAB) });
    els.configFormRoot.querySelectorAll('input, select, textarea').forEach((el) => {
      el.addEventListener('input', () => { setDirty(true); });
      el.addEventListener('change', () => { setDirty(true); });
    });
  };

  const loadConfig = () => {
    if (dirty && !window.confirm('Есть несохранённые изменения. Перезагрузить?')) return;

    setLoading(true);
    renderValidation(null);

    apiFetch(API_URL)
      .then(({ response, json, authError }) => {
        if (authError) {
          handleAuthError(authError, { promptDevKey: true });
          return;
        }
        if (!json.ok) throw new Error(json.error || 'Не удалось загрузить конфигурацию');

        showEditor();
        schema = json.schema;
        baseData = deepClone(json.data);
        activeFormat = json.format || 'yaml';
        if (els.formatSelect) els.formatSelect.value = activeFormat;
        if (els.configEditor) els.configEditor.value = json.content || '';
        renderForm();
        renderMeta(json);
        setDirty(false);
        restoreModeTab();
      })
      .catch((err) => {
        if (err?.message === 'Доступ запрещён') return;
        showAccessDenied('network');
        showToast(err?.name === 'AbortError' ? 'Истекло время ожидания' : (err.message || 'Ошибка загрузки'), { type: 'error' });
      })
      .finally(() => setLoading(false));
  };

  const validateConfig = () => {
    setLoading(true);
    normalizePayload(getPayload())
      .then((payload) => apiFetch(`${API_URL}/validate`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify(payload)
      }))
      .then(({ response, json, authError }) => {
        if (authError) { handleAuthError(authError); return; }
        renderValidation(json);
        showToast(json.ok ? 'Проверка пройдена' : (json.error || 'Ошибка валидации'), { type: json.ok ? 'success' : 'error' });
      })
      .catch((err) => showToast(err.message || 'Ошибка проверки', { type: 'error' }))
      .finally(() => setLoading(false));
  };

  const renderDiffModal = (diffResult) => {
    const diffs = diffResult.diffs || [];
    const validation = diffResult.validation || {};

    if (els.diffSummary) {
      els.diffSummary.textContent = diffs.length
        ? `Будет изменено полей: ${diffs.length}`
        : 'Изменений не обнаружено';
    }

    if (els.diffValidation) {
      if (!validation.ok && (validation.errors?.length || validation.error)) {
        const errs = validation.errors?.length ? validation.errors : [validation.error];
        els.diffValidation.innerHTML =
          `<div class="alert alert-danger py-2 small mb-0"><strong>Ошибки валидации:</strong> ${escapeHtml(errs.join('; '))}</div>`;
      } else if (validation.warnings?.length) {
        els.diffValidation.innerHTML =
          `<div class="alert alert-warning py-2 small mb-0">${escapeHtml(validation.warnings.join('; '))}</div>`;
      } else {
        els.diffValidation.innerHTML = '';
      }
    }

    if (els.diffTableBody) {
      els.diffTableBody.innerHTML = diffs.map((d) =>
        `<tr class="jr-diff-row jr-diff-row--${escapeHtml(d.change || 'changed')}">` +
        `<td><code class="small">${escapeHtml(d.path)}</code></td>` +
        `<td class="small font-monospace">${escapeHtml(d.oldValue ?? '—')}</td>` +
        `<td class="small font-monospace">${escapeHtml(d.newValue ?? '—')}</td>` +
        `</tr>`
      ).join('');
    }

    els.diffEmpty?.classList.toggle('d-none', diffs.length > 0);
    if (els.diffConfirmBtn) {
      els.diffConfirmBtn.disabled = !validation.ok;
    }
  };

  const performSave = (payload) => {
    setLoading(true);
    apiFetch(API_URL, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify(payload)
    })
      .then(({ response, json, authError }) => {
        if (authError) { handleAuthError(authError); return; }
        if (!json.ok) {
          renderValidation({ error: json.error });
          showToast(json.error || 'Ошибка сохранения', { type: 'error' });
          return;
        }
        setDirty(false);
        diffModalInstance?.hide();
        showToast('Конфигурация сохранена', { type: 'success' });
        loadConfig();
      })
      .catch(() => showToast('Ошибка сохранения', { type: 'error' }))
      .finally(() => setLoading(false));
  };

  const saveConfig = () => {
    setLoading(true);

    normalizePayload(getPayload())
      .then((payload) => apiFetch(`${API_URL}/diff`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify(payload)
      })
        .then(({ response, json, authError }) => {
          if (authError) { handleAuthError(authError); return; }
          if (!json.ok) {
            renderValidation({ error: json.error });
            showToast(json.error || 'Ошибка diff', { type: 'error' });
            return;
          }
          pendingSavePayload = payload;
          renderDiffModal(json);
          if (!diffModalInstance && els.diffModal) {
            diffModalInstance = new bootstrap.Modal(els.diffModal);
          }
          diffModalInstance?.show();
        }))
      .catch((err) => showToast(err.message || 'Ошибка подготовки diff', { type: 'error' }))
      .finally(() => setLoading(false));
  };

  const openDevKeyModal = () => {
    if (!devKeyModalInstance || !els.devKeyModal) return;
    if (els.devKeyInput) els.devKeyInput.value = LS.get('dev_key');
    if (els.devKeyModalError) {
      els.devKeyModalError.classList.add('d-none');
      els.devKeyModalError.textContent = '';
    }
    devKeyModalInstance.show();
    window.requestAnimationFrame(() => els.devKeyInput?.focus());
  };

  const saveDevKey = () => {
    const key = (els.devKeyInput?.value || '').trim();
    if (!key) {
      if (els.devKeyModalError) {
        els.devKeyModalError.textContent = 'Введите dev-ключ';
        els.devKeyModalError.classList.remove('d-none');
      }
      return;
    }
    LS.set('dev_key', key);
    devKeyModalInstance?.hide();
    showToast('Dev ключ сохранён', { type: 'success' });
    loadConfig();
  };

  const initDevKeyModal = () => {
    if (!els.devKeyModal || typeof bootstrap === 'undefined') return;
    devKeyModalInstance = new bootstrap.Modal(els.devKeyModal);
    els.devKeyBtn?.addEventListener('click', openDevKeyModal);
    els.accessDeniedDevKeyBtn?.addEventListener('click', openDevKeyModal);
    els.devKeyForm?.addEventListener('submit', (e) => {
      e.preventDefault();
      saveDevKey();
    });
    els.devKeyModalSave?.addEventListener('click', (e) => {
      e.preventDefault();
      saveDevKey();
    });
  };

  const syncFormToRaw = () => {
    if (!schema || !els.configEditor) return Promise.resolve();
    const data = collectFormData(schema, baseData, els.configFormRoot);
    const format = els.formatSelect?.value || activeFormat;
    return apiFetch(`${API_URL}/render`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify({ data, format })
    })
      .then(({ response, json, authError }) => {
        if (authError) { handleAuthError(authError); return; }
        if (!json.ok) throw new Error(json.error || 'Не удалось сериализовать конфиг');
        els.configEditor.value = json.content || '';
      });
  };

  const syncRawToForm = () => {
    if (!schema || !els.configEditor) return Promise.resolve();
    const content = els.configEditor.value || '';
    const format = els.formatSelect?.value || activeFormat;
    return apiFetch(`${API_URL}/parse`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify({ content, format })
    })
      .then(({ response, json, authError }) => {
        if (authError) { handleAuthError(authError); return; }
        if (!json.ok) throw new Error(json.error || 'Не удалось разобрать конфиг');
        baseData = deepClone(json.data);
        renderForm();
      });
  };

  const initUi = () => {
    els.reloadBtn?.addEventListener('click', loadConfig);
    els.validateBtn?.addEventListener('click', validateConfig);
    els.formatBtn?.addEventListener('click', () => formatConfig({ switchToRaw: true }));
    els.formatRawBtn?.addEventListener('click', () => formatConfig());
    els.saveBtn?.addEventListener('click', saveConfig);
    els.diffConfirmBtn?.addEventListener('click', () => {
      if (pendingSavePayload) performSave(pendingSavePayload);
    });
    els.configEditor?.addEventListener('input', () => { setDirty(true); });
    els.formatSelect?.addEventListener('change', () => {
      setDirty(true);
      if (!els.modeRawTab?.classList.contains('active')) return;
      syncFormToRaw().catch((err) => {
        showToast(err.message || 'Ошибка обновления редактора', { type: 'error' });
      });
    });

    els.modeRawTab?.addEventListener('shown.bs.tab', () => {
      LS.set(LS_MODE_TAB, 'raw');
      syncFormToRaw().catch((err) => {
        showToast(err.message || 'Ошибка синхронизации с редактором', { type: 'error' });
      });
    });
    els.modeFormTab?.addEventListener('shown.bs.tab', () => {
      LS.set(LS_MODE_TAB, 'form');
      syncRawToForm().catch((err) => {
        showToast(err.message || 'Ошибка синхронизации с формой', { type: 'error' });
      });
    });

    els.configFormRoot?.addEventListener('shown.bs.tab', (e) => {
      const btn = e.target?.closest?.('.jr-settings-nav .nav-link');
      if (!btn) return;
      const tabId = btn.getAttribute('data-bs-target')?.replace('#', '');
      if (tabId) LS.set(LS_FORM_TAB, tabId);
    });

    window.addEventListener('beforeunload', (e) => {
      if (!dirty) return;
      e.preventDefault();
      e.returnValue = '';
    });
  };

  initThemeToggle({
    toggleEl: document.getElementById('toggleTheme'),
    iconMoonEl: document.getElementById('iconMoon'),
    iconSunEl: document.getElementById('iconSun')
  });

  initDevKeyModal();
  initUi();
  loadConfig();
})();
