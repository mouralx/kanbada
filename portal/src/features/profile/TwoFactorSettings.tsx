import { QRCodeSVG } from 'qrcode.react';
import { useEffect, useState } from 'react';
import { apiRequest } from '../../infrastructure/apiClient';
import { useI18n } from '../../shared/i18n';

type Status = {
  available: boolean;
  enabled: boolean;
  recoveryCodesRemaining: number;
  required: boolean;
  passwordRequired: boolean;
};
type Setup = { secret: string; uri: string };
export function TwoFactorSettings({ onComplete }: { onComplete?: () => void } = {}) {
  const { t } = useI18n();
  const [status, setStatus] = useState<Status | null>(null);
  const [setup, setSetup] = useState<Setup | null>(null);
  const [action, setAction] = useState<'setup' | 'disable' | 'recovery-codes' | null>(null);
  const [password, setPassword] = useState('');
  const [code, setCode] = useState('');
  const [codes, setCodes] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const refresh = async () => setStatus(await apiRequest<Status>('/auth/two-factor'));
  useEffect(() => {
    void apiRequest<Status>('/auth/two-factor')
      .then(setStatus)
      .catch((e: unknown) =>
        setError(e instanceof Error ? e.message : 'Could not load security settings.'),
      );
  }, []);
  const reset = () => {
    setAction(null);
    setSetup(null);
    setPassword('');
    setCode('');
  };
  return (
    <section className="two-factor-settings" aria-labelledby="two-factor-heading">
      <h3 id="two-factor-heading">{t('Two-factor authentication')}</h3>
      {error && (
        <p className="form-error" role="alert">
          {t(error)}
        </p>
      )}
      {!status && (
        <button
          type="button"
          className="secondary"
          onClick={() => {
            setError('');
            void refresh().catch(() => setError('Could not load security settings.'));
          }}
        >
          {t('Reload security settings')}
        </button>
      )}
      {status && !status.available && (
        <p>{t('Manage two-factor authentication with your sign-in provider.')}</p>
      )}
      {status?.available && (
        <>
          <p>
            {t(
              status.enabled
                ? 'Your account is protected by an authenticator app.'
                : 'Add a verification code from Microsoft Authenticator or another authenticator app when you sign in.',
            )}
          </p>
          {status.required && <p>{t('Two-factor authentication is required for this account.')}</p>}
          {onComplete && status.enabled && codes.length === 0 && (
            <button type="button" className="primary" onClick={onComplete}>
              {t('Continue to workspace')}
            </button>
          )}
          {status.enabled && (
            <p>
              {t('Recovery codes remaining')}: {status.recoveryCodesRemaining}
            </p>
          )}
          {codes.length > 0 ? (
            <div className="recovery-codes">
              <h4>{t('Save your recovery codes')}</h4>
              <p>
                {t(
                  'Store these codes somewhere safe. Each code works once if you lose access to your authenticator. They will not be shown again.',
                )}
              </p>
              <pre>{codes.join('\n')}</pre>
              <button
                type="button"
                className="secondary"
                onClick={() => {
                  const url = URL.createObjectURL(
                    new Blob(['Kanbada recovery codes\n\n' + codes.join('\n')], {
                      type: 'text/plain',
                    }),
                  );
                  const link = document.createElement('a');
                  link.href = url;
                  link.download = 'kanbada-recovery-codes.txt';
                  link.click();
                  setTimeout(() => URL.revokeObjectURL(url), 1000);
                }}
              >
                {t('Download recovery codes')}
              </button>
              <button
                type="button"
                className="primary"
                onClick={() => {
                  setCodes([]);
                  onComplete?.();
                }}
              >
                {t('I have saved my codes')}
              </button>
            </div>
          ) : action ? (
            <form
              className="two-factor-form"
              onSubmit={async (event) => {
                event.preventDefault();
                if (busy) return;
                setBusy(true);
                setError('');
                try {
                  const result = await apiRequest<Setup | { codes: string[] } | undefined>(
                    '/auth/two-factor/' + (setup ? 'confirm' : action),
                    { method: 'POST', body: JSON.stringify({ password, code }) },
                  );
                  if (result && 'secret' in result) {
                    setSetup(result);
                    setCode('');
                  } else {
                    if (result && 'codes' in result) setCodes(result.codes);
                    // Reflect the successful mutation even if refreshing status fails.
                    setStatus({
                      ...status!,
                      available: true,
                      enabled: action !== 'disable',
                      recoveryCodesRemaining: result && 'codes' in result ? result.codes.length : 0,
                    });
                    reset();
                  }
                } catch (e) {
                  setError(
                    e instanceof Error ? e.message : 'Could not update two-factor authentication.',
                  );
                } finally {
                  setBusy(false);
                }
              }}
            >
              {action === 'disable' && (
                <p>{t('Confirm your password and a code to disable two-factor authentication.')}</p>
              )}
              {action === 'recovery-codes' && (
                <p>{t('Generating new recovery codes invalidates all previous codes.')}</p>
              )}
              {status.passwordRequired && (
                <label>
                  {t('Current password')}
                  <input
                    type="password"
                    autoComplete="current-password"
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    required
                    maxLength={200}
                    disabled={busy}
                  />
                </label>
              )}
              {setup && (
                <div className="authenticator-setup">
                  <p>
                    {t(
                      'In Microsoft Authenticator, add an Other account and scan this QR code. You can also enter the setup key manually.',
                    )}
                  </p>
                  <QRCodeSVG
                    value={setup.uri}
                    size={200}
                    marginSize={4}
                    title={t('Authenticator setup QR code')}
                  />
                  <label>
                    {t('Setup key')}
                    <input
                      value={setup.secret}
                      readOnly
                      onFocus={(e) => e.currentTarget.select()}
                    />
                  </label>
                  <p>
                    {t(
                      'Setup expires in ten minutes. Enter a six-digit code to enable protection.',
                    )}
                  </p>
                </div>
              )}
              {(setup || action !== 'setup') && (
                <label>
                  {t(setup ? 'Authenticator code' : 'Authenticator or recovery code')}
                  <input
                    autoComplete="one-time-code"
                    inputMode={setup ? 'numeric' : 'text'}
                    value={code}
                    onChange={(e) => setCode(e.target.value)}
                    required
                    maxLength={100}
                    disabled={busy}
                  />
                </label>
              )}
              <div className="two-factor-actions">
                <button className="primary" disabled={busy}>
                  {t(
                    busy
                      ? 'Working…'
                      : setup
                        ? 'Verify and enable'
                        : action === 'setup'
                          ? 'Set up authenticator'
                          : action === 'disable'
                            ? 'Disable two-factor authentication'
                            : 'Generate recovery codes',
                  )}
                </button>
                <button
                  type="button"
                  className="secondary"
                  disabled={busy}
                  onClick={() => {
                    reset();
                    setError('');
                  }}
                >
                  {t('Cancel')}
                </button>
              </div>
            </form>
          ) : (
            <div className="two-factor-actions">
              {status.enabled ? (
                <>
                  <button
                    type="button"
                    className="secondary"
                    onClick={() => {
                      setAction('recovery-codes');
                      setError('');
                    }}
                  >
                    {t('Generate recovery codes')}
                  </button>
                  {!status.required && (
                    <button
                      type="button"
                      className="secondary"
                      onClick={() => {
                        setAction('disable');
                        setError('');
                      }}
                    >
                      {t('Disable two-factor authentication')}
                    </button>
                  )}
                </>
              ) : (
                <button
                  type="button"
                  className="secondary"
                  onClick={() => {
                    setAction('setup');
                    setError('');
                  }}
                >
                  {t('Enable two-factor authentication')}
                </button>
              )}
            </div>
          )}
        </>
      )}
    </section>
  );
}
