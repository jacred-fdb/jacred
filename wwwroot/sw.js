'use strict';

const CACHE_NAME = 'jacred-static-1.0.0';

const VENDOR_PRECACHE = [
  '/css/fonts.css',
  '/vendor/bootstrap/bootstrap.min.css',
  '/vendor/bootstrap/bootstrap.bundle.min.js',
  '/vendor/bootstrap-icons/bootstrap-icons.min.css',
  '/vendor/bootstrap-icons/fonts/bootstrap-icons.woff2',
  '/vendor/gsap/gsap.min.js',
  '/vendor/gsap/ScrollTrigger.min.js',
  '/fonts/inter/inter-latin-400-normal.woff2',
  '/fonts/inter/inter-latin-500-normal.woff2',
  '/fonts/inter/inter-latin-600-normal.woff2',
  '/fonts/inter/inter-latin-700-normal.woff2',
  '/fonts/inter/inter-cyrillic-400-normal.woff2',
  '/fonts/inter/inter-cyrillic-500-normal.woff2',
  '/fonts/inter/inter-cyrillic-600-normal.woff2',
  '/fonts/inter/inter-cyrillic-700-normal.woff2'
];

const CRITICAL_PRECACHE = [
  ...VENDOR_PRECACHE,
  '/css/styles.css',
  '/js/theme.js',
  '/js/offline-inline.js',
  '/js/common.js',
  '/js/pwa.js'
];

const OPTIONAL_PRECACHE = [
  '/',
  '/stats',
  '/settings',
  '/js/animations.js',
  '/js/app.js',
  '/js/stats.js',
  '/js/settings.js',
  '/js/settings-form.js',
  '/manifest.json',
  '/openapi.yaml',
  '/img/jacred.png',
  '/img/favicon.ico',
  '/img/icon-192.png',
  '/img/icon-512.png',
  '/img/icon-maskable-512.png'
];

const MINIMAL_OFFLINE_HTML = '<!DOCTYPE html><html lang="ru"><head><meta charset="UTF-8">' +
  '<meta name="viewport" content="width=device-width,initial-scale=1">' +
  '<title>Нет сети | JacRed</title>' +
  '<style>body{font-family:system-ui,sans-serif;display:flex;align-items:center;justify-content:center;' +
  'min-height:100vh;margin:0;background:#0a0a0f;color:#e8e8e8;text-align:center;padding:1.5rem}' +
  'h1{font-size:1.35rem;margin:0 0 .75rem}p{opacity:.85;margin:0 0 1.25rem}' +
  'button{font:inherit;padding:.6rem 1.2rem;border-radius:.5rem;border:0;cursor:pointer;' +
  'background:#4285f4;color:#fff}</style></head><body><div><h1>Соединение потеряно</h1>' +
  '<p>Сервер JacRed недоступен. Проверьте интернет и попробуйте снова.</p>' +
  /* Form submit, not onclick — works under strict CSP in cached shell pages */
  '<form method="get" action="."><button type="submit">Повторить</button></form></div></body></html>';

const absUrl = (path) => new URL(path, self.location.origin).href;

const normalizePathname = (pathname) => {
  const p = pathname.replace(/\/+$/, '');
  return p || '/';
};

const isAppShellPath = (pathname) =>
  pathname === '/' || pathname === '/stats' || pathname.startsWith('/stats/') ||
  pathname === '/settings' || pathname.startsWith('/settings/');

const isStaticAsset = (url) => {
  const p = url.pathname;
  return p.startsWith('/css/') || p.startsWith('/js/') || p.startsWith('/img/') ||
    p.startsWith('/vendor/') || p.startsWith('/fonts/') ||
    /\.(css|js|png|ico|json|woff2?)$/i.test(p);
};

const minimalOfflineResponse = () => new Response(MINIMAL_OFFLINE_HTML, {
  headers: {
    'Content-Type': 'text/html; charset=utf-8',
    'Cache-Control': 'no-store'
  }
});

const putInCache = async (request, response) => {
  if (!response.ok) return;
  const cache = await caches.open(CACHE_NAME);
  await cache.put(request, response.clone());
};

const findInAnyCache = async (url) => {
  const direct = await caches.match(url);
  if (direct) return direct;
  const keys = await caches.keys();
  for (const key of keys) {
    if (key === CACHE_NAME) continue;
    const match = await (await caches.open(key)).match(url);
    if (match) return match;
  }
  return null;
};

const copyIntoCache = async (targetCache, url, response) => {
  await targetCache.put(url, response.clone());
  return true;
};

const precacheOne = async (targetCache, path) => {
  const url = absUrl(path);
  if (await targetCache.match(url)) return true;

  try {
    const res = await fetch(url, { cache: 'reload' });
    if (res.ok) {
      await copyIntoCache(targetCache, url, res);
      return true;
    }
  } catch (_) { }

  const existing = await findInAnyCache(url);
  if (existing) {
    await copyIntoCache(targetCache, url, existing);
    return true;
  }

  return false;
};

const migrateFromOldCaches = async (targetCache, paths) => {
  /* Reuse assets from prior CACHE_NAME after deploy so offline upgrade is seamless */
  const oldKeys = (await caches.keys()).filter((k) => k !== CACHE_NAME);
  for (const oldKey of oldKeys) {
    const oldCache = await caches.open(oldKey);
    for (const path of paths) {
      const url = absUrl(path);
      if (await targetCache.match(url)) continue;
      const match = await oldCache.match(url);
      if (match) await copyIntoCache(targetCache, url, match);
    }
  }
};

const precacheEntries = async (cache) => {
  await migrateFromOldCaches(cache, [...CRITICAL_PRECACHE, ...OPTIONAL_PRECACHE]);

  const criticalFailed = [];
  for (const path of CRITICAL_PRECACHE) {
    const ok = await precacheOne(cache, path);
    if (!ok) criticalFailed.push(path);
  }

  const optionalResults = await Promise.allSettled(
    OPTIONAL_PRECACHE.map((path) => precacheOne(cache, path))
  );
  const optionalFailed = [];
  optionalResults.forEach((result, index) => {
    if (result.status === 'rejected' || result.value !== true) {
      optionalFailed.push(OPTIONAL_PRECACHE[index]);
    }
  });

  if (criticalFailed.length) {
    console.warn('[SW] critical precache incomplete (using migration/fallback):', criticalFailed);
  }
  if (optionalFailed.length) {
    console.warn('[SW] optional precache skipped:', optionalFailed.length, optionalFailed);
  }
};

const getCachedAppShell = async (pathname) => {
  const p = normalizePathname(pathname);
  if (p === '/stats' || p.startsWith('/stats/')) return caches.match(absUrl('/stats'));
  if (p === '/settings' || p.startsWith('/settings/')) return caches.match(absUrl('/settings'));
  return caches.match(absUrl('/'));
};

const cacheAppShell = async (request, response) => {
  if (!response.ok) return;
  const p = normalizePathname(new URL(request.url).pathname);
  if (p !== '/' && p !== '/stats' && p !== '/settings') return;
  await putInCache(request, response);
};

self.addEventListener('install', (event) => {
  event.waitUntil(
    (async () => {
      const cache = await caches.open(CACHE_NAME);
      await precacheEntries(cache);
      await self.skipWaiting();
    })()
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    (async () => {
      const cache = await caches.open(CACHE_NAME);
      await migrateFromOldCaches(cache, [...CRITICAL_PRECACHE, ...OPTIONAL_PRECACHE]);
      const keys = await caches.keys();
      await Promise.all(keys.filter((k) => k !== CACHE_NAME).map((k) => caches.delete(k)));
      await self.clients.claim();
    })()
  );
});

const serveOfflineFallback = async (pathname) => {
  const cached = await getCachedAppShell(pathname);
  if (cached) return cached;
  return minimalOfflineResponse();
};

const fetchNetworkOnly = (request) =>
  fetch(new Request(request.url, {
    method: 'GET',
    cache: 'no-store',
    mode: 'same-origin',
    credentials: request.credentials,
    redirect: request.redirect,
    referrer: request.referrer,
    referrerPolicy: request.referrerPolicy
  }));

const handleAppShellNavigation = async (request) => {
  /* Network-first for / and /stats; fall back to cache when offline */
  const pathname = new URL(request.url).pathname;
  try {
    const res = await fetchNetworkOnly(request);
    if (res.ok) {
      await cacheAppShell(request, res);
      return res;
    }
    const cached = await getCachedAppShell(pathname);
    return cached || res;
  } catch (_) {
    return serveOfflineFallback(pathname);
  }
};

const isDocumentNavigation = (request) =>
  request.mode === 'navigate' || request.destination === 'document';

self.addEventListener('fetch', (event) => {
  if (event.request.method !== 'GET') return;
  const url = new URL(event.request.url);
  if (url.origin !== self.location.origin) return;
  /* API responses must never be cached or served from SW */
  if (url.pathname.startsWith('/api/')) return;

  if (isDocumentNavigation(event.request)) {
    if (isAppShellPath(url.pathname)) {
      event.respondWith(handleAppShellNavigation(event.request));
      return;
    }

    event.respondWith(
      (async () => {
        try {
          const res = await fetch(event.request);
          if (res.ok) await putInCache(event.request, res);
          return res;
        } catch (_) {
          const cached = await caches.match(event.request);
          return cached || serveOfflineFallback('/');
        }
      })()
    );
    return;
  }

  if (isStaticAsset(url)) {
    event.respondWith(
      (async () => {
        const cached = await caches.match(event.request);
        if (cached) return cached;
        try {
          const res = await fetch(event.request);
          if (res.ok) await putInCache(event.request, res);
          return res;
        } catch (_) {
          return new Response('', { status: 504, statusText: 'Network Error' });
        }
      })()
    );
    return;
  }

  event.respondWith(
    (async () => {
      try {
        return await fetch(event.request);
      } catch (_) {
        const cached = await caches.match(event.request);
        return cached || new Response('', { status: 504, statusText: 'Network Error' });
      }
    })()
  );
});
