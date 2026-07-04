(function (global) {
  'use strict';

  const LS = {
    get: (key) => {
      try { return localStorage.getItem(key) || ''; } catch (_) { return ''; }
    },
    set: (key, value) => {
      try { localStorage.setItem(key, value); } catch (e) { console.warn('localStorage.setItem(' + key + ') failed:', e); }
    },
    remove: (key) => {
      try { localStorage.removeItem(key); } catch (e) { console.warn('localStorage.removeItem(' + key + ') failed:', e); }
    }
  };

  const debounce = (func, wait) => {
    let timeout;
    return function (...args) {
      clearTimeout(timeout);
      timeout = setTimeout(() => func.apply(this, args), wait);
    };
  };

  const throttle = (func, limit) => {
    let inThrottle = false;
    return function (...args) {
      if (!inThrottle) {
        func.apply(this, args);
        inThrottle = true;
        setTimeout(() => { inThrottle = false; }, limit);
      }
    };
  };

  const scrollToTop = () => {
    const reduce = global.matchMedia('(prefers-reduced-motion: reduce)').matches;
    global.scrollTo({ top: 0, behavior: reduce ? 'auto' : 'smooth' });
  };

  const updateThemeIcons = (iconMoon, iconSun, isDark) => {
    if (iconMoon) iconMoon.classList.toggle('d-none', !isDark);
    if (iconSun) iconSun.classList.toggle('d-none', isDark);
  };

  const resolveThemeMode = () => {
    const stored = LS.get('theme');
    if (stored === 'dark' || stored === 'light') return stored;
    if (global.matchMedia?.('(prefers-color-scheme: dark)').matches) return 'dark';
    return 'light';
  };

  const syncThemeFromDom = (iconMoon, iconSun) => {
    const isDark = document.documentElement.getAttribute('data-bs-theme') === 'dark';
    updateThemeIcons(iconMoon, iconSun, isDark);
  };

  const toggleTheme = (iconMoon, iconSun) => {
    const isDark = resolveThemeMode() === 'dark';
    const next = isDark ? 'light' : 'dark';
    applyTheme(next, iconMoon, iconSun);
    LS.set('theme', next);
  };

  const syncThemeColorMeta = (isDark) => {
    const color = isDark ? '#0a0a0f' : '#e8f0fe';
    document.querySelectorAll('meta[name="theme-color"]').forEach((meta) => {
      meta.setAttribute('content', color);
    });
    const statusBar = document.querySelector('meta[name="apple-mobile-web-app-status-bar-style"]');
    if (statusBar) {
      statusBar.setAttribute('content', isDark ? 'black-translucent' : 'default');
    }
  };

  const applyTheme = (mode, iconMoon, iconSun) => {
    const isDark = mode === 'dark';
    document.documentElement.setAttribute('data-bs-theme', isDark ? 'dark' : 'light');
    document.documentElement.setAttribute('data-jr-glass', 'true');
    updateThemeIcons(iconMoon, iconSun, isDark);
    syncThemeColorMeta(isDark);
  };

  const initSystemThemeListener = (iconMoon, iconSun) => {
    if (!global.matchMedia) return;
    const mq = global.matchMedia('(prefers-color-scheme: dark)');
    const onChange = () => {
      if (LS.get('theme')) return;
      applyTheme(mq.matches ? 'dark' : 'light', iconMoon, iconSun);
    };
    if (typeof mq.addEventListener === 'function') mq.addEventListener('change', onChange);
    else if (typeof mq.addListener === 'function') mq.addListener(onChange);
  };

  const initThemeToggle = ({ toggleEl, iconMoonEl, iconSunEl }) => {
    if (!toggleEl) return;
    applyTheme(resolveThemeMode(), iconMoonEl, iconSunEl);
    toggleEl.addEventListener('click', () => toggleTheme(iconMoonEl, iconSunEl));
    initSystemThemeListener(iconMoonEl, iconSunEl);
  };

  const announceLive = (message, options = {}) => {
    const politeness = options.politeness || 'polite';
    let region = document.getElementById('jrAnnouncer');
    if (!region) {
      region = document.createElement('div');
      region.id = 'jrAnnouncer';
      region.className = 'visually-hidden';
      region.setAttribute('aria-live', politeness);
      region.setAttribute('aria-atomic', 'true');
      region.setAttribute('role', 'status');
      document.body.appendChild(region);
    }
    region.setAttribute('aria-live', politeness);
    region.textContent = '';
    global.requestAnimationFrame(() => { region.textContent = message; });
  };

  const ensureToastHost = () => {
    if (document.getElementById('jrToastHost')) return;
    const host = document.createElement('div');
    host.id = 'jrToastHost';
    host.className = 'jr-toast-host';
    host.setAttribute('aria-live', 'polite');
    host.setAttribute('aria-atomic', 'true');
    host.setAttribute('role', 'status');
    document.body.appendChild(host);
  };

  const showToast = (message, options = {}) => {
    if (!message) return;
    const type = options.type || 'info';
    const duration = options.duration != null ? options.duration : 2200;
    const fadeMs = options.fadeMs != null ? options.fadeMs : 180;
    const politeness = options.politeness || (type === 'error' ? 'assertive' : 'polite');
    announceLive(message, { politeness });
    ensureToastHost();
    const host = document.getElementById('jrToastHost');
    if (options.replace) {
      host.querySelectorAll('.jr-toast').forEach((existing) => existing.remove());
    }
    const toast = document.createElement('div');
    toast.className = 'jr-toast jr-toast--' + type;
    toast.textContent = message;
    host.appendChild(toast);
    global.requestAnimationFrame(() => toast.classList.add('jr-toast--visible'));
    global.setTimeout(() => {
      toast.classList.remove('jr-toast--visible');
      global.setTimeout(() => {
        if (toast.parentNode) toast.remove();
      }, fadeMs);
    }, duration);
  };

  const isTypingTarget = (target) => {
    if (!target) return false;
    const tag = target.tagName;
    return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || target.isContentEditable;
  };

  let deferredInstallPrompt = null;

  const initInstallPrompt = () => {
    if (LS.get('pwaInstallDismissed') === '1') return;

    global.addEventListener('beforeinstallprompt', (e) => {
      e.preventDefault();
      deferredInstallPrompt = e;
      if (document.getElementById('jrInstallBanner')) return;

      const banner = document.createElement('div');
      banner.id = 'jrInstallBanner';
      banner.className = 'jr-install-banner glass-card';
      banner.setAttribute('role', 'region');
      banner.setAttribute('aria-label', 'Установка приложения');
      banner.innerHTML =
        '<div class="jr-install-banner__text">' +
        '<strong class="d-block mb-1">Установить JacRed</strong>' +
        '<span class="small text-secondary">Быстрый доступ с главного экрана</span>' +
        '</div>' +
        '<div class="jr-install-banner__actions">' +
        '<button type="button" class="btn btn-sm btn-jr-primary" id="jrInstallAccept">Установить</button>' +
        '<button type="button" class="btn btn-sm btn-jr-ghost" id="jrInstallDismiss">Позже</button>' +
        '</div>';

      document.body.appendChild(banner);
      document.body.classList.add('jr-has-install-banner');
      requestAnimationFrame(() => banner.classList.add('jr-install-banner--visible'));

      const removeBanner = () => {
        document.body.classList.remove('jr-has-install-banner');
        banner.remove();
      };

      document.getElementById('jrInstallAccept')?.addEventListener('click', async () => {
        if (!deferredInstallPrompt) return;
        deferredInstallPrompt.prompt();
        try { await deferredInstallPrompt.userChoice; } catch (_) { }
        deferredInstallPrompt = null;
        removeBanner();
      });

      document.getElementById('jrInstallDismiss')?.addEventListener('click', () => {
        LS.set('pwaInstallDismissed', '1');
        banner.classList.remove('jr-install-banner--visible');
        setTimeout(removeBanner, 280);
      });
    });
  };

  const initKeyboardHelp = () => {
    const modalEl = document.getElementById('shortcutsModal');
    if (!modalEl || typeof bootstrap === 'undefined') return;
    const modal = new bootstrap.Modal(modalEl);

    global.addEventListener('keydown', (e) => {
      if (e.key !== '?' || e.ctrlKey || e.metaKey || e.altKey) return;
      if (isTypingTarget(e.target)) return;
      e.preventDefault();
      modal.show();
    });
  };

  const normalizePathname = (pathname) => {
    const p = pathname.replace(/\/+$/, '');
    return p || '/';
  };

  const isAppShellPath = (pathname) =>
    pathname === '/' || pathname === '/stats' || pathname.startsWith('/stats/');

  const syncOfflineUi = () => {
    if (global.JacredOffline?.syncInline) {
      global.JacredOffline.syncInline();
      return;
    }
    document.documentElement.classList.toggle('jr-offline', !navigator.onLine);
  };

  const initOnlineStatus = () => {
    global.addEventListener('online', () => {
      showToast('Соединение восстановлено', { type: 'success' });
    });
    global.addEventListener('offline', syncOfflineUi);
    if (!navigator.onLine) syncOfflineUi();
  };

  const initGlobalUi = () => {
    ensureToastHost();
    initInstallPrompt();
    initKeyboardHelp();
    initOnlineStatus();
    initTrackerIconFallback();
  };

  const initTrackerIconFallback = () => {
    if (document.body.dataset.jrTrackerIconFallback) return;
    document.body.dataset.jrTrackerIconFallback = '1';
    document.body.addEventListener('error', (e) => {
      const target = e.target;
      if (
        !(target instanceof HTMLImageElement) ||
        !target.classList.contains('tracker-icon') ||
        target.classList.contains('tracker-icon--fallback')
      ) return;
      target.classList.add('tracker-icon--fallback');
      target.removeAttribute('src');
    }, true);
  };

  const setInert = (el, inert) => {
    if (!el) return;
    if (inert) {
      el.setAttribute('inert', '');
      el.setAttribute('aria-hidden', 'true');
    } else {
      el.removeAttribute('inert');
      el.removeAttribute('aria-hidden');
    }
  };

  const getSearchSkeletonHtml = (count = 4) => {
    const cards = [];
    for (let i = 0; i < count; i++) {
      cards.push(
        '<div class="jr-skeleton-card" aria-hidden="true">' +
        '<div class="jr-skeleton jr-skeleton--title"></div>' +
        '<div class="jr-skeleton jr-skeleton--line"></div>' +
        '<div class="jr-skeleton-row">' +
        '<span class="jr-skeleton jr-skeleton--chip"></span>' +
        '<span class="jr-skeleton jr-skeleton--chip"></span>' +
        '<span class="jr-skeleton jr-skeleton--chip"></span>' +
        '</div></div>'
      );
    }
    return '<div class="jr-skeleton-list">' + cards.join('') + '</div>';
  };

  const getStatsSkeletonHtml = () => {
    const rows = new Array(5).fill('<div class="jr-skeleton jr-skeleton--stat-row"></div>').join('');
    return '<div class="jr-skeleton jr-skeleton--aggregate"></div>' + rows;
  };

  const registerServiceWorker = () => {
    if (!('serviceWorker' in navigator)) return;

    let reloadPending = false;
    navigator.serviceWorker.addEventListener('controllerchange', () => {
      if (!reloadPending) return;
      reloadPending = false;
      if (!navigator.onLine && isAppShellPath(global.location.pathname)) {
        syncOfflineUi();
        return;
      }
      global.location.reload();
    });

    const bindUpdateChecks = (registration) => {
      registration.addEventListener('updatefound', () => {
        const worker = registration.installing;
        if (!worker || !navigator.serviceWorker.controller) return;
        if (!navigator.onLine) return;
        reloadPending = true;
      });
      const checkForUpdates = () => {
        if (!navigator.onLine) return;
        registration.update().catch(() => {});
      };
      global.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') checkForUpdates();
      });
      global.addEventListener('focus', checkForUpdates);
      global.addEventListener('online', checkForUpdates);
    };

    const doRegister = () => {
      navigator.serviceWorker.register('/sw.js', { scope: '/' })
        .then((registration) => {
          bindUpdateChecks(registration);
          if (registration.waiting && navigator.serviceWorker.controller && navigator.onLine) {
            reloadPending = true;
          }
        })
        .catch((err) => {
          console.warn('Service worker registration failed:', err);
        });
    };

    const register = () => {
      if (!navigator.onLine) {
        global.addEventListener('online', () => doRegister(), { once: true });
        return;
      }
      doRegister();
    };

    if (document.readyState === 'complete') register();
    else global.addEventListener('load', register);
  };

  const appendApiKey = (url) => url; /* legacy export; key is sent via X-Api-Key header only */

  const withApiKeyHeaders = (options = {}) => {
    /* Header keeps the key out of URLs, logs, and Referer */
    const key = LS.get('api_key');
    if (!key) return options;
    const headers = new Headers(options.headers || {});
    if (!headers.has('X-Api-Key')) headers.set('X-Api-Key', key);
    return { ...options, headers };
  };

  const fetchWithApiKey = (url, options = {}) => fetch(url, withApiKeyHeaders(options));

  const getSafeIconPath = (trackerName) => {
    const rawName = String(trackerName || '').toLowerCase();
    const safeName = rawName.replace(/[^a-z0-9_-]/g, '');
    if (!safeName) return './img/jacred.png';
    return './img/ico/' + safeName + '.ico';
  };

  const isSafeHttpUrl = (url) => {
    if (!url || typeof url !== 'string') return false;
    try {
      const parsed = new URL(url, global.location.origin);
      return parsed.protocol === 'http:' || parsed.protocol === 'https:';
    } catch (_) {
      return false;
    }
  };

  const initApiKeyModal = ({
    apiBase = '/api/v1.0',
    modalEl,
    inputEl,
    errorEl,
    saveEl,
    triggerIds = ['apiKeyBtn'],
    confTimeoutMs = 5000,
    onSuccess = () => {}
  }) => {
    if (!modalEl || !inputEl || typeof bootstrap === 'undefined') {
      return { open: () => {}, close: () => {} };
    }

    const modal = new bootstrap.Modal(modalEl);
    let triggerEl = null;

    const close = () => {
      modal.hide();
      if (triggerEl) { triggerEl.focus(); triggerEl = null; }
    };

    const open = (e) => {
      triggerEl = (e && e.currentTarget) || document.getElementById(triggerIds[0]) || null;
      inputEl.value = LS.get('api_key');
      if (errorEl) { errorEl.classList.add('d-none'); errorEl.textContent = ''; }
      modal.show();
    };

    const save = () => {
      const key = (inputEl.value || '').trim();
      if (!key) {
        if (errorEl) { errorEl.textContent = 'Введите ключ'; errorEl.classList.remove('d-none'); }
        return;
      }
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), confTimeoutMs);
      fetch(apiBase + '/conf', {
        signal: controller.signal,
        headers: key ? { 'X-Api-Key': key } : undefined
      })
        .then(r => r.json())
        .then(json => {
          clearTimeout(timeoutId);
          if (json.apikey) {
            LS.set('api_key', key);
            close();
            onSuccess();
          } else if (errorEl) {
            errorEl.textContent = 'Ключ неверный или не принят сервером';
            errorEl.classList.remove('d-none');
          }
        })
        .catch(err => {
          clearTimeout(timeoutId);
          if (errorEl) {
            errorEl.textContent = err?.name === 'AbortError' ? 'Истекло время ожидания' : 'Ошибка проверки ключа';
            errorEl.classList.remove('d-none');
          }
        });
    };

    triggerIds.forEach((id) => {
      const btn = document.getElementById(id);
      if (btn) btn.addEventListener('click', open);
    });
    if (saveEl) saveEl.addEventListener('click', save);
    modalEl.querySelectorAll('[data-bs-dismiss="modal"], .btn-close').forEach((el) => {
      el.addEventListener('click', (e) => {
        e.preventDefault();
        close();
      });
    });
    inputEl.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') { e.preventDefault(); save(); }
    });

    return { open, close };
  };

  global.Jacred = Object.freeze({
    LS,
    debounce,
    throttle,
    scrollToTop,
    updateThemeIcons,
    syncThemeFromDom,
    toggleTheme,
    applyTheme,
    resolveThemeMode,
    initThemeToggle,
    initSystemThemeListener,
    announceLive,
    showToast,
    isTypingTarget,
    initGlobalUi,
    setInert,
    getSearchSkeletonHtml,
    getStatsSkeletonHtml,
    registerServiceWorker,
    appendApiKey,
    fetchWithApiKey,
    withApiKeyHeaders,
    getSafeIconPath,
    isSafeHttpUrl,
    initApiKeyModal
  });
})(window);
