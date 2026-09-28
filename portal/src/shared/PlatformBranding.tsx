import {
  createContext,
  useContext,
  useEffect,
  useLayoutEffect,
  useState,
  type ReactNode,
} from 'react';
import { apiEnabled, apiRequest } from '../infrastructure/apiClient';
import { useI18n } from './i18n';
import { foreground, themeVariables, type ThemeOptions } from './brandingTheme';
export { foreground } from './brandingTheme';

export type Branding = ThemeOptions & {
  version: number;
  name: string;
  logo: string | null;
  collapsedLogo: string | null;
  showName: boolean;
  primary: string;
  accent: string;
  lightBackground: string;
  darkBackground: string;
  defaultTheme: 'light' | 'dark' | 'system';
};
export const defaultBranding: Branding = {
  version: 0,
  name: 'kanbada',
  logo: null,
  collapsedLogo: null,
  showName: true,
  primary: '#334153',
  accent: '#aac2e1',
  lightBackground: '#f8f9fb',
  darkBackground: '#17191c',
  defaultTheme: 'system',
  lightSurface: null,
  darkSurface: null,
  lightText: null,
  darkText: null,
  lightBorder: null,
  darkBorder: null,
  sidebarBackground: null,
  sidebarText: null,
  fontFamily: 'default',
  fontScale: 100,
  cornerRadius: 8,
};

const Context = createContext<{
  branding: Branding;
  save: (input: Branding) => Promise<Branding>;
}>({
  branding: defaultBranding,
  save: async () => {
    throw new Error('Platform branding is unavailable.');
  },
});
export const useBranding = () => useContext(Context);

export function PlatformBrandingProvider({ children }: { children: ReactNode }) {
  const { t } = useI18n();
  const [branding, setBranding] = useState(defaultBranding);
  const [loaded, setLoaded] = useState(!apiEnabled);
  const [error, setError] = useState('');
  useEffect(() => {
    if (!apiEnabled) return;
    let active = true;
    let etag = '';
    const load = async () => {
      try {
        const response = await fetch('/api/platform/branding', {
          headers: etag ? { 'If-None-Match': etag } : {},
        });
        if (!response.ok && response.status !== 304)
          throw new Error('Could not load platform branding.');
        if (response.status !== 304) {
          const value: Branding = await response.json();
          if (active) {
            setBranding((current) => (value.version >= current.version ? value : current));
            etag = response.headers.get('ETag') ?? '';
          }
        }
        if (active) {
          setLoaded(true);
          setError('');
        }
      } catch (reason) {
        if (active)
          setError(reason instanceof Error ? reason.message : 'Could not load platform branding.');
      }
    };
    void load();
    const timer = window.setInterval(() => void load(), 60000);
    return () => {
      active = false;
      window.clearInterval(timer);
    };
  }, []);
  useLayoutEffect(() => {
    const root = document.documentElement;
    root.dataset.platformTheme = 'true';
    root.dataset.brandPrimary = String(branding.primary !== defaultBranding.primary);
    root.dataset.brandAccent = String(branding.accent !== defaultBranding.accent);
    root.dataset.brandLight = String(branding.lightBackground !== defaultBranding.lightBackground);
    root.dataset.brandDark = String(branding.darkBackground !== defaultBranding.darkBackground);
    const variables = {
      ...themeVariables(branding),
      '--brand-primary': branding.primary,
      '--brand-primary-text': foreground(branding.primary),
      '--brand-accent': branding.accent,
      '--brand-accent-text': foreground(branding.accent),
      '--brand-light': branding.lightBackground,
      '--brand-light-text': foreground(branding.lightBackground),
      '--brand-dark': branding.darkBackground,
      '--brand-dark-text': foreground(branding.darkBackground),
    };
    for (const [name, value] of Object.entries(variables)) root.style.setProperty(name, value);
    document.title =
      branding.name === 'kanbada' ? 'Kanbada — Make room for great work' : branding.name;
    document
      .querySelector('meta[name="description"]')
      ?.setAttribute('content', `${branding.name} — a calmer place to do your best work.`);
    const icon = document.querySelector<HTMLLinkElement>('link[data-platform-icon]');
    if (branding.logo) {
      const link = icon ?? document.createElement('link');
      link.rel = 'icon';
      link.dataset.platformIcon = 'true';
      link.href = branding.logo;
      if (!icon) document.head.append(link);
    } else icon?.remove();
  }, [branding]);
  async function save(input: Branding) {
    const value = await apiRequest<Branding>('/platform/branding', {
      method: 'PUT',
      body: JSON.stringify(input),
    });
    setBranding(value);
    return value;
  }
  if (!loaded)
    return (
      <div className="loading">
        {error ? (
          <>
            <p role="alert">{t(error)}</p>
            <button onClick={() => window.location.reload()}>{t('Retry')}</button>
          </>
        ) : (
          t('Loading…')
        )}
      </div>
    );
  return (
    <Context.Provider value={{ branding, save }}>
      {error && (
        <p className="branding-refresh-error" role="alert">
          {error}
        </p>
      )}
      {children}
    </Context.Provider>
  );
}
