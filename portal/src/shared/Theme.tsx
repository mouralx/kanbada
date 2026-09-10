import { ChevronDown, Monitor, Moon, Sun } from 'lucide-react';
import {
  createContext,
  useContext,
  useEffect,
  useId,
  useLayoutEffect,
  useRef,
  useState,
  type ReactNode,
} from 'react';
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
export function ThemeSelect({
  compact = false,
  iconOnly = false,
}: {
  compact?: boolean;
  iconOnly?: boolean;
}) {
  return iconOnly ? <ThemeIconSelect /> : <ThemeTextSelect compact={compact} />;
}
function ThemeIconSelect() {
  const { theme, setTheme } = useContext(Context);
  const { t } = useI18n();
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const id = useId();
  const options = [
    { value: 'light', label: 'Light', icon: Sun },
    { value: 'dark', label: 'Dark', icon: Moon },
    { value: 'system', label: 'System', icon: Monitor },
  ] as const;
  const selected = options.find((option) => option.value === theme)!;
  const Icon = selected.icon;
  useEffect(() => {
    if (!open) return;
    const dismiss = (event: PointerEvent) => {
      if (!root.current?.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener('pointerdown', dismiss);
    return () => document.removeEventListener('pointerdown', dismiss);
  }, [open]);
  return (
    <div
      className="theme-icon-control"
      ref={root}
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget)) setOpen(false);
      }}
      onKeyDown={(event) => {
        if (event.key === 'Escape' && open) {
          event.preventDefault();
          setOpen(false);
          trigger.current?.focus();
        }
      }}
    >
      <button
        type="button"
        className="theme-icon-trigger"
        ref={trigger}
        aria-label={`${t('Theme')}: ${t(selected.label)}`}
        title={`${t('Theme')}: ${t(selected.label)}`}
        aria-expanded={open}
        aria-controls={id}
        onClick={() => setOpen(!open)}
      >
        <Icon size={17} aria-hidden="true" />
        <ChevronDown size={12} aria-hidden="true" />
      </button>
      {open && (
        <div id={id} className="theme-icon-options" role="group" aria-label={t('Theme')}>
          {options.map(({ value, label, icon: OptionIcon }) => (
            <button
              type="button"
              key={value}
              aria-label={t(label)}
              title={t(label)}
              aria-pressed={theme === value}
              onClick={() => {
                setTheme(value);
                setOpen(false);
                trigger.current?.focus();
              }}
            >
              <OptionIcon size={18} aria-hidden="true" />
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
function ThemeTextSelect({ compact }: { compact: boolean }) {
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
