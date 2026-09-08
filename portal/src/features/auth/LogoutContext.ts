import { createContext } from 'react';
export const LogoutContext = createContext<{
  logout: () => Promise<void>;
  busy: boolean;
  error: string;
} | null>(null);
