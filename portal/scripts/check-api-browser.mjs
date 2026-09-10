import { authenticatorCode, enrollApiAccount, avatarPhoto } from './authenticator-test-helpers.mjs';
import { chromium, expect } from '@playwright/test';
import assert from 'node:assert/strict';
const browser = await chromium.launch();
const context = await browser.newContext({ viewport: { width: 1440, height: 1050 } });
const page = await context.newPage();
const errors = [];
const workspaceWrites = [];
page.on('request', (request) => {
  if (
    ['PUT', 'PATCH'].includes(request.method()) &&
    /\/api\/workspaces\/[^/]+$/.test(new URL(request.url()).pathname)
  )
    workspaceWrites.push(request.method());
});
page.on('pageerror', (e) => errors.push(e.message));
try {
  await page.goto('http://localhost:4173');
  await page.getByRole('button', { name: 'Continue with email', exact: true }).click();
  await page.getByRole('button', { name: 'New here? Create an account', exact: true }).click();
  await page.getByLabel('Full name').fill('API Browser Owner');
  await page.getByLabel('Email address').fill(`browser-${Date.now()}@example.test`);
  await page.getByLabel('Password', { exact: true }).fill('A-long-browser-test-password');
  await page.getByRole('button', { name: 'Create account', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Choose your profile photo' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Continue', exact: true })).toBeDisabled();
  await page.getByLabel('Upload photo').setInputFiles({
    name: 'avatar.png',
    mimeType: 'image/png',
    buffer: Buffer.from(avatarPhoto.split(',')[1], 'base64'),
  });
  await page.getByRole('button', { name: 'Continue', exact: true }).click();
  await expect(
    page.getByRole('heading', { name: 'Secure your account', exact: true }),
  ).toBeVisible();
  await page.getByLabel('Current password', { exact: true }).fill('A-long-browser-test-password');
  await page.getByRole('button', { name: 'Set up authenticator', exact: true }).click();
  const secret = await page.getByLabel('Setup key', { exact: true }).inputValue();
  await page.getByLabel('Authenticator code', { exact: true }).fill(authenticatorCode(secret));
  await page.getByRole('button', { name: 'Confirm authenticator', exact: true }).click();
  await page.getByRole('button', { name: 'I have saved my codes', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'My activities', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Help', exact: true }).click();
  await page.getByLabel('Search help').fill('due date');
  await page.getByText('Do cards need a due date? How do I remove one?', { exact: true }).click();
  await expect(page.getByText(/Due date is optional/)).toBeVisible();
  await page.getByLabel('Search help').fill('nothing-matches-here');
  await expect(page.getByText('No matching answers', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Show all topics', exact: true }).click();
  await expect(page.locator('.help-articles details')).toHaveCount(38);
  await page.getByRole('button', { name: 'My activities', exact: true }).click();
  await page.getByRole('button', { name: 'Add task', exact: true }).first().click();
  await page.getByLabel('Task title').fill('PostgreSQL card');
  await page.getByLabel('Due date', { exact: true }).fill('');
  await page.getByRole('button', { name: 'Save task', exact: true }).click();
  await expect(page.getByRole('article', { name: 'Open PostgreSQL card' })).toBeVisible();
  await page.reload();
  await expect(page.getByRole('article', { name: 'Open PostgreSQL card' })).toBeVisible();
  await page.getByRole('article', { name: 'Open PostgreSQL card' }).click();
  await page.getByRole('tab', { name: /History/ }).click();
  await expect(page.getByText('Created card in Backlog', { exact: true })).toBeVisible();
  await page.keyboard.press('Escape');
  // A non-owner edits their own profile, even though the owner is member zero.
  const memberContext = await browser.newContext();
  try {
    const email = `member-${Date.now()}@example.test`;
    const registration = await memberContext.request.post(
      'http://localhost:4173/api/auth/register',
      {
        headers: { 'X-Kanbada-Request': '1' },
        data: { email, name: 'API Browser Member', password: 'A-long-browser-test-password' },
      },
    );
    assert.equal(registration.status(), 200);
    await enrollApiAccount(memberContext.request, 'A-long-browser-test-password');
    const workspaceResponse = await page.request.post('http://localhost:4173/api/workspaces', {
      headers: { 'X-Kanbada-Request': '1' },
      data: { name: 'Profile collaboration' },
    });
    const workspace = await workspaceResponse.json();
    workspace.members.push({ name: 'API Browser Member', email, initials: 'AM', color: '#aabbcc' });
    const invitedResponse = await page.request.put(
      `http://localhost:4173/api/workspaces/${workspace.workspace.id}`,
      {
        headers: { 'X-Kanbada-Request': '1', 'If-Match': String(workspace.version) },
        data: workspace,
      },
    );
    const invited = await invitedResponse.json();
    const accepted = await memberContext.request.post(
      `http://localhost:4173/api/invitations/${invited.members[1].invitationToken}/accept`,
      {
        headers: { 'X-Kanbada-Request': '1' },
      },
    );
    assert.equal(accepted.status(), 200);
    const memberPage = await memberContext.newPage();
    await memberPage.goto(`http://localhost:4173/?workspace=${workspace.workspace.id}`);
    await memberPage.getByRole('button', { name: 'Edit profile', exact: true }).click();
    await memberPage.getByLabel('Name', { exact: true }).fill('Updated Browser Member');
    await memberPage.getByRole('button', { name: 'Save profile', exact: true }).click();
    await expect(memberPage.getByRole('dialog', { name: 'Profile', exact: true })).toBeHidden();
    const result = await (
      await memberContext.request.get(
        `http://localhost:4173/api/workspaces/${workspace.workspace.id}`,
      )
    ).json();
    assert.equal(result.members[0].name, 'API Browser Owner');
    assert.equal(result.members[1].name, 'Updated Browser Member');
  } finally {
    await memberContext.close();
  }
  // Wide boards use the available content area before horizontal overflow.
  const stateResponse = await page.request.get('http://localhost:4173/api/workspaces/studio');
  const state = await stateResponse.json();
  state.buckets = Array.from({ length: 7 }, (_, index) => ({
    id: `bucket-${index}`,
    name: `Bucket ${index + 1}`,
    color: '#849bb8',
    complete: false,
  }));
  const saveResponse = await page.request.put('http://localhost:4173/api/workspaces/studio', {
    headers: { 'X-Kanbada-Request': '1', 'If-Match': String(state.version) },
    data: state,
  });
  assert.equal(saveResponse.status(), 200);
  await page.reload();
  await page.getByLabel('Group board by').selectOption('Bucket');
  await page.setViewportSize({ width: 2560, height: 1050 });
  const board = page.locator('.kanban-board').first();
  await expect(board.locator('.kanban-column')).toHaveCount(8);
  const wide = await board.evaluate((element) => ({
    width: element.clientWidth,
    scroll: element.scrollWidth,
  }));
  assert.ok(wide.width > 1700, 'Wide boards must not retain the old content cap');
  assert.ok(wide.scroll <= wide.width + 1, 'Columns should fit when enough viewport space exists');
  await page.setViewportSize({ width: 1280, height: 900 });
  const narrow = await board.evaluate((element) => ({
    width: element.clientWidth,
    scroll: element.scrollWidth,
  }));
  assert.ok(narrow.scroll > narrow.width, 'Narrow boards retain horizontal scrolling');
  assert.ok(
    await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth),
    'Board overflow must not widen the page',
  );
  await page.getByRole('article', { name: 'Open PostgreSQL card' }).click();
  assert.equal(
    await page
      .locator('.card-drawer-overlay')
      .evaluate((element) => getComputedStyle(element).backdropFilter),
    'blur(5px)',
  );
  await page.keyboard.press('Escape');
  await page.setViewportSize({ width: 1440, height: 1050 });
  const reference = await context.newPage();
  await reference.goto('http://localhost:4173/api/scalar');
  await expect(
    reference.getByRole('heading', { name: 'Kanbada API', exact: true }).first(),
  ).toBeVisible();
  await reference.getByRole('button', { name: 'Open Group - Cards', exact: true }).click();
  await reference.getByText('Create a card', { exact: true }).first().click();
  await reference.getByRole('button', { name: /Test Request.*post.*cards/ }).click();
  const scalarCreation = reference.waitForResponse(
    (response) =>
      response.url().endsWith('/api/workspaces/studio/cards') &&
      response.request().method() === 'POST',
  );
  await reference
    .getByRole('button', { name: /Send Request/ })
    .last()
    .click();
  const scalarResponse = await scalarCreation;
  assert.equal(scalarResponse.status(), 201);
  assert.equal(new URL(scalarResponse.url()).origin, 'http://localhost:4173');
  assert.match((await scalarResponse.json()).id, /^KB-[A-F0-9]{32}$/);
  await reference.close();
  await page.getByRole('button', { name: 'Overview', exact: true }).click();
  await page.setViewportSize({ width: 2560, height: 1050 });
  assert.ok(
    await page.locator('.main-content').evaluate((element) => element.clientWidth > 1700),
    'Dashboards must use all available width',
  );
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Export PDF', exact: true }).click();
  assert.match((await download).suggestedFilename(), /\.pdf$/);
  await page.getByLabel('Interface language').selectOption('pt-PT');
  await page.getByRole('button', { name: 'Ajuda', exact: true }).click();
  assert.ok(
    await page.locator('.help-center').evaluate((element) => element.clientWidth > 1700),
    'Help must use all available width',
  );
  await page.getByLabel('Pesquisar ajuda').fill('fotografia');
  await expect(
    page.getByText('Como altero o nome, o email ou a fotografia de perfil?', { exact: true }),
  ).toBeVisible();
  await page.getByLabel('Tema', { exact: true }).selectOption('dark');
  await page.setViewportSize({ width: 1440, height: 1050 });
  await page.screenshot({
    path: process.env.KANBADA_SCREENSHOT_PATH || '/tmp/kanbada-help-api.png',
    fullPage: true,
  });
  assert.deepEqual(errors, []);
  assert.ok(workspaceWrites.includes('PATCH'), 'UI saves must use compact PATCH requests');
  assert.ok(!workspaceWrites.includes('PUT'), 'UI must never send full workspace snapshots');
  console.log(
    'PASS: real API registration/session, Help search/FAQ/PT, PostgreSQL card CRUD/history/reload, dashboard PDF, dark theme, responsive bucket widths, drawer blur, member profile, Scalar card creation.',
  );
} finally {
  await browser.close();
}
