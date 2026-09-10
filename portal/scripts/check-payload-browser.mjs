import { enrollApiAccount } from './authenticator-test-helpers.mjs';
import { chromium } from '@playwright/test';
import assert from 'node:assert/strict';
const browser = await chromium.launch();
try {
  const page = await browser.newPage();
  await page.goto('http://localhost:4173');
  const registration = await page.request.post('http://localhost:4173/api/auth/register', {
    headers: { 'X-Kanbada-Request': '1' },
    data: {
      email: `payload-${Date.now()}@example.test`,
      name: 'Payload Test',
      password: 'A-long-payload-test-password',
    },
  });
  assert.equal(registration.status(), 200);
  const { id } = await registration.json();
  await enrollApiAccount(page.request, 'A-long-payload-test-password');
  const metrics = await page.evaluate(async (id) => {
    const { remoteRepository: repository } =
      await import('/src/infrastructure/remoteRepository.ts');
    const { setStorageAccount } = await import('/src/infrastructure/accountStorage.ts');
    const { workspaceChanges, applyChanges, differences } =
      await import('/src/infrastructure/stateChanges.ts');
    const headers = { 'X-Kanbada-Request': '1', 'Content-Type': 'application/json' };
    setStorageAccount({ id, name: 'Payload Test', email: 'payload@example.test' });
    let state = await repository.load();
    state.workspace.banner = 'data:image/png;base64,' + 'x'.repeat(250000);
    state.tasks = Array.from({ length: 100 }, (_, index) => ({
      id: `KB-PAYLOAD-${index}`,
      title: `Card ${index}`,
      project: 'my-activities',
      status: 'Backlog',
      priority: 'Medium',
      description: 'Context '.repeat(300),
      due: '',
      labels: [],
      assignees: [],
      comments: [],
      checklist: [],
      attachments: [],
    }));
    // Fixture setup uses the compatibility endpoint; all subsequent edits use the real portal adapter.
    const seed = await fetch('/api/workspaces/studio', {
      method: 'PUT',
      headers: { ...headers, 'If-Match': String(state.version) },
      body: JSON.stringify(state),
    });
    if (!seed.ok) throw new Error(await seed.text());
    state = await repository.load();
    const fullBytes = new TextEncoder().encode(JSON.stringify(state)).length;
    const originalFetch = window.fetch;
    const requests = [];
    window.fetch = async (...args) => {
      const response = await originalFetch(...args);
      const init = args[1] || {};
      if (String(args[0]).includes('/api/workspaces/'))
        requests.push({
          method: init.method || 'GET',
          bodyBytes: new TextEncoder().encode(init.body || '').length,
          status: response.status,
          responseBytes: new TextEncoder().encode(await response.clone().text()).length,
        });
      return response;
    };
    try {
      const next = structuredClone(state);
      next.tasks[50].title = 'A tiny edit';
      next.notifications.unshift({
        id: crypto.randomUUID(),
        at: new Date().toISOString(),
        message: 'Task saved',
      });
      next.activity.unshift('A tiny edit · Backlog');
      const saved = await repository.save(next, state);
      if (
        saved.tasks[50].title !== 'A tiny edit' ||
        saved.tasks[50].history.length !== 2 ||
        saved.workspace.banner !== state.workspace.banner
      )
        throw new Error('Canonical delta reconstruction failed');
      const cached = await repository.load();
      if (JSON.stringify(cached) !== JSON.stringify(saved))
        throw new Error('Cached refresh changed state');
      // Exercise array insertion, removal, reordering and checklist fields in the shared diff algorithm.
      for (const [a, b] of [
        [
          [1, 2, 3],
          [0, 1, 2],
        ],
        [
          [1, 2, 3],
          [3, 1],
        ],
        [[{ text: 'one', done: false }], [{ text: 'one', done: true }]],
        [[1, 2], []],
      ]) {
        const before = { ...saved, activity: a };
        const changes = differences(a, b, '/activity');
        const after = applyChanges(before, { version: saved.version, changes });
        if (JSON.stringify(after.activity) !== JSON.stringify(b))
          throw new Error('Array diff failed');
      }
      if (workspaceChanges(saved, saved).length) throw new Error('No-op save produced changes');
      return { fullBytes, requests };
    } finally {
      window.fetch = originalFetch;
    }
  }, id);
  const edits = metrics.requests.filter((request) => request.method === 'PATCH');
  assert.equal(edits.length, 1);
  assert.ok(edits[0].bodyBytes < 1000, JSON.stringify(edits[0]));
  assert.ok(edits[0].responseBytes < 2000, JSON.stringify(edits[0]));
  assert.ok(edits[0].bodyBytes / metrics.fullBytes < 0.01);
  assert.ok(
    metrics.requests.some((request) => request.status === 304 && request.responseBytes === 0),
  );
  assert.ok(!metrics.requests.some((request) => request.method === 'PUT'));
  console.log('PASS: payload measurements', JSON.stringify(metrics));
} finally {
  await browser.close();
}
