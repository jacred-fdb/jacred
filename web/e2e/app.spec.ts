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
        {
          tracker: 'rutracker',
          title: 'Second release',
          sizeName: '2.0 GB',
          sid: 5,
          pir: 2,
          magnet:
            'magnet:?xt=urn:btih:abcdef0123456789abcdef0123456789abcdef01',
          createTime: '2026-07-22T10:00:00Z',
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
    page.getByRole('link', { name: /открыть в клиенте|open in client/i }).first(),
  ).toHaveAttribute('href', /^magnet:\?/)
})

test('keeps scroll near top after search (no false re-pin)', async ({
  page,
}) => {
  await page.goto('/')
  await page.getByRole('searchbox').fill('example')
  await page.getByRole('button', { name: /искать|search/i }).click()
  await expect(page.getByText('Example release')).toBeVisible()

  const y0 = await page.evaluate(() => window.scrollY)
  expect(y0).toBeLessThan(8)

  await page.waitForTimeout(160)
  const y1 = await page.evaluate(() => window.scrollY)
  expect(Math.abs(y1 - y0)).toBeLessThan(8)
})

test('toggles list and cards without losing the visible title', async ({
  page,
}) => {
  await page.goto('/?s=example&view=list')
  await expect(page.locator('[data-layout="list"]').first()).toBeVisible()
  await expect(page.getByText('Example release')).toBeVisible()

  await page.getByLabel(/^Cards$|^Карточки$/).click()
  await expect(page.locator('[data-layout="card"]').first()).toBeVisible()
  await expect(page.getByText('Example release')).toBeVisible()

  await page.getByLabel(/^List$|^Список$/).click()
  await expect(page.locator('[data-layout="list"]').first()).toBeVisible()
  await expect(page.getByText('Example release')).toBeVisible()
})

test('navigates stats and switches locale/theme', async ({ page }) => {
  await page.goto('/stats')
  await expect(
    page.getByRole('heading', { name: /статистика|stats|tracker stats/i }),
  ).toBeVisible()

  const langToEn = page.getByRole('button', { name: 'EN' })
  const langToRu = page.getByRole('button', { name: 'RU' })
  if (await langToEn.isVisible()) {
    await langToEn.click()
    await expect(
      page.getByRole('heading', { name: /tracker stats/i }),
    ).toBeVisible()
  } else {
    await langToRu.click()
    await expect(
      page.getByRole('heading', { name: /статистика/i }),
    ).toBeVisible()
    await page.getByRole('button', { name: 'EN' }).click()
    await expect(
      page.getByRole('heading', { name: /tracker stats/i }),
    ).toBeVisible()
  }

  const initialTheme = await page.locator('html').getAttribute('data-theme')
  await page
    .getByRole('button', { name: /dark theme|light theme|тёмная|светлая/i })
    .click()
  await expect(page.locator('html')).not.toHaveAttribute(
    'data-theme',
    initialTheme ?? '',
  )
})

test('restores search URL state on browser navigation', async ({ page }) => {
  await page.goto('/?s=first&sort=size&view=list')
  await expect(page.getByRole('searchbox')).toHaveValue('first')
  await expect(page.locator('[data-layout="list"]').first()).toBeVisible()

  await page.goto('/?s=second')
  await expect(page.getByRole('searchbox')).toHaveValue('second')
  await page.goBack()
  await expect(page.getByRole('searchbox')).toHaveValue('first')
  await expect(page.locator('[data-layout="list"]').first()).toBeVisible()
})
