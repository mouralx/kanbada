import { useEffect, useState } from 'react';
import { ShieldCheck, Trash2 } from 'lucide-react';
import { jiraRepository } from '../../infrastructure/jira';
import { githubRepository } from '../../infrastructure/github';
import { useI18n } from '../../shared/i18n';

export function ConnectorServerAccess({
  baseUrl,
  provider,
}: {
  baseUrl: string;
  provider: 'Jira' | 'GitHub';
}) {
  const { t } = useI18n();
  const repository = provider === 'Jira' ? jiraRepository : githubRepository;
  const [access, setAccess] = useState<Awaited<ReturnType<typeof jiraRepository.hosts>> | null>(
    null,
  );
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [busy, setBusy] = useState(false);
  const [confirmation, setConfirmation] = useState<{ url: string; revoke: boolean } | null>(null);
  useEffect(() => {
    let active = true;
    repository
      .hosts()
      .then((result) => {
        if (active) setAccess(result);
      })
      .catch((reason: unknown) => {
        if (active)
          setError(
            reason instanceof Error ? reason.message : t('Could not load server approvals.'),
          );
      });
    return () => {
      active = false;
    };
  }, [t, repository]);
  function confirm(url: string, revoke: boolean) {
    setError('');
    setMessage('');
    try {
      const parsed = new URL(url);
      if (
        parsed.protocol !== 'https:' ||
        parsed.username ||
        parsed.password ||
        parsed.search ||
        parsed.hash
      )
        throw new Error(t('Enter a valid HTTPS {0} base URL first.', provider));
      setConfirmation({ url: parsed.origin, revoke });
    } catch {
      setError(t('Enter a valid HTTPS {0} base URL first.', provider));
    }
  }
  async function apply() {
    if (!confirmation) return;
    setBusy(true);
    setError('');
    setMessage('');
    try {
      if (confirmation.revoke) await repository.revokeHost(confirmation.url);
      else await repository.approveHost(confirmation.url);
      setAccess(await repository.hosts());
      setMessage(
        t(
          confirmation.revoke
            ? 'Server approval removed. New requests to this server are blocked.'
            : 'Server approved. You can test the connection now; no restart is needed.',
        ),
      );
      setConfirmation(null);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : t('Could not update server approval.'));
    } finally {
      setBusy(false);
    }
  }
  return (
    <details className="jira-server-access jira-full">
      <summary>
        <ShieldCheck size={14} />
        {t('Server access')}
      </summary>
      <p className="jira-hint">
        {t(
          provider === 'Jira'
            ? 'HTTPS is required. Standard Jira Cloud sites are ready to connect. Other servers need a one-time approval from a platform administrator here, without editing files or restarting services.'
            : 'GitHub.com is ready to connect. GitHub Enterprise Server requires HTTPS and one-time platform administrator approval here.',
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
      {access?.canManage ? (
        <>
          <button
            type="button"
            className="secondary"
            disabled={busy || !baseUrl.trim()}
            onClick={() => confirm(baseUrl, false)}
          >
            <ShieldCheck size={14} />
            {t('Approve this {0} server', provider)}
          </button>
          <p className="jira-hint">
            {t(
              'Approval applies to all projects. Approve only servers you trust with credentials and server-side network access.',
            )}
          </p>
          {access.hosts.map((host) => (
            <div className="jira-approved-host" key={host.authority}>
              <code>{host.authority}</code>
              <button
                type="button"
                className="icon-button danger"
                disabled={busy}
                aria-label={t('Revoke approval for {0}', host.authority)}
                onClick={() => confirm(`https://${host.authority}`, true)}
              >
                <Trash2 size={14} />
              </button>
            </div>
          ))}
          {access.configuredHosts.length > 0 && (
            <p className="jira-hint">
              {t('Additional servers managed by deployment policy')}:{' '}
              {access.configuredHosts.join(', ')}
            </p>
          )}
          {confirmation && (
            <div className="jira-notice">
              <p>
                <strong>{confirmation.url}</strong>
              </p>
              <p>
                {t(
                  confirmation.revoke
                    ? 'Revoke access for this server? Existing synchronizations will fail until it is approved again. In-flight requests may finish.'
                    : 'Allow the platform to contact this server and send configured integration credentials?',
                )}
              </p>
              <div className="jira-server-actions">
                <button
                  type="button"
                  className="secondary"
                  disabled={busy}
                  onClick={() => setConfirmation(null)}
                >
                  {t('Cancel')}
                </button>
                <button
                  type="button"
                  className="secondary"
                  disabled={busy}
                  onClick={() => void apply()}
                >
                  {t(confirmation.revoke ? 'Revoke server approval' : 'Confirm server approval')}
                </button>
              </div>
            </div>
          )}
        </>
      ) : (
        access && (
          <p className="jira-hint">
            {t(
              'Ask a platform administrator to open this panel and approve your {0} server. You can then finish the connection yourself.',
              provider,
            )}
          </p>
        )
      )}
    </details>
  );
}
