import { expect, test } from '@playwright/test'

test.beforeEach(async ({ page }) => {
  await page.route('**/api/v1.0/conf**', (route) =>
    route.fulfill({ json: { apikey: true } }),
  )
  await page.route('**/api/v1.0/torrents**', (route) =>
    route.fulfill({
      json: [
        {
          tracker: 'rutor',
          title: 'Example release',
          sizeName: '1.2 GB',
          sid: 10,
          pir: 1,
          magnet:
            'magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567',
          createTime: '2026-07-23T10:00:00Z',
        },
      ],
    }),
  )
  await page.route('**/stats/torrents**', (route) =>
    route.fulfill({
      json: [{ trackerName: 'rutor', newtor: 2, alltorrents: 10 }],
    }),
  )
  await page.route('**/stats/meta**', (route) =>
    route.fulfill({ json: { ok: true, updatedAt: '2026-07-23T10:00:00Z' } }),
  )
})

test('searches and exposes result actions', async ({ page }) => {
  await page.goto('/')
  await page.getByRole('searchbox').fill('example')
  await page.getByRole('button', { name: /искать|search/i }).click()
  await expect(page.getByText('Example release')).toBeVisible()
  await expect(
    page.getByRole('link', { name: /открыть в клиенте|open in client/i }),
  ).toHaveAttribute('href', /^magnet:\?/)
})

test('navigates stats and switches locale/theme', async ({ page }) => {
  await page.goto('/stats')
  await expect(
    page.getByRole('heading', { name: /статистика|stats/i }),
  ).toBeVisible()
  await page.getByRole('button', { name: 'EN' }).click()
  await expect(page.getByRole('heading', { name: /tracker stats/i })).toBeVisible()
  const initialTheme = await page.locator('html').getAttribute('data-theme')
  await page
    .getByRole('button', { name: /dark theme|light theme/i })
    .click()
  await expect(page.locator('html')).not.toHaveAttribute(
    'data-theme',
    initialTheme ?? '',
  )
})

test('restores search URL state on browser navigation', async ({ page }) => {
  await page.goto('/?s=first&sort=size&view=list')
  await expect(page.getByRole('searchbox')).toHaveValue('first')
  await expect(page.locator('[data-layout="list"]')).toBeVisible()

  await page.goto('/?s=second')
  await expect(page.getByRole('searchbox')).toHaveValue('second')
  await page.goBack()
  await expect(page.getByRole('searchbox')).toHaveValue('first')
  await expect(page.locator('[data-layout="list"]')).toBeVisible()
})
