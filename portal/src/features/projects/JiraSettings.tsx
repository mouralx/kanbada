import { useEffect, useState } from 'react';
import {
  ArrowRight,
  Clock3,
  Folder,
  Link2,
  RefreshCw,
  Save,
  SlidersHorizontal,
} from 'lucide-react';
import './jira-settings.css';
import { ConnectorServerAccess } from './ConnectorServerAccess';
import { JiraAssigneeMappings, JiraStatusMappings } from './JiraMappings';
import type { Definition, Member, Project } from '../../domain/models';
import {
  jiraRepository,
  type JiraConnection,
  type JiraInput,
  type JiraMetadata,
} from '../../infrastructure/jira';
import { useI18n } from '../../shared/i18n';

export function JiraSettings({
  workspaceId,
  project,
  statuses,
  members,
}: {
  workspaceId: string;
  project: Project;
  statuses: Definition[];
  members: Member[];
}) {
  const { t } = useI18n();
  const [input, setInput] = useState<JiraInput>({
    version: 0,
    baseUrl: '',
    edition: 'data-center',
    email: '',
    token: '',
    jql: '',
    jiraProjectKey: '',
    issueTypeId: '',
    direction: 'jira-to-kanbada',
    cron: '*/15 * * * *',
    timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC',
    enabled: false,
    syncAssignees: false,
    mappings: [],
  });
  const [saved, setSaved] = useState<JiraConnection | null>(null);
  const [metadata, setMetadata] = useState<JiraMetadata | null>(null);
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [interval, setIntervalValue] = useState(15);
  const [unit, setUnit] = useState('minutes');
  const [resolutions, setResolutions] = useState<Record<string, string>>({});
  useEffect(() => {
    let cancelled = false;
    const controller = new AbortController();
    jiraRepository
      .read(workspaceId, project.id)
      .then(async ({ connection }) => {
        if (cancelled) return;
        setSaved(connection);
        if (connection) {
          const draft = { ...connection, token: '' };
          setInput(draft);
          const choices = await jiraRepository.test(
            workspaceId,
            project.id,
            draft,
            controller.signal,
          );
          if (!cancelled) setMetadata(choices);
        }
      })
      .catch((reason: unknown) => {
        if (!cancelled)
          setError(reason instanceof Error ? reason.message : t('Could not load Jira settings.'));
      })
      .finally(() => {
        if (!cancelled) setBusy(false);
      });
    return () => {
      cancelled = true;
      controller.abort();
    };
  }, [workspaceId, project.id, t]);

  function change<K extends keyof JiraInput>(key: K, value: JiraInput[K]) {
    if (['baseUrl', 'jiraProjectKey', 'jql', 'email', 'token'].includes(key)) setMetadata(null);
    setInput((current) => ({ ...current, [key]: value }));
  }
  async function perform(action: () => Promise<void>) {
    setBusy(true);
    setError('');
    setMessage('');
    try {
      await action();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : t('Jira request failed.'));
    } finally {
      setBusy(false);
    }
  }
  function mapping(kind: 'status' | 'priority', value: string, name: string) {
    const options = kind === 'status' ? metadata?.statuses : metadata?.priorities;
    const selected =
      input.mappings.find((m) => m.kind === kind && m.kanbadaValue === value)?.jiraValue ?? '';
    const update = (jiraValue: string) =>
      change('mappings', [
        ...input.mappings.filter((m) => !(m.kind === kind && m.kanbadaValue === value)),
        ...(jiraValue ? [{ kind, kanbadaValue: value, jiraValue }] : []),
      ]);
    return (
      <label className="jira-mapping-row" key={`${kind}-${value}`}>
        <span>{name}</span>
        <ArrowRight size={14} aria-hidden="true" />
        <select
          aria-label={name}
          value={selected}
          disabled={!options}
          onChange={(event) => update(event.target.value)}
        >
          <option value="">{t(options ? 'Not mapped' : 'Load Jira choices first')}</option>
          {selected && !options?.some((option) => option.id === selected) && (
            <option value={selected}>
              {t(options ? 'Saved Jira choice is unavailable' : 'Loading saved Jira choice')}
            </option>
          )}
          {options?.map((option) => (
            <option key={option.id} value={option.id}>
              {option.name}
            </option>
          ))}
        </select>
      </label>
    );
  }

  return (
    <form
      className="jira-settings"
      onSubmit={(event) => {
        event.preventDefault();
        void perform(async () => {
          const connection = await jiraRepository.save(workspaceId, project.id, input);
          setSaved(connection);
          setInput({ ...connection, token: '' });
          setMessage(t('Jira settings saved.'));
        });
      }}
    >
      <header className="jira-intro">
        <span className="jira-project-icon" style={{ background: project.color }}>
          <Folder size={24} />
        </span>
        <div>
          <span className="jira-eyebrow">{t('Jira synchronization')}</span>
          <h2>{project.name}</h2>
        </div>
        <span className={`jira-badge ${saved?.enabled ? 'is-enabled' : ''}`}>
          {saved?.enabled ? t('Enabled') : t('Disabled')}
        </span>
      </header>
      <p className="jira-intro-copy">
        {t(
          'Synchronize titles, descriptions, statuses, priorities, labels and due dates. Deletions are never synchronized.',
        )}
      </p>
      <details className="jira-help">
        <summary>{t('Synchronization behavior')}</summary>
        <p>
          {t(
            'In a bidirectional conflict, the system where the item was first created wins. Comments and attachments are not synchronized. Assignee mapping is optional and only reads from Jira.',
          )}
        </p>
      </details>
      {error && (
        <p className="jira-notice is-error" role="alert">
          {error}
        </p>
      )}
      {message && (
        <p className="jira-notice" role="status">
          {message}
        </p>
      )}
      {metadata?.warnings?.map((warning) => (
        <p className="jira-notice" role="status" key={warning}>
          {t(warning)}
        </p>
      ))}
      {!metadata && (
        <p className="jira-hint">
          {t(
            'Use Test connection and load mappings to load Jira names. Saved connections load choices automatically.',
          )}
        </p>
      )}
      <fieldset className="jira-section jira-connection-grid" disabled={busy}>
        <legend>
          <Link2 size={16} />
          {t('Jira connection')}
        </legend>
        <label>
          {t('Jira edition')}
          <select
            aria-label={t('Jira edition')}
            value={input.edition}
            onChange={(event) => {
              change('edition', event.target.value === 'cloud' ? 'cloud' : 'data-center');
              change('token', '');
              setMetadata(null);
            }}
          >
            <option value="data-center">Jira Data Center / Server (PAT)</option>
            <option value="cloud">Jira Cloud (API token)</option>
          </select>
        </label>
        <label>
          {t('Jira base URL')}
          <input
            type="url"
            required
            placeholder="https://example.atlassian.net"
            value={input.baseUrl}
            onChange={(event) => change('baseUrl', event.target.value)}
          />
        </label>
        <ConnectorServerAccess baseUrl={input.baseUrl} provider="Jira" />
        {input.edition === 'cloud' && (
          <label>
            {t('Jira account email')}
            <input
              type="email"
              required
              value={input.email}
              onChange={(event) => change('email', event.target.value)}
            />
          </label>
        )}
        <label>
          {input.edition === 'cloud' ? t('API token') : t('Personal access token')}
          <input
            type="password"
            autoComplete="new-password"
            value={input.token}
            onChange={(event) => change('token', event.target.value)}
            placeholder={saved?.hasToken ? t('Leave blank to keep the saved token') : ''}
          />
        </label>
        <label>
          {t('Jira project key for new issues')}
          <input
            required
            value={input.jiraProjectKey}
            onChange={(event) => change('jiraProjectKey', event.target.value)}
            placeholder="TEAM"
          />
        </label>
        <label className="jira-full">
          JQL
          <textarea
            required
            value={input.jql}
            onChange={(event) => change('jql', event.target.value)}
            placeholder="project = TEAM ORDER BY key"
          />
        </label>
        <p className="jira-full jira-hint">
          {t(
            'Only matching Jira issues are synchronized. New Kanbada cards are exported to the selected Jira project; make sure they will match the JQL.',
          )}
        </p>
        <button
          type="button"
          className="secondary"
          onClick={() =>
            void perform(async () => {
              const result = await jiraRepository.test(workspaceId, project.id, input);
              setMetadata(result);
              setMessage(t('Connected. JQL matches {0} issues.', result.matchedIssues));
            })
          }
        >
          <RefreshCw size={14} />
          {t('Test connection and load mappings')}
        </button>
        <label>
          {t('Jira issue type for new issues')}
          <select
            aria-label={t('Jira issue type for new issues')}
            required
            value={input.issueTypeId}
            disabled={!metadata}
            onChange={(event) => change('issueTypeId', event.target.value)}
          >
            <option value="">{t('Select issue type')}</option>
            {input.issueTypeId &&
              !metadata?.issueTypes.some((type) => type.id === input.issueTypeId) && (
                <option value={input.issueTypeId}>
                  {t(metadata ? 'Saved Jira choice is unavailable' : 'Loading saved Jira choice')}
                </option>
              )}
            {metadata?.issueTypes.map((type) => (
              <option key={type.id} value={type.id}>
                {type.name}
              </option>
            ))}
          </select>
        </label>
      </fieldset>
      <fieldset className="jira-section" disabled={busy}>
        <legend>
          <SlidersHorizontal size={16} />
          {t('Synchronization behavior')}
        </legend>
        <label>
          {t('Direction')}
          <select
            aria-label={t('Direction')}
            value={input.direction}
            onChange={(event) => {
              const direction = event.target.value;
              if (
                direction === 'jira-to-kanbada' ||
                direction === 'kanbada-to-jira' ||
                direction === 'bidirectional'
              )
                change('direction', direction);
            }}
          >
            <option value="jira-to-kanbada">{t('Jira to Kanbada')}</option>
            <option value="kanbada-to-jira">{t('Kanbada to Jira')}</option>
            <option value="bidirectional">{t('Bidirectional')}</option>
          </select>
        </label>
        <div className="jira-mapping-grid">
          <div className="jira-mapping-group">
            <h3>{t('Status mapping')}</h3>
            <JiraStatusMappings
              statuses={statuses}
              mappings={input.mappings}
              onChange={(mappings) => change('mappings', mappings)}
              metadata={metadata}
            />
          </div>
          <div className="jira-mapping-group">
            <h3>{t('Priority mapping')}</h3>
            {['Low', 'Medium', 'High'].map((priority) =>
              mapping('priority', priority, t(priority)),
            )}
          </div>
        </div>
        <p className="jira-hint">
          {t(
            'Several Jira statuses can map to one Kanbada status. Choose one default for writes to Jira; an already-matching Jira status is preserved. Jira status changes require an available direct transition.',
          )}
        </p>
      </fieldset>
      <fieldset className="jira-section" disabled={busy}>
        <legend>{t('Assignee mapping')}</legend>
        <label className="jira-assignee-toggle">
          <input
            type="checkbox"
            checked={input.syncAssignees ?? false}
            onChange={(event) => change('syncAssignees', event.target.checked)}
          />
          {t('Synchronize assignees from Jira')}
        </label>
        <p className="jira-hint">
          {t(
            'Jira assignees replace the card members. Unassigned or unmapped Jira users leave the card unassigned; missing mappings produce a warning. Jira assignees are never changed.',
          )}
        </p>
        {input.direction === 'kanbada-to-jira' && (
          <p className="jira-notice">
            {t('Assignee synchronization is inactive in Kanbada to Jira mode.')}
          </p>
        )}
        <JiraAssigneeMappings
          mappings={input.mappings}
          members={members}
          metadata={metadata}
          onChange={(mappings) => change('mappings', mappings)}
        />
      </fieldset>
      <fieldset className="jira-section" disabled={busy}>
        <legend>
          <Clock3 size={16} />
          {t('Cron expression')}
        </legend>
        <div className="jira-schedule-row">
          <label>
            {t('Repeat every')}
            <input
              type="number"
              min="1"
              max={unit === 'minutes' ? 59 : unit === 'hours' ? 23 : unit === 'days' ? 31 : 12}
              value={interval}
              onChange={(event) => setIntervalValue(Number(event.target.value))}
            />
          </label>
          <label>
            {t('Schedule unit')}
            <select
              aria-label={t('Schedule unit')}
              value={unit}
              onChange={(event) => {
                setUnit(event.target.value);
                setIntervalValue(1);
              }}
            >
              <option value="minutes">{t('Minutes')}</option>
              <option value="hours">{t('Hours')}</option>
              <option value="days">{t('Days')}</option>
              <option value="months">{t('Months')}</option>
            </select>
          </label>
          <button
            type="button"
            className="secondary"
            onClick={() => {
              const max =
                unit === 'minutes' ? 59 : unit === 'hours' ? 23 : unit === 'days' ? 31 : 12;
              if (!Number.isInteger(interval) || interval < 1 || interval > max) {
                setError(t('Choose an interval within the displayed range.'));
                return;
              }
              change(
                'cron',
                unit === 'minutes'
                  ? `*/${interval} * * * *`
                  : unit === 'hours'
                    ? `0 */${interval} * * *`
                    : unit === 'days'
                      ? `0 0 */${interval} * *`
                      : `0 0 1 */${interval} *`,
              );
            }}
          >
            {t('Apply schedule preset')}
          </button>
        </div>
        <div className="jira-mapping-grid">
          <label>
            {t('Cron expression')}
            <input
              required
              value={input.cron}
              onChange={(event) => change('cron', event.target.value)}
            />
          </label>
          <label>
            {t('Time zone')}
            <input
              required
              value={input.timeZone}
              onChange={(event) => change('timeZone', event.target.value)}
              placeholder="Europe/Lisbon"
            />
          </label>
        </div>
        <p className="jira-hint">
          {t(
            'Five cron fields: minute, hour, day, month, weekday. Presets use calendar boundaries, not elapsed durations. Monthly runs occur on the first day; a day absent from a month is skipped.',
          )}
        </p>
        {project.archived && <p>{t('Archived projects are not synchronized.')}</p>}
      </fieldset>
      {saved && (
        <fieldset className="jira-section jira-run-status" disabled={busy}>
          <legend>
            <RefreshCw size={16} />
            {t('Synchronization status')}
          </legend>
          <div className="jira-status-grid">
            <p>
              {t('Next run')}:{' '}
              {saved.enabled ? new Date(saved.nextRunAt).toLocaleString() : t('Disabled')}
            </p>
            <p>
              {t('Last started')}:{' '}
              {saved.lastStartedAt ? new Date(saved.lastStartedAt).toLocaleString() : t('Never')}
            </p>
            <p>
              {t('Last finished')}:{' '}
              {saved.lastFinishedAt ? new Date(saved.lastFinishedAt).toLocaleString() : t('Never')}
            </p>
            <p>
              {t('Items processed')}: {saved.lastSyncedCount}
            </p>
          </div>
          {saved.requestedAt && <p>{t('Run queued')}</p>}
          {saved.lastError && <p role="alert">{saved.lastError}</p>}
          <button
            type="button"
            className="secondary"
            disabled={!saved.enabled || project.archived}
            onClick={() =>
              void perform(async () => {
                await jiraRepository.run(workspaceId, project.id);
                setMessage(t('Run queued. The worker checks for scheduled work every 10 seconds.'));
                setSaved((await jiraRepository.read(workspaceId, project.id)).connection);
              })
            }
          >
            {t('Run saved configuration now')}
          </button>
          <button
            type="button"
            className="secondary"
            onClick={() =>
              void perform(async () =>
                setSaved((await jiraRepository.read(workspaceId, project.id)).connection),
              )
            }
          >
            {t('Refresh status')}
          </button>
          {saved.problems.map((problem) => (
            <div className="jira-problem" key={problem.id}>
              <p>
                <strong>
                  {problem.cardId} {problem.jiraKey}
                </strong>
                : {problem.lastError}
              </p>
              {problem.creationPending && (
                <>
                  <label>
                    {t('Existing Jira issue numeric ID')}
                    <input
                      inputMode="numeric"
                      value={resolutions[problem.id] ?? ''}
                      onChange={(event) =>
                        setResolutions({ ...resolutions, [problem.id]: event.target.value })
                      }
                    />
                  </label>
                  <button
                    type="button"
                    className="secondary"
                    onClick={() =>
                      void perform(async () => {
                        await jiraRepository.resolve(
                          workspaceId,
                          project.id,
                          problem.id,
                          resolutions[problem.id] ?? '',
                        );
                        setSaved((await jiraRepository.read(workspaceId, project.id)).connection);
                        setMessage(t('Issue linked. Request a new run to continue.'));
                      })
                    }
                  >
                    {t('Resolve interrupted creation')}
                  </button>
                </>
              )}
            </div>
          ))}
        </fieldset>
      )}
      <footer className="jira-save-bar">
        <label className="jira-toggle">
          <input
            type="checkbox"
            role="switch"
            disabled={busy}
            checked={input.enabled}
            onChange={(event) => change('enabled', event.target.checked)}
          />
          <span>{t('Enable synchronization')}</span>
        </label>
        <button type="submit" className="primary" disabled={busy}>
          <Save size={15} />
          {t('Save Jira settings')}
        </button>
      </footer>
    </form>
  );
}
