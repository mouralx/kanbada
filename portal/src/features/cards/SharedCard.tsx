import { ArrowLeft, Download, ShieldCheck } from 'lucide-react';
import { useEffect, useState, type ReactNode } from 'react';
import { apiEnabled } from '../../infrastructure/apiClient';
import { fileRepository } from '../../infrastructure/attachments';
import { authRepository } from '../../infrastructure/auth';
import { shareRepository } from '../../infrastructure/sharing';
import { useI18n } from '../../shared/i18n';
import { ThemeSelect } from '../../shared/Theme';
import { LogoutButton } from '../auth/AuthBoundary';
import { InvitationView } from '../workspaces/InvitationView';
import { CardLabels } from './CardLabels';
export function SharedCardRouter({ children }: { children: ReactNode }) {
  const params = new URLSearchParams(window.location.search);
  const invitation = params.get('invite');
  if (invitation && apiEnabled) return <InvitationView token={invitation} />;
  const token = params.get('share');
  return token ? <SharedCard token={token} /> : children;
}
function SharedCard({ token }: { token: string }) {
  const { t, locale } = useI18n();
  const [result, setResult] = useState<Awaited<ReturnType<typeof shareRepository.resolve>> | null>(
    null,
  );
  const [error, setError] = useState('');
  const [tab, setTab] = useState('Details');
  const [downloadError, setDownloadError] = useState('');
  useEffect(() => {
    let active = true;
    const refresh = async () => {
      try {
        const account = await authRepository.session();
        if (!account) throw new Error('Please sign in to view this card.');
        const next = await shareRepository.resolve(token, account);
        if (active) {
          setResult(next);
          setError('');
        }
      } catch (e) {
        if (active) {
          setResult(null);
          setError(
            e instanceof Error ? e.message : 'This link has expired or is no longer available.',
          );
        }
      }
    };
    void refresh();
    const timer = setInterval(refresh, 30000);
    window.addEventListener('storage', refresh);
    window.addEventListener('focus', refresh);
    return () => {
      active = false;
      clearInterval(timer);
      window.removeEventListener('storage', refresh);
      window.removeEventListener('focus', refresh);
    };
  }, [token]);
  return (
    <div className="shared-card-page">
      <header>
        <a href="/">
          <ArrowLeft size={15} />
          kanbada
        </a>
        <ThemeSelect compact />
        <LogoutButton />
      </header>
      {error ? (
        <section className="shared-card-document">
          <h1>{t('Card unavailable')}</h1>
          <p role="alert">{t(error)}</p>
          <a href="/">{t('Back to workspace')}</a>
        </section>
      ) : !result ? (
        <div className="loading">{t('Loading…')}</div>
      ) : (
        <article className="shared-card-document">
          <span className="shared-card-access">
            <ShieldCheck size={16} />
            {t('Shared card · View only')}
          </span>
          <p>
            {result.data.workspace.name} / {result.task.id.toUpperCase()}
          </p>
          <h1>{result.task.title}</h1>
          <CardLabels names={result.task.labels} definitions={result.data.labels} />
          <div className="card-tabs" role="tablist">
            {['Details', 'History'].map((value) => (
              <button
                role="tab"
                aria-selected={tab === value}
                key={value}
                onClick={() => setTab(value)}
              >
                {t(value)}
              </button>
            ))}
          </div>
          {tab === 'Details' ? (
            <>
              <dl className="shared-card-fields">
                {[
                  ['Status', t(result.task.status)],
                  ['Priority', t(result.task.priority)],
                  ['Assignees', result.task.assignees.join(', ') || t('Unassigned')],
                  ['Due date', result.task.due || t('No due date')],
                  ['Bucket', result.task.bucket || t('No bucket')],
                  ['Swimlane', result.task.swimlane || t('No swimlane')],
                ].map(([label, value]) => (
                  <div key={label}>
                    <dt>{t(label)}</dt>
                    <dd>{value}</dd>
                  </div>
                ))}
              </dl>
              <h3>{t('Description')}</h3>
              <p className="shared-description">{result.task.description}</p>
              <h3>{t('Checklist')}</h3>
              {result.task.checklist.map((item, i) => (
                <p key={i}>
                  {item.done ? '✓' : '○'} {item.text}
                </p>
              ))}
              <h3>{t('Attachments')}</h3>
              {result.task.attachments?.map((file) => (
                <button
                  className="shared-file"
                  key={file.id}
                  onClick={async () => {
                    try {
                      setDownloadError('');
                      const account = await authRepository.session();
                      if (!account) throw new Error();
                      await shareRepository.resolve(token, account);
                      await fileRepository.download(file, result.share.sourcePrefix);
                    } catch {
                      setDownloadError('This file is unavailable in this browser.');
                    }
                  }}
                >
                  <Download size={15} />
                  {file.name}
                </button>
              ))}
              {downloadError && <p role="alert">{t(downloadError)}</p>}
              <h3>{t('Conversation')}</h3>
              {result.task.comments.map((comment, i) => (
                <p key={i}>{comment}</p>
              ))}
            </>
          ) : (
            <div className="card-history">
              {[...(result.task.history ?? [])].reverse().map((entry) => (
                <section className="shared-history" key={entry.id}>
                  <b>{entry.actor}</b>
                  <time>{new Date(entry.at).toLocaleString(locale)}</time>
                  <ul>
                    {entry.changes.map((change, i) => (
                      <li key={i}>{t(change)}</li>
                    ))}
                  </ul>
                </section>
              ))}
            </div>
          )}
        </article>
      )}
    </div>
  );
}
