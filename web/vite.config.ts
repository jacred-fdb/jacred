import path from 'node:path'
import tailwindcss from '@tailwindcss/vite'
import vue from '@vitejs/plugin-vue'
import { loadEnv } from 'vite'
import { defineConfig } from 'vitest/config'
import { VitePWA } from 'vite-plugin-pwa'
import {
  isApiPathname,
  NAVIGATE_FALLBACK_DENYLIST,
  VITE_API_PROXY_PATHS,
} from './src/lib/spa-api-bypass.js'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const apiProxyTarget =
    env.VITE_API_PROXY_TARGET || 'http://localhost:9117'

  const proxy = Object.fromEntries(
    VITE_API_PROXY_PATHS.map((prefix: (typeof VITE_API_PROXY_PATHS)[number]) => [
      prefix,
      apiProxyTarget,
    ]),
  )

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
          background_color: '#000000',
          theme_color: '#000000',
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
          navigateFallbackDenylist: NAVIGATE_FALLBACK_DENYLIST,
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
              urlPattern: ({ url }) => isApiPathname(url.pathname),
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
      proxy,
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
