(function () {
  'use strict';

  const { LS, debounce, scrollToTop, initThemeToggle, initApiKeyModal, showToast, isTypingTarget, setInert, getSearchSkeletonHtml } = window.Jacred;

  const URL_FILTER_KEYS = ['type', 'tracker', 'voice', 'videotype', 'year', 'quality', 'season', 'refine', 'exclude'];

  const elements = {
    searchForm: null,
    searchInput: null,
    clear: null,
    submitButton: null,
    submitButtonText: null,
    validateError: null,
    loading: null,
    resultsDiv: null,
    filterToggle: null,
    filterToggleIcon: null,
    filterContainer: null,
    forcedSearch: null,
    toggleTheme: null,
    iconMoon: null,
    iconSun: null,
    backToTop: null
  };

  const PAGE_SIZE = 20;
  const API_BASE = '/api/v1.0';
  const FETCH_TIMEOUT_MS = 15000;
  const CONF_TIMEOUT_MS = 5000;

  const state = {
    isLoading: false,
    isLoadingMore: false,
    filtersVisible: false,
    allItems: [],
    filteredItems: [],
    displayCount: 0,
    currentQuery: '',
    filterRefine: '',
    filterExclude: '',
    listView: LS.get('jacredListView') === '1'
  };

  const animateResultsAfterRender = (onlyNodes) => {
    if (typeof window.jacredAnimateResults === 'function') {
      window.jacredAnimateResults(elements.resultsDiv, onlyNodes);
    }
  };

  const syncListViewUi = () => {
    document.body.classList.toggle('jr-results-list', state.listView);
    const btn = document.getElementById('toggleListView');
    if (btn) {
      btn.setAttribute('aria-pressed', String(state.listView));
      btn.title = state.listView ? 'Карточки' : 'Компактный список';
      const label = btn.querySelector('.jr-view-toggle__label');
      if (label) label.textContent = state.listView ? 'Карточки' : 'Список';
      const icon = btn.querySelector('i');
      if (icon) {
        icon.className = state.listView ? 'bi bi-grid' : 'bi bi-list-ul';
      }
    }
  };

  const toggleListView = () => {
    state.listView = !state.listView;
    LS.set('jacredListView', state.listView ? '1' : '0');
    syncListViewUi();
    if (state.filteredItems.length) {
      elements.resultsDiv.innerHTML = buildResultsFromJson(state.filteredItems, state.displayCount).html;
      animateResultsAfterRender();
    }
    if (state.currentQuery) syncUrlFromState(state.currentQuery);
  };

  const pluralResults = (n) => {
    const abs = Math.abs(n | 0);
    const mod10 = abs % 10;
    const mod100 = abs % 100;
    if (mod10 === 1 && mod100 !== 11) return 'результат';
    if (mod10 >= 2 && mod10 <= 4 && (mod100 < 10 || mod100 >= 20)) return 'результата';
    return 'результатов';
  };

  const setSearchResultsMode = (hasResults) => {
    document.body.classList.toggle('jr-has-results', !!hasResults);
  };

  const updateResultsHeader = () => {
    const header = document.getElementById('resultsHeader');
    const textEl = document.getElementById('resultsHeaderText');
    if (!header) return;
    const total = state.filteredItems.length;
    const hasResults = total > 0 && !!state.currentQuery;
    setSearchResultsMode(hasResults);
    syncListViewUi();
    if (!hasResults) {
      header.classList.add('d-none');
      if (textEl) textEl.textContent = '';
      return;
    }
    const shown = Math.min(state.displayCount, total);
    const suffix = shown < total ? ` · показано ${shown} из ${total}` : '';
    if (textEl) textEl.textContent = `${total} ${pluralResults(total)}${suffix}`;
    header.classList.remove('d-none');
  };

  const updateClearButton = () => {
    elements.clear.classList.toggle('d-none', elements.searchInput.value.length === 0);
  };

  const handleClear = () => {
    elements.searchInput.value = '';
    elements.searchInput.focus();
    updateClearButton();
    elements.validateError.classList.add('d-none');
  };

  const getActiveFilterCount = () => {
    const v = getFilterValues();
    return ['type', 'tracker', 'voice', 'videotype', 'year', 'quality', 'season', 'refine', 'exclude'].filter(k => !!v[k]).length;
  };

  const syncFilterFieldStates = () => {
    const resetBtn = document.getElementById('filterReset');
    const n = getActiveFilterCount();
    if (resetBtn) resetBtn.classList.toggle('has-active-filters', n > 0);
    if (elements.filterToggle) elements.filterToggle.classList.toggle('jr-filter-toggle--active', n > 0);
  };

  const updateFilterCountBadge = () => {
    const n = getActiveFilterCount();
    const badge = document.getElementById('filterCountBadge');
    const label = document.getElementById('filterToggleLabel');
    if (!badge || !label) return;
    badge.classList.toggle('d-none', n === 0);
    if (n > 0) {
      badge.textContent = String(n);
      label.setAttribute('aria-label', 'Дополнительные фильтры, активно ' + n);
    } else {
      label.removeAttribute('aria-label');
    }
    syncFilterFieldStates();
  };

  const toggleFilters = () => {
    state.filtersVisible = !state.filtersVisible;
    elements.filterContainer.classList.toggle('show', state.filtersVisible);
    setInert(elements.filterContainer, !state.filtersVisible);
    if (elements.filterToggleIcon) elements.filterToggleIcon.style.transform = state.filtersVisible ? 'rotate(180deg)' : 'rotate(0deg)';
    if (elements.filterToggle) elements.filterToggle.setAttribute('aria-expanded', String(state.filtersVisible));
    LS.set('jacredFiltersOpen', state.filtersVisible ? '1' : '0');
  };

  const resetFilterInputs = () => {
    const fc = elements.filterContainer || document.getElementById('filterContainer');
    if (fc) {
      fc.querySelectorAll('[name="refine"], [name="exclude"]').forEach((el) => { el.value = ''; });
      fc.querySelectorAll('select.filter-input').forEach((sel) => { sel.selectedIndex = 0; });
    }
    state.filterRefine = '';
    state.filterExclude = '';
    updateFilterCountBadge();
  };

  const syncUrlFromState = (query) => {
    const params = new URLSearchParams();
    if (query) params.set('s', query);
    const sortVal = (document.querySelector('input[name="sort"]:checked') || {}).value || 'sid';
    if (sortVal && sortVal !== 'sid') params.set('sort', sortVal);
    if (elements.forcedSearch && elements.forcedSearch.checked) params.set('exact', '1');
    if (state.listView) params.set('view', 'list');
    const f = getFilterValues();
    URL_FILTER_KEYS.forEach((key) => {
      if (f[key]) params.set(key, f[key]);
    });
    const qs = params.toString();
    const path = qs ? '/?' + qs : '/';
    try { history.replaceState({ search: query || '' }, document.title, path); } catch (_) { }
  };

  const applyFiltersFromParams = (params) => {
    const fc = elements.filterContainer;
    if (!fc) return;
    let hasFilter = false;
    URL_FILTER_KEYS.forEach((name) => {
      const val = params.get(name);
      if (!val) return;
      const el = fc.querySelector('[name="' + name + '"]');
      if (el) {
        el.value = val;
        hasFilter = true;
      }
    });
    if (hasFilter) {
      if (!state.filtersVisible) {
        state.filtersVisible = true;
        elements.filterContainer.classList.add('show');
        setInert(elements.filterContainer, false);
        if (elements.filterToggleIcon) elements.filterToggleIcon.style.transform = 'rotate(180deg)';
        if (elements.filterToggle) elements.filterToggle.setAttribute('aria-expanded', 'true');
      }
      updateFilterCountBadge();
    }
  };

  const readSearchQueryFromUrl = () => {
    const params = new URLSearchParams(window.location.search);
    return (params.get('s') || '').trim();
  };

  const setResultsDisabled = (disabled) => {
    elements.resultsDiv.style.pointerEvents = disabled ? 'none' : 'auto';
    elements.resultsDiv.setAttribute('aria-busy', disabled ? 'true' : 'false');
  };

  const escapeHtml = (str) => {
    if (str == null) return '';
    const div = document.createElement('div');
    div.textContent = str;
    return div.innerHTML;
  };

  const escapeAttr = (str) => {
    if (str == null) return '';
    return String(str).replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/'/g, '&#39;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  };

  const formatDate = (ts) => {
    if (ts == null || ts === '') return '—';
    let d;
    if (typeof ts === 'number') {
      d = new Date(ts < 1e12 ? ts * 1000 : ts);
    } else {
      d = new Date(ts);
    }
    if (isNaN(d.getTime())) return '—';
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  };

  const toTimestamp = (ts) => {
    if (ts == null || ts === '') return 0;
    if (typeof ts === 'number') {
      const d = new Date(ts < 1e12 ? ts * 1000 : ts);
      return isNaN(d.getTime()) ? 0 : d.getTime();
    }
    const d = new Date(ts);
    return isNaN(d.getTime()) ? 0 : d.getTime();
  };

  const SORT_API_MAP = Object.freeze({ sid: 'sid', size: 'size', date: 'create', update: 'update' });

  const normalizeSortParam = (val) => {
    const v = String(val || '').toLowerCase();
    if (v === 'create' || v === 'added') return 'date';
    if (v === 'pir') return 'sid';
    if (v === 'sid' || v === 'size' || v === 'date' || v === 'update') return v;
    return '';
  };

  const getSortValue = () =>
    (document.querySelector('input[name="sort"]:checked') || {}).value || 'sid';

  const sortItems = (items, sortVal) => {
    const list = Array.isArray(items) ? items.slice() : [];
    switch (sortVal) {
      case 'size':
        return list.sort((a, b) => (Number(b.size) || 0) - (Number(a.size) || 0));
      case 'date':
        return list.sort((a, b) => toTimestamp(b.createTime) - toTimestamp(a.createTime));
      case 'update':
        return list.sort((a, b) => toTimestamp(b.updateTime) - toTimestamp(a.updateTime));
      case 'sid':
      default:
        return list.sort((a, b) => (Number(b.sid) || 0) - (Number(a.sid) || 0));
    }
  };

  const applySearchSortAndFilters = () => {
    const sortVal = getSortValue();
    state.filteredItems = sortItems(applyClientFilters(state.allItems), sortVal);
  };

  const resortCurrentResults = () => {
    if (!state.allItems.length || !state.currentQuery) return;
    applySearchSortAndFilters();
    state.displayCount = Math.min(PAGE_SIZE, state.filteredItems.length);
    elements.resultsDiv.innerHTML = buildResultsFromJson(state.filteredItems, state.displayCount).html;
    updateResultsHeader();
    animateResultsAfterRender();
    syncUrlFromState(state.currentQuery);
  };

  const formatQualityLabel = (q) => {
    const n = Number(q);
    if (!n || !isFinite(n) || n < 1) return '';
    if (n === 4320) return '8K';
    if (n === 2160) return '4K';
    if (n === 1440) return '1440p';
    return n + 'p';
  };

  const qualityBadgeClass = (q) => {
    const n = Number(q);
    if (!n || !isFinite(n)) return 'quality-badge--default';
    if (n >= 2160) return 'quality-badge--4k';
    if (n >= 1080) return 'quality-badge--1080';
    if (n >= 720) return 'quality-badge--720';
    return 'quality-badge--sd';
  };

  const getFilterValues = () => {
    const c = document.getElementById('filterContainer');
    if (!c) return { type: '', tracker: '', voice: '', videotype: '', year: '', quality: '', season: '', refine: '', exclude: '' };
    const get = (name) => { const el = c.querySelector('[name="' + name + '"]'); return el && el.value ? String(el.value).trim() : ''; };
    return { type: get('type'), tracker: get('tracker'), voice: get('voice'), videotype: get('videotype'), year: get('year'), quality: get('quality'), season: get('season'), refine: get('refine'), exclude: get('exclude') };
  };

  const getCurrentTrackerFilter = () => getFilterValues().tracker;

  const buildCardHtml = (elem, activeTracker) => {
    const rawTitle = elem.title || elem.name || '';
    const title = escapeHtml(rawTitle);
    const titleTip = escapeAttr(rawTitle);
    const url = elem.url ? escapeAttr(elem.url) : '#';
    const magnetRaw = (elem.magnet || '').trim();
    const hasMagnet = !!magnetRaw;
    const magnetAttr = hasMagnet ? escapeAttr(magnetRaw) : '';
    const magnetOpenBtn = hasMagnet
      ? `<a href="${magnetAttr}" rel="noopener noreferrer" class="magneto btn-magnet" title="Открыть в торрент-клиенте" aria-label="Открыть магнит"><i class="bi bi-link-45deg"></i></a>`
      : `<button type="button" class="magneto btn-magnet" disabled title="Магнит недоступен" aria-label="Магнит недоступен"><i class="bi bi-link-45deg"></i></button>`;
    const magnetDataAttr = hasMagnet ? ` data-magnet="${magnetAttr}"` : '';
    const actionDisabled = hasMagnet ? '' : ' disabled aria-disabled="true"';
    const sizeName = escapeHtml(elem.sizeName || '—');
    const dateStr = formatDate(elem.createTime);
    const updateDateStr = elem.updateTime ? formatDate(elem.updateTime) : null;
    const sid = elem.sid != null ? elem.sid : 0;
    const pir = elem.pir != null ? elem.pir : 0;
    const tracker = (elem.tracker || '').toLowerCase();
    const icoSrc = `./img/ico/${tracker}.ico`;
    const trackerName = elem.tracker || '';
    const isActive = activeTracker && trackerName === activeTracker;
    const trackerBtnClass = isActive ? 'btn-filter-tracker tracker-btn--active' : 'btn-filter-tracker';
    const showUpdate = updateDateStr && updateDateStr !== dateStr;
    const updateChip = showUpdate
      ? `<span class="meta-chip meta-chip--update" title="Дата обновления"><i class="bi bi-arrow-repeat" aria-hidden="true"></i><span class="meta-chip__value">${updateDateStr}</span></span>`
      : '';
    const qualityLabel = formatQualityLabel(elem.quality);
    const qualityBadge = qualityLabel
      ? `<span class="quality-badge ${qualityBadgeClass(elem.quality)}" title="Качество: ${escapeAttr(String(elem.quality))}">${escapeHtml(qualityLabel)}</span>`
      : '';
    const metaPrimary = `<div class="result-meta-row result-meta-row--primary">
      <span class="result-meta-item result-meta-item--tracker"><img src="${icoSrc}" alt="" width="16" height="16" loading="lazy" onerror="this.style.display='none'"><strong>${escapeHtml(trackerName)}</strong></span>
      ${qualityBadge}
    </div>`;
    const metaSecondary = `<div class="result-meta-row result-meta-row--secondary">
      <span class="meta-chip meta-chip--size" title="Размер"><i class="bi bi-hdd"></i><span class="meta-chip__value">${sizeName}</span></span>
      <span class="meta-stat meta-stat--seeds" title="Сиды"><i class="bi bi-arrow-up"></i>${sid}</span>
      <span class="meta-stat meta-stat--peers" title="Пиры"><i class="bi bi-arrow-down"></i>${pir}</span>
      <span class="meta-chip meta-chip--date" title="Дата добавления"><i class="bi bi-calendar-plus" aria-hidden="true"></i><span class="meta-chip__value">${dateStr}</span></span>
      ${updateChip}
    </div>`;

    const titleClass = state.listView ? 'result-title line-clamp-1' : 'result-title result-title--card';
    const actionsHtml = `<div class="result-card__actions">
      ${magnetOpenBtn}
      <button type="button" class="btn-action btn-copy-magnet"${magnetDataAttr}${actionDisabled} title="Копировать магнит" aria-label="Копировать магнит"><i class="bi bi-clipboard"></i></button>
      <button type="button" class="btn-action btn-copy-hash"${magnetDataAttr}${actionDisabled} title="Копировать хеш" aria-label="Копировать хеш"><i class="bi bi-hash"></i></button>
      <button type="button" class="btn-action btn-send-to-tor"${magnetDataAttr}${actionDisabled} title="Отправить на TorrServer" aria-label="Отправить на TorrServer"><i class="bi bi-hdd-network"></i></button>
    </div>`;

    if (state.listView) {
      return `<article class="result-card result-card--list">
  <div class="result-card__head">
    <div class="result-card__title-row">
      <button type="button" class="${trackerBtnClass}" data-tracker="${escapeAttr(trackerName)}" title="${isActive ? 'Сбросить фильтр по трекеру' : 'Искать только на этом трекере'}" aria-label="Фильтр по трекеру ${escapeHtml(trackerName)}">
        <img src="${icoSrc}" alt="" width="14" height="14" loading="lazy" onerror="this.style.display='none'">
      </button>
      <a href="${url}" target="_blank" rel="noopener noreferrer" title="${titleTip}" class="${titleClass}">${title}</a>
    </div>
    <div class="result-card__meta result-card__meta--inline">${metaPrimary}${metaSecondary}</div>
    ${actionsHtml}
  </div>
</article>`;
    }

    return `<article class="result-card">
  <div class="result-card__head">
    <div class="result-card__title-row">
      <button type="button" class="${trackerBtnClass}" data-tracker="${escapeAttr(trackerName)}" title="${isActive ? 'Сбросить фильтр по трекеру' : 'Искать только на этом трекере'}" aria-label="Фильтр по трекеру ${escapeHtml(trackerName)}">
        <img src="${icoSrc}" alt="" width="14" height="14" loading="lazy" onerror="this.style.display='none'">
      </button>
      <a href="${url}" target="_blank" rel="noopener noreferrer" title="${titleTip}" class="${titleClass}">${title}</a>
    </div>
  </div>
  <div class="result-card__meta">${metaPrimary}${metaSecondary}</div>
  ${actionsHtml}
</article>`;
  };

  const getHelpHtml = () => { const t = document.getElementById('helpTemplate'); return t ? t.innerHTML : ''; };
  const getNotFoundHtml = () => { const t = document.getElementById('notfoundTemplate'); return t ? t.innerHTML : ''; };

  const clearSearchStorage = () => {
    LS.remove('search');
    LS.remove('sort');
    LS.remove('exact');
    elements.searchInput.value = '';
    const sidRadio = document.querySelector('input[type=radio][name=sort][value=sid]');
    if (sidRadio) sidRadio.checked = true;
    if (elements.forcedSearch) elements.forcedSearch.checked = false;
    resetFilterInputs();
    if (state.filtersVisible) toggleFilters();
    elements.resultsDiv.innerHTML = getHelpHtml();
    state.allItems = [];
    state.filteredItems = [];
    state.displayCount = 0;
    state.currentQuery = '';
    setSearchResultsMode(false);
    updateResultsHeader();
    updateClearButton();
    elements.validateError.classList.add('d-none');
    try { history.replaceState({}, document.title, '/'); } catch (_) { }
  };

  const buildResultsFromJson = (items, limit) => {
    if (!Array.isArray(items)) return { html: getHelpHtml() };
    if (!items.length) return { html: getNotFoundHtml() };
    const show = limit ? items.slice(0, limit) : items;
    const activeTracker = getCurrentTrackerFilter();
    return { html: show.map(e => buildCardHtml(e, activeTracker)).join('') };
  };

  const buildApiUrl = (query) => {
    const sortVal = getSortValue();
    const forcedSearch = elements.forcedSearch && elements.forcedSearch.checked;
    let url = API_BASE + '/torrents?search=' + encodeURIComponent(query) + '&sort=' + (SORT_API_MAP[sortVal] || 'sid');
    if (forcedSearch) url += '&exact=true';
    const f = getFilterValues();
    if (f.type) url += '&type=' + encodeURIComponent(f.type);
    if (f.tracker) url += '&tracker=' + encodeURIComponent(f.tracker);
    if (f.voice) url += '&voice=' + encodeURIComponent(f.voice);
    if (f.videotype) url += '&videotype=' + encodeURIComponent(f.videotype);
    if (f.year) url += '&relased=' + encodeURIComponent(f.year);
    if (f.quality) url += '&quality=' + encodeURIComponent(f.quality);
    if (f.season) url += '&season=' + encodeURIComponent(f.season);
    const apiKey = LS.get('api_key');
    if (apiKey) url += '&apikey=' + encodeURIComponent(apiKey);
    return url;
  };

  const initFilter = (items) => {
    const container = document.getElementById('filterContainer');
    if (!container || !items || !items.length) return;
    const quality = new Set(), years = new Set(), trackers = new Set(), voices = new Set(), seasons = new Set(), types = new Set();
    items.forEach(el => {
      if (el.quality) quality.add(el.quality);
      if (el.relased) years.add(el.relased);
      if (el.tracker) trackers.add(el.tracker);
      if (el.voices) el.voices.forEach(v => voices.add(v));
      if (el.seasons) el.seasons.forEach(s => seasons.add(s));
      if (el.types) el.types.forEach(t => types.add(t));
    });
    const fillSelect = (name, values) => {
      const sel = container.querySelector('[name="' + name + '"]');
      if (!sel) return;
      const current = (sel.value || '').trim();
      if (current) values.add(current);
      sel.querySelectorAll('option:not([value=""])').forEach(o => o.remove());
      Array.from(values).sort().forEach(v => {
        const opt = document.createElement('option');
        opt.value = v;
        opt.textContent = v;
        sel.appendChild(opt);
      });
      if (current) sel.value = current;
    };
    fillSelect('quality', quality);
    fillSelect('year', years);
    fillSelect('tracker', trackers);
    fillSelect('voice', voices);
    fillSelect('season', seasons);
    fillSelect('type', types);
  };

  const applyClientFilters = (items) => {
    const refine = state.filterRefine;
    const exclude = state.filterExclude;
    if (!refine && !exclude) return items;
    return items.filter(el => {
      const title = (el.title || el.name || '').toLowerCase();
      if (refine && !title.includes(refine)) return false;
      if (exclude && title.includes(exclude)) return false;
      return true;
    });
  };

  const ensureApiKey = () => new Promise((resolve, reject) => {
    const key = LS.get('api_key');
    const url = API_BASE + '/conf' + (key ? '?apikey=' + encodeURIComponent(key) : '');
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), CONF_TIMEOUT_MS);
    fetch(url, { signal: controller.signal })
      .then(r => r.json())
      .then(json => {
        clearTimeout(timeoutId);
        if (json.apikey) {
          if (key) LS.set('api_key', key);
          resolve();
        } else if (key) {
          elements.validateError.textContent = 'API ключ неверный. Введите правильный ключ.';
          elements.validateError.classList.remove('d-none');
          reject(new Error('Неверный API ключ'));
        } else {
              const modalEl = document.getElementById('apiKeyModal');
              if (modalEl && !modalEl.classList.contains('show')) document.getElementById('apiKeyBtn').click();
          reject(new Error('Требуется API ключ'));
        }
      })
      .catch(err => {
        clearTimeout(timeoutId);
        reject(new Error(err.name === 'AbortError' ? 'Истекло время ожидания ответа сервера' : 'Ошибка проверки API ключа'));
      });
  });

  const setSearchLoading = (loading) => {
    state.isLoading = loading;
    elements.loading.classList.toggle('d-none', !loading);
    elements.loading.setAttribute('aria-hidden', loading ? 'false' : 'true');
    const skeleton = document.getElementById('searchSkeleton');
    if (skeleton && loading) skeleton.innerHTML = getSearchSkeletonHtml(5);
    elements.submitButton.disabled = loading;
    elements.submitButton.setAttribute('aria-busy', loading ? 'true' : 'false');
    elements.submitButton.setAttribute('aria-label', loading ? 'Поиск...' : 'Искать');
    elements.submitButtonText.textContent = loading ? 'Поиск...' : 'Искать';
    setResultsDisabled(loading);
    if (loading) {
      elements.resultsDiv.innerHTML = '';
      elements.resultsDiv.setAttribute('aria-busy', 'true');
    } else {
      elements.resultsDiv.removeAttribute('aria-busy');
    }
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    const query = elements.searchInput.value.trim();
    if (!query) {
      elements.validateError.textContent = 'Поисковый запрос пустой!';
      elements.validateError.classList.remove('d-none');
      return;
    }
    if (state.isLoading) return;
    LS.set('search', query);
    setSearchLoading(true);
    elements.validateError.classList.add('d-none');
    try {
      await ensureApiKey();
    } catch (err) {
      elements.validateError.textContent = err.message || 'Требуется ключ API';
      elements.validateError.classList.remove('d-none');
      setSearchLoading(false);
      return;
    }
    try {
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), FETCH_TIMEOUT_MS);
      const response = await fetch(buildApiUrl(query), { signal: controller.signal });
      clearTimeout(timeoutId);
      if (response.status === 401) {
        elements.validateError.textContent = 'Требуется API ключ. Проверьте введённый ключ.';
        elements.validateError.classList.remove('d-none');
        elements.resultsDiv.innerHTML = getNotFoundHtml();
        updateResultsHeader();
        return;
      }
      if (response.status === 403) {
        elements.validateError.textContent = 'Доступ запрещён (403). Проверьте ключ API.';
        elements.validateError.classList.remove('d-none');
        elements.resultsDiv.innerHTML = getNotFoundHtml();
        updateResultsHeader();
        return;
      }
      if (response.status === 429) {
        elements.validateError.textContent = 'Слишком много запросов. Подождите несколько секунд.';
        elements.validateError.classList.remove('d-none');
        elements.resultsDiv.innerHTML = getNotFoundHtml();
        updateResultsHeader();
        return;
      }
      if (!response.ok) throw new Error('HTTP ' + response.status);
      const items = await response.json();
      state.allItems = Array.isArray(items) ? items : [];
      state.currentQuery = query;
      const f = getFilterValues();
      state.filterRefine = f.refine.toLowerCase();
      state.filterExclude = f.exclude.toLowerCase();
      initFilter(state.allItems);
      updateFilterCountBadge();
      applySearchSortAndFilters();
      state.displayCount = PAGE_SIZE;
      elements.resultsDiv.innerHTML = buildResultsFromJson(state.filteredItems, PAGE_SIZE).html;
      updateResultsHeader();
      animateResultsAfterRender();
      document.title = `Скачать торрент - ${query} | JacRed`;
      syncUrlFromState(query);
    } catch (error) {
      let msg = 'Ошибка выполнения поиска';
      if (error.name === 'AbortError') msg = 'Истекло время ожидания ответа. Попробуйте снова.';
      else if (error.message === 'Failed to fetch') msg = 'Ошибка сети. Проверьте соединение.';
      elements.validateError.textContent = msg;
      elements.validateError.classList.remove('d-none');
      elements.resultsDiv.innerHTML = getNotFoundHtml();
      updateResultsHeader();
    } finally {
      setSearchLoading(false);
    }
  };

  const loadMore = () => {
    if (!state.filteredItems.length || state.displayCount >= state.filteredItems.length || state.isLoadingMore || state.isLoading) return;
    state.isLoadingMore = true;
    const indicator = document.getElementById('loadMoreIndicator');
    if (indicator) indicator.classList.remove('d-none');

    const start = state.displayCount;
    state.displayCount = Math.min(state.displayCount + PAGE_SIZE, state.filteredItems.length);
    const newItems = state.filteredItems.slice(start, state.displayCount);
    const activeTracker = getCurrentTrackerFilter();
    elements.resultsDiv.insertAdjacentHTML('beforeend', newItems.map(e => buildCardHtml(e, activeTracker)).join(''));

    const allCards = elements.resultsDiv.querySelectorAll('.result-card');
    const newCards = Array.prototype.slice.call(allCards, start, state.displayCount);
    animateResultsAfterRender(newCards);
    updateResultsHeader();

    state.isLoadingMore = false;
    if (indicator) indicator.classList.add('d-none');
  };

  const extractInfoHash = (magnet) => {
    if (!magnet || typeof magnet !== 'string') return '';
    const m = magnet.match(/urn:btih:([a-fA-F0-9]{40}|[a-zA-Z2-7]{32}|[a-fA-F0-9]{64})/i);
    return m ? m[1].toLowerCase() : '';
  };

  const copyToClipboard = (text, btn, successHtml) => {
    if (!text || !btn) return;
    const write = () => {
      if (navigator.clipboard && navigator.clipboard.writeText) return navigator.clipboard.writeText(text);
      return new Promise((resolve, reject) => {
        const ta = document.createElement('textarea');
        ta.value = text;
        ta.style.cssText = 'position:fixed;opacity:0;top:0;left:0';
        document.body.appendChild(ta);
        ta.focus();
        ta.select();
        let ok = false;
        try { ok = document.execCommand('copy'); } finally { document.body.removeChild(ta); }
        ok ? resolve() : reject(new Error('copy failed'));
      });
    };
    write().then(() => {
      const old = btn.innerHTML;
      btn.innerHTML = successHtml || '<i class="bi bi-check-lg text-success"></i>';
      showToast('Скопировано в буфер обмена', { type: 'success', duration: 1200, replace: true });
      setTimeout(() => { btn.innerHTML = old; }, 1500);
    }).catch(() => {
      showToast('Не удалось скопировать', { type: 'error' });
    });
  };

  const handleScroll = debounce(() => {
    const scrolled = window.scrollY || document.documentElement.scrollTop;
    const visible = scrolled > 300;
    elements.backToTop.classList.toggle('visible', visible);
    elements.backToTop.tabIndex = visible ? 0 : -1;
  }, 100);

  const torBtnDefaultHtml = '<i class="bi bi-hdd-network"></i>';
  const torBtnSpinnerHtml = '<span class="jr-spinner jr-spinner--sm d-inline-block" aria-hidden="true"></span>';
  const torBtnSuccessHtml = '<i class="bi bi-check-lg"></i>';

  let openTorServerModal;

  const init = () => {
    elements.searchForm = document.getElementById('searchForm');
    elements.searchInput = document.getElementById('s');
    elements.clear = document.getElementById('clear');
    elements.submitButton = document.getElementById('submitButton');
    elements.submitButtonText = document.getElementById('submitButtonText');
    elements.validateError = document.getElementById('validateError');
    elements.loading = document.getElementById('loading');
    elements.resultsDiv = document.getElementById('resultsDiv');
    elements.filterToggle = document.getElementById('filterToggle');
    elements.filterToggleIcon = document.getElementById('filterToggleIcon');
    elements.filterContainer = document.getElementById('filterContainer');
    elements.forcedSearch = document.getElementById('forcedSearch');
    elements.toggleTheme = document.getElementById('toggleTheme');
    elements.iconMoon = document.getElementById('iconMoon');
    elements.iconSun = document.getElementById('iconSun');
    elements.backToTop = document.getElementById('backToTop');

    elements.resultsDiv.innerHTML = getHelpHtml();

    window.addEventListener('unhandledrejection', (e) => console.error('Unhandled rejection:', e.reason));

    initThemeToggle({
      toggleEl: elements.toggleTheme,
      iconMoonEl: elements.iconMoon,
      iconSunEl: elements.iconSun
    });

    elements.searchForm.addEventListener('submit', handleSubmit);
    elements.searchInput.addEventListener('input', () => { updateClearButton(); elements.validateError.classList.add('d-none'); });
    elements.searchInput.addEventListener('keydown', (e) => {
      if (e.key !== 'Enter' || e.isComposing) return;
      e.preventDefault();
      if (typeof elements.searchForm.requestSubmit === 'function') {
        elements.searchForm.requestSubmit(elements.submitButton);
      } else {
        elements.searchForm.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
      }
    });
    elements.clear.addEventListener('click', handleClear);
    elements.filterToggle.addEventListener('click', toggleFilters);

    if (LS.get('jacredFiltersOpen') === '1') {
      state.filtersVisible = true;
      elements.filterContainer.classList.add('show');
      setInert(elements.filterContainer, false);
      if (elements.filterToggleIcon) elements.filterToggleIcon.style.transform = 'rotate(180deg)';
      if (elements.filterToggle) elements.filterToggle.setAttribute('aria-expanded', 'true');
    } else {
      setInert(elements.filterContainer, true);
    }

    document.addEventListener('keydown', (e) => {
      if (e.key === '/' && !e.ctrlKey && !e.metaKey && !e.altKey && !isTypingTarget(e.target)) {
        e.preventDefault();
        elements.searchInput.focus();
        return;
      }
      if (e.key === 'Escape' && state.filtersVisible) {
        if (document.querySelector('.modal.show')) return;
        e.preventDefault();
        toggleFilters();
        elements.filterToggle?.focus();
      }
    });

    updateFilterCountBadge();
    syncListViewUi();

    document.getElementById('toggleListView')?.addEventListener('click', toggleListView);

    elements.backToTop.addEventListener('click', (e) => { e.preventDefault(); scrollToTop(); });

    const clearSearchStorageBtn = document.getElementById('clearSearchStorage');
    if (clearSearchStorageBtn) clearSearchStorageBtn.addEventListener('click', clearSearchStorage);

    initApiKeyModal({
      apiBase: API_BASE,
      modalEl: document.getElementById('apiKeyModal'),
      inputEl: document.getElementById('apiKeyInput'),
      errorEl: document.getElementById('apiKeyModalError'),
      saveEl: document.getElementById('apiKeyModalSave'),
      triggerIds: ['apiKeyBtn'],
      confTimeoutMs: CONF_TIMEOUT_MS
    });

    const torServerModalEl = document.getElementById('torServerModal');
    const torServerUrlInput = document.getElementById('torServerUrlInput');
    const torServerLoginInput = document.getElementById('torServerLoginInput');
    const torServerPasswordInput = document.getElementById('torServerPasswordInput');
    const torServerModalError = document.getElementById('torServerModalError');
    const torServerModalSave = document.getElementById('torServerModalSave');
    const torServerModal = torServerModalEl && typeof bootstrap !== 'undefined' ? new bootstrap.Modal(torServerModalEl) : null;
    let torServerModalTrigger = null;

    openTorServerModal = () => {
      if (!torServerModal || !torServerUrlInput) return;
      torServerModalTrigger = document.getElementById('torServerBtn');
      torServerUrlInput.value = LS.get('jacredTorServerUrl');
      if (torServerLoginInput) torServerLoginInput.value = LS.get('jacredTorServerLogin');
      if (torServerPasswordInput) torServerPasswordInput.value = LS.get('jacredTorServerPassword');
      torServerModalError.classList.add('d-none');
      torServerModalError.textContent = '';
      torServerModal.show();
    };

    const closeTorServerModal = () => {
      if (torServerModal) torServerModal.hide();
      if (torServerModalTrigger) { torServerModalTrigger.focus(); torServerModalTrigger = null; }
    };

    const saveTorServerFromModal = () => {
      const url = torServerUrlInput ? torServerUrlInput.value.trim() : '';
      const login = torServerLoginInput ? torServerLoginInput.value.trim() : '';
      const password = torServerPasswordInput ? torServerPasswordInput.value : '';
      if (!url) {
        if (torServerModalError) { torServerModalError.textContent = 'Введите URL сервера'; torServerModalError.classList.remove('d-none'); }
        return;
      }
      try { new URL(url); } catch (_) {
        if (torServerModalError) { torServerModalError.textContent = 'Некорректный URL'; torServerModalError.classList.remove('d-none'); }
        return;
      }
      LS.set('jacredTorServerUrl', url);
      LS.set('jacredTorServerLogin', login);
      LS.set('jacredTorServerPassword', password);
      if (torServerModalError) { torServerModalError.classList.add('d-none'); torServerModalError.textContent = ''; }
      closeTorServerModal();
    };

    document.getElementById('torServerBtn').addEventListener('click', openTorServerModal);
    if (torServerModalSave) torServerModalSave.addEventListener('click', saveTorServerFromModal);
    if (torServerModalEl) {
      torServerModalEl.querySelectorAll('[data-bs-dismiss="modal"], .btn-close').forEach((el) => {
        el.addEventListener('click', (e) => {
          e.preventDefault();
          closeTorServerModal();
        });
      });
    }

    [torServerUrlInput, torServerLoginInput, torServerPasswordInput].forEach(input => {
      if (!input) return;
      input.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') { e.preventDefault(); saveTorServerFromModal(); }
      });
    });

    const loadMoreSentinel = document.getElementById('loadMoreSentinel');
    if (loadMoreSentinel && 'IntersectionObserver' in window) {
      new IntersectionObserver((entries) => {
        entries.forEach(entry => {
          if (entry.isIntersecting && state.filteredItems.length && state.displayCount < state.filteredItems.length && !state.isLoadingMore && !state.isLoading) loadMore();
        });
      }, { rootMargin: '500px', threshold: 0 }).observe(loadMoreSentinel);
    }

    document.addEventListener('click', (e) => {
      const btn = e.target.closest('.btn-copy-magnet');
      if (!btn) return;
      e.preventDefault();
      e.stopPropagation();
      copyToClipboard(btn.getAttribute('data-magnet'), btn, '<i class="bi bi-check-lg text-success"></i>');
    });

    document.addEventListener('click', (e) => {
      const btn = e.target.closest('.btn-copy-hash');
      if (!btn) return;
      e.preventDefault();
      e.stopPropagation();
      const hash = extractInfoHash(btn.getAttribute('data-magnet'));
      if (hash) copyToClipboard(hash, btn, '<i class="bi bi-check-lg text-success"></i>');
    });

    document.addEventListener('click', async (e) => {
      const btn = e.target.closest('.btn-send-to-tor');
      if (!btn) return;
      e.preventDefault();
      e.stopPropagation();
      if (btn.disabled || btn.classList.contains('is-loading')) return;
      const magnet = btn.getAttribute('data-magnet');
      if (!magnet) return;
      const baseUrl = LS.get('jacredTorServerUrl').trim();
      if (!baseUrl) { openTorServerModal(); return; }
      const login = LS.get('jacredTorServerLogin').trim();
      const password = LS.get('jacredTorServerPassword').trim();
      const origTitle = btn.getAttribute('title');

      const setFeedback = (msg) => { btn.setAttribute('title', msg); setTimeout(() => btn.setAttribute('title', origTitle), 3000); };
      const setButtonState = (btnState) => {
        if (btnState === 'loading') {
          btn.disabled = true;
          btn.classList.add('pointer-events-none', 'is-loading');
          btn.setAttribute('aria-busy', 'true');
          btn.setAttribute('title', 'Отправка…');
          btn.innerHTML = torBtnSpinnerHtml;
        } else if (btnState === 'success') {
          btn.disabled = false;
          btn.classList.remove('pointer-events-none', 'is-loading');
          btn.setAttribute('aria-busy', 'false');
          btn.classList.add('is-sent');
          btn.innerHTML = torBtnSuccessHtml;
          setTimeout(() => { btn.classList.remove('is-sent'); btn.innerHTML = torBtnDefaultHtml; btn.setAttribute('title', origTitle); }, 1800);
        } else {
          btn.disabled = false;
          btn.classList.remove('pointer-events-none', 'is-loading');
          btn.setAttribute('aria-busy', 'false');
          btn.innerHTML = torBtnDefaultHtml;
        }
      };

      setButtonState('loading');
      let torUrl = baseUrl.replace(/\/$/, '') + '/torrents';
      let authHeader = null;
      try {
        const urlObj = new URL(baseUrl);
        if (urlObj.username || urlObj.password) {
          authHeader = 'Basic ' + btoa(decodeURIComponent(urlObj.username || '') + ':' + decodeURIComponent(urlObj.password || ''));
          torUrl = urlObj.origin.replace(/\/$/, '') + (urlObj.pathname === '/' ? '' : urlObj.pathname.replace(/\/$/, '')) + '/torrents';
        }
      } catch (_) { }
      if (!authHeader && login && password) authHeader = 'Basic ' + btoa(login + ':' + password);
      const headers = { 'Content-Type': 'application/json' };
      if (authHeader) headers['Authorization'] = authHeader;
      try {
        const res = await fetch(torUrl, { method: 'POST', headers, body: JSON.stringify({ action: 'add', link: magnet, save_to_db: true }) });
        if (res.ok) {
          setFeedback('Торрент отправлен на TorrServer');
          showToast('Торрент отправлен на TorrServer', { type: 'success' });
          setButtonState('success');
        } else {
          const text = await res.text().catch(() => '');
          if (res.status === 401) setFeedback('Ошибка авторизации: проверьте логин и пароль');
          else if (res.status === 403) setFeedback('CORS: разрешите запросы с этого сайта в настройках TorrServer');
          else setFeedback('Ошибка ' + res.status + (text ? ': ' + text.slice(0, 80) : ''));
          setButtonState('idle');
        }
      } catch (err) {
        setFeedback(err.message || 'Ошибка сети. Проверьте URL и CORS.');
        setButtonState('idle');
      }
    });

    document.addEventListener('click', (e) => {
      const trackerBtn = e.target.closest('.btn-filter-tracker');
      if (!trackerBtn) return;
      e.preventDefault();
      e.stopPropagation();
      const tracker = (trackerBtn.getAttribute('data-tracker') || '').trim();
      const sel = document.querySelector('#filterContainer select[name="tracker"]');
      if (!sel) return;
      const current = (sel.value || '').trim();
      const isSame = current && tracker && current.toLowerCase() === tracker.toLowerCase();
      if (isSame) {
        sel.value = '';
      } else {
        const opt = Array.from(sel.options).find(o => o.value.trim().toLowerCase() === tracker.toLowerCase());
        sel.value = opt ? opt.value : '';
      }
      updateFilterCountBadge();
      if (elements.searchInput.value.trim()) elements.searchForm.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    });

    const applyRefineExclude = () => {
      const f = getFilterValues();
      state.filterRefine = f.refine.toLowerCase();
      state.filterExclude = f.exclude.toLowerCase();
      updateFilterCountBadge();
      if (!state.allItems.length) return;
      applySearchSortAndFilters();
      state.displayCount = PAGE_SIZE;
      elements.resultsDiv.innerHTML = buildResultsFromJson(state.filteredItems, PAGE_SIZE).html;
      updateResultsHeader();
      animateResultsAfterRender();
      if (state.currentQuery) syncUrlFromState(state.currentQuery);
    };

    const fc = document.getElementById('filterContainer');
    if (fc) {
      fc.querySelectorAll('[name="refine"], [name="exclude"]').forEach(el => el.addEventListener('input', debounce(applyRefineExclude, 200)));
      fc.querySelectorAll('select.filter-input').forEach(sel => {
        sel.addEventListener('change', () => {
          updateFilterCountBadge();
          if (elements.searchInput.value.trim()) elements.searchForm.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
        });
      });
    }

    const resetBtn = document.getElementById('filterReset');
    if (resetBtn) {
      resetBtn.addEventListener('click', () => {
        resetFilterInputs();
        if (elements.searchInput.value.trim()) elements.searchForm.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
      });
    }

    document.querySelectorAll('input[type=radio][name=sort]').forEach(radio => {
      radio.addEventListener('change', function () {
        LS.set('sort', this.value);
        if (state.allItems.length && state.currentQuery) {
          resortCurrentResults();
          return;
        }
        if (elements.searchInput.value.trim()) {
          elements.searchForm.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
        }
      });
    });

    if (elements.forcedSearch) {
      elements.forcedSearch.addEventListener('change', function () {
        LS.set('exact', this.checked ? '1' : '0');
        if (elements.searchInput.value.trim()) elements.searchForm.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
      });
    }

    window.addEventListener('scroll', handleScroll, { passive: true });
    updateClearButton();

    const urlParams = new URLSearchParams(window.location.search);
    const hasUrlSearch = urlParams.has('s');
    applyFiltersFromParams(urlParams);

    let savedSort = '';
    if (urlParams.has('sort')) {
      savedSort = normalizeSortParam(urlParams.get('sort'));
    } else if (!hasUrlSearch) {
      savedSort = normalizeSortParam(LS.get('sort'));
    }
    if (savedSort) {
      const radio = document.querySelector('input[type=radio][name=sort][value="' + savedSort + '"]');
      if (radio) radio.checked = true;
    }

    if (urlParams.has('exact')) {
      if (elements.forcedSearch) elements.forcedSearch.checked = urlParams.get('exact') === '1';
    } else if (!hasUrlSearch && LS.get('exact') === '1') {
      if (elements.forcedSearch) elements.forcedSearch.checked = true;
    }

    if (urlParams.has('view')) {
      const viewParam = urlParams.get('view');
      if (viewParam === 'list') state.listView = true;
      else if (viewParam === 'cards') state.listView = false;
    } else if (!hasUrlSearch) {
      state.listView = LS.get('jacredListView') === '1';
    } else {
      state.listView = false;
    }
    syncListViewUi();

    const queryFromUrl = readSearchQueryFromUrl();
    if (queryFromUrl) {
      elements.searchInput.value = queryFromUrl;
      updateClearButton();
      elements.searchForm.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    } else {
      const savedSearch = LS.get('search').trim();
      if (savedSearch) {
        elements.searchInput.value = savedSearch;
        updateClearButton();
      }
    }

  };

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();
})();
