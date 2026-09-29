import { useEffect, useState } from 'react';
import { Clock3, Folder, Github, RefreshCw, Save, SlidersHorizontal, Trash2 } from 'lucide-react';
import type { Definition, Member, Project } from '../../domain/models';
import {
  githubRepository,
  type GitHubConnection,
  type GitHubInput,
  type GitHubMetadata,
} from '../../infrastructure/github';
import { useI18n } from '../../shared/i18n';
import { ConnectorServerAccess } from './ConnectorServerAccess';
import './jira-settings.css';

export function GitHubSettings({
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
  const { t, locale } = useI18n();
  const [input, setInput] = useState<GitHubInput>({
    version: 0,
    baseUrl: 'https://github.com',
    owner: '',
    ownerType: 'organization',
    projectNumber: 1,
    repository: '',
    token: '',
    direction: 'github-to-kanbada',
    statusFieldId: '',
    priorityFieldId: '',
    dueFieldId: '',
    syncLabels: true,
    syncAssignees: false,
    cron: '*/15 * * * *',
    timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC',
    enabled: false,
    mappings: [],
  });
  const [saved, setSaved] = useState<GitHubConnection | null>(null);
  const [metadata, setMetadata] = useState<GitHubMetadata | null>(null);
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [login, setLogin] = useState('');
  const [userNames, setUserNames] = useState<Record<string, string>>({});
  const [resolutions, setResolutions] = useState<Record<string, string>>({});
  const [retryConfirmed, setRetryConfirmed] = useState<Record<string, boolean>>({});
  useEffect(() => {
    const controller = new AbortController();
    githubRepository
      .read(workspaceId, project.id, controller.signal)
      .then(async ({ connection }) => {
        if (controller.signal.aborted) return;
        setSaved(connection);
        if (connection) {
          const draft = { ...connection, token: '' };
          setInput(draft);
          const choices = await githubRepository.test(
            workspaceId,
            project.id,
            draft,
            controller.signal,
          );
          if (!controller.signal.aborted) setMetadata(choices);
        }
      })
      .catch((reason: unknown) => {
        if (!controller.signal.aborted)
          setError(reason instanceof Error ? reason.message : t('Could not load GitHub settings.'));
      })
      .finally(() => {
        if (!controller.signal.aborted) setBusy(false);
      });
    return () => controller.abort();
  }, [workspaceId, project.id, t]);
  useEffect(() => {
    if (!saved?.id) return;
    const controller = new AbortController();
    const timer = window.setInterval(() => {
      githubRepository
        .read(workspaceId, project.id, controller.signal)
        .then(({ connection }) => {
          if (!controller.signal.aborted) setSaved(connection);
        })
        .catch((reason: unknown) => {
          if (!controller.signal.aborted)
            setError(
              reason instanceof Error
                ? reason.message
                : t('Could not refresh synchronization status.'),
            );
        });
    }, 5000);
    return () => {
      controller.abort();
      window.clearInterval(timer);
    };
  }, [workspaceId, project.id, saved?.id, t]);

  function change<K extends keyof GitHubInput>(key: K, value: GitHubInput[K]) {
    if (['baseUrl', 'owner', 'ownerType', 'projectNumber', 'repository', 'token'].includes(key))
      setMetadata(null);
    setInput((current) => ({ ...current, [key]: value }));
  }
  async function perform(action: () => Promise<void>) {
    setBusy(true);
    setError('');
    setMessage('');
    try {
      await action();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : t('GitHub request failed.'));
    } finally {
      setBusy(false);
    }
  }
  async function refresh() {
    const result = await githubRepository.read(workspaceId, project.id);
    setSaved(result.connection);
  }
  function mapValue(kind: 'status' | 'priority', remote: string, local: string) {
    const mappings = input.mappings.filter((m) => m.kind !== kind || m.gitHubValue !== remote);
    if (local) mappings.push({ kind, kanbadaValue: local, gitHubValue: remote, isDefault: false });
    const defaults = new Map<string, string>();
    for (const m of mappings.filter((m) => m.kind === kind))
      if (!defaults.has(m.kanbadaValue) || m.isDefault) defaults.set(m.kanbadaValue, m.gitHubValue);
    change(
      'mappings',
      mappings.map((m) =>
        m.kind === kind ? { ...m, isDefault: defaults.get(m.kanbadaValue) === m.gitHubValue } : m,
      ),
    );
  }
  function mappings(kind: 'status' | 'priority', fieldId: string) {
    const field = metadata?.fields.find((f) => f.id === fieldId);
    if (!field)
      return (
        <p className="jira-hint">{t('Test the connection and choose a field to load mappings.')}</p>
      );
    const choices = [{ id: '__none__', name: t('Empty GitHub field') }, ...field.options];
    const unavailable = input.mappings.filter(
      (m) => m.kind === kind && !choices.some((o) => o.id === m.gitHubValue),
    );
    const localOptions =
      kind === 'status' ? statuses : ['Low', 'Medium', 'High'].map((id) => ({ id, name: t(id) }));
    return [
      ...choices,
      ...unavailable.map((m) => ({
        id: m.gitHubValue,
        name: t('Unavailable option: {0}', m.gitHubValue),
      })),
    ].map((option) => {
      const mapping = input.mappings.find((m) => m.kind === kind && m.gitHubValue === option.id);
      return (
        <div className="github-mapping-row" key={option.id}>
          <label>
            {option.name}
            <select
              aria-label={t('Kanbada {0} for {1}', kind, option.name)}
              value={mapping?.kanbadaValue ?? ''}
              onChange={(e) => mapValue(kind, option.id, e.target.value)}
            >
              <option value="">{t('Not mapped')}</option>
              {localOptions.map((local) => (
                <option key={local.id} value={local.id}>
                  {local.name}
                </option>
              ))}
            </select>
          </label>
          <label className="jira-default-choice">
            <input
              type="radio"
              name={`${kind}-${mapping?.kanbadaValue ?? option.id}`}
              disabled={!mapping}
              checked={mapping?.isDefault ?? false}
              aria-label={t('Default outbound GitHub option: {0}', option.name)}
              onChange={() =>
                change(
                  'mappings',
                  input.mappings.map((m) =>
                    m.kind === kind && m.kanbadaValue === mapping?.kanbadaValue
                      ? { ...m, isDefault: m.gitHubValue === option.id }
                      : m,
                  ),
                )
              }
            />
            {t('Default outbound')}
          </label>
        </div>
      );
    });
  }
  function fieldSelector(
    key: 'statusFieldId' | 'priorityFieldId' | 'dueFieldId',
    label: string,
    type: string,
  ) {
    const fields = metadata?.fields.filter(
      (f) => f.dataType === type && (key !== 'priorityFieldId' || f.id !== input.statusFieldId),
    );
    return (
      <label>
        {t(label)}
        <select
          aria-label={t(label)}
          value={input[key]}
          required={key === 'statusFieldId'}
          disabled={!metadata}
          onChange={(e) => {
            const kind =
              key === 'statusFieldId' ? 'status' : key === 'priorityFieldId' ? 'priority' : null;
            setInput((current) => ({
              ...current,
              [key]: e.target.value,
              mappings: kind ? current.mappings.filter((m) => m.kind !== kind) : current.mappings,
            }));
          }}
        >
          <option value="">
            {t(key === 'statusFieldId' ? 'Choose a status field' : 'Not synchronized')}
          </option>
          {input[key] && !fields?.some((f) => f.id === input[key]) && (
            <option value={input[key]}>{t('Saved field is unavailable')}</option>
          )}
          {fields?.map((field) => (
            <option value={field.id} key={field.id}>
              {field.name}
            </option>
          ))}
        </select>
      </label>
    );
  }
  const timestamp = (value: string | null | undefined) =>
    value ? new Date(value).toLocaleString(locale) : t('Never');
  return (
    <form
      className="jira-settings"
      onSubmit={(event) => {
        event.preventDefault();
        void perform(async () => {
          const connection = await githubRepository.save(workspaceId, project.id, input);
          setSaved(connection);
          setInput({ ...connection, token: '' });
          setMessage(t('GitHub settings saved.'));
        });
      }}
    >
      <header className="jira-intro">
        <span className="jira-project-icon" style={{ background: project.color }}>
          <Folder size={24} />
        </span>
        <div>
          <span className="jira-eyebrow">{t('GitHub synchronization')}</span>
          <h2>{project.name}</h2>
        </div>
        <span className={`jira-badge ${saved?.enabled ? 'is-enabled' : ''}`}>
          {t(saved?.enabled ? 'Enabled' : 'Disabled')}
        </span>
      </header>
      <p className="jira-intro-copy">
        {t(
          'Connect GitHub Projects v2 issues, pull requests and draft items. New outbound cards become repository issues. One Jira or GitHub connector is allowed per Kanbada project.',
        )}
      </p>
      <details className="jira-help">
        <summary>{t('Synchronization behavior')}</summary>
        <p>
          {t(
            'Project status does not close issues or merge pull requests. Deletions, comments and attachments are not synchronized. In bidirectional conflicts, the original creation system wins. Interrupted outbound updates finish from Kanbada before normal conflict detection resumes.',
          )}
        </p>
        <p>
          {t(
            'Assignees are inbound only. Unmapped GitHub users are omitted with a visible warning. Draft items have no labels; local labels are preserved. Inbound-only linked cards stay read-only even when synchronization is paused.',
          )}
        </p>
      </details>
      {error && (
        <p role="alert" className="jira-notice is-error">
          {t(error)}
        </p>
      )}
      {message && (
        <p role="status" className="jira-notice">
          {message}
        </p>
      )}
      <fieldset className="jira-section jira-connection-grid" disabled={busy}>
        <legend>
          <Github size={16} />
          {t('GitHub connection')}
        </legend>
        <label>
          {t('GitHub server')}
          <input
            type="url"
            required
            value={input.baseUrl}
            onChange={(e) => change('baseUrl', e.target.value)}
            placeholder="https://github.com"
          />
        </label>
        <label>
          {t('Project owner type')}
          <select
            aria-label={t('Project owner type')}
            value={input.ownerType}
            onChange={(e) =>
              change('ownerType', e.target.value === 'user' ? 'user' : 'organization')
            }
          >
            <option value="organization">{t('Organization')}</option>
            <option value="user">{t('User')}</option>
          </select>
        </label>
        <label>
          {t('GitHub project owner')}
          <input
            required
            value={input.owner}
            onChange={(e) => change('owner', e.target.value.trim())}
            placeholder="example-org"
            autoComplete="off"
          />
        </label>
        <label>
          {t('GitHub project number')}
          <input
            type="number"
            required
            min={1}
            step={1}
            value={input.projectNumber}
            onChange={(e) => change('projectNumber', Number(e.target.value))}
          />
        </label>
        <label>
          {t('Repository for new issues')}
          <input
            required={input.direction !== 'github-to-kanbada'}
            value={input.repository}
            onChange={(e) => change('repository', e.target.value.trim())}
            placeholder="owner/repository"
          />
        </label>
        <label>
          {t('GitHub access token')}
          <input
            type="password"
            autoComplete="new-password"
            value={input.token}
            onChange={(e) => change('token', e.target.value)}
            placeholder={saved?.hasToken ? t('Leave blank to keep the saved token') : ''}
          />
        </label>
        <p className="jira-hint jira-full">
          {t(
            'Use a token with Projects read access, or write access for outbound synchronization, plus access to the relevant repositories. The token is encrypted on the server. Test does not create or modify GitHub items.',
          )}
        </p>
        <ConnectorServerAccess baseUrl={input.baseUrl} provider="GitHub" />
        <button
          type="button"
          className="secondary jira-full"
          onClick={() =>
            void perform(async () => {
              const choices = await githubRepository.test(workspaceId, project.id, input);
              setMetadata(choices);
              setInput((current) => ({
                ...current,
                statusFieldId:
                  current.statusFieldId ||
                  choices.fields.find(
                    (f) => f.name.toLowerCase() === 'status' && f.dataType === 'SINGLE_SELECT',
                  )?.id ||
                  '',
              }));
              setMessage(t('Connected to GitHub project: {0}', choices.title));
            })
          }
        >
          <RefreshCw size={15} />
          {t('Test connection and load mappings')}
        </button>
      </fieldset>
      <fieldset className="jira-section" disabled={busy}>
        <legend>
          <SlidersHorizontal size={16} />
          {t('Field mappings')}
        </legend>
        <div className="jira-mapping-grid">
          <div>
            {fieldSelector('statusFieldId', 'GitHub status field', 'SINGLE_SELECT')}
            {mappings('status', input.statusFieldId)}
          </div>
          <div>
            {fieldSelector('priorityFieldId', 'GitHub priority field (optional)', 'SINGLE_SELECT')}
            {input.priorityFieldId && mappings('priority', input.priorityFieldId)}
            {fieldSelector('dueFieldId', 'GitHub due-date field (optional)', 'DATE')}
          </div>
        </div>
        <p className="jira-hint">
          {t(
            'Map GitHub options to Kanbada values. Choose one default outbound option per mapped Kanbada value. Map empty fields explicitly when needed. When enabled, priority requires Low, Medium and High mappings.',
          )}
        </p>
        <label className="jira-assignee-toggle">
          <input
            type="checkbox"
            checked={input.syncLabels}
            onChange={(e) => change('syncLabels', e.target.checked)}
          />
          {t('Synchronize issue and pull-request labels')}
        </label>
        <label className="jira-assignee-toggle">
          <input
            type="checkbox"
            checked={input.syncAssignees}
            onChange={(e) => change('syncAssignees', e.target.checked)}
          />
          {t('Map GitHub assignees to workspace members')}
        </label>
        {input.syncAssignees && (
          <>
            <div className="github-mapping-row">
              <label>
                {t('Find GitHub user by login')}
                <input
                  value={login}
                  onChange={(e) => setLogin(e.target.value)}
                  autoComplete="off"
                />
              </label>
              <button
                type="button"
                className="secondary"
                disabled={!login.trim() || !metadata}
                onClick={() =>
                  void perform(async () => {
                    const user = await githubRepository.user(
                      workspaceId,
                      project.id,
                      input,
                      login.trim(),
                    );
                    setUserNames((names) => ({ ...names, [user.id]: user.name }));
                    if (
                      !input.mappings.some(
                        (m) => m.kind === 'assignee' && m.gitHubValue === user.id,
                      )
                    )
                      change('mappings', [
                        ...input.mappings,
                        {
                          kind: 'assignee',
                          gitHubValue: user.id,
                          kanbadaValue: '',
                          isDefault: true,
                        },
                      ]);
                    setLogin('');
                  })
                }
              >
                {t('Find user')}
              </button>
            </div>
            {input.mappings
              .filter((m) => m.kind === 'assignee')
              .map((mapping) => (
                <div className="jira-assignee-row" key={mapping.gitHubValue}>
                  <label>
                    {t('GitHub user')}
                    <input readOnly value={userNames[mapping.gitHubValue] ?? mapping.gitHubValue} />
                  </label>
                  <label>
                    {t('Workspace member')}
                    <select
                      aria-label={t(
                        'Workspace member for {0}',
                        userNames[mapping.gitHubValue] ?? mapping.gitHubValue,
                      )}
                      required
                      value={mapping.kanbadaValue}
                      onChange={(e) =>
                        change(
                          'mappings',
                          input.mappings.map((m) =>
                            m === mapping ? { ...m, kanbadaValue: e.target.value } : m,
                          ),
                        )
                      }
                    >
                      <option value="">{t('Choose a member')}</option>
                      {members
                        .filter((member) => member.userId)
                        .map((member) => (
                          <option key={member.userId} value={member.userId}>
                            {member.name}
                          </option>
                        ))}
                    </select>
                  </label>
                  <button
                    type="button"
                    className="icon-button"
                    aria-label={t('Remove assignee mapping')}
                    onClick={() =>
                      change(
                        'mappings',
                        input.mappings.filter((m) => m !== mapping),
                      )
                    }
                  >
                    <Trash2 size={15} />
                  </button>
                </div>
              ))}
          </>
        )}
      </fieldset>
      <fieldset className="jira-section jira-connection-grid" disabled={busy}>
        <legend>
          <Clock3 size={16} />
          {t('Synchronization schedule')}
        </legend>
        <label>
          {t('Direction')}
          <select
            aria-label={t('Direction')}
            value={input.direction}
            onChange={(e) => {
              const value = e.target.value;
              if (
                value === 'github-to-kanbada' ||
                value === 'kanbada-to-github' ||
                value === 'bidirectional'
              )
                change('direction', value);
            }}
          >
            <option value="github-to-kanbada">{t('GitHub to Kanbada')}</option>
            <option value="kanbada-to-github">{t('Kanbada to GitHub')}</option>
            <option value="bidirectional">{t('Bidirectional')}</option>
          </select>
        </label>
        <label>
          {t('Time zone')}
          <input
            required
            value={input.timeZone}
            onChange={(e) => change('timeZone', e.target.value)}
            placeholder="Europe/Lisbon"
          />
        </label>
        <label>
          {t('Cron expression')}
          <input
            required
            value={input.cron}
            onChange={(e) => change('cron', e.target.value)}
            placeholder="*/15 * * * *"
          />
        </label>
        <p className="jira-hint">
          {t(
            'Five-field cron: */15 * * * * runs every 15 minutes; 0 * * * * runs hourly; 0 9 * * * runs daily at 09:00 in the selected time zone.',
          )}
        </p>
        <label className="jira-assignee-toggle jira-full">
          <input
            type="checkbox"
            checked={input.enabled}
            onChange={(e) => change('enabled', e.target.checked)}
          />
          {t('Enable synchronization')}
        </label>
      </fieldset>
      {saved && (
        <section className="jira-section" aria-label={t('GitHub synchronization status')}>
          <h3>{t('Last run')}</h3>
          <p>
            {t('Started')}: {timestamp(saved.lastStartedAt)}
            <br />
            {t('Finished')}: {timestamp(saved.lastFinishedAt)}
            <br />
            {t('Next run')}: {saved.enabled ? timestamp(saved.nextRunAt) : t('Disabled')}
            <br />
            {t('Synchronized items')}: {saved.lastSyncedCount}
          </p>
          {saved.requestedAt && (
            <p role="status">{t('Synchronization queued. The worker will pick it up shortly.')}</p>
          )}
          {saved.lastStartedAt &&
            (!saved.lastFinishedAt || saved.lastStartedAt > saved.lastFinishedAt) && (
              <p role="status">{t('Synchronization is running.')}</p>
            )}
          {saved.lastError && (
            <p className="jira-notice is-error" style={{ whiteSpace: 'pre-line' }}>
              {t(saved.lastError)}
            </p>
          )}
          {saved.problems.length > 0 && (
            <p>
              {t(
                'Showing up to 100 items needing attention. Resolve them and refresh to see remaining problems.',
              )}
            </p>
          )}
          {saved.problems.map((problem) => (
            <article className="github-problem" key={problem.id}>
              <strong>
                {problem.cardId}
                {problem.displayKey ? ` · ${problem.displayKey}` : ''}
              </strong>
              <p>{t(problem.lastError)}</p>
              {problem.creationPending && (
                <fieldset disabled={busy}>
                  <label>
                    {t('Created GitHub issue URL')}
                    <input
                      type="url"
                      value={resolutions[problem.id] ?? ''}
                      onChange={(e) =>
                        setResolutions((current) => ({ ...current, [problem.id]: e.target.value }))
                      }
                      placeholder={`${input.baseUrl.replace(/\/$/, '')}/${input.repository}/issues/42`}
                    />
                  </label>
                  <button
                    type="button"
                    className="secondary"
                    disabled={!resolutions[problem.id]?.trim()}
                    onClick={() =>
                      void perform(async () => {
                        await githubRepository.resolve(
                          workspaceId,
                          project.id,
                          problem.id,
                          resolutions[problem.id].trim(),
                        );
                        await refresh();
                      })
                    }
                  >
                    {t('Attach verified issue')}
                  </button>
                  <label className="jira-assignee-toggle">
                    <input
                      type="checkbox"
                      checked={retryConfirmed[problem.id] ?? false}
                      onChange={(e) =>
                        setRetryConfirmed((current) => ({
                          ...current,
                          [problem.id]: e.target.checked,
                        }))
                      }
                    />
                    {t(
                      'I checked GitHub and confirm that no issue was created. Retrying without checking can create a duplicate.',
                    )}
                  </label>
                  <button
                    type="button"
                    className="secondary"
                    disabled={!retryConfirmed[problem.id]}
                    onClick={() =>
                      void perform(async () => {
                        await githubRepository.resolve(
                          workspaceId,
                          project.id,
                          problem.id,
                          null,
                          true,
                        );
                        await refresh();
                      })
                    }
                  >
                    {t('Allow a new creation attempt')}
                  </button>
                </fieldset>
              )}
            </article>
          ))}
        </section>
      )}
      <div className="jira-save-bar github-save-bar">
        <button
          type="button"
          className="secondary"
          disabled={busy || !saved?.enabled}
          onClick={() =>
            void perform(async () => {
              if (!saved) return;
              const connection = await githubRepository.pause(
                workspaceId,
                project.id,
                saved.version,
              );
              setSaved(connection);
              setInput((current) =>
                current.version === saved.version
                  ? { ...current, version: connection.version, enabled: false }
                  : current,
              );
              setMessage(t('GitHub synchronization paused. In-flight requests may finish.'));
            })
          }
        >
          {t('Pause synchronization')}
        </button>
        <button type="submit" className="primary" disabled={busy || !metadata}>
          <Save size={15} />
          {t('Save settings')}
        </button>
        <button
          type="button"
          className="secondary"
          disabled={busy || !saved?.enabled}
          onClick={() =>
            void perform(async () => {
              await githubRepository.run(workspaceId, project.id);
              await refresh();
              setMessage(t('Synchronization queued. The worker will pick it up shortly.'));
            })
          }
        >
          <RefreshCw size={15} />
          {t('Run saved settings')}
        </button>
        <button
          type="button"
          className="secondary"
          disabled={busy || !saved}
          onClick={() => void perform(refresh)}
        >
          {t('Refresh status')}
        </button>
      </div>
    </form>
  );
}
