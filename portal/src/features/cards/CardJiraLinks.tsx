import { ArrowUpRight } from 'lucide-react';
import { useEffect, useState } from 'react';
import { apiEnabled } from '../../infrastructure/apiClient';
import { jiraRepository } from '../../infrastructure/jira';
import { useI18n } from '../../shared/i18n';

export function CardJiraLinks({ workspace, card }: { workspace: string; card: string }) {
  const { t } = useI18n();
  const [links, setLinks] = useState<{ key: string; url: string }[]>([]);
  const [error, setError] = useState(false);
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    if (!apiEnabled) return;
    const controller = new AbortController();
    jiraRepository.cardLinks(workspace, card, controller.signal).then(
      ({ links }) => {
        if (!controller.signal.aborted) setLinks(links);
      },
      () => {
        if (!controller.signal.aborted) setError(true);
      },
    );
    return () => controller.abort();
  }, [workspace, card, attempt]);
  if (!apiEnabled) return null;
  if (error)
    return (
      <div className="card-project-association" role="alert">
        <span>{t('Could not load the Jira card link.')}</span>
        <button
          type="button"
          onClick={() => {
            setError(false);
            setAttempt(attempt + 1);
          }}
        >
          {t('Retry')}
        </button>
      </div>
    );
  return links.map((link) => (
    <a
      key={link.url}
      className="card-project-association card-jira-link"
      href={link.url}
      target="_blank"
      rel="noopener noreferrer"
      aria-label={`${t('Open in Jira')}: ${link.key} (${t('opens in a new tab')})`}
    >
      <ArrowUpRight size={18} aria-hidden="true" />
      <span>
        <small>{t('Open in Jira')}</small>
        <strong>{link.key}</strong>
      </span>
    </a>
  ));
}
