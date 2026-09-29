import { apiRequest } from './apiClient';
import { cardPath, type CardQuery } from './cards';

export type ExportKind = 'project-xlsx' | 'dashboard-pdf' | 'workspace-xlsx';
export type ExportJob = {
  id: string;
  kind: ExportKind;
  name: string;
  locale: string;
  status: 'queued' | 'running' | 'completed' | 'failed' | 'expired';
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  expiresAt: string | null;
  processed: number;
  total: number | null;
  bytes: number;
  snapshotVersion: number | null;
  error: string | null;
};
export type ExportPage = { items: ExportJob[]; nextCursor: string | null };
export const requestExport = (
  workspace: string,
  kind: ExportKind,
  query: CardQuery,
  locale: string,
) =>
  apiRequest<ExportJob>(cardPath(workspace) + '/exports', {
    method: 'POST',
    body: JSON.stringify({ id: crypto.randomUUID(), kind, query, locale }),
  });
export const listExports = (workspace: string, after?: string, signal?: AbortSignal) =>
  apiRequest<ExportPage>(
    cardPath(workspace) + '/exports' + (after ? '?after=' + encodeURIComponent(after) : ''),
    { signal },
  );
export const getExport = (workspace: string, id: string, signal?: AbortSignal) =>
  apiRequest<ExportJob>(cardPath(workspace) + '/exports/' + encodeURIComponent(id), { signal });
export const downloadExportUrl = (workspace: string, id: string) =>
  '/api' + cardPath(workspace) + '/exports/' + encodeURIComponent(id) + '/download';
