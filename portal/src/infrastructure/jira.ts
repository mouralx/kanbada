import { apiRequest } from './apiClient';

export type JiraMapping = {
  kind: 'status' | 'priority' | 'assignee';
  kanbadaValue: string;
  jiraValue: string;
  isDefault?: boolean;
};
export type JiraInput = {
  version: number;
  baseUrl: string;
  edition: 'cloud' | 'data-center';
  email: string;
  token: string;
  jql: string;
  jiraProjectKey: string;
  issueTypeId: string;
  direction: 'jira-to-kanbada' | 'kanbada-to-jira' | 'bidirectional';
  cron: string;
  timeZone: string;
  enabled: boolean;
  syncAssignees?: boolean;
  importMissingAssignees?: boolean;
  mappings: JiraMapping[];
};
export type JiraConnection = Omit<JiraInput, 'token'> & {
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
    jiraKey: string | null;
    jiraIssueId: string | null;
    creationPending: boolean;
    lastError: string | null;
  }[];
};
type JiraOption = { id: string; name: string };
export type JiraMetadata = {
  issueTypes: JiraOption[];
  statuses: JiraOption[];
  priorities: JiraOption[];
  assignees?: JiraOption[];
  warnings?: string[];
  matchedIssues: number;
};
const path = (workspace: string, project: string) =>
  `/workspaces/${encodeURIComponent(workspace)}/projects/${encodeURIComponent(project)}/jira`;
export const jiraRepository = {
  cardLinks: (workspace: string, card: string, signal: AbortSignal) =>
    apiRequest<{ links: { key: string; url: string }[] }>(
      `/workspaces/${encodeURIComponent(workspace)}/cards/${encodeURIComponent(card)}/jira`,
      { signal },
    ),
  hosts: () =>
    apiRequest<{
      canManage: boolean;
      hosts: { authority: string; approvedAt: string }[];
      configuredHosts: string[];
    }>('/jira/hosts'),
  approveHost: (baseUrl: string) =>
    apiRequest<{ authority: string }>('/jira/hosts', {
      method: 'POST',
      body: JSON.stringify({ baseUrl }),
    }),
  revokeHost: (baseUrl: string) =>
    apiRequest<void>('/jira/hosts', { method: 'DELETE', body: JSON.stringify({ baseUrl }) }),
  read: (workspace: string, project: string) =>
    apiRequest<{ connection: JiraConnection | null }>(path(workspace, project)),
  save: (workspace: string, project: string, input: JiraInput) =>
    apiRequest<JiraConnection>(path(workspace, project), {
      method: 'PUT',
      body: JSON.stringify(input),
    }),
  test: (workspace: string, project: string, input: JiraInput, signal?: AbortSignal) =>
    apiRequest<JiraMetadata>(path(workspace, project) + '/test', {
      method: 'POST',
      body: JSON.stringify(input),
      signal,
    }),
  run: (workspace: string, project: string) =>
    apiRequest<{ queued: boolean }>(path(workspace, project) + '/run', { method: 'POST' }),
  resolve: (workspace: string, project: string, linkId: string, jiraIssueId: string) =>
    apiRequest<void>(path(workspace, project) + `/links/${encodeURIComponent(linkId)}/resolve`, {
      method: 'POST',
      body: JSON.stringify({ jiraIssueId }),
    }),
};
