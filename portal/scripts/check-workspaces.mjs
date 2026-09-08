import { expect } from '@playwright/test';
import assert from 'node:assert/strict';
export async function checkWorkspaces(browser) {
  const page = await browser.newPage({
    viewport: { width: 1440, height: 1050 },
    reducedMotion: 'reduce',
  });
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  await page.goto('http://localhost:4173');
  await page.getByRole('heading', { name: 'Website redesign', exact: true }).waitFor();
  await page.getByRole('button', { name: 'Collapse sidebar', exact: true }).click();
  await expect(page.locator('.app')).toHaveClass(/sidebar-collapsed/);
  await page.reload();
  await expect(page.getByRole('button', { name: 'Expand sidebar', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Projects', exact: true }).click();
  await expect(page.locator('.directory-project')).toHaveCount(4);
  await page.getByRole('button', { name: 'Archived (0)', exact: true }).click();
  await expect(page.locator('.directory-project')).toHaveCount(0);
  await page.getByRole('button', { name: 'Live (4)', exact: true }).click();
  await page.getByRole('button', { name: 'Open project Website redesign', exact: true }).click();
  await page.getByRole('button', { name: 'Expand sidebar', exact: true }).click();
  // Bucket/swimlane metrics, filters, charts and list grouping.
  await page.getByRole('button', { name: 'Manage swimlanes', exact: true }).click();
  await page.getByRole('button', { name: 'Add swimlane', exact: true }).click();
  await page.getByLabel('Swimlane name 1', { exact: true }).fill('Delivery');
  await page.getByRole('button', { name: 'Save swimlanes', exact: true }).click();
  await page.getByRole('article', { name: 'Open Design the landing page' }).click();
  await page.getByLabel('Card swimlane').selectOption('Delivery');
  await page.getByRole('button', { name: 'Save task', exact: true }).click();
  await page.getByRole('button', { name: 'List', exact: true }).click();
  await page.getByLabel('Group list by').selectOption('Swimlane');
  const group = page
    .locator('.list-group')
    .filter({ has: page.getByText('Delivery · Website redesign', { exact: true }) });
  await expect(group).toContainText('1 total');
  await expect(group.locator('.list-row')).toHaveCount(1);
  await page.getByRole('button', { name: 'Filter', exact: true }).click();
  await page.getByLabel('Filter by swimlane').selectOption({ label: 'Delivery (1)' });
  await expect(page.locator('.list-row')).toHaveCount(1);
  await page.getByLabel('Filter by card metrics').selectOption('Completed');
  await expect(page.locator('.list-row')).toHaveCount(0);
  await page.getByRole('button', { name: 'Clear filters', exact: true }).click();
  await page.getByRole('button', { name: 'Filter', exact: true }).click();
  await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await page.getByLabel('Dashboard swimlane').selectOption({ label: 'Delivery' });
  await expect(page.locator('.kpi-card').first().locator('strong')).toHaveText('1');
  await page.getByRole('button', { name: /^Swimlane Delivery: 1 total/ }).click();
  await expect(page.locator('.metric-task')).toHaveCount(1);
  await page.getByRole('button', { name: 'Close metric details', exact: true }).click();
  await page.getByRole('button', { name: 'Clear dashboard filters', exact: true }).click();
  await page.getByLabel('Dashboard bucket').selectOption({ label: 'Discovery' });
  await expect(page.locator('.kpi-card').first().locator('strong')).toHaveText('3');
  await page.getByRole('button', { name: 'Clear dashboard filters', exact: true }).click();
  await page
    .getByRole('button', { name: 'Completion chart: 3 completed out of 11 cards', exact: true })
    .click();
  await expect(page.locator('.metric-task')).toHaveCount(3);
  await page.getByRole('button', { name: 'Close metric details', exact: true }).click();
  await page.locator('.due-chart-day').first().click();
  await expect(page.locator('.dashboard-drilldown')).toBeVisible();
  await page.getByRole('button', { name: 'Close metric details', exact: true }).click();
  await page.screenshot({ path: '/private/tmp/kanbada-charts-final.png', fullPage: true });
  // Remove workspace members without removing their cards.
  await page.getByRole('button', { name: 'Members', exact: true }).click();
  await page.getByRole('button', { name: 'Remove member Sophie Chen', exact: true }).click();
  await page.getByRole('button', { name: 'Keep member', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Sophie Chen', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Remove member Sophie Chen', exact: true }).click();
  await page.getByRole('button', { name: 'Confirm removal', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Sophie Chen', exact: true })).toHaveCount(0);
  const after = await page.evaluate(() => JSON.parse(localStorage.getItem('kanbada-v1')));
  assert.equal(after.tasks.length, 11);
  assert.equal(
    after.tasks.some((task) => task.assignees.includes('Sophie Chen')),
    false,
  );
  // New workspace is isolated and can be switched back to the original.
  await page.getByRole('button', { name: 'Switch workspace', exact: true }).click();
  await page.getByLabel('New workspace name').fill('Second workspace');
  await page.getByRole('button', { name: 'Create workspace', exact: true }).click();
  await expect(page.locator('.workspace')).toContainText('Second workspace');
  await expect(page.locator('.kpi-card').first().locator('strong')).toHaveText('0');
  await page.getByRole('button', { name: 'New project', exact: true }).click();
  await page.getByLabel('Project name').fill('Isolated project');
  await page
    .getByRole('dialog')
    .getByRole('button', { name: 'Create project', exact: true })
    .click();
  await page.reload();
  await expect(page.locator('.workspace')).toContainText('Second workspace');
  await page.getByRole('button', { name: 'Switch workspace', exact: true }).click();
  await page.getByRole('button', { name: 'Switch to My Workspace', exact: true }).click();
  await expect(page.locator('.kpi-card').first().locator('strong')).toHaveText('11');
  await expect(page.locator('.project-nav')).not.toContainText('Isolated project');
  // Portuguese is functional and keeps internal option values stable.
  await page.getByLabel('Interface language').selectOption('pt-PT');
  await expect(page.locator('html')).toHaveAttribute('lang', 'pt-PT');
  await page.getByRole('button', { name: 'Projetos', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Projetos', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Abrir projeto Website redesign', exact: true }).click();
  await page.getByRole('button', { name: 'Adicionar tarefa', exact: true }).first().click();
  await page.getByLabel('Título da tarefa').fill('Tarefa em português');
  await page.getByLabel('Prioridade', { exact: true }).selectOption('High');
  await page.getByRole('button', { name: 'Guardar tarefa', exact: true }).click();
  await expect(
    page.getByRole('article', { name: 'Abrir Tarefa em português', exact: true }),
  ).toBeVisible();
  await page.getByRole('button', { name: 'Painel', exact: true }).click();
  await expect(
    page.getByRole('heading', { name: 'Desempenho por faixa', exact: false }),
  ).toBeVisible();
  await page.screenshot({ path: '/private/tmp/kanbada-portuguese.png', fullPage: true });
  await page.reload();
  await expect(page.getByLabel('Idioma da interface')).toHaveValue('pt-PT');
  await page.getByLabel('Idioma da interface').selectOption('en-US');
  await expect(page.getByRole('button', { name: 'Projects', exact: true })).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole('button', { name: 'Toggle navigation', exact: true }).click();
  await page.getByRole('button', { name: 'Projects', exact: true }).click();
  await page.screenshot({
    path: '/private/tmp/kanbada-project-directory-mobile.png',
    fullPage: true,
  });
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
  assert.deepEqual(errors, []);
  await page.close();
  console.log(
    'PASS: sidebar collapse/persistence, project directory live/archive tabs, list and dashboard bucket/swimlane metrics, chart drilldowns, member removal, workspace creation/switching/isolation, EN/PT translation and Portuguese task creation, mobile directory.',
  );
}
