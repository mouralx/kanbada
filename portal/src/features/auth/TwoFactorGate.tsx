import { useState } from 'react';
import { apiRequest } from '../../infrastructure/apiClient';
import { useI18n } from '../../shared/i18n';
import { Brand } from '../../shared/Brand';
import { TwoFactorSettings } from '../profile/TwoFactorSettings';

export function TwoFactorGate({
  setup,
  onComplete,
  onLogout,
}: {
  setup: boolean;
  onComplete: () => void;
  onLogout: () => void;
}) {
  const { t } = useI18n();
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  return (
    <main className="enrollment-page">
      <div className="enrollment-card">
        <div className="logo">
          <Brand />
        </div>
        <h1>{t(setup ? 'Secure your account' : 'Verify your sign-in')}</h1>
        <p>
          {t(
            setup
              ? 'Set up two-factor authentication to finish registration and open your workspace.'
              : 'Enter a code from your authenticator app, or one of your saved recovery codes.',
          )}
        </p>
        {setup ? (
          <TwoFactorSettings onComplete={onComplete} />
        ) : (
          <form
            className="two-factor-form"
            onSubmit={async (event) => {
              event.preventDefault();
              if (busy) return;
              setBusy(true);
              setError('');
              try {
                await apiRequest('/auth/two-factor/verify', {
                  method: 'POST',
                  body: JSON.stringify({ password: '', code }),
                });
                setCode('');
                onComplete();
              } catch (e) {
                setError(e instanceof Error ? e.message : 'Could not sign in. Please try again.');
              } finally {
                setBusy(false);
              }
            }}
          >
            <label>
              {t('Authenticator or recovery code')}
              <input
                autoComplete="one-time-code"
                autoFocus
                required
                maxLength={100}
                value={code}
                onChange={(e) => setCode(e.target.value)}
              />
            </label>
            <button className="primary" disabled={busy}>
              {t(busy ? 'Working…' : 'Verify your sign-in')}
            </button>
          </form>
        )}
        {error && (
          <p role="alert" className="form-error">
            {t(error)}
          </p>
        )}
        <button type="button" className="secondary" onClick={onLogout}>
          {t('Sign out')}
        </button>
      </div>
    </main>
  );
}
