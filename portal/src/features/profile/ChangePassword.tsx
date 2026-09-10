import { useState } from 'react';
import { apiRequest } from '../../infrastructure/apiClient';
import { useI18n } from '../../shared/i18n';

export function ChangePassword({ onChanged }: { onChanged: () => void }) {
  const { t } = useI18n();
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [saved, setSaved] = useState(false);
  return (
    <div className="change-password">
      {saved && (
        <p role="status">{t('Password changed. Your other sessions have been signed out.')}</p>
      )}
      {open ? (
        <form
          className="two-factor-form"
          aria-label={t('Change password')}
          onSubmit={async (event) => {
            event.preventDefault();
            if (busy) return;
            const element = event.currentTarget;
            const data = new FormData(element);
            setError('');
            setSaved(false);
            if (data.get('newPassword') !== data.get('confirmation')) {
              setError('The new passwords do not match.');
              return;
            }
            setBusy(true);
            try {
              await apiRequest('/auth/change-password', {
                method: 'POST',
                body: JSON.stringify({
                  password: data.get('password'),
                  newPassword: data.get('newPassword'),
                  code: data.get('code'),
                }),
              });
              element.reset();
              setOpen(false);
              setSaved(true);
              onChanged();
            } catch (e) {
              setError(e instanceof Error ? e.message : 'Could not change your password.');
            } finally {
              setBusy(false);
            }
          }}
        >
          <h4>{t('Change password')}</h4>
          <p>
            {t(
              'Your current password and an authenticator or recovery code are required. Other sessions will be signed out.',
            )}
          </p>
          <label>
            {t('Current password')}
            <input
              name="password"
              type="password"
              autoComplete="current-password"
              required
              maxLength={200}
              disabled={busy}
            />
          </label>
          <label>
            {t('New password')}
            <input
              name="newPassword"
              type="password"
              autoComplete="new-password"
              required
              minLength={12}
              maxLength={200}
              disabled={busy}
            />
          </label>
          <label>
            {t('Confirm new password')}
            <input
              name="confirmation"
              type="password"
              autoComplete="new-password"
              required
              minLength={12}
              maxLength={200}
              disabled={busy}
            />
          </label>
          <label>
            {t('Authenticator or recovery code')}
            <input
              name="code"
              autoComplete="one-time-code"
              required
              maxLength={100}
              disabled={busy}
            />
          </label>
          {error && (
            <p role="alert" className="form-error">
              {t(error)}
            </p>
          )}
          <div className="two-factor-actions">
            <button className="primary" disabled={busy}>
              {t(busy ? 'Working…' : 'Save new password')}
            </button>
            <button
              type="button"
              className="secondary"
              disabled={busy}
              onClick={() => {
                setOpen(false);
                setError('');
              }}
            >
              {t('Cancel')}
            </button>
          </div>
        </form>
      ) : (
        <button
          type="button"
          className="secondary"
          onClick={() => {
            setOpen(true);
            setSaved(false);
          }}
        >
          {t('Change password')}
        </button>
      )}
    </div>
  );
}
