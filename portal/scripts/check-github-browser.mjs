import { chromium, expect } from '@playwright/test';
import assert from 'node:assert/strict';
import { enrollApiAccount } from './authenticator-test-helpers.mjs';

const base = 'http://localhost:4173';
const headers = { 'X-Kanbada-Request': '1' };
const browser = await chromium.launch();
const context = await browser.newContext();
const errors = [];
try {
  const password = 'GitHub-browser-test-password';
  const registered = await context.request.post(`${base}/api/auth/register`, {
    headers,
    data: {
      email: `github-browser-${Date.now()}@example.test`,
      name: 'GitHub Browser Owner',
      password,
    },
  });
  assert.equal(registered.status(), 200);
  const owner = await registered.json();
  await enrollApiAccount(context.request, password);
  const page = await context.newPage();
  page.on('pageerror', (error) => errors.push(error.message));
  let connection = null;
  let testCalls = 0;
  let queued = 0;
  let resolution = null;
  const saves = [];
  const metadata = {
    projectId: 'PROJECT',
    title: 'Engineering',
    url: 'https://github.com/orgs/acme/projects/1',
    repositoryId: 'REPO',
    fields: [
      {
        id: 'STATUS',
        name: 'Status',
        dataType: 'SINGLE_SELECT',
        options: [
          { id: 'todo', name: 'Todo' },
          { id: 'doing', name: 'Doing' },
          { id: 'done', name: 'Done' },
        ],
      },
      {
        id: 'PRIORITY',
        name: 'Priority',
        dataType: 'SINGLE_SELECT',
        options: [
          { id: 'low', name: 'Low' },
          { id: 'medium', name: 'Medium' },
          { id: 'high', name: 'High' },
        ],
      },
      { id: 'DUE', name: 'Due', dataType: 'DATE', options: [] },
    ],
  };
  await page.route('**/api/github/hosts', (route) =>
    route.fulfill({ json: { canManage: false, hosts: [], configuredHosts: [] } }),
  );
  await page.route(
    /\/api\/workspaces\/[^/]+\/projects\/my-activities\/github(?:\/.*)?$/,
    async (route) => {
      const url = new URL(route.request().url());
      if (url.pathname.endsWith('/test')) {
        testCalls++;
        return route.fulfill({ json: metadata });
      }
      if (url.pathname.endsWith('/users/octocat'))
        return route.fulfill({ json: { id: 'USER-1', name: 'octocat' } });
      if (url.pathname.endsWith('/run')) {
        queued++;
        connection.requestedAt = new Date().toISOString();
        connection.lastError = 'An issue creation needs verification.';
        connection.problems = [
          {
            id: 'PENDING',
            cardId: 'KB-PENDING',
            displayKey: null,
            url: null,
            creationPending: true,
            lastError: 'Check GitHub before retrying.',
          },
        ];
        return route.fulfill({ status: 202, json: { queued: true } });
      }
      if (url.pathname.endsWith('/pause')) {
        assert.equal(route.request().postDataJSON().version, connection.version);
        connection.enabled = false;
        connection.requestedAt = null;
        connection.version++;
        return route.fulfill({ json: connection });
      }
      if (url.pathname.endsWith('/resolve')) {
        resolution = route.request().postDataJSON();
        connection.problems = [];
        connection.lastError = null;
        return route.fulfill({ status: 204 });
      }
      if (route.request().method() === 'PUT') {
        const input = route.request().postDataJSON();
        saves.push(structuredClone(input));
        const { token, ...safe } = input;
        assert.ok(token);
        connection = {
          ...safe,
          id: 'CONNECTION',
          version: input.version + 1,
          hasToken: true,
          nextRunAt: new Date().toISOString(),
          requestedAt: null,
          lastStartedAt: null,
          lastFinishedAt: null,
          lastError: null,
          lastSyncedCount: 0,
          problems: [],
        };
        return route.fulfill({ json: connection });
      }
      return route.fulfill({ json: { connection } });
    },
  );
  await page.goto(base);
  await page.getByRole('button', { name: 'Project options', exact: true }).click();
  await page.getByRole('button', { name: 'GitHub synchronization', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'GitHub synchronization' });
  await expect(dialog.getByLabel('GitHub project owner', { exact: true })).toBeEnabled();
  await dialog.getByLabel('GitHub project owner', { exact: true }).fill('acme');
  await dialog.getByLabel('Repository for new issues').fill('acme/repo');
  await dialog.getByLabel('GitHub access token').fill('browser-only-fake-token');
  await dialog.getByRole('button', { name: 'Test connection and load mappings' }).click();
  await expect(dialog.getByRole('status')).toHaveText('Connected to GitHub project: Engineering');
  await dialog.getByLabel('Kanbada status for Todo', { exact: true }).selectOption('backlog');
  await dialog
    .getByLabel('Kanbada status for Empty GitHub field', { exact: true })
    .selectOption('backlog');
  await dialog
    .getByLabel('Default outbound GitHub option: Empty GitHub field', { exact: true })
    .check();
  await dialog.getByLabel('Kanbada status for Doing', { exact: true }).selectOption('progress');
  await dialog.getByLabel('GitHub priority field (optional)').selectOption('PRIORITY');
  for (const value of ['Low', 'Medium', 'High'])
    await dialog.getByLabel(`Kanbada priority for ${value}`, { exact: true }).selectOption(value);
  await dialog.getByLabel('GitHub due-date field (optional)').selectOption('DUE');
  await dialog.getByLabel('Map GitHub assignees to workspace members').check();
  await dialog.getByLabel('Find GitHub user by login').fill('octocat');
  await dialog.getByRole('button', { name: 'Find user', exact: true }).click();
  await dialog.getByLabel('Workspace member for octocat', { exact: true }).selectOption(owner.id);
  await dialog.getByLabel('Direction', { exact: true }).selectOption('bidirectional');
  await dialog.getByLabel('Enable synchronization', { exact: true }).check();
  await dialog.getByRole('button', { name: 'Save settings', exact: true }).click();
  await expect(dialog.getByRole('status')).toHaveText('GitHub settings saved.');
  await expect(dialog.getByLabel('GitHub access token')).toHaveValue('');
  assert.equal(saves.length, 1);
  assert.equal(saves[0].direction, 'bidirectional');
  assert.equal(saves[0].priorityFieldId, 'PRIORITY');
  assert.equal(saves[0].dueFieldId, 'DUE');
  assert.ok(
    saves[0].mappings.some(
      (m) => m.kind === 'assignee' && m.kanbadaValue === owner.id && m.gitHubValue === 'USER-1',
    ),
  );
  assert.deepEqual(
    saves[0].mappings
      .filter((m) => m.kind === 'status' && m.kanbadaValue === 'backlog')
      .map((m) => [m.gitHubValue, m.isDefault])
      .sort(),
    [
      ['__none__', true],
      ['todo', false],
    ],
  );
  for (const width of [1280, 390, 320]) {
    await page.setViewportSize({ width, height: 900 });
    for (const theme of ['light', 'dark']) {
      await page.evaluate((value) => (document.documentElement.dataset.theme = value), theme);
      await expect(
        dialog.getByRole('button', { name: 'Save settings', exact: true }),
      ).toBeInViewport();
      assert.ok(
        await dialog.evaluate((element) => {
          const rect = element.getBoundingClientRect();
          return [
            ...element.querySelectorAll(
              'input:not([type=radio]):not([type=checkbox]),select,button',
            ),
          ]
            .filter((node) => node.getClientRects().length)
            .every((node) => {
              const bounds = node.getBoundingClientRect();
              return bounds.left >= rect.left && bounds.right <= rect.right;
            });
        }),
        `GitHub controls must fit at ${width}px in ${theme} mode`,
      );
    }
  }
  await page.setViewportSize({ width: 1280, height: 900 });
  await dialog.getByRole('button', { name: 'Run saved settings', exact: true }).click();
  assert.equal(queued, 1);
  await expect(dialog.getByLabel('Created GitHub issue URL')).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Allow a new creation attempt' })).toBeDisabled();
  await dialog.getByLabel('GitHub access token').fill('unsaved-rotated-token');
  await expect(dialog.getByRole('button', { name: 'Save settings', exact: true })).toBeDisabled();
  const beforePause = testCalls;
  await dialog.getByRole('button', { name: 'Pause synchronization', exact: true }).click();
  await expect(dialog.getByRole('status')).toHaveText(
    'GitHub synchronization paused. In-flight requests may finish.',
  );
  assert.equal(testCalls, beforePause, 'Pausing must not depend on GitHub availability');
  await expect(dialog.getByLabel('GitHub access token')).toHaveValue('unsaved-rotated-token');
  await dialog
    .getByLabel(
      'I checked GitHub and confirm that no issue was created. Retrying without checking can create a duplicate.',
    )
    .check();
  await dialog.getByRole('button', { name: 'Allow a new creation attempt' }).click();
  await expect(dialog.getByLabel('Created GitHub issue URL')).toHaveCount(0);
  assert.deepEqual(resolution, { issueUrl: null, retryCreation: true });
  await dialog.getByRole('button', { name: 'Close dialog' }).click();
  const created = await context.request.post(`${base}/api/workspaces/studio/cards`, {
    headers,
    data: { title: 'GitHub linked browser card', project: 'my-activities' },
  });
  assert.equal(created.status(), 201);
  const card = await created.json();
  await page.route(/\/api\/workspaces\/studio\/cards(?:\?|\/KB-)/, async (route) => {
    const response = await route.fetch();
    if (route.request().method() !== 'GET' || response.status() !== 200)
      return route.fulfill({ response });
    const data = await response.json();
    const task = data.items
      ? data.items.find((item) => item.id === card.id)
      : data.id === card.id
        ? data
        : null;
    if (task) task.readOnly = true;
    return route.fulfill({ response, json: data });
  });
  await page.route('**/cards/*/github', (route) =>
    route.fulfill({
      json: { links: [{ key: 'acme/repo#42', url: 'https://github.com/acme/repo/issues/42' }] },
    }),
  );
  await page.goto(`${base}/?workspace=studio&card=${encodeURIComponent(card.id)}`);
  const drawer = page.getByRole('dialog', { name: 'Task details' });
  await expect(drawer.getByRole('link', { name: /Open in GitHub/ })).toHaveAttribute(
    'href',
    'https://github.com/acme/repo/issues/42',
  );
  await expect(drawer.getByRole('link', { name: /Open in GitHub/ })).toHaveAttribute(
    'rel',
    'noopener noreferrer',
  );
  await expect(drawer.getByLabel('Task title', { exact: true })).toBeDisabled();
  await expect(drawer.getByRole('button', { name: 'Save task', exact: true })).toBeDisabled();
  await expect(drawer.getByRole('note')).toContainText('Make changes in the connected system.');
  assert.deepEqual(errors, []);
  console.log('GitHub settings, recovery, responsive layout and card-link browser checks passed.');
} finally {
  await context.close();
  await browser.close();
}
