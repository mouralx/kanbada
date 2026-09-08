import { expect } from '@playwright/test';
import assert from 'node:assert/strict';
export async function checkNewFeatures(browser) {
  const page = await browser.newPage({
    viewport: { width: 1440, height: 1050 },
    reducedMotion: 'reduce',
  });
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  await page.goto('http://localhost:4173');
  await page.getByRole('heading', { name: 'Website redesign', exact: true }).waitFor();
  const openCard = () =>
    page.getByRole('article', { name: 'Open Design the landing page', exact: true }).click();
  const dialog = () => page.getByRole('dialog');
  const dragCard = async () => {
    const source = page.getByRole('article', { name: 'Open Design the landing page', exact: true });
    await source.scrollIntoViewIfNeeded();
    await source.hover({ position: { x: 30, y: 30 } });
    const box = await source.boundingBox();
    await page.mouse.down();
    await page.mouse.move(box.x + 45, box.y + 40, { steps: 5 });
    const target = page.locator('.kanban-column').last().locator('.column-heading');
    await target.scrollIntoViewIfNeeded();
    await target.hover();
    await target.hover();
    await page.mouse.up();
  };

  await openCard();
  await page.getByRole('checkbox', { name: 'Assign Sophie Chen', exact: true }).check();
  await page.getByLabel('Card bucket').selectOption('Discovery');
  await page.getByLabel('Attach documents').setInputFiles({
    name: 'project-brief.txt',
    mimeType: 'text/plain',
    buffer: Buffer.from('Kanbada attachment verification'),
  });
  await expect(
    page.getByRole('button', { name: 'Download project-brief.txt', exact: true }),
  ).toBeVisible();
  await page.getByRole('button', { name: 'Save task', exact: true }).click();
  await expect(dialog()).toHaveCount(0);
  await openCard();
  await page.getByRole('tab', { name: /History/ }).click();
  await expect(page.getByRole('tabpanel')).toContainText(
    'Assignees: Alex Morgan → Alex Morgan, Sophie Chen',
  );
  await expect(page.getByRole('tabpanel')).toContainText('Attached file: project-brief.txt');
  await expect(page.getByRole('tabpanel')).toContainText('Bucket: Experience → Discovery');
  await page.getByRole('tab', { name: 'Details', exact: true }).click();
  const downloadEvent = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download project-brief.txt', exact: true }).click();
  const download = await downloadEvent;
  assert.equal(download.suggestedFilename(), 'project-brief.txt');
  const stream = await download.createReadStream();
  let contents = '';
  for await (const chunk of stream) contents += chunk.toString();
  assert.equal(contents, 'Kanbada attachment verification');
  await page.getByRole('button', { name: 'Close task', exact: true }).click();
  await page.reload();
  await openCard();
  await expect(
    page.getByRole('checkbox', { name: 'Assign Sophie Chen', exact: true }),
  ).toBeChecked();
  await expect(
    page.getByRole('button', { name: 'Download project-brief.txt', exact: true }),
  ).toBeVisible();
  await page
    .getByRole('button', { name: 'Remove attachment project-brief.txt', exact: true })
    .click();
  await page.getByRole('button', { name: 'Save task', exact: true }).click();
  await openCard();
  await page.getByRole('tab', { name: /History/ }).click();
  await expect(page.getByRole('tabpanel')).toContainText('Removed attachment: project-brief.txt');
  await page.getByRole('button', { name: 'Close task', exact: true }).click();
  // Configurable workflow, rename safety, completed semantics, and validation.
  await page.getByRole('button', { name: 'Manage statuses', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Delete In progress', exact: true }),
  ).toBeDisabled();
  await page.getByRole('button', { name: 'Add status', exact: true }).click();
  await page.getByLabel('Status name 5', { exact: true }).fill('Published');
  await page.locator('.definition-row').last().getByRole('checkbox').check();
  await page.getByRole('button', { name: 'Save statuses', exact: true }).click();
  await expect(page.locator('.kanban-column')).toHaveCount(5);
  await dragCard();
  await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await expect(
    page
      .locator('.kpi-card')
      .filter({ has: page.getByText('Completed', { exact: true }) })
      .locator('strong'),
  ).toHaveText('4');
  await page.getByRole('button', { name: 'Manage statuses', exact: true }).click();
  await page.getByLabel('Status name 5', { exact: true }).fill('Released');
  await page.getByRole('button', { name: 'Save statuses', exact: true }).click();
  await page.getByRole('button', { name: 'Board', exact: true }).click();
  await openCard();
  await expect(page.getByLabel('Status', { exact: true })).toHaveValue('Released');
  await page.getByRole('tab', { name: /History/ }).click();
  await expect(page.getByRole('tabpanel')).toContainText('Status: Published → Released');
  await page.screenshot({ path: '/private/tmp/kanbada-history.png' });
  await page.getByRole('button', { name: 'Close task', exact: true }).click();
  await page.getByRole('button', { name: 'Manage statuses', exact: true }).click();
  await page.getByLabel('Status name 5', { exact: true }).fill('Done');
  await page.getByRole('button', { name: 'Save statuses', exact: true }).click();
  await expect(page.getByRole('alert')).toHaveText('Names must be unique.');
  await page.getByRole('button', { name: 'Close dialog', exact: true }).click();
  await page.getByRole('button', { name: 'Manage buckets', exact: true }).click();
  await page.getByRole('button', { name: 'Add bucket', exact: true }).click();
  await page.getByLabel('Bucket name 3', { exact: true }).fill('Launch');
  await page.getByRole('button', { name: 'Save buckets', exact: true }).click();
  await page.getByLabel('Group board by').selectOption('Bucket');
  await dragCard();
  await openCard();
  await expect(page.getByLabel('Card bucket')).toHaveValue('Launch');
  await expect(page.getByLabel('Status', { exact: true })).toHaveValue('Released');
  await page.getByRole('button', { name: 'Close task', exact: true }).click();
  await page.getByRole('button', { name: 'Manage buckets', exact: true }).click();
  await page.getByLabel('Bucket name 3', { exact: true }).fill('Go live');
  await page.getByRole('button', { name: 'Move Go live up', exact: true }).click();
  await page.getByRole('button', { name: 'Save buckets', exact: true }).click();
  await page.getByRole('button', { name: 'Filter', exact: true }).click();
  await page.getByLabel('Filter by bucket').selectOption('Go live');
  await expect(page.locator('.task-card')).toHaveCount(1);
  await page.getByRole('button', { name: 'Clear filters', exact: true }).click();
  await page.getByRole('button', { name: 'Filter', exact: true }).click();
  await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await page.screenshot({ path: '/private/tmp/kanbada-project-dashboard.png', fullPage: true });
  await page.getByRole('button', { name: 'Overview', exact: true }).click();
  await expect(
    page.getByRole('region', { name: 'Workspace dashboard', exact: true }),
  ).toBeVisible();
  await expect(page.locator('.kpi-card').first().locator('strong')).toHaveText('11');
  await page
    .locator('.kpi-card')
    .filter({ has: page.getByText('Completed', { exact: true }) })
    .click();
  await expect(page.locator('.metric-task')).toHaveCount(4);
  await page.getByRole('button', { name: 'Close metric details', exact: true }).click();
  await page.screenshot({ path: '/private/tmp/kanbada-workspace-dashboard.png', fullPage: true });

  // Profile photo preview, validation, persistence, and removal.
  const photo = await page.screenshot({ clip: { x: 250, y: 10, width: 64, height: 64 } });
  await page.getByRole('button', { name: 'Your profile', exact: true }).click();
  await page
    .getByLabel('Profile picture')
    .setInputFiles({ name: 'avatar.png', mimeType: 'image/png', buffer: photo });
  await expect(page.getByAltText('Profile photo preview', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Save profile', exact: true }).click();
  await expect(page.locator('.top-actions .avatar img')).toBeVisible();
  await page.reload();
  await expect(page.locator('.top-actions .avatar img')).toBeVisible();
  await page.getByRole('button', { name: 'Your profile', exact: true }).click();
  await page.getByLabel('Profile picture').setInputFiles({
    name: 'invalid.txt',
    mimeType: 'text/plain',
    buffer: Buffer.from('not an image'),
  });
  await expect(page.getByRole('alert')).toContainText('Choose a JPG');
  await page.getByRole('button', { name: 'Remove photo', exact: true }).click();
  await page.getByRole('button', { name: 'Save profile', exact: true }).click();
  await expect(page.locator('.top-actions .avatar img')).toHaveCount(0);
  // Global metrics count cards from other projects without polluting project totals.
  await page.getByRole('button', { name: 'Mobile app', exact: true }).click();
  await page.getByRole('button', { name: 'Add task', exact: true }).first().click();
  await page.getByLabel('Task title').fill('Mobile-only card');
  await page.getByRole('button', { name: 'Save task', exact: true }).click();
  await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await expect(page.locator('.kpi-card').first().locator('strong')).toHaveText('1');
  await page.getByRole('button', { name: 'Overview', exact: true }).click();
  await expect(page.locator('.kpi-card').first().locator('strong')).toHaveText('12');
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: '/private/tmp/kanbada-dashboard-mobile.png', fullPage: true });
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
  assert.deepEqual(errors, []);
  await page.close();
  // Existing local data is upgraded without losing cards or changing their assignments.
  const legacy = await browser.newPage();
  await legacy.goto('http://localhost:4173');
  await legacy.getByRole('heading', { name: 'Website redesign', exact: true }).waitFor();
  const { initialState } = await legacy.evaluate(() => import('/src/data.ts'));
  delete initialState.statuses;
  delete initialState.buckets;
  for (const task of initialState.tasks) {
    task.assignee = task.assignees[0];
    delete task.assignees;
    delete task.history;
  }
  await legacy.evaluate(
    (state) => localStorage.setItem('kanbada-v1', JSON.stringify(state)),
    initialState,
  );
  await legacy.reload();
  await legacy.getByRole('article', { name: 'Open Design the landing page' }).click();
  await expect(
    legacy.getByRole('checkbox', { name: 'Assign Alex Morgan', exact: true }),
  ).toBeChecked();
  await legacy.getByRole('tab', { name: /History/ }).click();
  await expect(legacy.getByRole('tabpanel')).toContainText('History tracking started');
  await legacy.close();
  console.log(
    'PASS: multiple assignees, history, file upload/download/removal, reload persistence, configurable statuses and buckets, rename/reorder safety, bucket drag/drop/filter, live KPI completion, all-project totals, profile picture upload/removal/validation, mobile, legacy migration.',
  );
}
