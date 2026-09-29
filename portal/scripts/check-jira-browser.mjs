import { chromium, expect } from '@playwright/test';
import assert from 'node:assert/strict';
import { enrollApiAccount } from './authenticator-test-helpers.mjs';

const browser = await chromium.launch();
const context = await browser.newContext();
const headers = { 'X-Kanbada-Request': '1' };
const base = 'http://localhost:4173';
const settingsPath = `${base}/api/workspaces/studio/projects/my-activities/jira`;
const password = 'Jira-browser-test-password';
const errors = [];
try {
  const registration = await context.request.post(`${base}/api/auth/register`, {
    headers,
    data: {
      email: `jira-browser-${Date.now()}@example.test`,
      name: 'Jira Browser Owner',
      password,
    },
  });
  assert.equal(registration.status(), 200);
  const registeredUser = await registration.json();
  await enrollApiAccount(context.request, password);
  const page = await context.newPage();
  let approvedHosts = [];
  await page.route('**/api/jira/hosts', async (route) => {
    if (route.request().method() === 'POST') {
      approvedHosts = [
        {
          authority: new URL(route.request().postDataJSON().baseUrl).host,
          approvedAt: new Date().toISOString(),
        },
      ];
      return route.fulfill({ json: { authority: approvedHosts[0].authority } });
    }
    if (route.request().method() === 'DELETE') {
      approvedHosts = [];
      return route.fulfill({ status: 204 });
    }
    return route.fulfill({ json: { canManage: true, hosts: approvedHosts, configuredHosts: [] } });
  });
  page.on('pageerror', (error) => errors.push(error.message));
  await page.route('**/projects/my-activities/jira/test', (route) =>
    route.fulfill({
      json: {
        matchedIssues: 7,
        issueTypes: [{ id: '10001', name: 'Task' }],
        statuses: [
          { id: '10', name: 'Open' },
          { id: '11', name: 'Selected' },
        ],
        assignees: [{ id: 'jira-user', name: 'Jira Browser Owner' }],
        priorities: [
          { id: '1', name: 'Low' },
          { id: '2', name: 'Medium' },
          { id: '3', name: 'High' },
        ],
      },
    }),
  );
  await page.goto(base);
  await page.getByRole('button', { name: 'Project options', exact: true }).click();
  await page.getByRole('button', { name: 'Jira synchronization', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Jira synchronization' });
  await expect(dialog.getByLabel('Jira edition')).toBeEnabled();
  await dialog.getByLabel('Jira edition').selectOption('cloud');
  await dialog.getByLabel('Jira base URL').fill('https://example.atlassian.net');
  await dialog.getByText('Server access', { exact: true }).click();
  await dialog.getByRole('button', { name: 'Approve this Jira server', exact: true }).click();
  await dialog.getByRole('button', { name: 'Confirm server approval', exact: true }).click();
  await expect(
    dialog.getByText('Server approved. You can test the connection now; no restart is needed.', {
      exact: true,
    }),
  ).toBeVisible();
  await dialog
    .getByRole('button', { name: 'Revoke approval for example.atlassian.net', exact: true })
    .click();
  await dialog.getByRole('button', { name: 'Revoke server approval', exact: true }).click();
  await expect(
    dialog.getByText('Server approval removed. New requests to this server are blocked.', {
      exact: true,
    }),
  ).toBeVisible();
  await dialog.getByText('Server access', { exact: true }).click();
  await dialog.getByLabel('Jira account email').fill('jira@example.test');
  await dialog.getByLabel('API token', { exact: true }).fill('browser-test-token');
  await dialog.getByLabel('Jira project key for new issues').fill('TEAM');
  await dialog.getByLabel('JQL', { exact: true }).fill('project = TEAM ORDER BY key');
  await dialog.getByRole('button', { name: 'Test connection and load mappings' }).click();
  await expect(dialog.getByRole('status')).toHaveText('Connected. JQL matches 7 issues.');
  await dialog.getByLabel('Jira issue type for new issues').selectOption('10001');
  await dialog.getByLabel('Backlog', { exact: true }).selectOption('10');
  await dialog.getByRole('button', { name: 'Add Jira status for Backlog', exact: true }).click();
  await dialog
    .getByLabel('Additional Jira status for Backlog (1)', { exact: true })
    .selectOption('11');
  await dialog.getByLabel('Default Jira status for Backlog (2)', { exact: true }).check();
  await dialog.getByLabel('Synchronize assignees from Jira', { exact: true }).check();
  await dialog.getByRole('button', { name: 'Add assignee mapping', exact: true }).click();
  await dialog
    .getByLabel('Jira user (1)', { exact: true })
    .selectOption({ label: 'Jira Browser Owner' });
  await dialog.getByLabel('Workspace member (1)', { exact: true }).selectOption(registeredUser.id);
  for (const [name, id] of [
    ['Low', '1'],
    ['Medium', '2'],
    ['High', '3'],
  ])
    await dialog.getByLabel(name, { exact: true }).selectOption(id);
  await dialog.getByLabel('Direction', { exact: true }).selectOption('bidirectional');
  await dialog.getByLabel('Schedule unit').selectOption('months');
  await dialog.getByLabel('Repeat every').fill('3');
  await dialog.getByRole('button', { name: 'Apply schedule preset' }).click();
  await expect(dialog.getByLabel('Cron expression')).toHaveValue('0 0 1 */3 *');
  await dialog.getByRole('button', { name: 'Save Jira settings' }).click();
  await expect(dialog.getByRole('status')).toHaveText('Jira settings saved.');
  await expect(dialog.getByLabel('API token', { exact: true })).toHaveValue('');
  const response = await context.request.get(settingsPath);
  assert.equal(response.status(), 200);
  const { connection } = await response.json();
  assert.equal(connection.hasToken, true);
  assert.equal(connection.enabled, false);
  assert.equal(connection.syncAssignees, true);
  assert.deepEqual(
    connection.mappings
      .filter((m) => m.kind === 'status')
      .sort((a, b) => a.jiraValue.localeCompare(b.jiraValue)),
    [
      { kind: 'status', kanbadaValue: 'backlog', jiraValue: '10', isDefault: false },
      { kind: 'status', kanbadaValue: 'backlog', jiraValue: '11', isDefault: true },
    ],
  );
  assert.ok(
    connection.mappings.some(
      (m) =>
        m.kind === 'assignee' &&
        m.kanbadaValue === registeredUser.id &&
        m.jiraValue === 'jira-user',
    ),
  );
  assert.equal(connection.direction, 'bidirectional');
  assert.equal(connection.cron, '0 0 1 */3 *');
  assert.equal(JSON.stringify(connection).includes('browser-test-token'), false);
  assert.equal('token' in connection, false);
  assert.equal('protectedToken' in connection, false);
  await expect(dialog.getByRole('button', { name: 'Run saved configuration now' })).toBeDisabled();
  await dialog.getByRole('button', { name: 'Save Jira settings' }).click();
  await expect(dialog.getByRole('status')).toHaveText('Jira settings saved.');
  await dialog.getByRole('button', { name: 'Close dialog' }).click();
  await page.reload();
  await page.getByRole('button', { name: 'Project options', exact: true }).click();
  await page.getByRole('button', { name: 'Jira synchronization', exact: true }).click();
  await expect(dialog.getByLabel('Cron expression')).toHaveValue('0 0 1 */3 *');
  await expect(
    dialog.getByLabel('Additional Jira status for Backlog (1)', { exact: true }),
  ).toHaveValue('11');
  await expect(
    dialog.getByLabel('Default Jira status for Backlog (2)', { exact: true }),
  ).toBeChecked();
  await expect(dialog.getByLabel('Jira user (1)', { exact: true })).toHaveValue('jira-user');
  await expect(dialog.getByLabel('Workspace member (1)', { exact: true })).toHaveValue(
    registeredUser.id,
  );
  await expect(dialog.getByLabel('Backlog', { exact: true }).locator('option:checked')).toHaveText(
    'Open',
  );
  await expect(
    dialog
      .getByLabel('Additional Jira status for Backlog (1)', { exact: true })
      .locator('option:checked'),
  ).toHaveText('Selected');
  await expect(
    dialog.getByLabel('Jira user (1)', { exact: true }).locator('option:checked'),
  ).toHaveText('Jira Browser Owner');
  await expect(dialog.locator('input[inputmode="numeric"]')).toHaveCount(0);
  await expect(dialog.getByLabel('API token', { exact: true })).toHaveValue('');
  const stale = await context.request.put(settingsPath, {
    headers,
    data: { ...connection, version: 0, token: '' },
  });
  assert.equal(stale.status(), 409);
  for (const width of [1280, 390]) {
    await page.setViewportSize({ width, height: 900 });
    for (const theme of ['light', 'dark']) {
      await page.evaluate(
        (value) => document.documentElement.setAttribute('data-theme', value),
        theme,
      );
      await expect(dialog.getByRole('button', { name: 'Save Jira settings' })).toBeInViewport();
      const dropdownsFit = await dialog.locator('select').evaluateAll((elements) =>
        elements.every((element) => {
          const style = getComputedStyle(element);
          return (
            style.appearance === 'none' &&
            parseFloat(style.paddingRight) >= 34 &&
            style.backgroundImage.includes('data:image/svg+xml') &&
            style.backgroundPosition.includes('10px')
          );
        }),
      );
      assert.ok(dropdownsFit, 'All Jira dropdowns must reserve space for an inset arrow');
      const layout = await dialog.evaluate((element) => ({
        width: element.getBoundingClientRect().width,
        scrollWidth: element.scrollWidth,
        clientWidth: element.clientWidth,
      }));
      assert.ok(layout.width <= width, 'Dialog must fit the viewport');
      assert.ok(
        layout.scrollWidth <= layout.clientWidth + 1,
        'Dialog must not scroll horizontally',
      );
      if (width === 1280)
        assert.ok(layout.width >= 800, 'Desktop settings should use the wider layout');
      if (process.env.JIRA_SCREENSHOT_DIR) {
        await dialog.evaluate((element) => element.scrollTo(0, 0));
        await page.screenshot({
          path: `${process.env.JIRA_SCREENSHOT_DIR}/jira-${width}-${theme}.png`,
        });
      }
    }
  }
  await dialog.getByRole('button', { name: 'Close dialog' }).click();
  const created = await context.request.post(`${base}/api/workspaces/studio/cards`, {
    headers,
    data: { title: 'Jira linked browser card', project: 'my-activities' },
  });
  assert.equal(created.status(), 201);
  const card = await created.json();
  await page.route(/\/api\/workspaces\/studio\/cards(?:\?|\/KB-)/, async (route) => {
    const response = await route.fetch();
    if (route.request().method() !== 'GET' || response.status() !== 200)
      return route.fulfill({ response });
    const state = await response.json();
    const linked = state.items
      ? state.items.find((task) => task.id === card.id)
      : state.id === card.id
        ? state
        : null;
    if (linked) linked.readOnly = true;
    return route.fulfill({ response, json: state });
  });
  let linkState = 'linked';
  await page.route('**/cards/*/jira', (route) => {
    if (linkState === 'error') return route.fulfill({ status: 503, json: {} });
    return route.fulfill({
      json: {
        links:
          linkState === 'linked'
            ? [{ key: 'TEAM-42', url: 'https://example.atlassian.net/browse/TEAM-42' }]
            : [],
      },
    });
  });
  await page.goto(`${base}/?workspace=studio&card=${encodeURIComponent(card.id)}`);
  const cardDialog = page.getByRole('dialog', { name: 'Task details' });
  const jiraLink = cardDialog.getByRole('link', { name: /Open in Jira/ });
  await expect(cardDialog.getByLabel('Task title', { exact: true })).toBeDisabled();
  await expect(cardDialog.getByRole('button', { name: 'Save task', exact: true })).toBeDisabled();
  await expect(cardDialog.getByRole('button', { name: 'Delete task', exact: true })).toBeDisabled();
  await expect(cardDialog.getByLabel('Comment', { exact: true })).toBeDisabled();
  await expect(cardDialog.getByLabel('New checklist item', { exact: true })).toBeDisabled();
  await expect(cardDialog.getByLabel('Attach documents', { exact: true })).toBeDisabled();
  await expect(
    page.locator('.task-card').filter({ hasText: 'Jira linked browser card' }),
  ).toHaveAttribute('draggable', 'false');
  await expect(jiraLink).toBeVisible();
  await expect(jiraLink).toHaveAttribute('href', 'https://example.atlassian.net/browse/TEAM-42');
  await expect(jiraLink).toHaveAttribute('target', '_blank');
  await expect(jiraLink).toHaveAttribute('rel', 'noopener noreferrer');
  for (const width of [1280, 390]) {
    await page.setViewportSize({ width, height: 900 });
    await expect(jiraLink).toBeInViewport();
  }
  await cardDialog.getByRole('button', { name: 'Close task', exact: true }).click();
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.locator('.view-tabs').getByRole('button', { name: 'List', exact: true }).click();
  const lockedRow = page.locator('.list-row').filter({ hasText: 'Jira linked browser card' });
  const aligned = await lockedRow.evaluate((element) => {
    const title = element.querySelector('.list-task-title > b').getBoundingClientRect();
    const lock = element.querySelector('.list-task-lock').getBoundingClientRect();
    return Math.abs(title.top - lock.top) < 2;
  });
  assert.ok(aligned, 'Jira lock must share the title row, not add a separate line');
  if (process.env.JIRA_SCREENSHOT_DIR)
    await page.screenshot({ path: `${process.env.JIRA_SCREENSHOT_DIR}/jira-list.png` });
  await lockedRow.click();
  linkState = 'error';
  await page.reload();
  await expect(cardDialog.getByRole('alert')).toContainText('Could not load the Jira card link.');
  linkState = 'unlinked';
  await cardDialog.getByRole('button', { name: 'Retry', exact: true }).click();
  await expect(cardDialog.getByRole('alert')).toHaveCount(0);
  await expect(jiraLink).toHaveCount(0);
  const assigned = await context.request.post(`${base}/api/workspaces/studio/cards`, {
    headers,
    data: { title: 'Assigned to me notification', assignees: ['Jira Browser Owner'] },
  });
  assert.equal(assigned.status(), 201);
  const assignedCard = await assigned.json();
  await page.goto(base);
  await page.getByRole('button', { name: 'Notifications', exact: true }).click();
  await expect(
    page.getByText(`You were assigned to Assigned to me notification (${assignedCard.id}).`, {
      exact: true,
    }),
  ).toBeVisible();
  assert.deepEqual(errors, []);
  console.log('Jira settings browser checks passed.');
} finally {
  await context.close();
  await browser.close();
}
