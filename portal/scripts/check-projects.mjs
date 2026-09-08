import { expect } from '@playwright/test';
import assert from 'node:assert/strict';
export async function checkProjects(browser) {
  const page = await browser.newPage({
    viewport: { width: 1440, height: 1050 },
    reducedMotion: 'reduce',
  });
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  await page.goto('http://localhost:4173');
  await page.getByRole('heading', { name: 'Website redesign', exact: true }).waitFor();
  await page.getByRole('button', { name: 'Manage swimlanes', exact: true }).click();
  await page.getByRole('button', { name: 'Add swimlane', exact: true }).click();
  await page.getByLabel('Swimlane name 1', { exact: true }).fill('Urgent');
  await page.getByRole('button', { name: 'Add swimlane', exact: true }).click();
  await page.getByLabel('Swimlane name 2', { exact: true }).fill('Roadmap');
  await page.getByRole('button', { name: 'Save swimlanes', exact: true }).click();
  await expect(page.locator('.swimlane-heading[aria-expanded="true"]')).toHaveCount(3);
  const urgent = () => page.getByRole('region', { name: 'Urgent', exact: true });
  await urgent().locator('.add-card').first().click();
  await expect(page.getByLabel('Card swimlane')).toHaveValue('Urgent');
  await page.getByLabel('Task title').fill('Swimlane card');
  await page.getByLabel('Attach documents').setInputFiles({
    name: 'lane-notes.txt',
    mimeType: 'text/plain',
    buffer: Buffer.from('Project document'),
  });
  await expect(page.getByRole('button', { name: 'Download lane-notes.txt' })).toBeVisible();
  await page.getByRole('button', { name: 'Save task', exact: true }).click();
  await expect(urgent().getByRole('article', { name: 'Open Swimlane card' })).toBeVisible();
  await urgent().locator('.swimlane-heading').click();
  await expect(urgent().locator('.swimlane-heading')).toHaveAttribute('aria-expanded', 'false');
  await expect(page.getByRole('article', { name: 'Open Swimlane card' })).toHaveCount(0);
  await urgent().locator('.swimlane-heading').click();
  const source = page.getByRole('article', { name: 'Open Swimlane card' });
  await source.scrollIntoViewIfNeeded();
  await source.hover({ position: { x: 30, y: 30 } });
  const box = await source.boundingBox();
  await page.mouse.down();
  await page.mouse.move(box.x + 45, box.y + 40, { steps: 5 });
  const target = page
    .getByRole('region', { name: 'Roadmap', exact: true })
    .locator('.column-heading')
    .first();
  await target.scrollIntoViewIfNeeded();
  await target.hover();
  await target.hover();
  await page.mouse.up();
  await expect(
    page
      .getByRole('region', { name: 'Roadmap', exact: true })
      .getByRole('article', { name: 'Open Swimlane card' }),
  ).toBeVisible();
  await page.getByRole('button', { name: 'Manage swimlanes', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Delete Roadmap', exact: true })).toBeDisabled();
  await page.getByLabel('Swimlane name 2', { exact: true }).fill('Later');
  await page.getByRole('button', { name: 'Save swimlanes', exact: true }).click();
  await page.getByRole('article', { name: 'Open Swimlane card' }).click();
  await expect(page.getByLabel('Card swimlane')).toHaveValue('Later');
  await page.getByRole('tab', { name: /History/ }).click();
  await expect(page.getByRole('tabpanel')).toContainText('Swimlane: Urgent → Roadmap');
  await expect(page.getByRole('tabpanel')).toContainText('Swimlane: Roadmap → Later');
  await page.getByRole('button', { name: 'Close task', exact: true }).click();
  await page
    .getByRole('region', { name: 'Later', exact: true })
    .locator('.swimlane-heading')
    .click();
  await page.reload();
  await expect(page.locator('.swimlane-heading[aria-expanded="true"]')).toHaveCount(3);
  await page
    .getByRole('region', { name: 'No swimlane', exact: true })
    .locator('.swimlane-heading')
    .click();
  await page.screenshot({ path: '/private/tmp/kanbada-swimlanes.png', fullPage: true });
  const options = () => page.getByRole('button', { name: 'Project options', exact: true }).click();
  await options();
  await page.getByRole('button', { name: 'Archive project', exact: true }).click();
  await expect(page.getByRole('dialog')).toContainText('12 cards');
  await page.getByRole('button', { name: 'Confirm archive', exact: true }).click();
  await expect(
    page.locator('.project-nav').getByRole('button', { name: 'Website redesign', exact: true }),
  ).toHaveCount(0);
  await page.getByRole('button', { name: 'Overview', exact: true }).click();
  await expect(page.locator('.kpi-card').first().locator('strong')).toHaveText('0');
  await page.getByRole('button', { name: 'Projects', exact: true }).click();
  await page.getByRole('button', { name: 'Open project Website redesign', exact: true }).click();
  await expect(page.locator('.archive-banner')).toBeVisible();
  await page.getByRole('button', { name: 'Restore project', exact: true }).click();
  await expect(
    page.locator('.project-nav').getByRole('button', { name: 'Website redesign', exact: true }),
  ).toBeVisible();
  await options();
  await page.getByRole('button', { name: 'Delete project', exact: true }).click();
  await page.getByRole('button', { name: 'Keep project', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Website redesign', exact: true })).toBeVisible();
  await options();
  await page.getByRole('button', { name: 'Delete project', exact: true }).click();
  await page.getByRole('button', { name: 'Confirm deletion', exact: true }).click();
  await expect(
    page.locator('.project-nav').getByRole('button', { name: 'Website redesign', exact: true }),
  ).toHaveCount(0);
  const state = await page.evaluate(() => JSON.parse(localStorage.getItem('kanbada-v1')));
  assert.equal(state.tasks.length, 0);
  assert.equal(state.swimlanes.length, 0);
  await expect
    .poll(() =>
      page.evaluate(
        () =>
          new Promise((resolve, reject) => {
            const request = indexedDB.open('kanbada-files', 1);
            request.onsuccess = () => {
              const db = request.result;
              const keys = db.transaction('files', 'readonly').objectStore('files').getAllKeys();
              keys.onsuccess = () => {
                resolve(keys.result.length);
                db.close();
              };
              keys.onerror = () => reject(keys.error);
            };
          }),
      ),
    )
    .toBe(0);
  for (const name of ['Mobile app', 'Brand identity']) {
    await page.locator('.project-nav').getByRole('button', { name, exact: true }).click();
    await options();
    await page.getByRole('button', { name: 'Delete project', exact: true }).click();
    await page.getByRole('button', { name: 'Confirm deletion', exact: true }).click();
  }
  await page.reload();
  await expect(page.getByRole('heading', { name: 'My activities', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'New project', exact: true }).click();
  await page.getByLabel('Project name').fill('Fresh start');
  await page
    .getByRole('dialog')
    .getByRole('button', { name: 'Create project', exact: true })
    .click();
  await expect(page.getByRole('heading', { name: 'Fresh start', exact: true })).toBeVisible();
  assert.deepEqual(errors, []);
  await page.close();
  console.log(
    'PASS: swimlane creation, default expansion, collapse, cross-lane drag, history, renaming, reload, archive/restore, delete cancellation, cascading deletion, file cleanup, and empty-workspace recovery.',
  );
}
