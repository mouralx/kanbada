import { expect } from '@playwright/test';
import assert from 'node:assert/strict';
export async function checkMyTasks(browser) {
  const page = await browser.newPage({
    viewport: { width: 1440, height: 1050 },
    reducedMotion: 'reduce',
  });
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
  await page.goto('http://localhost:4173');
  await page.getByRole('heading', { name: 'Website redesign', exact: true }).waitFor();
  await page.evaluate(async () => {
    const { repository } = await import('/src/data.ts');
    const state = await repository.load();
    const base = state.tasks[0];
    state.tasks.push({
      ...base,
      id: 'KB-CROSS',
      title: 'Across projects',
      project: 'mobile',
      assignees: ['Alex Morgan'],
      due: '',
      history: [],
    });
    state.tasks.push({
      ...base,
      id: 'KB-OTHER',
      title: 'Someone else',
      project: 'mobile',
      assignees: ['Sophie Chen'],
      due: '',
    });
    state.projects.find((p) => p.id === 'brand').archived = true;
    state.tasks.push({
      ...base,
      id: 'KB-ARCHIVED',
      title: 'Archived assignment',
      project: 'brand',
      assignees: ['Alex Morgan'],
      due: '',
    });
    await repository.save(state);
  });
  await page.reload();
  // Carrying a project status filter must not narrow My tasks.
  await page.getByRole('button', { name: 'Filter', exact: true }).click();
  await page.getByLabel('Filter by status').selectOption('Done');
  await page.getByRole('button', { name: 'My tasks', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'My tasks', exact: true })).toBeVisible();
  await expect(page.getByLabel('Group list by')).toHaveValue('Project');
  await expect(page.locator('.list-row').filter({ hasText: 'Across projects' })).toHaveCount(1);
  await expect(page.locator('.list-row').filter({ hasText: 'Someone else' })).toHaveCount(0);
  await expect(page.locator('.list-row').filter({ hasText: 'Archived assignment' })).toHaveCount(0);
  await expect(page.locator('.project-banner')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Project options', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Add task', exact: true }).first().click();
  await expect(page.getByRole('group', { name: 'Project', exact: true })).toContainText(
    'My activities',
  );
  await page.getByLabel('Task title').fill('No deadline needed');
  await expect(page.getByLabel('Due date', { exact: true })).toHaveValue('');
  await page.getByRole('button', { name: 'Save task', exact: true }).click();
  await expect(page.locator('.list-row').filter({ hasText: 'No deadline needed' })).toHaveCount(1);
  const saved = await page.evaluate(() =>
    JSON.parse(localStorage.getItem('kanbada-v1')).tasks.find(
      (t) => t.title === 'No deadline needed',
    ),
  );
  assert.equal(saved.project, 'my-activities');
  assert.equal(saved.due, '');
  await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await page.getByRole('button', { name: /^Overdue|0 Overdue/ }).count();
  const late = page
    .locator('.kpi-card')
    .filter({ has: page.getByText('Overdue', { exact: true }) });
  await expect(late.locator('strong')).toHaveText('0');
  await page.getByRole('button', { name: 'My tasks', exact: true }).click();
  await page.locator('.list-row').filter({ hasText: 'Across projects' }).click();
  await expect(page.getByRole('group', { name: 'Project', exact: true })).toContainText(
    'Mobile app',
  );
  await page.getByLabel('Due date', { exact: true }).fill('2000-01-01');
  await page.getByRole('button', { name: 'Save task', exact: true }).click();
  await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await expect(late.locator('strong')).toHaveText('1');
  await page.getByRole('button', { name: 'My tasks', exact: true }).click();
  await page.locator('.list-row').filter({ hasText: 'Across projects' }).click();
  await page.getByLabel('Due date', { exact: true }).fill('');
  await page.getByRole('button', { name: 'Save task', exact: true }).click();
  await page.reload();
  await page.getByRole('button', { name: 'My tasks', exact: true }).click();
  await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
  await expect(late.locator('strong')).toHaveText('0');
  await page.close();
  console.log(
    'PASS: My tasks spans active projects, resets project filters, defaults to project-grouped list; optional/cleared due dates persist and are excluded from overdue metrics.',
  );
}
