import { Plus, Trash2 } from 'lucide-react';
import type { Definition, Member } from '../../domain/models';
import type { JiraMapping, JiraMetadata } from '../../infrastructure/jira';
import { useI18n } from '../../shared/i18n';

type Props = {
  mappings: JiraMapping[];
  onChange: (mappings: JiraMapping[]) => void;
  metadata: JiraMetadata | null;
};

export function JiraStatusMappings({
  mappings,
  onChange,
  metadata,
  statuses,
}: Props & { statuses: Definition[] }) {
  const { t } = useI18n();
  return statuses.map((status) => {
    const configured = mappings.filter((m) => m.kind === 'status' && m.kanbadaValue === status.id);
    const blank: JiraMapping = {
      kind: 'status',
      kanbadaValue: status.id,
      jiraValue: '',
      isDefault: true,
    };
    const rows = configured.length ? configured : [blank];
    const update = (next: JiraMapping[]) =>
      onChange([
        ...mappings.filter((m) => !(m.kind === 'status' && m.kanbadaValue === status.id)),
        ...next,
      ]);
    return (
      <div className="jira-status-group" key={status.id}>
        <h4>{status.name}</h4>
        {rows.map((row, index) => {
          const label =
            index === 0
              ? status.name
              : t('Additional Jira status for {0} ({1})', status.name, String(index));
          const setValue = (jiraValue: string) =>
            update(
              rows.length === 1 && !jiraValue
                ? []
                : rows.map((m, i) => (i === index ? { ...m, jiraValue } : m)),
            );
          return (
            <div className="jira-status-row" key={index}>
              <select
                aria-label={label}
                value={row.jiraValue}
                required={configured.length > 0}
                disabled={!metadata}
                onChange={(event) => setValue(event.target.value)}
              >
                <option value="">{t(metadata ? 'Not mapped' : 'Load Jira choices first')}</option>
                {row.jiraValue && !metadata?.statuses.some((s) => s.id === row.jiraValue) && (
                  <option value={row.jiraValue}>
                    {t(metadata ? 'Saved Jira choice is unavailable' : 'Loading saved Jira choice')}
                  </option>
                )}
                {metadata?.statuses.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name}
                  </option>
                ))}
              </select>
              <label className="jira-default-choice">
                <input
                  type="radio"
                  name={`jira-default-${status.id}`}
                  checked={row.isDefault !== false}
                  disabled={!metadata}
                  aria-label={t(
                    'Default Jira status for {0} ({1})',
                    status.name,
                    String(index + 1),
                  )}
                  onChange={() => update(rows.map((m, i) => ({ ...m, isDefault: i === index })))}
                />
                {t('Default')}
              </label>
              <button
                type="button"
                className="icon-button"
                aria-label={t('Remove Jira status for {0} ({1})', status.name, String(index + 1))}
                onClick={() => {
                  const next = rows.filter((_, i) => i !== index);
                  if (row.isDefault !== false && next[0]) next[0] = { ...next[0], isDefault: true };
                  update(next);
                }}
              >
                <Trash2 size={15} />
              </button>
            </div>
          );
        })}
        <button
          type="button"
          className="jira-add-mapping"
          aria-label={t('Add Jira status for {0}', status.name)}
          onClick={() => update([...rows, { ...blank, isDefault: false }])}
        >
          <Plus size={14} />
          {t('Add Jira status')}
        </button>
      </div>
    );
  });
}

export function JiraAssigneeMappings({
  mappings,
  onChange,
  metadata,
  members,
}: Props & { members: Member[] }) {
  const { t } = useI18n();
  const rows = mappings.filter((m) => m.kind === 'assignee');
  const update = (next: JiraMapping[]) =>
    onChange([...mappings.filter((m) => m.kind !== 'assignee'), ...next]);
  const registered = members.filter((member) => member.userId);
  return (
    <div className="jira-assignee-mappings">
      <p className="jira-hint">
        {t(
          'Select Jira users by name. Choices include assignees in the current JQL and saved mappings.',
        )}
      </p>
      {rows.map((row, index) => (
        <div className="jira-assignee-row" key={index}>
          <label>
            {t('Jira user')}
            <select
              value={row.jiraValue}
              required
              disabled={!metadata}
              aria-label={t('Jira user ({0})', String(index + 1))}
              onChange={(event) =>
                update(
                  rows.map((m, i) => (i === index ? { ...m, jiraValue: event.target.value } : m)),
                )
              }
            >
              <option value="">
                {t(metadata ? 'Select a Jira user' : 'Load Jira choices first')}
              </option>
              {row.jiraValue && !metadata?.assignees?.some((a) => a.id === row.jiraValue) && (
                <option value={row.jiraValue}>
                  {t(metadata ? 'Saved Jira choice is unavailable' : 'Loading saved Jira choice')}
                </option>
              )}
              {metadata?.assignees?.map((a) => (
                <option key={a.id} value={a.id}>
                  {a.name}
                </option>
              ))}
            </select>
          </label>
          <label>
            {t('Workspace member')}
            <select
              value={row.kanbadaValue}
              required
              aria-label={t('Workspace member ({0})', String(index + 1))}
              onChange={(event) =>
                update(
                  rows.map((m, i) =>
                    i === index ? { ...m, kanbadaValue: event.target.value } : m,
                  ),
                )
              }
            >
              <option value="">{t('Select a member')}</option>
              {row.kanbadaValue && !registered.some((m) => m.userId === row.kanbadaValue) && (
                <option value={row.kanbadaValue}>{t('Unavailable member')}</option>
              )}
              {registered.map((m) => (
                <option key={m.userId} value={m.userId}>
                  {m.name} ({m.email})
                </option>
              ))}
            </select>
          </label>
          <button
            type="button"
            className="icon-button"
            aria-label={t('Remove assignee mapping ({0})', String(index + 1))}
            onClick={() => update(rows.filter((_, i) => i !== index))}
          >
            <Trash2 size={15} />
          </button>
        </div>
      ))}
      <button
        type="button"
        className="jira-add-mapping"
        onClick={() =>
          update([...rows, { kind: 'assignee', kanbadaValue: '', jiraValue: '', isDefault: false }])
        }
      >
        <Plus size={14} />
        {t('Add assignee mapping')}
      </button>
    </div>
  );
}
