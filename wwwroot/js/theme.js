(function (global) {
  'use strict';

  /* Runs in <head> before paint — theme, offline shell, PWA safe-area hooks */

  var isAppShellPath = function (pathname) {
    return pathname === '/' || pathname === '/stats' || pathname.indexOf('/stats/') === 0;
  };

  if (!navigator.onLine && isAppShellPath(global.location.pathname)) {
    document.documentElement.classList.add('jr-offline-inline-active', 'jr-offline');
  }

  try {
    var stored = localStorage.getItem('theme');
    var prefersDark = global.matchMedia && global.matchMedia('(prefers-color-scheme: dark)').matches;
    var mode = stored === 'dark' || stored === 'light'
      ? stored
      : (prefersDark ? 'dark' : 'light');
    document.documentElement.setAttribute('data-bs-theme', mode);
    document.documentElement.setAttribute('data-jr-glass', 'true');
    var color = mode === 'dark' ? '#0a0a0f' : '#e8f0fe';
    document.querySelectorAll('meta[name="theme-color"]').forEach(function (meta) {
      meta.setAttribute('content', color);
    });
    var statusBar = document.querySelector('meta[name="apple-mobile-web-app-status-bar-style"]');
    if (statusBar) {
      /* Manifest theme_color is static; iOS status bar style must track light/dark here */
      statusBar.setAttribute('content', mode === 'dark' ? 'black-translucent' : 'default');
    }
    var standalone = (global.matchMedia && global.matchMedia('(display-mode: standalone)').matches)
      || global.navigator.standalone === true; /* legacy iOS Home Screen */
    if (standalone) {
      document.documentElement.classList.add('jr-standalone');
    }
  } catch (_) {
    document.documentElement.setAttribute('data-bs-theme', 'dark');
    document.documentElement.setAttribute('data-jr-glass', 'true');
  }
})(window);
