# JacRed Web UI (`wwwroot`)

Статический фронтенд: поиск (`index.html`), статистика (`stats.html`), PWA (`manifest.json`, `sw.js`).

## CDN-зависимости

Bootstrap 5.3.3, Bootstrap Icons 1.11.3, GSAP 3.12.5 подключаются с [jsDelivr](https://cdn.jsdelivr.net) в `index.html` и `stats.html` (не хранятся в репозитории). Для работы UI нужен доступ в интернет; при offline показывается inline-overlay (`offline-inline.js`).

## PWA: `theme_color` и тёмная тема

В `manifest.json` заданы единые `theme_color` и `background_color` (`#e8f0fe`) — это светлая палитра Google Material, совпадающая с дефолтом UI.

**Ограничение:** Web App Manifest не поддерживает разные `theme_color` для светлой и тёмной темы. После установки PWA системная строка состояния и splash на части платформ останутся со светлым оттенком, даже если в приложении включена тёмная тема.

Для вкладки в браузере `theme.js` обновляет `<meta name="theme-color">` при переключении темы; на установленное standalone-приложение это не всегда влияет — только manifest.
