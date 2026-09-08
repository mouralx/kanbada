import { expect } from '@playwright/test';
import assert from 'node:assert/strict';
export async function checkLabels(browser) {
  const page = await browser.newPage({
    viewport: { width: 1440, height: 1050 },
    reducedMotion: 'reduce',
  });
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  await page.goto('http://localhost:4173');
  await page.getByRole('heading', { name: 'Website redesign', exact: true }).waitFor();
  const card = () =>
    page.getByRole('article', { name: 'Open Design the landing page', exact: true });
  await expect(card().locator('.label-pill')).toHaveText(['Design']);
  await card().click();
  await page.getByRole('checkbox', { name: 'Apply label Research', exact: true }).check();
  await page
    .getByRole('dialog', { name: 'Task details' })
    .getByRole('button', { name: 'Manage labels', exact: true })
    .click();
  await page.getByRole('button', { name: 'Add label', exact: true }).click();
  await page.getByLabel('Label name 6', { exact: true }).fill('Release ready');
  await page.getByLabel('Color for Release ready', { exact: true }).fill('#c98752');
  await page.getByRole('button', { name: 'Save labels', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Task details' })).toBeVisible();
  await expect(
    page.getByRole('checkbox', { name: 'Apply label Research', exact: true }),
  ).toBeChecked();
  await page.getByRole('checkbox', { name: 'Apply label Release ready', exact: true }).check();
  await page.getByRole('button', { name: 'Save task', exact: true }).click();
  await expect(card().locator('.label-pill')).toHaveText(['Design', 'Research', 'Release ready']);
  await page.getByRole('button', { name: 'List', exact: true }).click();
  const row = page
    .locator('.list-row')
    .filter({ has: page.getByText('Design the landing page', { exact: true }) });
  await expect(row.locator('.label-pill')).toHaveText(['Design', 'Research', 'Release ready']);
  await row.click();
  await page.getByRole('tab', { name: /History/ }).click();
  await expect(page.getByRole('tabpanel')).toContainText(
    'Labels: Design → Design, Research, Release ready',
  );
  await page.getByRole('button', { name: 'Close task', exact: true }).click();
  await page.getByRole('button', { name: 'Manage labels', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Delete Release ready', exact: true }),
  ).toBeDisabled();
  await page.getByLabel('Label name 6', { exact: true }).fill('Launch ready');
  await page.getByLabel('Color for Launch ready', { exact: true }).fill('#507db5');
  await page.getByRole('button', { name: 'Move Launch ready up', exact: true }).click();
  await page.getByRole('button', { name: 'Save labels', exact: true }).click();
  await expect(row.locator('.label-pill').last()).toHaveText('Launch ready');
  await expect(row.locator('.label-pill').last()).toHaveCSS(
    'background-color',
    'rgba(80, 125, 181, 0.125)',
  );
  await page.getByRole('button', { name: 'Manage labels', exact: true }).click();
  await page.getByLabel('Label name 5', { exact: true }).fill('design');
  await page.getByRole('button', { name: 'Save labels', exact: true }).click();
  await expect(page.getByRole('alert')).toHaveText('Names must be unique.');
  await page.getByRole('button', { name: 'Close dialog', exact: true }).click();
  await page.reload();
  await expect(card().locator('.label-pill')).toHaveText(['Design', 'Research', 'Launch ready']);
  await card().click();
  await page.getByRole('tab', { name: /History/ }).click();
  await expect(page.getByRole('tabpanel')).toContainText('Launch ready');
  await page.getByRole('tab', { name: 'Details', exact: true }).click();
  await page
    .getByRole('dialog', { name: 'Task details' })
    .getByRole('button', { name: 'Manage labels', exact: true })
    .click();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog', { name: 'Task details' })).toBeVisible();
  for (const name of ['Design', 'Research', 'Launch ready'])
    await page.getByRole('checkbox', { name: 'Apply label ' + name, exact: true }).uncheck();
  await page.getByRole('button', { name: 'Save task', exact: true }).click();
  await page.getByRole('button', { name: 'Manage labels', exact: true }).click();
  await page.getByRole('button', { name: 'Delete Launch ready', exact: true }).click();
  await page.getByRole('button', { name: 'Save labels', exact: true }).click();
  await page.reload();
  await expect(card().locator('.label-pill')).toHaveCount(0);
  await page.getByRole('button', { name: 'Manage labels', exact: true }).click();
  await expect(page.getByRole('textbox', { name: /Label name/ })).toHaveCount(5);
  await page.getByRole('button', { name: 'Close dialog', exact: true }).click();
  await page.getByLabel('Interface language').selectOption('pt-PT');
  await page.getByRole('button', { name: 'Gerir etiquetas', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Adicionar etiqueta', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Fechar janela', exact: true }).click();
  await page.getByLabel('Idioma da interface').selectOption('en-US');
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole('button', { name: 'List', exact: true }).click();
  await page.screenshot({ path: '/private/tmp/kanbada-label-list-mobile.png', fullPage: true });
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
  await page.setViewportSize({ width: 1440, height: 1050 });
  // Workspaces: protected personal workspace, cancellation, current/inactive deletion and file cleanup.
  await page.getByRole('button', { name: 'Switch workspace', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Switch to My Workspace', exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole('button', { name: 'Delete workspace My Workspace', exact: true }),
  ).toHaveCount(0);
  const protectedError = await page.evaluate(async () => {
    const { repository } = await import('/src/data.ts');
    try {
      await repository.deleteWorkspace('studio');
      return '';
    } catch (e) {
      return e.message;
    }
  });
  assert.equal(protectedError, 'My Workspace cannot be deleted.');
  await page.getByLabel('New workspace name').fill('Disposable');
  await page.getByRole('button', { name: 'Create workspace', exact: true }).click();
  await page.getByRole('button', { name: 'New project', exact: true }).click();
  await page.getByLabel('Project name').fill('Disposable project');
  await page
    .getByRole('dialog')
    .getByRole('button', { name: 'Create project', exact: true })
    .click();
  await page.getByRole('button', { name: 'Add task', exact: true }).first().click();
  await page.getByLabel('Task title').fill('Disposable card');
  await expect(
    page.getByText('Create your first label with Manage labels.', { exact: true }),
  ).toBeVisible();
  await page.getByLabel('Attach documents').setInputFiles({
    name: 'cleanup.txt',
    mimeType: 'text/plain',
    buffer: Buffer.from('Clean this up'),
  });
  await expect(
    page.getByRole('button', { name: 'Download cleanup.txt', exact: true }),
  ).toBeVisible();
  await page.getByRole('button', { name: 'Save task', exact: true }).click();
  const deletedId = await page.evaluate(() => localStorage.getItem('kanbada-active-workspace'));
  await page.getByRole('button', { name: 'Switch workspace', exact: true }).click();
  await page.getByRole('button', { name: 'Delete workspace Disposable', exact: true }).click();
  await expect(page.getByRole('dialog')).toContainText('You will return to My Workspace');
  await page.getByRole('button', { name: 'Keep workspace', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Switch to Disposable', exact: true }),
  ).toBeVisible();
  await page.getByRole('button', { name: 'Delete workspace Disposable', exact: true }).click();
  await page.getByRole('button', { name: 'Confirm deletion', exact: true }).click();
  await expect(page.locator('.workspace')).toContainText('My Workspace');
  await expect(page.locator('.kpi-card').first().locator('strong')).toHaveText('11');
  assert.equal(
    await page.evaluate((id) => localStorage.getItem('kanbada-workspace-' + id), deletedId),
    null,
  );
  const keys = await page.evaluate(
    () =>
      new Promise((resolve) => {
        const request = indexedDB.open('kanbada-files', 1);
        request.onsuccess = () => {
          const db = request.result;
          const query = db.transaction('files', 'readonly').objectStore('files').getAllKeys();
          query.onsuccess = () => {
            resolve(query.result);
            db.close();
          };
        };
      }),
  );
  assert.deepEqual(keys, []);
  await page.getByRole('button', { name: 'Switch workspace', exact: true }).click();
  await page.getByLabel('New workspace name').fill('Another workspace');
  await page.getByRole('button', { name: 'Create workspace', exact: true }).click();
  await page.getByRole('button', { name: 'Switch workspace', exact: true }).click();
  await page.getByRole('button', { name: 'Switch to My Workspace', exact: true }).click();
  await page.getByRole('button', { name: 'Switch workspace', exact: true }).click();
  await page
    .getByRole('button', { name: 'Delete workspace Another workspace', exact: true })
    .click();
  await page.getByRole('button', { name: 'Confirm deletion', exact: true }).click();
  await expect(page.locator('.workspace')).toContainText('My Workspace');
  await page.reload();
  await page.getByRole('button', { name: 'Switch workspace', exact: true }).click();
  await expect(page.locator('.workspace-option-row')).toHaveCount(1);
  await page.getByRole('button', { name: 'Close dialog', exact: true }).click();
  // Legacy tags and original workspace name migrate without losing the existing cards.
  await page.evaluate(() => {
    const state = JSON.parse(localStorage.getItem('kanbada-v1'));
    delete state.labels;
    state.workspace.name = 'Studio workspace';
    for (const task of state.tasks) {
      task.tag = task.labels[0] ?? '';
      delete task.labels;
    }
    state.tasks[0].tag = 'Legacy custom label';
    localStorage.setItem('kanbada-v1', JSON.stringify(state));
    localStorage.setItem('kanbada-workspaces-v1', JSON.stringify([]));
  });
  await page.reload();
  await expect(page.locator('.workspace')).toContainText('My Workspace');
  await expect(
    page
      .getByRole('article', { name: 'Open Map the customer journey', exact: true })
      .locator('.label-pill'),
  ).toHaveText('Legacy custom label');
  await page.getByRole('button', { name: 'Switch workspace', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Switch to My Workspace', exact: true }),
  ).toBeVisible();
  assert.deepEqual(errors, []);
  await page.close();
  console.log(
    'PASS: label creation/color/reorder/rename/validation/multiple assignment/history/board/list/editor/removal/migration/PT/mobile; protected My Workspace, workspace deletion cancellation/current/inactive cleanup and persistence.',
  );
}
