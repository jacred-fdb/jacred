(function (global) {
  'use strict';

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
    var meta = document.querySelector('meta[name="theme-color"]');
    if (meta) meta.setAttribute('content', mode === 'dark' ? '#000000' : '#e8f0fe');
  } catch (_) {
    document.documentElement.setAttribute('data-bs-theme', 'dark');
    document.documentElement.setAttribute('data-jr-glass', 'true');
  }
})(window);
