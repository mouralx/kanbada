import { applyChanges, workspaceChanges, type ChangeResult } from './stateChanges';
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
    '/workspaces/' + encodeURIComponent(id),
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
    const changes = workspaceChanges(previous, state);
    if (!changes.length) return structuredClone(previous);
    const key = cacheKey(state.workspace.id);
    const result = await apiRequest<ChangeResult>(
      '/workspaces/' + encodeURIComponent(state.workspace.id),
      {
        method: 'PATCH',
        headers: { 'If-Match': String(previous.version) },
        body: JSON.stringify({ changes }),
      },
    );
    const saved = applyChanges(previous, result);
    snapshots.set(key, structuredClone(saved));
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
