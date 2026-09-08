import { Monitor, Moon, Sun } from 'lucide-react';
import { createContext, useContext, useLayoutEffect, useState, type ReactNode } from 'react';
import { useI18n } from './i18n';
type Theme = 'light' | 'dark' | 'system';
const valid = (value: string | null): Theme =>
  value === 'light' || value === 'dark' ? value : 'system';
const key = 'kanbada-theme';
const Context = createContext<{ theme: Theme; setTheme: (theme: Theme) => void }>({
  theme: 'system',
  setTheme: () => {},
});
export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setTheme] = useState<Theme>(() => {
    try {
      return valid(localStorage.getItem(key));
    } catch {
      return 'system';
    }
  });
  useLayoutEffect(() => {
    const media = window.matchMedia('(prefers-color-scheme: dark)');
    const apply = () => {
      const resolved = theme === 'system' ? (media.matches ? 'dark' : 'light') : theme;
      document.documentElement.dataset.theme = resolved;
      document.documentElement.style.colorScheme = resolved;
      document
        .querySelector('meta[name="theme-color"]')
        ?.setAttribute('content', resolved === 'dark' ? '#121b28' : '#f8f9fb');
    };
    apply();
    media.addEventListener('change', apply);
    return () => media.removeEventListener('change', apply);
  }, [theme]);
  useLayoutEffect(() => {
    const changed = (event: StorageEvent) => {
      if (event.key === key || event.key === null) setTheme(valid(event.newValue));
    };
    window.addEventListener('storage', changed);
    return () => window.removeEventListener('storage', changed);
  }, []);
  const choose = (value: Theme) => {
    setTheme(value);
    try {
      localStorage.setItem(key, value);
    } catch {
      /* Retain the selection for this session. */
    }
  };
  return <Context.Provider value={{ theme, setTheme: choose }}>{children}</Context.Provider>;
}
export function ThemeSelect({ compact = false }: { compact?: boolean }) {
  const { theme, setTheme } = useContext(Context);
  const { t } = useI18n();
  const Icon = theme === 'dark' ? Moon : theme === 'light' ? Sun : Monitor;
  return (
    <label className={`theme-control ${compact ? 'theme-compact' : ''}`}>
      <Icon size={16} />
      {!compact && <span>{t('Theme')}</span>}
      <select
        aria-label={t('Theme')}
        value={theme}
        onChange={(e) => setTheme(e.target.value as Theme)}
      >
        <option value="light">{t('Light')}</option>
        <option value="dark">{t('Dark')}</option>
        <option value="system">{t('System')}</option>
      </select>
    </label>
  );
}
