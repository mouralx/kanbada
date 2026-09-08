import type { Account } from './auth';
let account: Account | null = null;
export const setStorageAccount = (value: Account | null) => {
  account = value;
};
export const currentStorageAccount = () => account;
export const accountStoragePrefix = () =>
  account && account.id !== 'owner' ? `kanbada-account-${account.id}:` : '';
export const accountStorage = {
  getItem: (key: string) => localStorage.getItem(accountStoragePrefix() + key),
  setItem: (key: string, value: string) =>
    localStorage.setItem(accountStoragePrefix() + key, value),
  removeItem: (key: string) => localStorage.removeItem(accountStoragePrefix() + key),
};
