import path from 'node:path'
import tailwindcss from '@tailwindcss/vite'
import vue from '@vitejs/plugin-vue'
import { loadEnv } from 'vite'
import { defineConfig } from 'vitest/config'
import { VitePWA } from 'vite-plugin-pwa'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const apiProxyTarget =
    env.VITE_API_PROXY_TARGET || 'http://localhost:9117'
  return {
    plugins: [
      vue(),
      tailwindcss(),
      VitePWA({
        registerType: 'prompt',
        includeAssets: [
          'img/favicon.ico',
          'img/icon-32.png',
          'img/icon-64.png',
          'img/icon-192.png',
          'img/icon-maskable-192.png',
          'img/icon-512.png',
          'img/icon-maskable-512.png',
          'img/apple-touch-icon.png',
        ],
        // Icons: `npm run generate-pwa-assets` (see pwa-assets.config.ts).
        // Vite `pwaAssets` left disabled — assets-generator during closeBundle hung builds.
        pwaAssets: {
          disabled: true,
        },
        manifest: {
          id: '/',
          name: 'JacRed — Поиск торрентов',
          short_name: 'JacRed',
          description: 'Торрент агрегатор. Поиск торрентов и статистика трекеров',
          lang: 'ru',
          dir: 'ltr',
          start_url: '/',
          scope: '/',
          display: 'standalone',
          display_override: ['standalone', 'minimal-ui', 'browser'],
          background_color: '#0a0a0f',
          theme_color: '#0a0a0f',
          orientation: 'any',
          categories: ['utilities', 'entertainment'],
          prefer_related_applications: false,
          launch_handler: {
            client_mode: 'navigate-existing',
          },
          handle_links: 'preferred',
          icons: [
            {
              src: 'img/icon-192.png',
              sizes: '192x192',
              type: 'image/png',
              purpose: 'any',
            },
            {
              src: 'img/icon-maskable-192.png',
              sizes: '192x192',
              type: 'image/png',
              purpose: 'maskable',
            },
            {
              src: 'img/icon-512.png',
              sizes: '512x512',
              type: 'image/png',
              purpose: 'any',
            },
            {
              src: 'img/icon-maskable-512.png',
              sizes: '512x512',
              type: 'image/png',
              purpose: 'maskable',
            },
          ],
          shortcuts: [
            {
              name: 'Поиск торрентов',
              short_name: 'Поиск',
              url: '/',
              icons: [
                {
                  src: 'img/icon-192.png',
                  sizes: '192x192',
                  type: 'image/png',
                },
              ],
            },
            {
              name: 'Статистика трекеров',
              short_name: 'Статистика',
              url: '/stats',
              icons: [
                {
                  src: 'img/icon-192.png',
                  sizes: '192x192',
                  type: 'image/png',
                },
              ],
            },
          ],
          share_target: {
            action: '/',
            method: 'GET',
            enctype: 'application/x-www-form-urlencoded',
            params: {
              title: 'title',
              text: 'text',
              url: 'url',
            },
          },
        },
        workbox: {
          navigateFallback: '/index.html',
          navigateFallbackDenylist: [
            /^\/api\//,
            /^\/swagger/,
            /^\/torznab/,
            /^\/sync\//,
            /^\/health/,
            /^\/opensearch\.xml/,
            /^\/openapi\.yaml/,
          ],
          globPatterns: ['**/*.{js,css,html,ico,png,svg,woff2,webmanifest}'],
          globIgnores: ['img/jacred.png', 'img/jacred-social-preview.png'],
          runtimeCaching: [
            {
              urlPattern: ({ url }) =>
                url.pathname.startsWith('/img/') ||
                url.pathname.startsWith('/fonts/'),
              handler: 'CacheFirst',
              options: {
                cacheName: 'jacred-static',
                expiration: {
                  maxEntries: 200,
                  maxAgeSeconds: 60 * 60 * 24 * 30,
                },
              },
            },
            {
              urlPattern: ({ url }) =>
                url.pathname.startsWith('/api/') ||
                url.pathname.startsWith('/stats/torrents') ||
                url.pathname.startsWith('/stats/meta') ||
                url.pathname.startsWith('/stats/tracks'),
              handler: 'NetworkOnly',
            },
          ],
        },
      }),
    ],
    resolve: {
      alias: {
        '@': path.resolve(__dirname, './src'),
      },
    },
    server: {
      proxy: {
        '/api': apiProxyTarget,
        '/stats/torrents': apiProxyTarget,
        '/stats/meta': apiProxyTarget,
        '/stats/tracks': apiProxyTarget,
        '/health': apiProxyTarget,
        '/opensearch.xml': apiProxyTarget,
        // openapi.yaml is served from web/public in dev (source of truth)
      },
    },
    build: {
      outDir: 'dist',
      emptyOutDir: true,
    },
    test: {
      environment: 'happy-dom',
      include: ['src/**/*.{test,spec}.ts'],
    },
  }
})
