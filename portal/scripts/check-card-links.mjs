import { expect } from '@playwright/test';
import assert from 'node:assert/strict';
export async function checkCardLinks(browser) {
  const context = await browser.newContext({
    viewport: { width: 1440, height: 1050 },
    permissions: ['clipboard-read', 'clipboard-write'],
    reducedMotion: 'reduce',
  });
  await context.addInitScript(() => {
    if (!localStorage.getItem('kanbada-session-v1'))
      localStorage.setItem(
        'kanbada-session-v1',
        JSON.stringify({
          id: 'owner',
          name: 'Alex Morgan',
          email: 'alex@studio.co',
          provider: 'google',
        }),
      );
  });
  const page = await context.newPage();
  await page.goto('http://localhost:4173');
  await page.getByRole('article', { name: 'Open Design the landing page' }).click();
  const drawer = page.getByRole('dialog', { name: 'Task details', exact: true });
  await expect(drawer).toBeVisible();
  await expect(page).toHaveURL(/card=KB-104/);
  const bounds = await drawer.boundingBox();
  assert.ok(Math.abs(bounds.x + bounds.width - 1440) < 2);
  assert.equal(bounds.y, 0);
  assert.equal(Math.round(bounds.height), 1050);
  await page.getByRole('button', { name: 'Copy card link', exact: true }).click();
  assert.match(await page.evaluate(() => navigator.clipboard.readText()), /card=KB-104/);
  await page.getByRole('tab', { name: /History/ }).click();
  await expect(page.getByText('Created card in In progress', { exact: true })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(drawer).toHaveCount(0);
  await page.goBack();
  await expect(drawer).toBeVisible();
  await page.reload();
  await expect(drawer).toBeVisible();
  await page.screenshot({ path: '/private/tmp/kanbada-card-drawer.png', fullPage: true });
  await page.getByRole('button', { name: 'Share link', exact: true }).click();
  await page.getByRole('button', { name: 'Create share link', exact: true }).click();
  const url = await page.getByRole('textbox', { name: 'Share link', exact: true }).inputValue();
  await page.getByRole('button', { name: 'Copy link', exact: true }).last().click();
  assert.equal(await page.evaluate(() => navigator.clipboard.readText()), url);
  const viewer = await context.newPage();
  await viewer.goto(url);
  await expect(
    viewer.getByRole('heading', { name: 'Design the landing page', exact: true }),
  ).toBeVisible();
  await expect(viewer.getByRole('button', { name: 'Save task', exact: true })).toHaveCount(0);
  await viewer.getByRole('tab', { name: 'History', exact: true }).click();
  await expect(viewer.getByText('Created card in In progress', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Revoke link', exact: true }).click();
  await expect(
    viewer.getByRole('heading', { name: 'Card unavailable', exact: true }),
  ).toBeVisible();
  await page.getByLabel('Link access').selectOption('members');
  await page.getByRole('button', { name: 'Create share link', exact: true }).click();
  const restricted = await page
    .getByRole('textbox', { name: 'Share link', exact: true })
    .inputValue();
  const snapshot = await context.storageState();
  const outsider = await browser.newContext({ storageState: snapshot });
  const guest = await outsider.newPage();
  await guest.goto(restricted);
  await guest.evaluate(() => localStorage.removeItem('kanbada-session-v1'));
  await guest.reload();
  await expect(
    guest.getByRole('button', { name: 'Continue with Microsoft', exact: true }),
  ).toBeVisible();
  await guest.getByRole('button', { name: 'Continue with Microsoft', exact: true }).click();
  await guest.getByRole('button', { name: /Alex Morgan alex@outlook.com/ }).click();
  await expect(guest.getByRole('alert')).toContainText('does not have access');
  await outsider.close();
  await page.keyboard.press('Escape');
  await page.getByRole('button', { name: 'Close task', exact: true }).click();
  await page.getByRole('button', { name: 'Add task', exact: true }).first().click();
  await page.getByLabel('Task title').fill('Uppercase check');
  const id = await page.locator('.card-drawer .modal-heading>span').innerText();
  assert.match(id, /^KB-[0-9A-F]{8}$/);
  await page.getByRole('button', { name: 'Save task', exact: true }).click();
  await page.goto('http://localhost:4173/?workspace=studio&card=' + id.toLowerCase());
  await expect(page.getByLabel('Task title')).toHaveValue('Uppercase check');
  await expect(page).toHaveURL(new RegExp(id));
  await page.setViewportSize({ width: 390, height: 844 });
  const mobile = await drawer.boundingBox();
  assert.equal(Math.round(mobile.width), 390);
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  await page.keyboard.press('Escape');
  await page.goto('http://localhost:4173/?workspace=studio&card=KB-MISSING');
  await expect(page.getByRole('status')).toContainText('unavailable');
  await expect(drawer).toHaveCount(0);
  await context.close();
  console.log(
    'PASS: right drawer, direct card URLs, copy, back/reload, uppercase IDs/legacy URLs, shared read-only view, revocation, authentication/member gate and mobile.',
  );
}
