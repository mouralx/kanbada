import { apiRequest } from './apiClient';

export type GitHubMapping = {
  kind: 'status' | 'priority' | 'assignee';
  kanbadaValue: string;
  gitHubValue: string;
  isDefault: boolean;
};
export type GitHubInput = {
  version: number;
  baseUrl: string;
  owner: string;
  ownerType: 'organization' | 'user';
  projectNumber: number;
  repository: string;
  token: string;
  direction: 'github-to-kanbada' | 'kanbada-to-github' | 'bidirectional';
  statusFieldId: string;
  priorityFieldId: string;
  dueFieldId: string;
  syncLabels: boolean;
  syncAssignees: boolean;
  cron: string;
  timeZone: string;
  enabled: boolean;
  mappings: GitHubMapping[];
};
export type GitHubConnection = Omit<GitHubInput, 'token'> & {
  id: string;
  hasToken: boolean;
  nextRunAt: string;
  requestedAt: string | null;
  lastStartedAt: string | null;
  lastFinishedAt: string | null;
  lastError: string | null;
  lastSyncedCount: number;
  problems: {
    id: string;
    cardId: string;
    displayKey: string | null;
    url: string | null;
    creationPending: boolean;
    lastError: string | null;
  }[];
};
export type GitHubOption = { id: string; name: string };
export type GitHubMetadata = {
  projectId: string;
  title: string;
  url: string;
  repositoryId: string;
  fields: { id: string; name: string; dataType: string; options: GitHubOption[] }[];
};
const path = (workspace: string, project: string) =>
  `/workspaces/${encodeURIComponent(workspace)}/projects/${encodeURIComponent(project)}/github`;
export const githubRepository = {
  read: (workspace: string, project: string, signal?: AbortSignal) =>
    apiRequest<{ connection: GitHubConnection | null }>(path(workspace, project), { signal }),
  save: (workspace: string, project: string, input: GitHubInput) =>
    apiRequest<GitHubConnection>(path(workspace, project), {
      method: 'PUT',
      body: JSON.stringify(input),
    }),
  test: (workspace: string, project: string, input: GitHubInput, signal?: AbortSignal) =>
    apiRequest<GitHubMetadata>(path(workspace, project) + '/test', {
      method: 'POST',
      body: JSON.stringify(input),
      signal,
    }),
  user: (workspace: string, project: string, input: GitHubInput, login: string) =>
    apiRequest<GitHubOption>(path(workspace, project) + '/users/' + encodeURIComponent(login), {
      method: 'POST',
      body: JSON.stringify(input),
    }),
  run: (workspace: string, project: string) =>
    apiRequest<{ queued: boolean }>(path(workspace, project) + '/run', { method: 'POST' }),
  pause: (workspace: string, project: string, version: number) =>
    apiRequest<GitHubConnection>(path(workspace, project) + '/pause', {
      method: 'POST',
      body: JSON.stringify({ version }),
    }),
  resolve: (
    workspace: string,
    project: string,
    link: string,
    issueUrl: string | null,
    retryCreation = false,
  ) =>
    apiRequest<void>(path(workspace, project) + `/links/${encodeURIComponent(link)}/resolve`, {
      method: 'POST',
      body: JSON.stringify({ issueUrl, retryCreation }),
    }),
  cardLinks: (workspace: string, card: string, signal: AbortSignal) =>
    apiRequest<{ links: { key: string; url: string }[] }>(
      `/workspaces/${encodeURIComponent(workspace)}/cards/${encodeURIComponent(card)}/github`,
      { signal },
    ),
  hosts: () =>
    apiRequest<{
      canManage: boolean;
      hosts: { authority: string; approvedAt: string }[];
      configuredHosts: string[];
    }>('/github/hosts'),
  approveHost: (baseUrl: string) =>
    apiRequest<{ authority: string }>('/github/hosts', {
      method: 'POST',
      body: JSON.stringify({ baseUrl }),
    }),
  revokeHost: (baseUrl: string) =>
    apiRequest<void>('/github/hosts', { method: 'DELETE', body: JSON.stringify({ baseUrl }) }),
};
