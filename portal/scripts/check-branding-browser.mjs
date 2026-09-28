import { chromium, expect } from '@playwright/test';
import assert from 'node:assert/strict';
import { enrollApiAccount, avatarPhoto } from './authenticator-test-helpers.mjs';

const browser = await chromium.launch();
const context = await browser.newContext({ viewport: { width: 1360, height: 1000 } });
const base = 'http://localhost:4173';
const defaults = {
  version: 0,
  name: 'kanbada',
  logo: null,
  collapsedLogo: null,
  showName: true,
  primary: '#334153',
  accent: '#aac2e1',
  lightBackground: '#f8f9fb',
  darkBackground: '#17191c',
  defaultTheme: 'system',
  lightSurface: null,
  darkSurface: null,
  lightText: null,
  darkText: null,
  lightBorder: null,
  darkBorder: null,
  sidebarBackground: null,
  sidebarText: null,
  fontFamily: 'default',
  fontScale: 100,
  cornerRadius: 8,
};
let saved = { ...defaults };
const errors = [];
async function assertDropdowns(root) {
  const controls = await root
    .locator('select:not([multiple]):not([size])')
    .evaluateAll((elements) =>
      elements
        .filter((element) => element.getClientRects().length)
        .map((element) => {
          const style = getComputedStyle(element);
          return {
            label: element.getAttribute('aria-label'),
            appearance: style.appearance,
            padding: parseFloat(style.paddingRight),
            leftPadding: parseFloat(style.paddingLeft),
            image: style.backgroundImage,
            position: style.backgroundPosition,
          };
        }),
    );
  assert.ok(controls.length > 0);
  for (const control of controls) {
    assert.equal(control.appearance, 'none', control.label);
    assert.ok(control.padding >= 34, control.label);
    assert.equal(control.leftPadding, 10, control.label);
    assert.ok(control.image.includes('data:image/svg+xml'), control.label);
    assert.ok(control.position.includes('10px'), control.label);
  }
}
try {
  // Mock only platform administration: never change shared installation branding.
  await context.route(/\/api\/platform\/branding(?:\/access)?$/, async (route) => {
    if (route.request().url().endsWith('/access'))
      return route.fulfill({ json: { canManage: true } });
    if (route.request().method() === 'PUT') {
      const input = route.request().postDataJSON();
      if (input.version !== saved.version)
        return route.fulfill({
          status: 409,
          json: { detail: 'Platform branding changed. Reload before saving.' },
        });
      saved = { ...input, version: saved.version + 1 };
    }
    return route.fulfill({ json: saved, headers: { ETag: `"branding-${saved.version}"` } });
  });
  const password = 'Branding-browser-test-password';
  const registration = await context.request.post(`${base}/api/auth/register`, {
    headers: { 'X-Kanbada-Request': '1' },
    data: {
      email: `branding-browser-${Date.now()}@example.test`,
      name: 'Branding Browser Owner',
      password,
    },
  });
  assert.equal(registration.status(), 200);
  await enrollApiAccount(context.request, password);
  const page = await context.newPage();
  page.on('pageerror', (error) => errors.push(error.message));
  await page.goto(base);
  await page.getByRole('button', { name: 'Settings', exact: true }).click();
  await page.getByRole('button', { name: 'Platform appearance', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Platform appearance' });
  await dialog.getByLabel('Platform name', { exact: true }).fill('Studio North');
  await dialog.getByLabel('Upload logo').setInputFiles({
    name: 'logo.png',
    mimeType: 'image/png',
    buffer: Buffer.from(avatarPhoto.split(',')[1], 'base64'),
  });
  await expect(dialog.getByAltText('Logo preview')).toBeVisible();
  await dialog.getByLabel('Upload logo').setInputFiles({
    name: 'logo.svg',
    mimeType: 'image/svg+xml',
    buffer: Buffer.from(
      '<svg xmlns="http://www.w3.org/2000/svg" width="200" height="100"><script>window.svgExecuted=true</script><rect width="100" height="100" fill="#7030a0"/></svg>',
    ),
  });
  await expect(dialog.getByAltText('Logo preview')).toHaveAttribute(
    'src',
    /^data:image\/png;base64,/,
  );
  const convertedLogo = await dialog.getByAltText('Logo preview').getAttribute('src');
  const logoPixels = await page.evaluate(async (src) => {
    const image = new Image();
    image.src = src;
    await image.decode();
    const canvas = document.createElement('canvas');
    canvas.width = image.width;
    canvas.height = image.height;
    const ctx = canvas.getContext('2d');
    ctx.drawImage(image, 0, 0);
    return {
      width: image.width,
      height: image.height,
      alpha: ctx.getImageData(image.width - 1, 0, 1, 1).data[3],
      executed: window.svgExecuted === true,
    };
  }, convertedLogo);
  assert.deepEqual(logoPixels, { width: 1024, height: 512, alpha: 0, executed: false });
  await dialog.getByLabel('Upload collapsed logo', { exact: true }).setInputFiles({
    name: 'collapsed.png',
    mimeType: 'image/png',
    buffer: Buffer.from(avatarPhoto.split(',')[1], 'base64'),
  });
  await expect(dialog.getByAltText('Collapsed logo preview')).toHaveAttribute('src', avatarPhoto);
  await dialog.getByLabel('Primary color', { exact: true }).fill('#7030a0');
  await dialog.getByLabel('Accent color', { exact: true }).fill('#f0b040');
  await dialog.getByLabel('Light background', { exact: true }).fill('#fff8ef');
  await dialog.getByLabel('Dark background', { exact: true }).fill('#181225');
  await dialog.getByLabel('Light panels', { exact: true }).fill('#ffffff');
  await dialog.getByLabel('Dark panels', { exact: true }).fill('#202040');
  await dialog.getByLabel('Sidebar background', { exact: true }).fill('#001122');
  await dialog.getByLabel('Sidebar text', { exact: true }).fill('#ffffff');
  await dialog.getByLabel('Font family', { exact: true }).selectOption('system');
  await dialog.getByLabel('Text size', { exact: true }).press('End');
  await dialog.getByLabel('Corner rounding', { exact: true }).press('End');
  await dialog.getByLabel('Default theme', { exact: true }).selectOption('dark');
  await expect(dialog.locator('.platform-preview strong')).toHaveText('Studio North');
  await dialog.getByLabel('Show name beside logo', { exact: true }).uncheck();
  await expect(dialog.locator('.platform-preview strong')).toHaveCount(0);
  await expect(dialog.locator('.platform-preview header')).toHaveCSS('justify-content', 'center');
  await expect(page.locator('.sidebar .logo-word')).toHaveText('kanbada');
  await dialog.getByLabel('Show name beside logo', { exact: true }).check();
  await expect(dialog.locator('.platform-preview')).toHaveCSS(
    'background-color',
    'rgb(255, 248, 239)',
  );
  await dialog.getByRole('button', { name: 'Dark', exact: true }).click();
  await expect(dialog.locator('.platform-preview')).toHaveCSS(
    'background-color',
    'rgb(24, 18, 37)',
  );
  await dialog.getByRole('button', { name: 'Light', exact: true }).click();
  await expect(page.locator('.sidebar .logo-word')).toHaveText('kanbada');
  for (const width of [1360, 768, 390, 320]) {
    await page.setViewportSize({ width, height: 1000 });
    await assertDropdowns(dialog);
    const dimensions = await dialog.evaluate((element) => ({
      scroll: element.scrollWidth,
      client: element.clientWidth,
      overflowing: [...element.querySelectorAll('*')]
        .filter(
          (child) =>
            child.getBoundingClientRect().right > element.getBoundingClientRect().right + 1,
        )
        .map((child) => `${child.tagName}.${child.className}`),
    }));
    assert.ok(
      dimensions.scroll <= dimensions.client + 1,
      `Branding dialog must not overflow horizontally at ${width}: ${JSON.stringify(dimensions)}`,
    );
    await expect(dialog.getByRole('button', { name: 'Save platform appearance' })).toBeInViewport();
    if (process.env.BRANDING_SCREENSHOT_DIR) {
      await dialog.evaluate((element) => element.scrollTo(0, 0));
      await page.screenshot({
        path: `${process.env.BRANDING_SCREENSHOT_DIR}/branding-${width}.png`,
      });
    }
  }
  await page.setViewportSize({ width: 1360, height: 1000 });
  await dialog.getByRole('button', { name: 'Save platform appearance' }).click();
  await expect(
    dialog.getByRole('status').filter({ hasText: 'Platform appearance saved.' }),
  ).toBeVisible();
  await expect(page.locator('.sidebar .logo-word')).toHaveText('Studio North');
  await expect(page).toHaveTitle('Studio North');
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await expect(dialog.getByRole('button', { name: 'Save platform appearance' })).toHaveCSS(
    'background-color',
    'rgb(112, 48, 160)',
  );
  assert.equal(saved.logo, convertedLogo);
  assert.equal(saved.collapsedLogo, avatarPhoto);
  assert.equal(saved.darkSurface, '#202040');
  assert.equal(saved.fontScale, 120);
  assert.equal(saved.cornerRadius, 16);
  await expect(page.locator('.sidebar')).toHaveCSS('background-color', 'rgb(0, 17, 34)');
  await expect(dialog).toHaveCSS('background-color', 'rgb(32, 32, 64)');
  await dialog.getByRole('button', { name: 'Reset to defaults', exact: true }).click();
  await dialog.getByRole('button', { name: 'Restore defaults', exact: true }).click();
  await expect(dialog.getByLabel('Platform name', { exact: true })).toHaveValue('kanbada');
  await expect(dialog.getByLabel('Text size', { exact: true })).toHaveValue('100');
  await expect(dialog.getByLabel('Corner rounding', { exact: true })).toHaveValue('8');
  await expect(dialog.getByLabel('Show name beside logo', { exact: true })).toBeChecked();
  await expect(page.locator('.sidebar .logo-word')).toHaveText('Studio North');
  await dialog.getByRole('button', { name: 'Close dialog' }).click();
  await page.reload();
  await expect(page.locator('.sidebar .logo-word')).toHaveText('Studio North');
  for (const theme of ['light', 'dark']) {
    await page.evaluate((value) => localStorage.setItem('kanbada-theme', value), theme);
    saved = { ...saved, accent: '#f8f9fb' };
    await page.reload();
    await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
    await assertDropdowns(page);
    for (const label of ['Collapse sidebar', 'Expand sidebar']) {
      const toggle = page.getByRole('button', { name: label, exact: true });
      const fullyClickable = await toggle.evaluate((element) => {
        const rect = element.getBoundingClientRect();
        return [rect.left + 2, rect.right - 2].every((x) =>
          element.contains(document.elementFromPoint(x, rect.top + rect.height / 2)),
        );
      });
      assert.ok(fullyClickable, 'Both edges of the sidebar toggle must be visible and clickable');
      await toggle.click();
      const activeLogo = page.locator(
        label === 'Collapse sidebar'
          ? '.sidebar .brand-collapsed-logo'
          : '.sidebar .brand-expanded-logo',
      );
      await expect(activeLogo).toBeVisible();
      await expect(activeLogo).toHaveAttribute(
        'src',
        label === 'Collapse sidebar' ? avatarPhoto : convertedLogo,
      );
    }
    const project = page.locator('.sidebar .project-nav button.selected');
    await expect(project).toHaveCSS('background-color', 'rgb(14, 30, 46)');
    await expect(project.locator('.project-nav-text')).toHaveCSS('color', 'rgb(255, 255, 255)');
    for (const name of ['Board', 'List', 'Calendar']) {
      const tab = page.locator('.view-tabs').getByRole('button', { name, exact: true });
      await tab.click();
      await expect(tab).toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
      await expect(tab).toHaveCSS(
        'color',
        theme === 'dark' ? 'rgb(255, 255, 255)' : 'rgb(0, 0, 0)',
      );
      const underline = await tab.evaluate((element) => {
        const style = getComputedStyle(element, '::after');
        return { height: style.height, color: style.backgroundColor };
      });
      assert.deepEqual(underline, {
        height: '2px',
        color: theme === 'dark' ? 'rgb(255, 255, 255)' : 'rgb(0, 0, 0)',
      });
    }
    await page.locator('.view-tabs').getByRole('button', { name: 'Board', exact: true }).click();
    if (process.env.BRANDING_SCREENSHOT_DIR)
      await page.screenshot({
        path: `${process.env.BRANDING_SCREENSHOT_DIR}/theme-selection-${theme}.png`,
      });
  }
  await page.getByRole('button', { name: 'Add task', exact: true }).first().click();
  const drawer = page.locator('.card-drawer');
  await drawer.evaluate(async (element) => {
    await Promise.all(element.getAnimations().map((animation) => animation.finished));
  });
  for (const width of [1360, 390, 320]) {
    await page.setViewportSize({ width, height: 800 });
    await assertDropdowns(drawer);
    await expect(drawer.locator('.modal-footer')).toBeInViewport();
    const layout = await drawer.evaluate((element) => {
      const heading = element.querySelector('.modal-heading').getBoundingClientRect();
      const tabs = element.querySelector('.card-tabs').getBoundingClientRect();
      return {
        fits: element.scrollWidth <= element.clientWidth + 1,
        tabsBelowHeading: tabs.top >= heading.bottom - 1,
      };
    });
    assert.deepEqual(layout, { fits: true, tabsBelowHeading: true });
    if (process.env.BRANDING_SCREENSHOT_DIR)
      await page.screenshot({
        path: `${process.env.BRANDING_SCREENSHOT_DIR}/theme-card-${width}.png`,
      });
  }
  await drawer.getByRole('button', { name: 'Close task', exact: true }).click();
  await page.setViewportSize({ width: 1360, height: 1000 });
  await page.getByRole('button', { name: 'Edit profile', exact: true }).click();
  const profile = page.locator('.profile-modal');
  await profile.evaluate(async (element) => {
    await Promise.all(
      [...element.getAnimations(), ...element.parentElement.getAnimations()].map(
        (animation) => animation.finished,
      ),
    );
  });
  const security = profile.locator('.two-factor-settings');
  await expect(security.getByRole('heading', { name: 'Two-factor authentication' })).toHaveCSS(
    'font-size',
    '19.2px',
  );
  await expect(security.getByRole('heading', { name: 'Two-factor authentication' })).toHaveCSS(
    'font-weight',
    '600',
  );
  for (const width of [1360, 390]) {
    await page.setViewportSize({ width, height: 1000 });
    await expect(security).toHaveCSS('border-top-width', width > 760 ? '0px' : '1px');
    assert.ok(await profile.evaluate((element) => element.scrollWidth <= element.clientWidth + 1));
    if (process.env.BRANDING_SCREENSHOT_DIR)
      await page.screenshot({
        path: `${process.env.BRANDING_SCREENSHOT_DIR}/theme-profile-${width}.png`,
      });
  }
  await profile.getByRole('button', { name: 'Close dialog', exact: true }).click();
  await page.setViewportSize({ width: 1360, height: 1000 });
  await page.getByRole('button', { name: 'Settings', exact: true }).click();
  await page.getByRole('button', { name: 'Platform appearance', exact: true }).click();
  await dialog.getByLabel('Show name beside logo', { exact: true }).uncheck();
  await dialog.getByRole('button', { name: 'Save platform appearance' }).click();
  await expect(
    dialog.getByRole('status').filter({ hasText: 'Platform appearance saved.' }),
  ).toBeVisible();
  assert.equal(saved.showName, false);
  await dialog.getByRole('button', { name: 'Close dialog' }).click();
  await page.evaluate(() => localStorage.setItem('kanbada-theme', 'light'));
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await expect(page.locator('.sidebar .logo-word')).toHaveCount(0);
  await expect(page.locator('.sidebar .logo')).toHaveAccessibleName('Studio North');
  await expect(page).toHaveTitle('Studio North');
  const centered = await page.locator('.sidebar .logo').evaluate((element) => {
    const parent = element.getBoundingClientRect();
    const image = element.querySelector('img').getBoundingClientRect();
    return Math.abs((parent.left + parent.right) / 2 - (image.left + image.right) / 2) < 1;
  });
  assert.ok(centered, 'Logo-only branding must be centered in the sidebar');
  await page.getByRole('button', { name: 'Collapse sidebar', exact: true }).click();
  await expect(page.locator('.sidebar .brand-collapsed-logo')).toHaveAttribute('src', avatarPhoto);
  await expect(page.locator('.sidebar .brand-collapsed-logo')).toHaveCSS('width', '30px');
  await page.setViewportSize({ width: 390, height: 1000 });
  await expect(page.locator('.sidebar .brand-collapsed-logo')).toHaveCSS('display', 'none');
  await expect(page.locator('.sidebar .brand-expanded-logo')).toHaveCSS('width', '160px');
  await page.setViewportSize({ width: 1360, height: 1000 });
  saved = { ...saved, collapsedLogo: null };
  await page.reload();
  await expect(page.locator('.sidebar .brand-collapsed-logo')).toHaveCount(0);
  await expect(page.locator('.sidebar .platform-logo')).toHaveAttribute('src', convertedLogo);
  await page.getByRole('button', { name: 'Expand sidebar', exact: true }).click();
  await page.getByRole('button', { name: 'Sign out', exact: true }).click();
  await expect(page.locator('.auth-logo .logo-word')).toHaveCount(0);
  await expect(page.locator('.auth-logo')).toHaveAccessibleName('Studio North');
  await expect(page.locator('.auth-logo .brand-logo-only')).toHaveCSS('justify-content', 'center');
  await expect(page.locator('.auth-logo .platform-logo')).toHaveAttribute('src', convertedLogo);
  await expect(page.locator('.auth-content')).toHaveCSS('background-color', 'rgb(255, 248, 239)');
  assert.deepEqual(errors, []);
  console.log('Platform branding preview, save, theme, responsive and sign-in checks passed.');
} finally {
  await context.close();
  await browser.close();
}
