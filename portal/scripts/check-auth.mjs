import { expect } from '@playwright/test';
import assert from 'node:assert/strict';
export async function checkAuthentication(browser) {
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  await page.goto('http://localhost:4173');
  await expect(page.getByRole('heading', { name: 'Welcome to your workspace.' })).toBeVisible();
  await page.screenshot({ path: '/private/tmp/kanbada-sign-in.png', fullPage: true });
  await page.getByRole('button', { name: 'Continue with Google', exact: true }).click();
  await page.getByRole('button', { name: 'Back', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Welcome to your workspace.' })).toBeVisible();
  await page.getByRole('button', { name: 'Continue with Google', exact: true }).click();
  await page.getByRole('button', { name: /Alex Morgan alex@studio.co/ }).click();
  await expect(page.getByRole('heading', { name: 'Website redesign', exact: true })).toBeVisible();
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Website redesign', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Edit profile', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Sign out', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Welcome to your workspace.' })).toBeVisible();
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Welcome to your workspace.' })).toBeVisible();
  await page.getByRole('button', { name: 'Continue with Microsoft', exact: true }).click();
  await page.getByRole('button', { name: 'Use another account', exact: true }).click();
  await page.getByLabel('Full name').fill('Taylor Test');
  await page.getByLabel('Email address').fill('taylor@example.com');
  await page.getByRole('button', { name: 'Continue', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'My activities', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Website redesign', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Edit profile', exact: true }).click();
  await expect(page.getByLabel('Name', { exact: true })).toHaveValue('Taylor Test');
  await page.keyboard.press('Escape');
  await page.getByRole('button', { name: 'New project', exact: true }).click();
  await page.getByLabel('Project name').fill('Taylor project');
  await page
    .getByRole('dialog')
    .getByRole('button', { name: 'Create project', exact: true })
    .click();
  await expect(page.getByRole('heading', { name: 'Taylor project', exact: true })).toBeVisible();
  const firstId = await page.evaluate(
    () => JSON.parse(localStorage.getItem('kanbada-session-v1')).id,
  );
  await page.reload();
  await expect(page.getByRole('button', { name: 'Taylor project', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Collapse sidebar', exact: true }).click();
  await page.getByRole('button', { name: 'Sign out', exact: true }).click();
  await page.getByRole('button', { name: 'Continue with Microsoft', exact: true }).click();
  await page.getByRole('button', { name: /Taylor Test taylor@example.com/ }).click();
  assert.equal(
    await page.evaluate(() => JSON.parse(localStorage.getItem('kanbada-session-v1')).id),
    firstId,
  );
  await expect(page.getByRole('button', { name: 'Taylor project', exact: true })).toBeVisible();
  // Session removal in another tab immediately closes the workspace.
  const other = await page.context().newPage();
  await other.goto('http://localhost:4173');
  await other.getByRole('button', { name: 'Sign out', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Welcome to your workspace.' })).toBeVisible();
  await other.close();
  await page.getByLabel('Interface language').selectOption('pt-PT');
  await expect(
    page.getByRole('button', { name: 'Continuar com Google', exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole('button', { name: 'Continuar com Microsoft', exact: true }),
  ).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  await page.screenshot({ path: '/private/tmp/kanbada-sign-in-mobile.png', fullPage: true });
  await page.getByRole('button', { name: 'Continuar com Google', exact: true }).click();
  await page.getByRole('button', { name: /Alex Morgan alex@studio.co/ }).click();
  await page.getByRole('button', { name: 'O seu perfil', exact: true }).click();
  await page
    .getByRole('dialog')
    .getByRole('button', { name: 'Terminar sessão', exact: true })
    .click();
  await expect(
    page.getByRole('button', { name: 'Continuar com Google', exact: true }),
  ).toBeVisible();
  assert.deepEqual(errors, []);
  await context.close();
  console.log(
    'PASS: Google/Microsoft account flows, registration, returning login, logout, cross-tab session, isolated workspace persistence, Portuguese and mobile.',
  );
}
