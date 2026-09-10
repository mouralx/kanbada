import { expect } from '@playwright/test';
import assert from 'node:assert/strict';
export async function checkThemes(browser) {
  const context = await browser.newContext({
    viewport: { width: 1440, height: 1050 },
    colorScheme: 'light',
  });
  const page = await context.newPage();
  await page.goto('http://localhost:4173');
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await expect(page.getByRole('button', { name: 'Theme: System', exact: true })).toBeVisible();
  await page.emulateMedia({ colorScheme: 'dark' });
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await page.getByRole('button', { name: 'Theme: System', exact: true }).click();
  await page.getByRole('button', { name: 'Light', exact: true }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await page.getByRole('button', { name: 'Theme: Light', exact: true }).click();
  await page.getByRole('button', { name: 'Dark', exact: true }).click();
  await page.emulateMedia({ colorScheme: 'light' });
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await page.getByRole('button', { name: 'Continue with Google', exact: true }).click();
  await page.getByRole('button', { name: /Alex Morgan alex@studio.co/ }).click();
  await expect(page.locator('.task-card')).toHaveCount(11);
  await page.screenshot({ path: '/private/tmp/kanbada-dark-board.png', fullPage: true });
  await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await page.screenshot({ path: '/private/tmp/kanbada-dark-dashboard.png', fullPage: true });
  await page.getByRole('button', { name: 'Settings', exact: true }).click();
  await expect(page.getByRole('dialog').getByLabel('Theme', { exact: true })).toHaveValue('dark');
  await page.getByRole('dialog').getByLabel('Theme', { exact: true }).selectOption('system');
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await page.emulateMedia({ colorScheme: 'dark' });
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await page.keyboard.press('Escape');
  const other = await context.newPage();
  await other.goto('http://localhost:4173');
  await other.getByLabel('Theme', { exact: true }).selectOption('light');
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await other.close();
  await page.getByLabel('Interface language').selectOption('pt-PT');
  await page.getByLabel('Tema', { exact: true }).selectOption('dark');
  await expect(
    page.getByLabel('Tema', { exact: true }).getByRole('option', { name: 'Escuro', exact: true }),
  ).toBeAttached();
  await page.setViewportSize({ width: 390, height: 844 });
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  await page.screenshot({ path: '/private/tmp/kanbada-dark-mobile.png', fullPage: true });
  await context.close();
  console.log(
    'PASS: light/dark/system, OS changes, explicit override, reload, cross-tab sync, settings, Portuguese and mobile.',
  );
}
