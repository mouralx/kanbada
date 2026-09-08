import { checkMyTasks } from './check-my-tasks.mjs';
import { checkCardLinks } from './check-card-links.mjs';
import { checkThemes } from './check-theme.mjs';
import { checkAuthentication } from './check-auth.mjs';
import { checkWorkspaceImages } from './check-workspace-images.mjs';
import { checkDashboardExport } from './check-dashboard-export.mjs';
import { chromium } from '@playwright/test';
import assert from 'node:assert/strict';
import { checkNotifications } from './check-notifications.mjs';
import { checkLabels } from './check-labels.mjs';
import { checkWorkspaces } from './check-workspaces.mjs';
import { checkProjects } from './check-projects.mjs';
import { checkNewFeatures } from './check-features.mjs';
const browser = await chromium.launch({ headless: true });
await checkAuthentication(browser);
await checkThemes(browser);
await checkCardLinks(browser);
await checkMyTasks(browser);
const newPage = browser.newPage.bind(browser);
browser.newPage = async (options) => {
  const page = await newPage(options);
  await page.addInitScript(() => {
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
  return page;
};
const page = await browser.newPage({
  viewport: { width: 1440, height: 1050 },
  reducedMotion: 'reduce',
});
const errors = [];
page.on('pageerror', (e) => errors.push(e.message));
try {
  await page.goto('http://localhost:4173');
  await page.getByRole('heading', { name: 'Website redesign', exact: true }).waitFor();
  await page.screenshot({ path: '/private/tmp/kanbada-desktop.png', fullPage: true });
  assert.equal(await page.locator('.task-card').count(), 11);
  await page.getByRole('button', { name: 'Add task', exact: true }).first().click();
  await page.getByRole('textbox', { name: 'Task title' }).fill('Browser test task');
  await page.getByLabel('Due date', { exact: true }).fill('2026-09-14');
  await page.getByLabel('Status', { exact: true }).selectOption('In progress');
  await page.getByLabel('New checklist item').fill('Verify the workflow');
  await page.getByLabel('New checklist item').press('Enter');
  await page.getByLabel('Comment', { exact: true }).fill('Ready for review');
  await page.getByRole('button', { name: 'Add comment', exact: true }).click();
  await page.getByRole('button', { name: 'Save task', exact: true }).click();
  await page.getByRole('article', { name: 'Open Browser test task' }).waitFor();
  assert.equal(await page.locator('.column-1 .task-card').count(), 4);
  await page.reload();
  await page.getByRole('article', { name: 'Open Browser test task' }).waitFor();
  await page
    .getByRole('article', { name: 'Open Browser test task' })
    .dragTo(page.locator('.column-3'));
  assert.equal(await page.locator('.column-3 .task-card').count(), 4);
  await page.getByRole('article', { name: 'Open Browser test task' }).click();
  await page.getByRole('checkbox', { name: 'Verify the workflow' }).check();
  await page.getByRole('button', { name: 'Save task', exact: true }).click();
  await page.getByRole('button', { name: 'List', exact: true }).click();
  assert.equal(await page.locator('.list-row').count(), 12);
  await page.getByLabel('Search tasks').fill('Browser test');
  assert.equal(await page.locator('.list-row').count(), 1);
  await page.getByLabel('Search tasks').fill('');
  await page.getByRole('button', { name: 'Filter', exact: true }).click();
  await page.getByLabel('Priority', { exact: true }).selectOption('High');
  assert.equal(await page.locator('.list-row').count(), 5);
  await page.getByRole('button', { name: 'Clear filters', exact: true }).click();
  await page.getByRole('button', { name: 'Filter', exact: true }).click();
  await page.getByRole('button', { name: 'Calendar', exact: true }).click();
  assert.equal(await page.locator('.calendar-task').count(), 12);
  await page.getByRole('button', { name: 'Next month', exact: true }).click();
  assert.equal(await page.locator('.calendar-task').count(), 0);
  await page.getByRole('button', { name: 'New project', exact: true }).click();
  await page.getByLabel('Project name').fill('Test project');
  await page
    .getByRole('dialog')
    .getByRole('button', { name: 'Create project', exact: true })
    .click();
  await page.getByRole('heading', { name: 'Test project', exact: true }).waitFor();
  await page.getByRole('button', { name: 'Invite members', exact: true }).click();
  await page.getByLabel('Full name').fill('Taylor Brooks');
  await page.getByLabel('Email address').fill('taylor@example.com');
  await page.getByRole('button', { name: 'Add teammate', exact: true }).click();
  await page.getByRole('button', { name: 'Members', exact: true }).click();
  await page.getByRole('heading', { name: 'Taylor Brooks' }).waitFor();
  await page.getByRole('button', { name: 'Website redesign', exact: true }).click();
  await page.getByRole('button', { name: 'Board', exact: true }).click();
  await page.getByRole('article', { name: 'Open Browser test task' }).click();
  await page.getByRole('button', { name: 'Delete task', exact: true }).click();
  assert.equal(await page.locator('.task-card').count(), 11);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: '/private/tmp/kanbada-mobile.png', fullPage: true });
  assert.equal(
    await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth),
    true,
    'No horizontal page overflow on mobile',
  );
  await page.getByRole('button', { name: 'Toggle navigation' }).click();
  await page.getByRole('button', { name: 'Overview', exact: true }).click();
  await page.getByRole('heading', { name: 'A good day to make progress.' }).waitFor();
  assert.deepEqual(errors, []);
  console.log(
    'PASS: task CRUD, checklist, comments, persistence, drag/drop, search, filters, list, calendar, projects, team, mobile navigation; no browser errors.',
  );
  await checkNewFeatures(browser);
  await checkProjects(browser);
  await checkWorkspaces(browser);
  await checkLabels(browser);
  await checkNotifications(browser);
  await checkDashboardExport(browser);
  await checkWorkspaceImages(browser);
} finally {
  await browser.close();
}
