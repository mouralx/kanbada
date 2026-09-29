import { applyChanges, workspaceChanges, type Change, type ChangeResult } from './stateChanges';
import type { Member, State, Workspace } from '../domain/models';
import { accountStorage, accountStoragePrefix } from './accountStorage';
import { ApiError, apiRequest } from './apiClient';
export const activeWorkspaceId = () =>
  accountStorage.getItem('kanbada-active-workspace') || 'studio';
const snapshots = new Map<string, State>();
const cacheKey = (id: string) => accountStoragePrefix() + id;
async function loadWorkspace(id: string): Promise<State> {
  const key = cacheKey(id);
  const cached = snapshots.get(key);
  const state = await apiRequest<State>(
    '/workspaces/' + encodeURIComponent(id) + '?metadataOnly=true',
    {
      headers: cached?.version === undefined ? {} : { 'If-None-Match': `"${cached.version}"` },
    },
    cached,
  );
  snapshots.set(key, structuredClone(state));
  return state;
}
export const remoteRepository = {
  async load(): Promise<State> {
    try {
      return await loadWorkspace(activeWorkspaceId());
    } catch (e) {
      if (e instanceof ApiError && e.status === 404 && activeWorkspaceId() !== 'studio') {
        accountStorage.removeItem('kanbada-active-workspace');
        return loadWorkspace('studio');
      }
      throw e;
    }
  },
  async save(state: State, previous: State): Promise<State> {
    if (state.workspace.id !== previous.workspace.id || previous.version === undefined)
      throw new Error('Reload this workspace before saving.');
    const changes = workspaceChanges({ ...previous, tasks: [] }, { ...state, tasks: [] });
    const oldCards = new Map(previous.tasks.map((task) => [task.id, task]));
    const nextIds = new Set(state.tasks.map((task) => task.id));
    const upserts = state.tasks
      .filter((task) => JSON.stringify(task) !== JSON.stringify(oldCards.get(task.id)))
      .map((task) => {
        const old = oldCards.get(task.id);
        return old
          ? {
              id: task.id,
              changes: workspaceChanges(
                { ...previous, tasks: [old] },
                { ...previous, tasks: [task] },
              ),
            }
          : { id: task.id, value: task };
      })
      .filter((edit) => !edit.changes || edit.changes.length);
    const removed = previous.tasks.filter((task) => !nextIds.has(task.id)).map((task) => task.id);
    if (!changes.length && !upserts.length && !removed.length) return structuredClone(previous);
    const key = cacheKey(state.workspace.id);
    const result = await apiRequest<
      ChangeResult & { cards: { id: string; changes: Change[] }[]; removed: string[] }
    >('/workspaces/' + encodeURIComponent(state.workspace.id) + '/changes', {
      method: 'PATCH',
      headers: { 'If-Match': String(previous.version) },
      body: JSON.stringify({
        changes,
        upserts,
        removed,
        retained: previous.tasks.map((task) => task.id),
      }),
    });
    const saved = applyChanges({ ...previous, tasks: [] }, result);
    snapshots.set(key, structuredClone(saved));
    saved.tasks = result.cards.flatMap((card) => {
      const old = oldCards.get(card.id);
      const edit = upserts.find((edit) => edit.id === card.id);
      return applyChanges(
        { ...previous, tasks: edit?.value || !old ? [] : [old] },
        { version: result.version, changes: card.changes },
      ).tasks;
    });
    return saved;
  },
  async listWorkspaces(): Promise<Workspace[]> {
    return apiRequest('/workspaces');
  },
  async switchWorkspace(id: string): Promise<State> {
    const state = await loadWorkspace(id);
    accountStorage.setItem('kanbada-active-workspace', id);
    return state;
  },
  async createWorkspace(name: string, _owner: Member): Promise<State> {
    const state = await apiRequest<State>('/workspaces', {
      method: 'POST',
      body: JSON.stringify({ name }),
    });
    accountStorage.setItem('kanbada-active-workspace', state.workspace.id);
    return state;
  },
  async deleteWorkspace(id: string): Promise<State> {
    await apiRequest('/workspaces/' + encodeURIComponent(id), { method: 'DELETE' });
    snapshots.delete(cacheKey(id));
    if (activeWorkspaceId() === id) accountStorage.setItem('kanbada-active-workspace', 'studio');
    return this.load();
  },
};
