import {
  defineConfig,
  minimal2023Preset,
} from '@vite-pwa/assets-generator/config'

/**
 * Generate PWA icons from the JacRed brand image into `public/img/`.
 * Run: npm run generate-pwa-assets
 * Also wired via VitePWA `pwaAssets` on build.
 */
export default defineConfig({
  headLinkOptions: {
    preset: '2023',
    basePath: '/img/',
  },
  preset: {
    ...minimal2023Preset,
    transparent: {
      sizes: [32, 64, 192, 512],
      favicons: [[32, 'favicon.ico']],
      padding: 0.05,
    },
    maskable: {
      sizes: [192, 512],
      padding: 0.1,
    },
    apple: {
      sizes: [180],
      padding: 0.1,
    },
    assetName(type, size) {
      const width = typeof size === 'number' ? size : size.width
      switch (type) {
        case 'transparent':
          if (width === 32) return 'icon-32.png'
          if (width === 64) return 'icon-64.png'
          if (width === 192) return 'icon-192.png'
          if (width === 512) return 'icon-512.png'
          return `icon-${width}.png`
        case 'maskable':
          return `icon-maskable-${width}.png`
        case 'apple':
          return 'apple-touch-icon.png'
        default:
          return `icon-${width}.png`
      }
    },
  },
  images: ['public/img/jacred.png'],
})
