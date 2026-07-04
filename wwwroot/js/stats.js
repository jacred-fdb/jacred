(function () {
  'use strict';

  const { LS, debounce, throttle, scrollToTop, initThemeToggle, initApiKeyModal, fetchWithApiKey, showToast, isTypingTarget, getStatsSkeletonHtml, getSafeIconPath } = window.Jacred;

  const API_BASE = '/api/v1.0';
  const CONF_TIMEOUT_MS = 5000;

  let openApiKeyModalRef = null;

  const CONFIG = Object.freeze({
    API_URL: '/stats/torrents',
    LAST_UPDATE_URL: '/lastupdatedb',
    DEBOUNCE_DELAY: 300,
    RETRY_ATTEMPTS: 3,
    RETRY_DELAY: 1000,
    FETCH_TIMEOUT_MS: 10000,
    LAST_UPDATE_TIMEOUT_MS: 5000,
    PAGE_SIZE: 50,
    BACK_TO_TOP_THRESHOLD: 300,
    VIEW_BREAKPOINT: 768
  });

  const isDesktopViewport = () =>
    window.matchMedia(`(min-width: ${CONFIG.VIEW_BREAKPOINT}px)`).matches;

  const TRACKER_LABELS = Object.freeze({
    anidub: 'AniDub',
    aniliberty: 'AniLiberty',
    animelayer: 'AnimeLayer',
    baibako: 'Baibako',
    bitru: 'BitRu',
    hdrezka: 'HDRezka',
    kinozal: 'Kinozal',
    knaben: 'Knaben',
    lostfilm: 'LostFilm',
    mazepa: 'Mazepa',
    megapeer: 'Megapeer',
    nnmclub: 'NNM-Club',
    rutor: 'RuTor',
    rutracker: 'RuTracker',
    selezen: 'Selezen',
    toloka: 'Toloka',
    torrentby: 'Torrent.by'
  });

  const getTrackerDisplayName = (slug) => {
    const key = String(slug || '').toLowerCase();
    if (!key) return '—';
    return TRACKER_LABELS[key] || key.charAt(0).toUpperCase() + key.slice(1);
  };

  const formatTrackerSlug = (slug) => String(slug || '').toLowerCase() || '—';

  const pluralize = (n, one, few, many) => {
    const num = Number(n) || 0;
    const abs = Math.abs(num | 0);
    const mod10 = abs % 10;
    const mod100 = abs % 100;
    if (mod10 === 1 && mod100 !== 11) return one;
    if (mod10 >= 2 && mod10 <= 4 && (mod100 < 10 || mod100 >= 20)) return few;
    return many;
  };

  const escapeHtml = (str) => {
    if (str == null) return '';
    return String(str)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  };

  const escapeAttr = (str) => {
    if (str == null) return '';
    return String(str)
      .replace(/&/g, '&amp;')
      .replace(/"/g, '&quot;');
  };

  const getEndpointLabel = (url) => {
    if (url == null || url === '') return 'сервер';
    const path = String(url)
      .replace(/\?.*$/, '')
      .replace(/^https?:\/\/[^/]+/, '') || '/';
    if (path.endsWith('/stats/torrents')) return 'статистика трекеров';
    if (path.endsWith('/lastupdatedb')) return 'время обновления';
    return 'сервер';
  };

  const THOUSAND = 1e3;
  const MILLION = 1e6;

  const formatNumber = (n) => {
    const num = Number(n);
    if (n == null || Number.isNaN(num)) return '0';
    if (!state.fullNumbers) {
      if (num >= MILLION) return (num / MILLION).toFixed(1) + 'M';
      if (num >= THOUSAND) return (num / THOUSAND).toFixed(1) + 'K';
      return String(num);
    }
    return num.toLocaleString('ru-RU');
  };

  const formatNumberFull = (n) => {
    const num = Number(n);
    if (n == null || Number.isNaN(num)) return '0';
    return num.toLocaleString('ru-RU');
  };

  const parseUTCDate = (dateStr) => {
    if (dateStr == null) return null;
    const parts = dateStr.match(/(\d{2})\.(\d{2})\.(\d{4})\s+(\d{2}):(\d{2})/);
    if (!parts) return null;
    return new Date(Date.UTC(
      parseInt(parts[3], 10),
      parseInt(parts[2], 10) - 1,
      parseInt(parts[1], 10),
      parseInt(parts[4], 10),
      parseInt(parts[5], 10)
    ));
  };

  const formatLocalDateTime = (utcDate) => {
    if (!utcDate) return '—';
    return utcDate.toLocaleString('ru-RU', {
      year: 'numeric', month: '2-digit', day: '2-digit',
      hour: '2-digit', minute: '2-digit'
    });
  };

  const state = {
    data: [],
    fullNumbers: true,
    wideMode: false,
    tableView: true,
    isLoading: false,
    page: 1,
    viewManuallySet: false
  };

  const elements = {};

  const getTracksData = (item) => ({
    wait: Number(item.tracks?.wait) || 0,
    confirm: Number(item.tracks?.confirm) || 0,
    skip: Number(item.tracks?.skip) || 0
  });

  const getSafeIconPathForTracker = (item) => getSafeIconPath(item.trackerName || item.tracker);

  const statValue = (type, value, fullLabel) =>
    `<span class="stat-value stat-value--${type} stat-number fw-semibold" aria-label="${escapeAttr(fullLabel)}">${value}</span>`;

  const renderTrackerIdentity = (item, { compact = false } = {}) => {
    const slug = formatTrackerSlug(item.trackerName);
    const displayName = getTrackerDisplayName(slug);
    const slugSafe = escapeHtml(slug);
    const displaySafe = escapeHtml(displayName);
    const ico = escapeAttr(getSafeIconPathForTracker(item));
    const showSlug = displayName.toLowerCase() !== slug;

    if (compact) {
      return `
        <div class="stats-tracker stats-tracker--compact">
          <span class="jr-icon-wrap jr-icon-wrap--neutral">
            <img src="${ico}" alt="" role="presentation" class="tracker-icon" width="16" height="16" loading="lazy" />
          </span>
          <span class="stats-tracker__name fw-medium">${displaySafe}</span>
        </div>`;
    }

    return `
      <div class="stats-tracker">
        <span class="jr-icon-wrap jr-icon-wrap--neutral stats-tracker__icon">
          <img src="${ico}" alt="" role="presentation" class="tracker-icon" width="20" height="20" loading="lazy" />
        </span>
        <div class="stats-tracker__text min-w-0">
          <span class="stats-tracker__name fw-semibold">${displaySafe}</span>
          ${showSlug ? `<span class="stats-tracker__slug">${slugSafe}</span>` : ''}
        </div>
      </div>`;
  };

  const renderMetricBlock = (type, label, value, fullLabel, { wide = false } = {}) =>
    `<div class="stat-card__metric stat-card__metric--${type}${wide ? ' stat-card__metric--wide' : ''}">
       <span class="stat-card__metric-label">${escapeHtml(label)}</span>
       ${type === 'lastnew'
      ? `<span class="stat-value stat-value--lastnew">${escapeHtml(value)}</span>`
      : statValue(type, value, fullLabel)}
     </div>`;

  const renderMetricsGrid = (item, tracks) => {
    const lastNew = item.lastnewtor || '—';
    return `
      <div class="stat-card__metrics stat-card__metrics--full">
        ${renderMetricBlock('lastnew', 'Последний новый', lastNew, lastNew, { wide: true })}
        ${renderMetricBlock('new', 'Новые', formatNumber(item.newtor), formatNumberFull(item.newtor))}
        ${renderMetricBlock('update', 'Обновлено', formatNumber(item.update), formatNumberFull(item.update))}
        ${renderMetricBlock('total', 'Всего', formatNumber(item.alltorrents), formatNumberFull(item.alltorrents))}
        ${renderMetricBlock('confirm', 'Подтверждено', formatNumber(tracks.confirm), formatNumberFull(tracks.confirm))}
        ${renderMetricBlock('wait', 'Ожидает', formatNumber(tracks.wait), formatNumberFull(tracks.wait))}
        ${renderMetricBlock('skip', 'Пропущено', formatNumber(tracks.skip), formatNumberFull(tracks.skip))}
      </div>`;
  };

  const renderMobileListItem = (item) => {
    const tracks = getTracksData(item);
    const slug = formatTrackerSlug(item.trackerName);

    return `
      <article class="stats-list-item glass-card" data-name="${escapeAttr(slug)}">
        <header class="stats-list-item__head">
          ${renderTrackerIdentity(item)}
        </header>
        ${renderMetricsGrid(item, tracks)}
      </article>`;
  };

  const renderCard = (item) => {
    const tracks = getTracksData(item);
    const slug = formatTrackerSlug(item.trackerName);

    return `
      <div class="stat-card" data-name="${escapeAttr(slug)}">
        <div class="stat-card__head">
          ${renderTrackerIdentity(item)}
        </div>
        ${renderMetricsGrid(item, tracks)}
      </div>`;
  };

  const renderTableRow = (item) => {
    const tracks = getTracksData(item);
    const slug = formatTrackerSlug(item.trackerName);

    return `
      <tr data-name="${escapeAttr(slug)}">
        <td class="stats-table__tracker">
          ${renderTrackerIdentity(item, { compact: true })}
        </td>
        <td class="text-end stats-td stats-td--lastnew">${statValue('lastnew', escapeHtml(item.lastnewtor || '—'), item.lastnewtor || '—')}</td>
        <td class="text-end stats-td stats-td--new">${statValue('new', formatNumber(item.newtor), formatNumberFull(item.newtor))}</td>
        <td class="text-end stats-td stats-td--update">${statValue('update', formatNumber(item.update), formatNumberFull(item.update))}</td>
        <td class="text-end stats-td stats-td--total">${statValue('total', formatNumber(item.alltorrents), formatNumberFull(item.alltorrents))}</td>
        <td class="text-end stats-td stats-td--confirm">${statValue('confirm', formatNumber(tracks.confirm), formatNumberFull(tracks.confirm))}</td>
        <td class="text-end stats-td stats-td--wait">${statValue('wait', formatNumber(tracks.wait), formatNumberFull(tracks.wait))}</td>
        <td class="text-end stats-td stats-td--skip">${statValue('skip', formatNumber(tracks.skip), formatNumberFull(tracks.skip))}</td>
      </tr>`;
  };

  const AGGREGATE_STATS = [
    { key: 'newtor', label: 'Новые за сегодня', color: 'stat-stat-new' },
    { key: 'update', label: 'Обновлено', color: 'stat-stat-update' },
    { key: 'alltorrents', label: 'Всего торрентов', color: 'stat-stat-total' },
    { key: 'confirm', label: 'Подтверждено', color: 'stat-stat-confirm' },
    { key: 'wait', label: 'Ожидает', color: 'stat-stat-wait' },
    { key: 'skip', label: 'Пропущено', color: 'stat-stat-skip' }
  ];

  const renderAggregate = (list) => {
    const sum = list.reduce((acc, item) => {
      const tracks = getTracksData(item);
      acc.newtor += Number(item.newtor) || 0;
      acc.update += Number(item.update) || 0;
      acc.alltorrents += Number(item.alltorrents) || 0;
      acc.confirm += tracks.confirm;
      acc.wait += tracks.wait;
      acc.skip += tracks.skip;
      return acc;
    }, { newtor: 0, update: 0, alltorrents: 0, confirm: 0, wait: 0, skip: 0 });

    const statBoxes = AGGREGATE_STATS.map(({ key, label, color }) =>
      `<div class="aggregate-stat ${color}">
         <div class="aggregate-stat__label">${escapeHtml(label)}</div>
         <div class="stat-number fw-bold ${color}" aria-label="${formatNumberFull(sum[key])}">${formatNumber(sum[key])}</div>
       </div>`
    ).join('');

    return `
      <div class="aggregate-card glass-card">
        <div class="aggregate-card__head">
          <span class="aggregate-card__icon" aria-hidden="true"><i class="bi bi-bar-chart-steps"></i></span>
          <h2 class="aggregate-card__title">Сводка по всем трекерам</h2>
        </div>
        <div class="jr-aggregate-grid">${statBoxes}</div>
      </div>`;
  };

  const getSortKey = (item, key) => {
    switch (key) {
      case 'name': return getTrackerDisplayName((item.trackerName || '').toLowerCase()).toLowerCase();
      case 'newtor': return Number(item.newtor) || 0;
      case 'update': return Number(item.update) || 0;
      case 'alltorrents': return Number(item.alltorrents) || 0;
      case 'confirm': return getTracksData(item).confirm;
      case 'wait': return getTracksData(item).wait;
      case 'skip': return getTracksData(item).skip;
      default: return 0;
    }
  };

  const applyFilterAndSort = () => {
    const query = (elements.searchTracker?.value || '').toLowerCase().trim();
    const sortVal = elements.sortSelect?.value || 'name';

    const list = state.data.filter((item) => {
      if (!query) return true;
      const slug = (item.trackerName || '').toLowerCase();
      const label = getTrackerDisplayName(slug).toLowerCase();
      return slug.includes(query) || label.includes(query);
    });

    list.sort((a, b) => {
      const ka = getSortKey(a, sortVal);
      const kb = getSortKey(b, sortVal);
      if (sortVal === 'name') return ka < kb ? -1 : ka > kb ? 1 : 0;
      return (kb - ka);
    });

    return list;
  };

  const SORT_COL_CLASS = Object.freeze({
    name: 'stats-th--tracker',
    newtor: 'stats-th--new',
    update: 'stats-th--update',
    alltorrents: 'stats-th--total',
    confirm: 'stats-th--confirm',
    wait: 'stats-th--wait',
    skip: 'stats-th--skip'
  });

  const syncStatsUrl = () => {
    const params = new URLSearchParams();
    const q = (elements.searchTracker?.value || '').trim();
    if (q) params.set('q', q);
    const sortVal = elements.sortSelect?.value || 'name';
    if (sortVal && sortVal !== 'name') params.set('sort', sortVal);
    params.set('view', state.tableView ? 'table' : 'cards');
    if (!state.fullNumbers) params.set('numbers', 'short');
    const qs = params.toString();
    const path = qs ? '/stats?' + qs : '/stats';
    try { history.replaceState(null, document.title, path); } catch (_) { }
  };

  const applyStatsFromParams = () => {
    const params = new URLSearchParams(window.location.search);
    const q = (params.get('q') || '').trim();
    if (q && elements.searchTracker) elements.searchTracker.value = q;
    const sort = params.get('sort');
    if (sort && elements.sortSelect) {
      const hasOpt = Array.from(elements.sortSelect.options).some((o) => o.value === sort);
      if (hasOpt) elements.sortSelect.value = sort;
    }
    const view = params.get('view');
    if (view === 'table' || view === 'cards') {
      state.tableView = view === 'table';
      state.viewManuallySet = true;
    }
    const numbers = params.get('numbers');
    if (numbers === 'short') state.fullNumbers = false;
    else if (numbers === 'full') state.fullNumbers = true;
  };

  const syncSortAria = () => {
    const sortVal = elements.sortSelect?.value || 'name';
    const table = elements.statsTable?.querySelector('table');
    if (!table) return;
    table.querySelectorAll('th[scope="col"]').forEach((th) => th.removeAttribute('aria-sort'));
    const colClass = SORT_COL_CLASS[sortVal];
    if (!colClass) return;
    const th = table.querySelector('.' + colClass);
    if (!th) return;
    th.setAttribute('aria-sort', sortVal === 'name' ? 'ascending' : 'descending');
  };

  const syncPaginationA11y = () => {
    if (elements.paginationPrev) {
      elements.paginationPrev.setAttribute('aria-disabled', elements.paginationPrev.disabled ? 'true' : 'false');
    }
    if (elements.paginationNext) {
      elements.paginationNext.setAttribute('aria-disabled', elements.paginationNext.disabled ? 'true' : 'false');
    }
  };

  const render = () => {
    const list = applyFilterAndSort();
    const isMobile = !isDesktopViewport();
    const useTable = state.tableView;
    const useMobileList = isMobile && !useTable;
    const useCards = !isMobile && !useTable;

    elements.statsGrid.className = useMobileList ? 'stats-list' : 'stats-grid';
    elements.statsGrid.classList.toggle('stats-grid--wide', useCards && state.wideMode);
    elements.statsGrid.classList.toggle('stats-list--wide', useMobileList && state.wideMode);

    elements.statsGrid.classList.toggle('d-none', useTable);
    elements.statsTable.classList.toggle('d-none', !useTable);

    if (list.length === 0) {
      state.page = 1;
      elements.aggregateHost.innerHTML = '';
      elements.statsGrid.innerHTML = '';
      elements.statsTableBody.innerHTML = '';
      elements.statsGrid.classList.add('d-none');
      elements.statsTable.classList.add('d-none');
      elements.viewContainer?.classList.add('d-none');
      elements.tablePagination.classList.add('d-none');
      elements.cardsCounter.textContent = '0';
      elements.emptyState.classList.remove('d-none');
      syncSortAria();
      return;
    }

    elements.emptyState.classList.add('d-none');
    elements.viewContainer?.classList.remove('d-none');
    elements.cardsCounter.textContent = String(list.length);

    elements.aggregateHost.innerHTML = renderAggregate(list);

    if (useMobileList) {
      elements.statsGrid.innerHTML = list.map(renderMobileListItem).join('');
      elements.tablePagination.classList.add('d-none');
    } else if (useTable) {
      const total = list.length;
      const totalPages = Math.max(1, Math.ceil(total / CONFIG.PAGE_SIZE));
      state.page = Math.min(Math.max(1, state.page), totalPages);

      const start = (state.page - 1) * CONFIG.PAGE_SIZE;
      const pageList = list.slice(start, start + CONFIG.PAGE_SIZE);
      elements.statsTableBody.innerHTML = pageList.map(renderTableRow).join('');

      if (total > CONFIG.PAGE_SIZE) {
        elements.tablePagination.classList.remove('d-none');
        const end = Math.min(start + CONFIG.PAGE_SIZE, total);
        elements.paginationInfo.textContent =
          `${start + 1}–${end} из ${total}`;
        elements.paginationPrev.disabled = state.page <= 1;
        elements.paginationNext.disabled = state.page >= totalPages;
      } else {
        elements.tablePagination.classList.add('d-none');
      }
    } else {
      elements.statsGrid.innerHTML = list.map(renderCard).join('');
      elements.tablePagination.classList.add('d-none');
    }

    if (typeof window.jacredAnimateStatCards === 'function' && !useTable) {
      window.jacredAnimateStatCards(elements.statsGrid);
    }

    syncPaginationA11y();
    syncSortAria();
    syncStatsUrl();
  };

  const fetchWithRetry = async (url, attempts = CONFIG.RETRY_ATTEMPTS) => {
    const label = getEndpointLabel(url);
    let lastError;

    for (let i = 0; i < attempts; i++) {
      const controller = new AbortController();
      const timerId = setTimeout(() => controller.abort(), CONFIG.FETCH_TIMEOUT_MS);

      try {
        const response = await fetchWithApiKey(url, { signal: controller.signal });
        clearTimeout(timerId);

        if (!response.ok) {
          const status = response.status;
          let msg;
          if (status >= 500) {
            msg = `Серверная ошибка при запросе «${label}»: HTTP ${status}. Попробуйте позже.`;
          } else if (status >= 400) {
            msg = `Ошибка запроса к «${label}»: HTTP ${status}. Проверьте корректность запроса.`;
          } else {
            msg = `Неожиданный HTTP-ответ от «${label}»: статус ${status}.`;
          }
          if (status >= 400 && status < 500) throw new Error(msg);
          lastError = new Error(msg);
        } else {
          return await response.text();
        }
      } catch (err) {
        clearTimeout(timerId);

        if (err.name === 'AbortError') {
          lastError = new Error(
            `Превышено время ожидания ответа от «${label}». Проверьте подключение к сети.`
          );
        } else if (err instanceof TypeError) {
          lastError = new Error(
            `Ошибка сети при обращении к «${label}». Проверьте подключение к интернету.`
          );
        } else {
          if (i === attempts - 1 || err.message.includes('HTTP 4')) throw err;
          lastError = err;
        }
      }

      if (i < attempts - 1) {
        await new Promise(resolve => setTimeout(resolve, CONFIG.RETRY_DELAY * (i + 1)));
      }
    }

    throw lastError ?? new Error(`Не удалось загрузить данные («${label}») после ${attempts} попыток.`);
  };

  const renderLastUpdateFallback = (title = 'Не удалось загрузить время обновления') => {
    elements.lastUpdate.innerHTML =
      '<i class="bi bi-clock" aria-hidden="true"></i><span title="' + escapeAttr(title) + '">—</span>';
  };

  const loadLastUpdate = async () => {
    const controller = new AbortController();
    const timerId = setTimeout(() => controller.abort(), CONFIG.LAST_UPDATE_TIMEOUT_MS);

    try {
      const response = await fetchWithApiKey(CONFIG.LAST_UPDATE_URL, { signal: controller.signal });
      clearTimeout(timerId);

      if (!response.ok) {
        console.warn(`Не удалось загрузить время обновления (HTTP ${response.status}).`);
        renderLastUpdateFallback();
        return;
      }

      let data;
      try {
        data = await response.json();
      } catch {
        console.warn('Некорректный JSON от /lastupdatedb.');
        renderLastUpdateFallback();
        return;
      }

      if (data && data.lastupdatedb) {
        const utcDate = parseUTCDate(data.lastupdatedb);
        const formattedDate = utcDate ? formatLocalDateTime(utcDate) : String(data.lastupdatedb);
        elements.lastUpdate.innerHTML =
          '<i class="bi bi-clock" aria-hidden="true"></i><span>' + escapeHtml(formattedDate) + '</span>';
      } else {
        renderLastUpdateFallback('Время обновления недоступно');
      }
    } catch (err) {
      clearTimeout(timerId);
      console.warn('Ошибка при загрузке времени обновления:', err);
      renderLastUpdateFallback();
    }
  };

  let statsLoadedOnce = false;

  const load = async () => {
    if (state.isLoading) return;

    state.isLoading = true;
    elements.loading.classList.remove('d-none');
    elements.loading.setAttribute('aria-hidden', 'false');
    const statsSkeleton = document.getElementById('statsSkeleton');
    if (statsSkeleton) statsSkeleton.innerHTML = getStatsSkeletonHtml();
    elements.error.classList.add('d-none');
    elements.aggregateHost.innerHTML = '';
    elements.statsGrid.innerHTML = '';
    elements.statsTableBody.innerHTML = '';
    elements.emptyState.classList.add('d-none');
    elements.refreshBtn.disabled = true;
    elements.refreshBtnText.textContent = 'Загрузка...';
    elements.refreshIcon.classList.add('spinner');
    syncRefreshBtnA11y(true);

    try {
      const raw = await fetchWithRetry(CONFIG.API_URL);

      if (typeof raw !== 'string' || raw.trim() === '') {
        throw new Error(
          raw == null
            ? 'Сервер вернул некорректный ответ. Ожидается текстовый JSON.'
            : 'Сервер вернул пустой ответ. Попробуйте обновить страницу позже.'
        );
      }

      let parsed;
      try {
        parsed = JSON.parse(raw);
      } catch {
        throw new Error(
          'Сервер вернул некорректные данные (ошибка разбора JSON). Проверьте работу сервера.'
        );
      }

      if (!Array.isArray(parsed)) {
        throw new Error(
          `Сервер вернул данные в неожиданном формате (${Object.prototype.toString.call(parsed)}). Ожидается массив.`
        );
      }

      state.data = parsed;
      render();
      if (statsLoadedOnce) showToast('Статистика обновлена', { type: 'success' });
      statsLoadedOnce = true;
      loadLastUpdate().catch(() => { });

    } catch (err) {
      console.error('Ошибка загрузки данных:', err);
      const is401 = err && String(err.message).includes('401');
      elements.errorMessage.textContent = is401
        ? 'Требуется API ключ. Нажмите «API ключ» и введите ключ.'
        : ((err && err.message) ? err.message : 'Не удалось загрузить данные. Проверьте подключение и попробуйте снова.');
      elements.error.classList.remove('d-none');
      if (is401 && openApiKeyModalRef) openApiKeyModalRef();
    } finally {
      elements.loading.classList.add('d-none');
      elements.loading.setAttribute('aria-hidden', 'true');
      elements.refreshBtn.disabled = false;
      elements.refreshBtnText.textContent = 'Обновить';
      elements.refreshIcon.classList.remove('spinner');
      syncRefreshBtnA11y(false);
      state.isLoading = false;
    }
  };

  const handleScroll = () => {
    const scrolled = window.scrollY || document.documentElement.scrollTop;
    const visible = scrolled > CONFIG.BACK_TO_TOP_THRESHOLD;
    elements.backToTop.classList.toggle('visible', visible);
    elements.backToTop.tabIndex = visible ? 0 : -1;
  };

  const handleSearch = debounce(() => {
    state.page = 1;
    render();
  }, CONFIG.DEBOUNCE_DELAY);

  const handleSortChange = () => {
    state.page = 1;
    render();
  };

  const setToggleLabel = (btn, text) => {
    if (!btn) return;
    const label = btn.querySelector('span');
    if (label) label.textContent = text;
    else btn.textContent = text;
    btn.setAttribute('aria-label', text);
  };

  const syncRefreshBtnA11y = (loading) => {
    if (!elements.refreshBtn) return;
    elements.refreshBtn.setAttribute('aria-label', loading ? 'Загрузка статистики' : 'Обновить статистику');
    elements.refreshBtn.setAttribute('aria-busy', loading ? 'true' : 'false');
  };

  const syncNumbersToggleA11y = () => {
    if (!elements.toggleNumbers) return;
    const text = state.fullNumbers ? 'Короткие числа' : 'Полные числа';
    setToggleLabel(elements.toggleNumbers, text);
    elements.toggleNumbers.setAttribute('aria-pressed', state.fullNumbers ? 'true' : 'false');
  };

  const syncWidthToggleA11y = () => {
    if (!elements.toggleWidth) return;
    const text = state.wideMode ? 'Узкий режим' : 'Широкий режим';
    setToggleLabel(elements.toggleWidth, text);
    elements.toggleWidth.setAttribute('aria-pressed', state.wideMode ? 'true' : 'false');
  };

  const handleToggleNumbers = () => {
    state.fullNumbers = !state.fullNumbers;
    syncNumbersToggleA11y();
    render();
  };

  const handleToggleWidth = () => {
    state.wideMode = !state.wideMode;
    syncWidthToggleA11y();
    render();
  };

  const getDefaultTableView = () => isDesktopViewport();

  const syncViewToggleLabel = () => {
    if (!elements.toggleView) return;
    const isMobile = !isDesktopViewport();
    const label = state.tableView
      ? (isMobile ? 'Список' : 'Карточки')
      : 'Таблица';
    setToggleLabel(elements.toggleView, label);
    elements.toggleView.setAttribute('aria-pressed', state.tableView ? 'true' : 'false');
  };

  const applyViewForViewport = () => {
    if (state.viewManuallySet) return;
    state.tableView = getDefaultTableView();
    syncViewToggleLabel();
    syncWidthToggleA11y();
  };

  const handleToggleView = () => {
    state.tableView = !state.tableView;
    state.viewManuallySet = true;
    syncViewToggleLabel();
    render();
  };

  const init = () => {
    const IDS = [
      'searchTracker', 'sortSelect', 'cardsCounter', 'loading', 'error', 'errorMessage',
      'retryBtn', 'aggregateHost', 'viewContainer', 'statsGrid', 'statsTable', 'statsTableBody',
      'tablePagination', 'paginationInfo', 'paginationPrev', 'paginationNext',
      'emptyState', 'refreshBtn', 'refreshBtnText', 'refreshIcon', 'lastUpdate',
      'toggleNumbers', 'toggleWidth', 'toggleView', 'toggleTheme',
      'iconMoon', 'iconSun', 'backToTop', 'homeLink', 'apiKeyModal', 'apiKeyInput',
      'apiKeyModalError', 'apiKeyModalSave'
    ];

    const missing = [];
    for (const id of IDS) {
      const el = document.getElementById(id);
      if (!el) { missing.push(id); continue; }
      elements[id] = el;
    }
    if (missing.length) {
      throw new Error('Отсутствуют обязательные элементы DOM: ' + missing.join(', '));
    }

    initThemeToggle({
      toggleEl: elements.toggleTheme,
      iconMoonEl: elements.iconMoon,
      iconSunEl: elements.iconSun
    });

    applyStatsFromParams();

    if (!state.viewManuallySet) {
      state.tableView = getDefaultTableView();
    }

    syncViewToggleLabel();
    syncNumbersToggleA11y();
    syncWidthToggleA11y();
    syncRefreshBtnA11y(false);

    elements.searchTracker.addEventListener('input', handleSearch);
    elements.sortSelect.addEventListener('change', handleSortChange);
    elements.refreshBtn.addEventListener('click', load);
    elements.retryBtn.addEventListener('click', load);

    const apiKey = initApiKeyModal({
      apiBase: API_BASE,
      modalEl: elements.apiKeyModal,
      inputEl: elements.apiKeyInput,
      errorEl: elements.apiKeyModalError,
      saveEl: elements.apiKeyModalSave,
      triggerIds: ['apiKeyBtn'],
      confTimeoutMs: CONF_TIMEOUT_MS,
      onSuccess: () => {
        elements.error.classList.add('d-none');
        load();
      }
    });
    openApiKeyModalRef = apiKey.open;

    elements.toggleNumbers.addEventListener('click', handleToggleNumbers);
    elements.toggleWidth.addEventListener('click', handleToggleWidth);
    elements.toggleView.addEventListener('click', handleToggleView);

    elements.backToTop.addEventListener('click', (e) => {
      e.preventDefault();
      scrollToTop();
    });

    elements.paginationPrev.addEventListener('click', () => {
      if (state.page > 1) { state.page--; render(); }
    });

    elements.paginationNext.addEventListener('click', () => {
      const total = applyFilterAndSort().length;
      const totalPages = Math.max(1, Math.ceil(total / CONFIG.PAGE_SIZE));
      if (state.page < totalPages) { state.page++; render(); }
    });

    window.addEventListener('scroll', throttle(handleScroll, 100), { passive: true });

    window.matchMedia(`(min-width: ${CONFIG.VIEW_BREAKPOINT}px)`)
      .addEventListener('change', () => {
        applyViewForViewport();
        render();
      });

    document.addEventListener('keydown', (e) => {
      if (e.key === '/' && !e.ctrlKey && !e.metaKey && !e.altKey && !isTypingTarget(e.target)) {
        e.preventDefault();
        elements.searchTracker.focus();
      }
    });

    load();
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }

})();
