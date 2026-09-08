import type { State } from '../domain/models';
import { accountStoragePrefix } from './accountStorage';
import { apiEnabled, apiRequest } from './apiClient';
import type { Account } from './auth';
export type CardShare = {
  token: string;
  cardId: string;
  workspaceId: string;
  sourcePrefix: string;
  access: 'signed-in' | 'members';
  expiresAt: string | null;
  createdAt: string;
};
const key = 'kanbada-card-shares-v1';
const entries = (): CardShare[] => {
  try {
    const value = JSON.parse(localStorage.getItem(key) || '[]');
    return Array.isArray(value) ? value : [];
  } catch {
    return [];
  }
};
// Local sharing adapter. The API must issue tokens and enforce authentication/access.
export const shareRepository = {
  async current(workspaceId: string, cardId: string) {
    if (apiEnabled)
      return apiRequest<CardShare | null>(`/workspaces/${workspaceId}/cards/${cardId}/share`);
    return (
      entries().find(
        (s) =>
          s.sourcePrefix === accountStoragePrefix() &&
          s.workspaceId === workspaceId &&
          s.cardId.toUpperCase() === cardId.toUpperCase(),
      ) ?? null
    );
  },
  async create(workspaceId: string, cardId: string, access: CardShare['access'], days: number) {
    if (apiEnabled)
      return apiRequest<CardShare>(`/workspaces/${workspaceId}/cards/${cardId}/share`, {
        method: 'POST',
        body: JSON.stringify({ access, days }),
      });
    const share: CardShare = {
      token: crypto.randomUUID(),
      workspaceId,
      cardId,
      sourcePrefix: accountStoragePrefix(),
      access,
      expiresAt: days ? new Date(Date.now() + days * 86400000).toISOString() : null,
      createdAt: new Date().toISOString(),
    };
    const remaining = entries().filter(
      (s) =>
        !(
          s.sourcePrefix === share.sourcePrefix &&
          s.workspaceId === workspaceId &&
          s.cardId.toUpperCase() === cardId.toUpperCase()
        ),
    );
    localStorage.setItem(key, JSON.stringify([...remaining, share]));
    return share;
  },
  async revoke(token: string) {
    if (apiEnabled) {
      await apiRequest('/shares/' + token, { method: 'DELETE' });
      return;
    }
    localStorage.setItem(
      key,
      JSON.stringify(
        entries().filter((s) => s.token !== token || s.sourcePrefix !== accountStoragePrefix()),
      ),
    );
  },
  async resolve(token: string, account: Account) {
    if (apiEnabled)
      return apiRequest<{ share: CardShare; task: State['tasks'][number]; data: State }>(
        '/shares/' + token,
      );
    const share = entries().find((s) => s.token === token);
    if (!share || (share.expiresAt && new Date(share.expiresAt).getTime() < Date.now()))
      throw new Error('This link has expired or is no longer available.');
    const raw = localStorage.getItem(
      share.sourcePrefix +
        (share.workspaceId === 'studio' ? 'kanbada-v1' : 'kanbada-workspace-' + share.workspaceId),
    );
    const data: State | null = raw ? JSON.parse(raw) : null;
    const task = data?.tasks.find((task) => task.id.toUpperCase() === share.cardId.toUpperCase());
    if (!data || !task) throw new Error('This link has expired or is no longer available.');
    if (
      share.access === 'members' &&
      !data.members.some((member) => member.email.toLowerCase() === account.email.toLowerCase())
    )
      throw new Error('This account does not have access to this card.');
    return { share, task, data };
  },
};
export const cardShareUrl = (token: string) => {
  const url = new URL(window.location.href);
  url.search = '';
  url.hash = '';
  url.searchParams.set('share', token);
  return url.href;
};
