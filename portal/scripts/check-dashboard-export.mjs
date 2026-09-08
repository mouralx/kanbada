import { expect } from '@playwright/test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
export async function checkDashboardExport(browser) {
  const page = await browser.newPage({ viewport: { width: 1440, height: 1050 } });
  await page.goto('http://localhost:4173');
  await expect(page.getByRole('button', { name: 'My activities', exact: true })).toBeVisible();
  await expect(page.locator('.upgrade-card')).toHaveCount(0);
  await expect(
    page.getByRole('button', { name: 'Help & getting started', exact: true }),
  ).toHaveCount(0);
  await page.getByRole('button', { name: 'Dashboard', exact: true }).click();
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Export PDF', exact: true }).click();
  const file = await download;
  assert.match(file.suggestedFilename(), /Website redesign.*\.pdf$/);
  await file.saveAs('/private/tmp/kanbada-dashboard-report.pdf');
  const bytes = await readFile(await file.path());
  assert.equal(bytes.subarray(0, 5).toString(), '%PDF-');
  assert.ok(bytes.includes(Buffer.from('Bucket performance')));
  assert.ok(bytes.includes(Buffer.from('Swimlane performance')));
  assert.ok(bytes.includes(Buffer.from('Checklist progress')));
  await page.getByLabel('Dashboard bucket').selectOption('');
  const filtered = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Export PDF', exact: true }).click();
  assert.ok((await readFile(await (await filtered).path())).includes(Buffer.from('No bucket')));
  await page.getByRole('button', { name: 'Overview', exact: true }).click();
  const global = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Export PDF', exact: true }).click();
  assert.match((await global).suggestedFilename(), /My Workspace/);
  await page.getByLabel('Interface language').selectOption('pt-PT');
  await expect(
    page.getByRole('button', { name: 'As minhas atividades', exact: true }),
  ).toBeVisible();
  await page.getByRole('button', { name: 'As minhas atividades', exact: true }).click();
  await expect(
    page.getByRole('heading', { name: 'As minhas atividades', exact: true }),
  ).toBeVisible();
  await page.getByRole('button', { name: 'Painel', exact: true }).click();
  const personal = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Exportar PDF', exact: true }).click();
  assert.match((await personal).suggestedFilename(), /As minhas atividades/);
  await page.screenshot({ path: '/private/tmp/kanbada-dashboard-export.png', fullPage: true });
  await page.reload();
  await expect(
    page.getByRole('button', { name: 'As minhas atividades', exact: true }),
  ).toBeVisible();
  const guards = await page.evaluate(async () => {
    const { repository } = await import('/src/data.ts');
    const state = await repository.load();
    let blocked = 0;
    for (const projects of [
      state.projects.filter((p) => p.system !== 'activities'),
      state.projects.map((p) => (p.system === 'activities' ? { ...p, archived: true } : p)),
    ]) {
      try {
        await repository.save({ ...state, projects });
      } catch {
        blocked++;
      }
    }
    return blocked;
  });
  assert.equal(guards, 2);
  await page.close();
  console.log(
    'PASS: project, filtered, workspace and Portuguese PDF downloads; translated permanent project, protection guards and simplified sidebar.',
  );
}
