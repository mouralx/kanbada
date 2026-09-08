import { apiEnabled, apiRequest } from './apiClient';
export type Provider = 'google' | 'microsoft';
export type Account = { id: string; name: string; email: string; provider: Provider };
const sessionKey = 'kanbada-session-v1';
const accountsKey = 'kanbada-accounts-v1';
export const sampleAccounts: Account[] = [
  { id: 'owner', name: 'Alex Morgan', email: 'alex@studio.co', provider: 'google' },
  { id: 'microsoft-alex', name: 'Alex Morgan', email: 'alex@outlook.com', provider: 'microsoft' },
];
// Local UI adapter. Replace these methods with the API's identity/session endpoints.
// No passwords, provider tokens, or actual Google/Microsoft authentication are used here.
export const authRepository = {
  async session(): Promise<Account | null> {
    if (apiEnabled) return (await apiRequest<{ user: Account | null }>('/auth/session')).user;
    try {
      const value = JSON.parse(localStorage.getItem(sessionKey) || 'null');
      return value &&
        typeof value.id === 'string' &&
        typeof value.name === 'string' &&
        typeof value.email === 'string' &&
        ['google', 'microsoft'].includes(value.provider)
        ? value
        : null;
    } catch {
      return null;
    }
  },
  async accounts(provider: Provider): Promise<Account[]> {
    let saved: Account[] = [];
    try {
      saved = JSON.parse(localStorage.getItem(accountsKey) || '[]');
      if (!Array.isArray(saved)) saved = [];
    } catch {
      /* Keep the built-in accounts available. */
    }
    return [
      ...sampleAccounts,
      ...saved.filter((a) => !sampleAccounts.some((s) => s.id === a.id)),
    ].filter((a) => a.provider === provider);
  },
  async signIn(provider: Provider, name: string, email: string): Promise<Account> {
    name = name.trim();
    email = email.trim().toLowerCase();
    if (!name || !/^\S+@\S+\.\S+$/.test(email))
      throw new Error('Enter your name and a valid email address.');
    const existing = (await this.accounts(provider)).find((a) => a.email.toLowerCase() === email);
    const account = existing ?? { id: crypto.randomUUID(), name, email, provider };
    if (!existing) {
      let saved: Account[] = [];
      try {
        saved = JSON.parse(localStorage.getItem(accountsKey) || '[]');
        if (!Array.isArray(saved)) saved = [];
      } catch {
        /* Start a fresh directory. */
      }
      localStorage.setItem(accountsKey, JSON.stringify([...saved, account]));
    }
    localStorage.setItem(sessionKey, JSON.stringify(account));
    return account;
  },
  async signOut() {
    localStorage.removeItem(sessionKey);
  },
};
export const authSessionKey = sessionKey;
