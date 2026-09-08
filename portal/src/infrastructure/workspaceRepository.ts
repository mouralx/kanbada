import type { Member, State, Workspace } from '../domain/models';
import { apiEnabled } from './apiClient';
import { localRepository } from './local/localRepository';
import { remoteRepository } from './remoteRepository';

/** Persistence contract. All saves return a new canonical state; adapters never mutate callers. */
export interface WorkspaceRepository {
  load(): Promise<State>;
  save(state: State, previous: State): Promise<State>;
  listWorkspaces(): Promise<Workspace[]>;
  switchWorkspace(id: string): Promise<State>;
  createWorkspace(name: string, owner: Member): Promise<State>;
  deleteWorkspace(id: string): Promise<State>;
}

export const repository: WorkspaceRepository = apiEnabled ? remoteRepository : localRepository;
