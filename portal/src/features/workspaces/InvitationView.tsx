import { useEffect, useState } from 'react';
import { apiRequest } from '../../infrastructure/apiClient';
import { remoteRepository } from '../../infrastructure/remoteRepository';
import { useI18n } from '../../shared/i18n';
import { LogoutButton } from '../auth/AuthBoundary';
export function InvitationView({ token }: { token: string }) {
  const { t } = useI18n();
  const [invite, setInvite] = useState<{ workspace: string; email: string } | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    apiRequest<{ workspace: string; email: string }>('/invitations/' + encodeURIComponent(token))
      .then(setInvite)
      .catch((e) => setError(e.message));
  }, [token]);
  return (
    <div className="shared-card-page">
      <header>
        <a href="/">kanbada</a>
        <LogoutButton />
      </header>
      <section className="shared-card-document">
        <h1>{t('Workspace invitation')}</h1>
        {invite && (
          <>
            <p>{t('You have been invited to {0}', invite.workspace)}</p>
            <p>{invite.email}</p>
            <button
              className="primary"
              disabled={busy}
              onClick={async () => {
                setBusy(true);
                setError('');
                try {
                  const result = await apiRequest<{ workspaceId: string }>(
                    '/invitations/' + token + '/accept',
                    { method: 'POST' },
                  );
                  await remoteRepository.switchWorkspace(result.workspaceId);
                  window.location.assign('/');
                } catch (e) {
                  setError(e instanceof Error ? e.message : 'Could not accept invitation.');
                } finally {
                  setBusy(false);
                }
              }}
            >
              {t('Accept invitation')}
            </button>
          </>
        )}
        {error && <p role="alert">{t(error)}</p>}
      </section>
    </div>
  );
}
