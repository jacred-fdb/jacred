(function (global) {
  'use strict';

  var INLINE_ID = 'jrOfflineInline';

  var normalizePathname = function (pathname) {
    var p = pathname.replace(/\/+$/, '');
    return p || '/';
  };

  var isAppShellPath = function (pathname) {
    return pathname === '/' || pathname === '/stats' || pathname.indexOf('/stats/') === 0;
  };

  var appHomePath = function () {
    var p = normalizePathname(global.location.pathname);
    return p === '/stats' || p.indexOf('/stats/') === 0 ? '/stats' : '/';
  };

  var probeConnection = function () {
    return fetch('/health', { method: 'GET', cache: 'no-store', credentials: 'same-origin' })
      .then(function (res) {
        return res.ok;
      })
      .catch(function () {
        return false;
      });
  };

  var setRetryBusy = function (busy) {
    var retryBtn = document.getElementById('jrOfflineInlineRetry');
    var label = document.getElementById('jrOfflineInlineRetryLabel');
    var spinner = document.getElementById('jrOfflineInlineRetrySpinner');
    if (retryBtn) {
      retryBtn.disabled = busy;
      retryBtn.setAttribute('aria-busy', busy ? 'true' : 'false');
    }
    if (label) label.hidden = busy;
    if (spinner) spinner.hidden = !busy;
  };

  var ensureOverlay = function () {
    var existing = document.getElementById(INLINE_ID);
    if (existing) return existing;

    var home = appHomePath();
    var root = document.createElement('div');
    root.id = INLINE_ID;
    root.className = 'jr-offline-inline';
    root.hidden = true;
    root.setAttribute('aria-hidden', 'true');
    root.innerHTML =
      '<div class="jr-offline-inline__panel glass-card" role="alertdialog" aria-modal="true" aria-labelledby="jrOfflineInlineTitle">' +
      '<p class="jr-offline-badge" id="jrOfflineInlineStatus" role="status" aria-live="polite">' +
      '<span class="jr-offline-badge__dot" aria-hidden="true"></span>' +
      '<span id="jrOfflineInlineStatusText">Нет подключения к сети</span></p>' +
      '<div class="jr-offline-hero">' +
      '<div class="jr-offline-hero__glow" aria-hidden="true"></div>' +
      '<div class="jr-offline-hero__icon" aria-hidden="true">' +
      '<svg width="56" height="56" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">' +
      '<path d="M5 12.55a11 11 0 0 1 14.08 0"/><path d="M1.42 9a16 16 0 0 1 21.16 0"/>' +
      '<path d="M8.53 16.11a6 6 0 0 1 6.95 0"/><circle cx="12" cy="20" r="1" fill="currentColor" stroke="none"/>' +
      '<line x1="2" y1="2" x2="22" y2="22"/></svg></div>' +
      '<h1 class="jr-offline-hero__title" id="jrOfflineInlineTitle">Соединение потеряно</h1>' +
      '<p class="jr-offline-hero__lead">Сервер JacRed сейчас недоступен. Проверьте Wi‑Fi или мобильный интернет и попробуйте снова.</p>' +
      '</div>' +
      '<section class="jr-offline-tips" aria-labelledby="jrOfflineInlineTipsTitle">' +
      '<h2 id="jrOfflineInlineTipsTitle" class="jr-offline-tips__title">Что можно сделать</h2>' +
      '<ul class="jr-offline-tips__list">' +
      '<li class="jr-offline-tips__item"><span class="jr-offline-tips__marker" aria-hidden="true"></span>' +
      '<span>Нажмите «Повторить», когда сеть восстановится</span></li>' +
      '<li class="jr-offline-tips__item"><span class="jr-offline-tips__marker" aria-hidden="true"></span>' +
      '<span>Вы остаётесь на текущей странице — после восстановления связи можно продолжить</span></li>' +
      '</ul></section>' +
      '<div class="jr-offline-actions">' +
      '<button type="button" id="jrOfflineInlineRetry" class="btn-jr-primary tap-target jr-offline-actions__primary">' +
      '<span class="jr-offline-actions__label" id="jrOfflineInlineRetryLabel">Повторить</span>' +
      '<span class="jr-spinner jr-spinner--inline jr-offline-actions__spinner" id="jrOfflineInlineRetrySpinner" hidden aria-hidden="true"></span>' +
      '</button>' +
      (home === '/stats'
        ? '<a href="/" class="btn-jr-ghost tap-target jr-offline-actions__secondary">На главную</a>'
        : '') +
      '</div></div>';

    document.body.appendChild(root);

    var retryBtn = document.getElementById('jrOfflineInlineRetry');
    if (retryBtn) {
      retryBtn.addEventListener('click', function () {
        setRetryBusy(true);
        probeConnection().then(function (ok) {
          if (ok) {
            global.location.reload();
            return;
          }
          setRetryBusy(false);
          setStatusText(false);
          showOfflineInline();
        });
      });
    }

    return root;
  };

  var setStatusText = function (online) {
    var statusText = document.getElementById('jrOfflineInlineStatusText');
    var panel = document.querySelector('#' + INLINE_ID + ' .jr-offline-inline__panel');
    if (statusText) {
      statusText.textContent = online
        ? 'Соединение восстановлено'
        : 'Нет подключения к сети';
    }
    if (panel) panel.classList.toggle('jr-offline-inline__panel--online', online);
  };

  var showOfflineInline = function () {
    if (!isAppShellPath(global.location.pathname)) return;
    var overlay = ensureOverlay();
    document.documentElement.classList.add('jr-offline-inline-active', 'jr-offline');
    overlay.hidden = false;
    overlay.setAttribute('aria-hidden', 'false');
    setStatusText(false);
  };

  var hideOfflineInline = function () {
    var overlay = document.getElementById(INLINE_ID);
    document.documentElement.classList.remove('jr-offline-inline-active', 'jr-offline');
    if (overlay) {
      overlay.hidden = true;
      overlay.setAttribute('aria-hidden', 'true');
    }
  };

  var syncOfflineInline = function () {
    if (!isAppShellPath(global.location.pathname)) return;

    if (!navigator.onLine) {
      showOfflineInline();
      return;
    }

    probeConnection().then(function (ok) {
      if (ok) hideOfflineInline();
      else showOfflineInline();
    });
  };

  var initOfflineInline = function () {
    ensureOverlay();
    syncOfflineInline();
    global.addEventListener('offline', syncOfflineInline);
    global.addEventListener('online', function () {
      setStatusText(true);
      probeConnection().then(function (ok) {
        if (ok) global.location.reload();
        else syncOfflineInline();
      });
    });
    global.addEventListener('pageshow', syncOfflineInline);
    global.setInterval(function () {
      if (!navigator.onLine) syncOfflineInline();
    }, 2000);
  };

  global.JacredOffline = global.JacredOffline || {};
  global.JacredOffline.showInline = showOfflineInline;
  global.JacredOffline.hideInline = hideOfflineInline;
  global.JacredOffline.syncInline = syncOfflineInline;
  global.JacredOffline.isAppShellPath = isAppShellPath;
  global.JacredOffline.initOfflineInline = initOfflineInline;

  if (document.body) initOfflineInline();
  else document.addEventListener('DOMContentLoaded', initOfflineInline);
})(window);
