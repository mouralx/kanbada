import { Download, RefreshCw } from 'lucide-react';
import { useCallback, useEffect, useRef, useState } from 'react';
import {
  downloadExportUrl,
  getExport,
  listExports,
  requestExport,
  type ExportJob,
} from '../../infrastructure/exports';
import { useI18n } from '../../shared/i18n';

export function Exports({ workspace }: { workspace: string }) {
  const { t, locale } = useI18n();
  const [jobs, setJobs] = useState<ExportJob[]>([]);
  const [cursor, setCursor] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const inFlight = useRef(false);
  const load = useCallback(
    async (after?: string, signal?: AbortSignal) => {
      if (inFlight.current) return;
      inFlight.current = true;
      setLoading(true);
      try {
        const page = await listExports(workspace, after, signal);
        if (signal?.aborted) return;
        setJobs((current) =>
          after
            ? [...current, ...page.items.filter((job) => !current.some((old) => old.id === job.id))]
            : page.items,
        );
        setCursor(page.nextCursor);
        setError('');
      } catch (error) {
        if (!signal?.aborted)
          setError(error instanceof Error ? error.message : 'Could not load exports.');
      } finally {
        inFlight.current = false;
        if (!signal?.aborted) setLoading(false);
      }
    },
    [workspace],
  );
  useEffect(() => {
    const controller = new AbortController();
    // Defer the first read so StrictMode's discarded effect cannot own the request.
    const timer = setTimeout(() => void load(undefined, controller.signal), 0);
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [load]);
  useEffect(() => {
    const controller = new AbortController();
    let refreshing = false;
    const timer = setInterval(async () => {
      if (refreshing || document.hidden) return;
      const active = jobs.filter(
        (job) =>
          job.status === 'queued' ||
          job.status === 'running' ||
          (job.status === 'completed' &&
            job.expiresAt &&
            new Date(job.expiresAt).getTime() <= Date.now()),
      );
      if (!active.length) return;
      refreshing = true;
      try {
        const updated = await Promise.all(
          active.map((job) => getExport(workspace, job.id, controller.signal)),
        );
        if (!controller.signal.aborted) {
          setJobs((current) =>
            current.map((job) => updated.find((next) => next.id === job.id) ?? job),
          );
          setError('');
        }
      } catch (error) {
        if (!controller.signal.aborted)
          setError(error instanceof Error ? error.message : 'Could not load exports.');
      } finally {
        refreshing = false;
      }
    }, 3000);
    return () => {
      clearInterval(timer);
      controller.abort();
    };
  }, [jobs, workspace]);
  const queueWorkspace = async () => {
    if (loading) return;
    setLoading(true);
    try {
      const job = await requestExport(workspace, 'workspace-json', {}, locale);
      setJobs((current) => [job, ...current]);
      setError('');
    } catch (error) {
      setError(error instanceof Error ? error.message : 'Could not queue export.');
    } finally {
      setLoading(false);
    }
  };
  const status = {
    queued: 'Queued',
    running: 'Generating',
    completed: 'Ready to download',
    failed: 'Failed',
    expired: 'Expired',
  };
  const kind = {
    'project-json': 'Project JSON',
    'dashboard-pdf': 'Dashboard PDF',
    'workspace-json': 'Workspace JSON',
  };
  return (
    <section className="exports-area" aria-label={t('Exports')}>
      <div className="exports-toolbar">
        <p>{t('Your private exports. Files are available for 7 days after completion.')}</p>
        <button className="secondary" disabled={loading} onClick={() => void load()}>
          <RefreshCw size={15} />
          {t('Refresh')}
        </button>
        <button className="primary" disabled={loading} onClick={() => void queueWorkspace()}>
          {t('Export workspace')}
        </button>
      </div>
      {error && (
        <p role="alert">
          {t(error)} <button onClick={() => void load()}>{t('Retry')}</button>
        </p>
      )}
      {loading && <p role="status">{t('Loading exports…')}</p>}
      {!loading && !jobs.length && !error && (
        <p>{t('No exports yet. Request a project export or dashboard PDF to get started.')}</p>
      )}
      <div className="exports-list" aria-live="polite">
        {jobs.map((job) => (
          <article className="export-job" key={job.id}>
            <div>
              <h2>{job.name}</h2>
              <span>
                {t(kind[job.kind])} · {new Date(job.createdAt).toLocaleString(locale)}
              </span>
              <p>
                <strong>{t(status[job.status])}</strong>
                {job.status === 'running' &&
                  job.total !== null &&
                  ` · ${t('{0} of {1} cards processed', job.processed, job.total)}`}
              </p>
              {job.status === 'queued' && (
                <small>{t('Waiting for the export worker. You can leave this page.')}</small>
              )}
              {job.status === 'running' && (
                <progress
                  aria-label={t('Export progress')}
                  value={job.total ? Math.min(job.processed, job.total) : undefined}
                  max={job.total || 1}
                />
              )}
              {job.status === 'failed' && (
                <p role="alert">
                  {t(
                    job.error ||
                      'Export generation failed. Request a new export or contact an administrator.',
                  )}
                </p>
              )}
              {(job.status === 'failed' || job.status === 'expired') && (
                <small>
                  {t(
                    'Return to the project or dashboard to request a new export with its current filters.',
                  )}
                </small>
              )}
              {job.status === 'completed' && job.expiresAt && (
                <small>
                  {t('Available until {0}', new Date(job.expiresAt).toLocaleString(locale))} ·{' '}
                  {(job.bytes / 1024).toLocaleString(locale, { maximumFractionDigits: 1 })} KB
                </small>
              )}
            </div>
            {job.status === 'completed' && (
              <a className="secondary" href={downloadExportUrl(workspace, job.id)} download>
                <Download size={16} />
                {t('Download')}
              </a>
            )}
          </article>
        ))}
      </div>
      {cursor && (
        <button className="secondary" disabled={loading} onClick={() => void load(cursor)}>
          {t('Load more')}
        </button>
      )}
    </section>
  );
}
