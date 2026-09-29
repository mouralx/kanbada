import type { Task } from '../domain/models';
import { apiRequest } from './apiClient';

export type CardQuery = {
  project?: string;
  mine?: boolean;
  active?: boolean;
  search?: string;
  priority?: string;
  person?: string;
  bucket?: string;
  swimlane?: string;
  status?: string;
  completion?: string;
  from?: string;
  to?: string;
  today?: string;
  unassigned?: boolean;
};
export type CardPage = { items: Task[]; total: number; nextCursor: string | null; version: number };
export type GroupCounts = { total: number; completed: number; overdue: number };
export type CardSummary = {
  version: number;
  counts: GroupCounts & { open: number; highPriority: number; unassigned: number };
  groups: (GroupCounts & {
    project: string;
    status: string;
    bucket: string | null;
    swimlane: string | null;
  })[];
  due: { date: string; total: number; completed: number }[];
  workload: { email: string; total: number }[];
  checklist: { total: number; completed: number } | null;
  usedLabels: string[];
  activity: {
    cardId: string;
    title: string;
    id: string;
    at: string;
    actor: string;
    change: string;
  }[];
  myOpen: number;
};
export function queryString(query: CardQuery): string {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query))
    if (value !== undefined) params.set(key, String(value));
  return params.toString();
}
export const cardPath = (workspace: string) => '/workspaces/' + encodeURIComponent(workspace);
export const getCard = (workspace: string, id: string) =>
  apiRequest<Task>(cardPath(workspace) + '/cards/' + encodeURIComponent(id));
export const getCards = (
  workspace: string,
  query: CardQuery | string,
  after?: string,
  signal?: AbortSignal,
) =>
  apiRequest<CardPage>(
    cardPath(workspace) +
      '/cards?' +
      (typeof query === 'string' ? query : queryString(query)) +
      (after ? '&after=' + encodeURIComponent(after) : ''),
    { signal },
  );
export const getSummary = (workspace: string, query: CardQuery | string, signal?: AbortSignal) =>
  apiRequest<CardSummary>(
    cardPath(workspace) +
      '/card-summary?' +
      (typeof query === 'string' ? query : queryString(query)),
    { signal },
  );

export function sumGroups(
  summary: CardSummary | null,
  match: (group: CardSummary['groups'][number]) => boolean,
): GroupCounts {
  return (summary?.groups ?? []).filter(match).reduce(
    (sum, group) => ({
      total: sum.total + group.total,
      completed: sum.completed + group.completed,
      overdue: sum.overdue + group.overdue,
    }),
    { total: 0, completed: 0, overdue: 0 },
  );
}
