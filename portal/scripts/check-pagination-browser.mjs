import { chromium, expect } from '@playwright/test';
import assert from 'node:assert/strict';
import { enrollApiAccount } from './authenticator-test-helpers.mjs';
import { readWorkbook } from './workbook-test-helpers.mjs';

const browser = await chromium.launch();
const context = await browser.newContext({
  baseURL: 'http://localhost:4173',
  viewport: { width: 1500, height: 1050 },
  acceptDownloads: true,
});
const page = await context.newPage();
const headers = { 'X-Kanbada-Request': '1' };
const now = new Date();
const dueDate = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-28`;
const errors = [];
page.on('pageerror', (error) => errors.push(error.message));
try {
  await page.goto('http://localhost:4173');
  const registration = await page.request.post('/api/auth/register', {
    headers,
    data: {
      email: `pagination-${Date.now()}@example.test`,
      name: 'Pagination Tester',
      password: 'A-long-pagination-test-password',
    },
  });
  assert.equal(registration.status(), 200);
  await enrollApiAccount(page.request, 'A-long-pagination-test-password');
  let state = await (await page.request.get('/api/workspaces/studio')).json();
  state.labels.push({ id: 'paging-label', name: 'Needle', color: '#123456', complete: false });
  state.tasks = Array.from({ length: 125 }, (_, index) => ({
    id: `KB-BROWSE-${String(index).padStart(4, '0')}`,
    title: `Paged card ${index}`,
    description: 'Description retained during pagination',
    project: 'my-activities',
    status: index < 100 ? 'Backlog' : 'Done',
    priority: index === 99 ? 'High' : 'Medium',
    due: index === 99 ? dueDate : '',
    labels: index === 99 ? ['Needle'] : [],
    assignees: ['Pagination Tester'],
    comments: [],
    checklist: [],
    attachments: [],
  }));
  const seeded = await page.request.put('/api/workspaces/studio', {
    headers: { ...headers, 'If-Match': String(state.version) },
    data: state,
  });
  assert.equal(seeded.status(), 200);
  const downloadedPages = [];
  const bootstraps = [];
  page.on('response', async (response) => {
    const url = new URL(response.url());
    if (!response.ok() || response.status() === 304) return;
    if (await response.finished()) return;
    if (url.pathname === '/api/workspaces/studio/cards')
      downloadedPages.push(await response.json());
    if (
      url.pathname === '/api/workspaces/studio' &&
      url.searchParams.get('metadataOnly') === 'true'
    )
      bootstraps.push(await response.json());
  });
  await page.reload();
  const backlog = page
    .locator('.kanban-column')
    .filter({ has: page.getByRole('heading', { name: 'Backlog', exact: true }) });
  await expect(
    backlog.getByRole('article', { name: 'Open Paged card 0', exact: true }),
  ).toBeVisible();
  await expect(backlog.locator('.column-count')).toHaveText('100');
  assert.ok(bootstraps.length);
  assert.ok(
    bootstraps.every(
      (snapshot) => snapshot.tasks.length === 0 && snapshot.notifications.length === 0,
    ),
  );
  assert.equal(bootstraps[0].notificationCount, 125);
  await expect(backlog.locator('.task-card')).toHaveCount(40);
  assert.ok(downloadedPages.every((result) => result.items.length <= 40));
  await backlog.locator('.card-pagination').scrollIntoViewIfNeeded();
  await expect(backlog.locator('.task-card')).toHaveCount(80);
  await backlog.locator('.card-pagination').scrollIntoViewIfNeeded();
  await expect(backlog.locator('.task-card')).toHaveCount(100);

  await page.getByLabel('Search tasks').fill('Needle');
  await expect(backlog.getByRole('article')).toHaveCount(1);
  await expect(
    backlog.getByRole('article', { name: 'Open Paged card 99', exact: true }),
  ).toBeVisible();
  await expect(backlog.locator('.column-count')).toHaveText('1');
  let failPage = true;
  await page.route(/\/api\/workspaces\/studio\/cards\?/, async (route) => {
    if (failPage && new URL(route.request().url()).searchParams.get('search') === 'Paged card 99')
      return route.fulfill({ status: 503, json: { detail: 'Temporary card failure' } });
    return route.fallback();
  });
  await page.getByLabel('Search tasks').fill('Paged card 99');
  await expect(backlog.getByRole('alert')).toHaveText('Temporary card failure');
  failPage = false;
  await backlog.getByRole('button', { name: 'Retry', exact: true }).click();
  await expect(backlog.getByRole('article')).toHaveCount(1);
  await expect(backlog.getByRole('alert')).toHaveCount(0);
  await page.getByLabel('Search tasks').fill('');
  await page.getByRole('button', { name: 'List', exact: true }).click();
  await page.getByLabel('Group list by').selectOption('None');
  await expect(page.locator('.list-row')).toHaveCount(40);
  await expect(page.locator('.list-metrics-toolbar')).toContainText('125');
  await page.getByRole('button', { name: 'Load more cards', exact: true }).click();
  await expect(page.locator('.list-row')).toHaveCount(80);
  await page.getByRole('button', { name: 'Calendar', exact: true }).click();
  await expect(page.locator('.calendar-task')).toHaveCount(1);
  await expect(page.locator('.calendar-task')).toHaveText('Paged card 99');
  await page.getByRole('button', { name: 'Next month', exact: true }).click();
  await expect(page.locator('.calendar-task')).toHaveCount(0);

  await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await expect(page.getByRole('button', { name: /125 Total cards/ })).toBeVisible();
  await page.getByRole('button', { name: /125 Total cards/ }).click();
  await expect(page.locator('.metric-task')).toHaveCount(40);
  const pagesBeforeExport = downloadedPages.length;
  const browserRenderRequests = [];
  page.on('request', (request) => {
    if (request.url().includes('dashboardPdf') || request.url().includes('jspdf'))
      browserRenderRequests.push(request.url());
  });
  await page.getByRole('button', { name: 'Export PDF', exact: true }).click();
  const pdfJob = page.locator('.export-job').filter({ hasText: 'Dashboard PDF' });
  await expect(pdfJob.getByText('Ready to download', { exact: true })).toBeVisible({
    timeout: 60000,
  });
  const pdf = page.waitForEvent('download');
  await pdfJob.getByRole('link', { name: 'Download', exact: true }).click();
  assert.match((await pdf).suggestedFilename(), /\.pdf$/);
  assert.equal(
    downloadedPages.length,
    pagesBeforeExport,
    'Export must not fetch all card pages in the browser',
  );
  assert.deepEqual(browserRenderRequests, [], 'PDF must be rendered by the worker');
  await page.getByRole('button', { name: 'My activities', exact: true }).click();
  await page.getByRole('button', { name: 'Project options', exact: true }).click();
  await page.getByRole('button', { name: 'Export project', exact: true }).click();
  const projectJob = page.locator('.export-job').filter({ hasText: 'Project XLSX' });
  await expect(projectJob.getByText('Ready to download', { exact: true })).toBeVisible({
    timeout: 60000,
  });
  const exported = page.waitForEvent('download');
  await projectJob.getByRole('link', { name: 'Download', exact: true }).click();
  const workbook = await readWorkbook(page, await exported);
  assert.equal(workbook.Cards.length, 125);
  assert.ok(workbook.Cards.some((task) => task.id === 'KB-BROWSE-0099'));
  assert.equal(workbook.Assignees.length, 125);
  await page.removeAllListeners('response', { behavior: 'wait' });
  await page.reload();
  await page.getByRole('button', { name: 'Exports', exact: true }).click();
  await expect(page.locator('.export-job')).toHaveCount(2);
  const actions = page.locator('.exports-actions button');
  const sizes = await actions.evaluateAll((buttons) =>
    buttons.map((button) => {
      const rect = button.getBoundingClientRect();
      const css = getComputedStyle(button);
      return { height: rect.height, top: rect.top, display: css.display, gap: css.gap };
    }),
  );
  assert.equal(sizes[0].height, sizes[1].height);
  assert.equal(sizes[0].top, sizes[1].top);
  assert.equal(sizes[0].display, 'flex');
  assert.equal(sizes[0].gap, '8px');
  await page.setViewportSize({ width: 375, height: 812 });
  assert.ok(
    await actions.evaluateAll((buttons) =>
      buttons.every((button) => {
        const rect = button.getBoundingClientRect();
        return rect.width > 0 && rect.height >= 38 && rect.right <= window.innerWidth;
      }),
    ),
  );
  await page.setViewportSize({ width: 1500, height: 1050 });
  await page.getByRole('button', { name: 'Export workspace', exact: true }).click();
  const workspaceJob = page.locator('.export-job').filter({ hasText: 'Workspace XLSX' });
  await expect(workspaceJob.getByText('Ready to download', { exact: true })).toBeVisible({
    timeout: 60000,
  });
  const workspaceDownload = page.waitForEvent('download');
  await workspaceJob.getByRole('link', { name: 'Download', exact: true }).click();
  const workspaceWorkbook = await readWorkbook(page, await workspaceDownload);
  assert.equal(workspaceWorkbook.Cards.length, 125);
  assert.equal(workspaceWorkbook.Workspace[0].id, 'studio');

  await page.goto('http://localhost:4173/?workspace=studio&card=KB-BROWSE-0099');
  await expect(page.getByLabel('Task title')).toHaveValue('Paged card 99');
  await page.getByLabel('Task title').fill('Edited unloaded card');
  await page.getByRole('button', { name: 'Save task', exact: true }).click();
  await expect(page.getByLabel('Task title')).toHaveCount(0);
  state = await (await page.request.get('/api/workspaces/studio')).json();
  assert.equal(state.tasks.length, 125);
  assert.equal(state.tasks[99].title, 'Edited unloaded card');
  assert.equal(state.tasks[0].title, 'Paged card 0');
  assert.equal(state.tasks[99].history.length, 2);
  await page.getByRole('button', { name: 'Notifications', exact: true }).click();
  await expect(page.locator('.notification-item')).toHaveCount(40);
  await expect(page.locator('.notifications-toolbar')).toContainText('126 unread notifications');
  await page.getByRole('button', { name: 'Load more', exact: true }).click();
  await expect(page.locator('.notification-item')).toHaveCount(80);
  await page.getByRole('button', { name: 'Clear notifications', exact: true }).click();
  await expect(page.locator('.notification-item')).toHaveCount(0);
  await expect(page.locator('.notifications-toolbar')).toContainText('0 unread notifications');
  await page.evaluate(async () => {
    const { remoteRepository: repository } =
      await import('/src/infrastructure/remoteRepository.ts');
    const { getCard } = await import('/src/infrastructure/cards.ts');
    const state = await repository.load();
    state.tasks = [await getCard(state.workspace.id, 'KB-BROWSE-0099')];
    const saved = await repository.save(
      {
        ...state,
        labels: [
          ...state.labels,
          { id: 'new-unused-label', name: 'Unused', color: '#123456', complete: false },
        ],
      },
      state,
    );
    if (saved.tasks[0]?.id !== 'KB-BROWSE-0099' || saved.tasks[0].title !== 'Edited unloaded card')
      throw new Error('Metadata saves lost the currently open card');
  });
  assert.deepEqual(errors, []);
  const localDownload = page.waitForEvent('download');
  await page.evaluate(async () => {
    const { exportProjectWorkbook } = await import('/src/infrastructure/local/projectWorkbook.ts');
    await exportProjectWorkbook(
      { id: 'local-demo', name: 'Local demo', description: '', color: '#123456' },
      [
        {
          id: 'KB-LOCAL',
          project: 'local-demo',
          title: '=1+1',
          description: 'Ação ' + 'x'.repeat(33000),
          status: 'Backlog',
          priority: 'High',
          due: '',
          labels: ['Label'],
          assignees: ['Alex'],
          comments: ['A comment'],
          checklist: [{ text: 'Step', done: true }],
          attachments: [],
        },
      ],
    );
  });
  const localWorkbook = await readWorkbook(page, await localDownload);
  assert.equal(localWorkbook.Cards[0].title, '=1+1');
  assert.equal(localWorkbook.Checklist[0].done, true);
  assert.equal(
    localWorkbook['Long text'].map((row) => row.text).join(''),
    'Ação ' + 'x'.repeat(33000),
  );
  console.log(
    'PASS: bounded bootstrap, scrolling, remote search, list paging, full metrics/exports, direct links, saves and notification paging',
  );
} finally {
  await page.removeAllListeners('response', { behavior: 'wait' });
  await browser.close();
}
