import { expect } from '@playwright/test';
import assert from 'node:assert/strict';
export async function checkNotifications(browser) {
  const page = await browser.newPage({
    viewport: { width: 1440, height: 1050 },
    reducedMotion: 'reduce',
  });
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  await page.goto('http://localhost:4173');
  await page.getByRole('heading', { name: 'Website redesign', exact: true }).waitFor();
  await expect(page.locator('.unread-indicator')).toHaveCount(1);
  await page.getByRole('button', { name: 'Notifications', exact: true }).click();
  await expect(page.locator('.notification-item')).toHaveCount(1);
  await page.getByRole('button', { name: 'Clear notifications', exact: true }).click();
  await expect(page.getByText('You’re all caught up.', { exact: true })).toBeVisible();
  await expect(
    page.getByRole('button', { name: 'Clear notifications', exact: true }),
  ).toBeDisabled();
  await expect(page.locator('.unread-indicator')).toHaveCount(0);
  await page.getByRole('button', { name: 'Close dialog', exact: true }).click();
  await page.reload();
  await expect(page.locator('.unread-indicator')).toHaveCount(0);
  await page.getByRole('article', { name: 'Open Design the landing page', exact: true }).click();
  await page.getByLabel('Priority', { exact: true }).selectOption('Low');
  await page.getByRole('button', { name: 'Save task', exact: true }).click();
  await expect(page.locator('.unread-indicator')).toHaveCount(1);
  const before = await page.evaluate(() => JSON.parse(localStorage.getItem('kanbada-v1')));
  await page.getByRole('button', { name: 'Notifications', exact: true }).click();
  await expect(page.locator('.notification-item')).toHaveCount(1);
  await page.locator('.notification-item .icon-button').click();
  await expect(page.locator('.notification-item')).toHaveCount(0);
  await page.getByRole('button', { name: 'Close dialog', exact: true }).click();
  const after = await page.evaluate(() => JSON.parse(localStorage.getItem('kanbada-v1')));
  assert.deepEqual(after.activity, before.activity);
  assert.deepEqual(after.tasks, before.tasks);
  assert.deepEqual(after.notifications, []);
  await page.getByRole('article', { name: 'Open Design the landing page', exact: true }).click();
  await page.getByLabel('Priority', { exact: true }).selectOption('High');
  await page.getByRole('button', { name: 'Save task', exact: true }).click();
  await page.getByRole('button', { name: 'Manage labels', exact: true }).click();
  await page.getByRole('button', { name: 'Save labels', exact: true }).click();
  await page.getByRole('button', { name: 'Notifications', exact: true }).click();
  await expect(page.locator('.notification-item')).toHaveCount(2);
  await page.screenshot({ path: '/private/tmp/kanbada-notifications.png' });
  await page.getByRole('button', { name: 'Clear notifications', exact: true }).click();
  await page.getByRole('button', { name: 'Close dialog', exact: true }).click();
  await page.getByLabel('Interface language').selectOption('pt-PT');
  await page.getByRole('button', { name: 'Notificações', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Limpar notificações', exact: true }),
  ).toBeVisible();
  await expect(page.getByText('Está tudo em dia.', { exact: true })).toBeVisible();
  assert.deepEqual(errors, []);
  await page.close();
  console.log(
    'PASS: clear/dismiss notifications, unread indicator, reload persistence, new update notifications, unchanged card history and activity, Portuguese panel.',
  );
}
