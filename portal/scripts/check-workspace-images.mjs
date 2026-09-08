import { expect } from '@playwright/test';
import assert from 'node:assert/strict';
export async function checkWorkspaceImages(browser) {
  const page = await browser.newPage({ viewport: { width: 1440, height: 1050 } });
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  await page.goto('http://localhost:4173');
  const image = await page.evaluate(() => {
    const c = document.createElement('canvas');
    c.width = 1200;
    c.height = 500;
    const ctx = c.getContext('2d');
    const g = ctx.createLinearGradient(0, 0, 1200, 500);
    g.addColorStop(0, '#104879');
    g.addColorStop(1, '#bba0de');
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, 1200, 500);
    return c.toDataURL('image/png').split(',')[1];
  });
  const file = {
    name: 'workspace.png',
    mimeType: 'image/png',
    buffer: Buffer.from(image, 'base64'),
  };
  const editor = async () => {
    await page.getByRole('button', { name: 'Settings', exact: true }).click();
    await page.getByRole('button', { name: 'Workspace appearance', exact: true }).click();
  };
  await editor();
  await page.getByLabel('Workspace icon', { exact: true }).setInputFiles(file);
  await expect(page.getByAltText('Workspace icon preview')).toBeVisible();
  await page.getByLabel('Workspace banner', { exact: true }).setInputFiles(file);
  await expect(page.getByAltText('Workspace banner preview')).toBeVisible();
  await page.getByLabel('Banner position').fill('75');
  await page.getByRole('button', { name: 'Save workspace images', exact: true }).click();
  await expect(page.locator('.workspace-icon img')).toBeVisible();
  await expect(page.locator('.workspace-banner-image')).toBeVisible();
  const stored = await page.evaluate(
    () => JSON.parse(localStorage.getItem('kanbada-v1')).workspace,
  );
  assert.equal(stored.bannerPosition, 75);
  assert.ok(stored.icon.startsWith('data:image/jpeg;'));
  await page.reload();
  await expect(page.locator('.workspace-icon img')).toBeVisible();
  await expect(page.locator('.workspace-banner-image')).toHaveCSS('object-position', '50% 75%');
  await page.getByRole('button', { name: 'Overview', exact: true }).click();
  await expect(page.locator('.workspace-overview-cover img')).toBeVisible();
  await page.getByRole('button', { name: 'Switch workspace', exact: true }).click();
  await expect(page.locator('.workspace-option-icon img')).toHaveCount(1);
  await page.getByLabel('New workspace name').fill('Image isolation');
  await page.getByRole('button', { name: 'Create workspace', exact: true }).click();
  await expect(page.locator('.workspace-icon img')).toHaveCount(0);
  await expect(page.locator('.workspace-overview-cover')).toHaveCount(0);
  await page.getByRole('button', { name: 'Switch workspace', exact: true }).click();
  await page.getByRole('button', { name: 'Switch to My Workspace', exact: true }).click();
  await expect(page.locator('.workspace-icon img')).toBeVisible();
  await editor();
  await page.getByRole('button', { name: 'Remove icon', exact: true }).click();
  await page.keyboard.press('Escape');
  await expect(page.locator('.workspace-icon img')).toBeVisible();
  await editor();
  await page
    .getByLabel('Workspace icon', { exact: true })
    .setInputFiles({ name: 'bad.png', mimeType: 'image/png', buffer: Buffer.from('not an image') });
  await expect(page.getByRole('alert')).toContainText('could not be opened');
  await page.getByLabel('Workspace icon', { exact: true }).setInputFiles({
    name: 'large.png',
    mimeType: 'image/png',
    buffer: Buffer.alloc(10 * 1024 * 1024 + 1),
  });
  await expect(page.getByRole('alert')).toContainText('smaller than 10 MB');
  await page.getByRole('button', { name: 'Remove icon', exact: true }).click();
  await page.getByRole('button', { name: 'Remove banner', exact: true }).click();
  await page.getByRole('button', { name: 'Save workspace images', exact: true }).click();
  await page.reload();
  await expect(page.locator('.workspace-icon img')).toHaveCount(0);
  await expect(page.locator('.workspace-banner-image')).toHaveCount(0);
  await page.getByLabel('Interface language').selectOption('pt-PT');
  await page.getByRole('button', { name: 'Definições', exact: true }).click();
  await page.getByRole('button', { name: 'Aparência do espaço de trabalho', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Guardar imagens do espaço de trabalho', exact: true }),
  ).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  assert.deepEqual(errors, []);
  await page.close();
  console.log(
    'PASS: workspace icon/banner uploads, position, migration/reload, switcher/overview, isolation, cancellation, invalid/oversized files, removal, Portuguese and mobile.',
  );
}
