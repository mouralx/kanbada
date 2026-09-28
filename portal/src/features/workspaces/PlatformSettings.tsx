import { useEffect, useState, type CSSProperties } from 'react';
import { ImagePlus, Palette, RotateCcw, Save } from 'lucide-react';
import { apiEnabled, apiRequest } from '../../infrastructure/apiClient';
import { defaultBranding, useBranding, type Branding } from '../../shared/PlatformBranding';
import { isThemeColor, themePalette, themeVariables } from '../../shared/brandingTheme';
import { useI18n } from '../../shared/i18n';
import '../projects/jira-settings.css';
import './platform-settings.css';

function ThemeColorField({
  label,
  value,
  automatic,
  optional,
  onChange,
}: {
  label: string;
  value: string | null | undefined;
  automatic: string;
  optional: boolean;
  onChange: (value: string | null) => void;
}) {
  const { t } = useI18n();
  const invalid = value != null && !isThemeColor(value);
  return (
    <div>
      <label>
        {t(label)}
        <span className="platform-color-field">
          <input
            type="color"
            aria-label={t(label)}
            value={isThemeColor(value) ? value : automatic}
            onChange={(event) => onChange(event.target.value)}
          />
          <input
            type="text"
            aria-label={t('{0} hexadecimal value', t(label))}
            value={value ?? automatic}
            pattern="#[0-9a-fA-F]{6}"
            maxLength={7}
            required
            spellCheck={false}
            autoComplete="off"
            aria-invalid={invalid}
            onChange={(event) => onChange(event.target.value)}
          />
        </span>
      </label>
      {optional && (
        <button
          type="button"
          className="platform-auto-color"
          aria-pressed={value == null}
          aria-label={t('Use automatic {0}', t(label))}
          onClick={() => onChange(null)}
        >
          {t('Automatic')}
        </button>
      )}
      {invalid && <small role="alert">{t('Use a six-digit color such as #0072BC.')}</small>}
    </div>
  );
}

export function PlatformSettingsButton({ onOpen }: { onOpen: () => void }) {
  const { t } = useI18n();
  const [allowed, setAllowed] = useState(false);
  const [error, setError] = useState('');
  useEffect(() => {
    if (!apiEnabled) return;
    let active = true;
    apiRequest<{ canManage: boolean }>('/platform/branding/access')
      .then((result) => {
        if (active) setAllowed(result.canManage);
      })
      .catch((reason: unknown) => {
        if (active)
          setError(
            reason instanceof Error ? reason.message : t('Could not load platform permissions.'),
          );
      });
    return () => {
      active = false;
    };
  }, [t]);
  return (
    <>
      {error && <p role="alert">{error}</p>}
      {allowed && (
        <button className="secondary" onClick={onOpen}>
          <Palette size={16} />
          {t('Platform appearance')}
        </button>
      )}
    </>
  );
}

export function PlatformSettings() {
  const { branding, save } = useBranding();
  const { t } = useI18n();
  const [draft, setDraft] = useState(branding);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [resetting, setResetting] = useState(false);
  const [previewTheme, setPreviewTheme] = useState<'light' | 'dark'>('light');
  const dirty = JSON.stringify(draft) !== JSON.stringify(branding);
  useEffect(() => {
    if (!dirty) return;
    const warn = (event: BeforeUnloadEvent) => event.preventDefault();
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [dirty]);
  function change<K extends keyof Branding>(key: K, value: Branding[K]) {
    setDraft((current) => ({ ...current, [key]: value }));
    setMessage('');
  }
  async function upload(key: 'logo' | 'collapsedLogo', file?: File) {
    if (!file) return;
    setError('');
    const svg =
      file.type === 'image/svg+xml' || (!file.type && file.name.toLowerCase().endsWith('.svg'));
    if (
      (!svg && !['image/png', 'image/jpeg', 'image/gif', 'image/webp'].includes(file.type)) ||
      file.size > 2 * 1024 * 1024
    ) {
      setError(t('Upload a PNG, JPG, GIF, WebP, or SVG logo smaller than 2 MB.'));
      return;
    }
    setBusy(true);
    try {
      let logo = await new Promise<string>((resolve, reject) => {
        const reader = new FileReader();
        reader.onerror = () => reject(new Error(t('Could not read the logo.')));
        reader.onload = () =>
          typeof reader.result === 'string'
            ? resolve(reader.result)
            : reject(new Error(t('Could not read the logo.')));
        reader.readAsDataURL(file);
      });
      if (svg) {
        const image = new Image();
        image.src = logo;
        await image.decode();
        if (!image.naturalWidth || !image.naturalHeight)
          throw new Error(t('The SVG must have valid dimensions or a viewBox.'));
        const scale = 1024 / Math.max(image.naturalWidth, image.naturalHeight);
        const canvas = document.createElement('canvas');
        canvas.width = Math.max(1, Math.round(image.naturalWidth * scale));
        canvas.height = Math.max(1, Math.round(image.naturalHeight * scale));
        const context = canvas.getContext('2d');
        if (!context)
          throw new Error(t('Your browser could not convert this SVG. Upload a PNG instead.'));
        context.drawImage(image, 0, 0, canvas.width, canvas.height);
        logo = canvas.toDataURL('image/png');
        if ((logo.split(',')[1].length * 3) / 4 > 2 * 1024 * 1024)
          throw new Error(
            t('The converted SVG is too large. Simplify the image or upload a smaller PNG.'),
          );
      }
      change(key, logo);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : t('Could not read the logo.'));
    } finally {
      setBusy(false);
    }
  }
  const colorGroups = [
    [
      'Brand colors',
      [
        ['primary', 'Primary color'],
        ['accent', 'Accent color'],
      ],
    ],
    [
      'Light theme',
      [
        ['lightBackground', 'Light background'],
        ['lightSurface', 'Light panels'],
        ['lightText', 'Light text'],
        ['lightBorder', 'Light borders'],
      ],
    ],
    [
      'Dark theme',
      [
        ['darkBackground', 'Dark background'],
        ['darkSurface', 'Dark panels'],
        ['darkText', 'Dark text'],
        ['darkBorder', 'Dark borders'],
      ],
    ],
    [
      'Sidebar',
      [
        ['sidebarBackground', 'Sidebar background'],
        ['sidebarText', 'Sidebar text'],
      ],
    ],
  ] as const;
  const light = themePalette(draft, 'light');
  const dark = themePalette(draft, 'dark');
  const effective = {
    primary: light.primary,
    accent: light.accent,
    lightBackground: light.bg,
    darkBackground: dark.bg,
    lightSurface: light.surface,
    darkSurface: dark.surface,
    lightText: light.text,
    darkText: dark.text,
    lightBorder: light.border,
    darkBorder: dark.border,
    sidebarBackground: light.sidebar,
    sidebarText: light.sidebarText,
  };
  const previewStyle = {
    ...themeVariables(draft),
  } as CSSProperties;
  return (
    <form
      className="jira-settings platform-settings"
      onSubmit={(event) => {
        event.preventDefault();
        setBusy(true);
        setError('');
        setMessage('');
        void save(draft)
          .then((value) => {
            setDraft(value);
            setMessage(t('Platform appearance saved.'));
          })
          .catch((reason: unknown) =>
            setError(
              reason instanceof Error ? reason.message : t('Could not save platform appearance.'),
            ),
          )
          .finally(() => setBusy(false));
      }}
    >
      <header className="jira-intro">
        <span className="platform-heading-icon">
          <Palette size={24} />
        </span>
        <div>
          <span className="jira-eyebrow">{t('Platform administration')}</span>
          <h2>{t('Make it your own.')}</h2>
        </div>
      </header>
      <p className="jira-intro-copy">
        {t(
          'Your identity, across every workspace and the sign-in page. Changes apply to everyone after saving.',
        )}
      </p>
      {error && (
        <p role="alert" className="jira-notice is-error">
          {error}
        </p>
      )}
      {message && (
        <p role="status" className="jira-notice">
          {message}
        </p>
      )}
      {draft.version !== branding.version && (
        <p role="alert" className="jira-notice">
          {t('Platform appearance changed elsewhere. Reload the saved settings before editing.')}{' '}
          <button
            type="button"
            className="secondary"
            onClick={() => {
              setDraft(branding);
              setError('');
            }}
          >
            {t('Reload saved settings')}
          </button>
        </p>
      )}
      <div className="platform-settings-columns">
        <div>
          <fieldset className="jira-section" disabled={busy}>
            <legend>{t('Brand identity')}</legend>
            <label>
              {t('Platform name')}
              <input
                required
                maxLength={60}
                autoComplete="off"
                name="platformName"
                value={draft.name}
                onChange={(event) => change('name', event.target.value)}
              />
            </label>
            <label className="jira-toggle">
              <input
                type="checkbox"
                checked={draft.showName !== false}
                onChange={(event) => change('showName', event.target.checked)}
              />
              {t('Show name beside logo')}
            </label>
            <p className="jira-hint">
              {t(
                'When hidden, the logo is centered. The name is still used for the browser title and accessibility.',
              )}
            </p>
            {(
              [
                ['logo', 'Expanded sidebar logo', 'Upload logo', 'Logo preview', 'Remove logo'],
                [
                  'collapsedLogo',
                  'Collapsed sidebar logo',
                  'Upload collapsed logo',
                  'Collapsed logo preview',
                  'Remove collapsed logo',
                ],
              ] as const
            ).map(([key, heading, uploadLabel, previewLabel, removeLabel]) => (
              <div className="platform-logo-editor" key={key}>
                <div className="platform-logo-tile">
                  {draft[key] ? (
                    <img src={draft[key]} alt={t(previewLabel)} width={54} height={54} />
                  ) : (
                    <ImagePlus size={30} />
                  )}
                </div>
                <div>
                  <h3 className="platform-logo-heading">{t(heading)}</h3>
                  <label className="platform-upload">
                    {t(uploadLabel)}
                    <input
                      type="file"
                      accept="image/png,image/jpeg,image/gif,image/webp,image/svg+xml,.svg"
                      onChange={(event) => {
                        void upload(key, event.target.files?.[0]);
                        event.target.value = '';
                      }}
                    />
                  </label>
                  <p className="jira-hint">
                    {t(
                      'PNG, JPG, GIF, WebP or SVG. Maximum 2 MB. SVG is converted to a high-resolution PNG for safe display.',
                    )}
                  </p>
                  {key === 'collapsedLogo' && (
                    <p className="jira-hint">
                      {t('Optional. Uses the main logo when no collapsed logo is uploaded.')}
                    </p>
                  )}
                  {draft[key] && (
                    <button type="button" className="text-button" onClick={() => change(key, null)}>
                      {t(removeLabel)}
                    </button>
                  )}
                </div>
              </div>
            ))}
          </fieldset>
          <fieldset className="jira-section" disabled={busy}>
            <legend>{t('Colors and theme')}</legend>
            {colorGroups.map(([group, fields]) => (
              <section className="platform-color-group" key={group}>
                <h3>{t(group)}</h3>
                <div className="platform-colors">
                  {fields.map(([key, label]) => {
                    const optional = ![
                      'primary',
                      'accent',
                      'lightBackground',
                      'darkBackground',
                    ].includes(key);
                    return (
                      <ThemeColorField
                        key={key}
                        label={label}
                        value={draft[key]}
                        automatic={effective[key]}
                        optional={optional}
                        onChange={(value) =>
                          change(key, value ?? (optional ? null : effective[key]))
                        }
                      />
                    );
                  })}
                </div>
              </section>
            ))}
            <label>
              {t('Default theme')}
              <select
                aria-label={t('Default theme')}
                value={draft.defaultTheme}
                onChange={(event) => {
                  const value = event.target.value;
                  if (value === 'light' || value === 'dark' || value === 'system')
                    change('defaultTheme', value);
                }}
              >
                <option value="light">{t('Light')}</option>
                <option value="dark">{t('Dark')}</option>
                <option value="system">{t('System')}</option>
              </select>
            </label>
            <p className="jira-hint">
              {t(
                'The default applies until a user chooses their own theme. Text contrast is adjusted automatically.',
              )}
            </p>
          </fieldset>
          <fieldset className="jira-section" disabled={busy}>
            <legend>{t('Typography and shape')}</legend>
            <label>
              {t('Font family')}
              <select
                aria-label={t('Font family')}
                value={draft.fontFamily ?? 'default'}
                onChange={(event) => change('fontFamily', event.target.value)}
              >
                <option value="default">{t('Platform default')}</option>
                <option value="system">{t('System font')}</option>
                <option value="dm-sans">DM Sans</option>
                <option value="manrope">Manrope</option>
              </select>
            </label>
            <label className="platform-range">
              {t('Text size')} <output>{draft.fontScale ?? 100}%</output>
              <input
                type="range"
                aria-label={t('Text size')}
                min={90}
                max={120}
                step={5}
                value={draft.fontScale ?? 100}
                onChange={(event) => change('fontScale', Number(event.target.value))}
              />
            </label>
            <label className="platform-range">
              {t('Corner rounding')} <output>{draft.cornerRadius ?? 8}</output>
              <input
                type="range"
                aria-label={t('Corner rounding')}
                min={0}
                max={16}
                value={draft.cornerRadius ?? 8}
                onChange={(event) => change('cornerRadius', Number(event.target.value))}
              />
            </label>
          </fieldset>
        </div>
        <aside className="platform-preview-column">
          <div className="platform-preview-heading">
            <h3>{t('Live preview')}</h3>
            <div className="platform-preview-tabs" role="group" aria-label={t('Preview theme')}>
              {(['light', 'dark'] as const).map((theme) => (
                <button
                  key={theme}
                  type="button"
                  aria-pressed={previewTheme === theme}
                  onClick={() => setPreviewTheme(theme)}
                >
                  {t(theme === 'light' ? 'Light' : 'Dark')}
                </button>
              ))}
            </div>
          </div>
          <div
            className="platform-preview"
            data-platform-theme="true"
            data-theme={previewTheme}
            style={previewStyle}
          >
            <header className={draft.showName === false ? 'platform-preview-logo-only' : undefined}>
              {draft.logo ? (
                <img
                  src={draft.logo}
                  alt={draft.showName === false ? draft.name : ''}
                  width={30}
                  height={30}
                />
              ) : (
                <span className="platform-preview-mark">
                  <i />
                  <i />
                  <i />
                </span>
              )}
              {draft.showName !== false && <strong>{draft.name || t('Platform name')}</strong>}
            </header>
            <section>
              <span className="platform-preview-caption">{t('Sign-in page')}</span>
              <h3>{t('Welcome back.')}</h3>
              <div className="platform-preview-input">{t('Email address')}</div>
              <div className="platform-preview-input">••••••••••</div>
              <span className="platform-preview-button">{t('Continue with email')}</span>
            </section>
            <section>
              <span className="platform-preview-caption">{t('Workspace')}</span>
              <div className="platform-preview-nav">
                <span>{t('Board')}</span>
                <span>{t('List')}</span>
                <span>{t('Calendar')}</span>
              </div>
              <article>
                <small>{t('Your next project')}</small>
                <h4>{t('Make room for great work')}</h4>
                <span className="platform-preview-label">{t('In progress')}</span>
              </article>
            </section>
          </div>
          <p className="jira-hint">{t('Preview only. Nothing changes until you save.')}</p>
          <button
            className="secondary"
            type="button"
            disabled={busy}
            onClick={() => setResetting(true)}
          >
            <RotateCcw size={14} />
            {t('Reset to defaults')}
          </button>
          {resetting && (
            <div className="jira-notice">
              <p>
                {t(
                  'Restore the original Kanbada name, logo, colors and default theme? Save to apply the reset.',
                )}
              </p>
              <div className="platform-reset-actions">
                <button type="button" className="secondary" onClick={() => setResetting(false)}>
                  {t('Cancel')}
                </button>
                <button
                  type="button"
                  className="secondary"
                  onClick={() => {
                    setDraft({ ...defaultBranding, version: draft.version });
                    setResetting(false);
                    setMessage('');
                  }}
                >
                  {t('Restore defaults')}
                </button>
              </div>
            </div>
          )}
        </aside>
      </div>
      <footer className="jira-save-bar">
        <span className="jira-hint">{dirty ? t('Unsaved changes') : t('All changes saved')}</span>
        <button type="submit" className="primary" disabled={busy || !dirty}>
          <Save size={15} />
          {t('Save platform appearance')}
        </button>
      </footer>
    </form>
  );
}
