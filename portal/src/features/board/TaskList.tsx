import { CheckCheck, Clock3, Folder, LockKeyhole, Rows3 } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import type { State, Task } from '../../domain/models';
import { isActivitiesProject } from '../../domain/projectRules';
import { useI18n } from '../../shared/i18n';
import { CardLabels } from '../cards/CardLabels';
import { sumGroups, type CardSummary } from '../../infrastructure/cards';
export function TaskList({
  tasks,
  data,
  onOpen,
  avatar,
  personal = false,
  summary,
}: {
  tasks: Task[];
  personal?: boolean;
  data: State;
  onOpen: (task: Task) => void;
  avatar: (name: string, small?: boolean) => ReactNode;
  summary?: CardSummary | null;
}) {
  const { t } = useI18n();
  const projectName = (id: string) => {
    const p = data.projects.find((p) => p.id === id);
    return p ? (isActivitiesProject(p) ? t('My activities') : p.name) : '';
  };
  const [groupBy, setGroupBy] = useState(personal ? 'Project' : 'Bucket');
  const done = (t: Task) => !!data.statuses.find((s) => s.name === t.status)?.complete;
  const now = new Date();
  const today = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
  const groups =
    groupBy === 'Project'
      ? data.projects
          .filter((p) => !p.archived)
          .map((p) => ({
            name: projectName(p.id),
            tasks: tasks.filter((task) => task.project === p.id),
            counts: summary ? sumGroups(summary, (g) => g.project === p.id) : undefined,
          }))
          .filter((group) => group.tasks.length)
      : groupBy === 'None'
        ? [{ name: 'All matching cards', tasks, counts: summary?.counts }]
        : groupBy === 'Bucket'
          ? [
              ...data.buckets.map((bucket) => ({
                name: bucket.name,
                tasks: tasks.filter((t) => t.bucket === bucket.name),
                counts: summary ? sumGroups(summary, (g) => g.bucket === bucket.id) : undefined,
              })),
              {
                name: 'No bucket',
                tasks: tasks.filter((t) => !t.bucket),
                counts: summary ? sumGroups(summary, (g) => !g.bucket) : undefined,
              },
            ].filter((group) => group.tasks.length)
          : [
              ...data.swimlanes.map((lane) => ({
                name: lane.name + ' · ' + (projectName(lane.project) ?? ''),
                tasks: tasks.filter((t) => t.project === lane.project && t.swimlane === lane.name),
                counts: summary ? sumGroups(summary, (g) => g.swimlane === lane.id) : undefined,
              })),
              {
                name: 'No swimlane',
                tasks: tasks.filter((t) => !t.swimlane),
                counts: summary ? sumGroups(summary, (g) => !g.swimlane) : undefined,
              },
            ].filter((group) => group.tasks.length);
  return (
    <section className="list-workspace">
      <div className="list-metrics-toolbar">
        <span>
          <Rows3 size={15} />
          {summary?.counts.total ?? tasks.length}
          {' ' + t('matching cards')}
        </span>
        <label>
          {t('Group list by')}
          <select
            aria-label={t('Group list by')}
            value={groupBy}
            onChange={(e) => setGroupBy(e.target.value)}
          >
            <option value="Project">{t('Project')}</option>
            <option value={'Bucket'}>{t('Bucket')}</option>
            <option value={'Swimlane'}>{t('Swimlane')}</option>
            <option value={'None'}>{t('None')}</option>
          </select>
        </label>
      </div>
      <div className="task-list">
        <div className="list-header">
          <span>{t('Task name')}</span>
          <span>{t('Status')}</span>
          <span>{t('Priority')}</span>
          <span>{t('Assignees')}</span>
          <span>{t('Due date')}</span>
        </div>
        {groups.map((group) => {
          const total = group.counts?.total ?? group.tasks.length;
          const complete = group.counts?.completed ?? group.tasks.filter(done).length;
          const overdue =
            group.counts?.overdue ??
            group.tasks.filter((t) => !done(t) && !!t.due && t.due < today).length;
          return (
            <section className="list-group" key={group.name} aria-label={group.name}>
              <header className="list-group-heading">
                <strong>
                  <Folder size={13} />
                  {group.name}
                </strong>
                <div>
                  <span>
                    {total}
                    {' ' + t('total')}
                  </span>
                  <span>
                    {total - complete}
                    {' ' + t('open')}
                  </span>
                  <span>
                    <CheckCheck size={12} />
                    {complete}
                    {' ' + t('completed')}
                  </span>
                  <span className={overdue ? 'overdue' : ''}>
                    <Clock3 size={12} />
                    {overdue}
                    {' ' + t('overdue')}
                  </span>
                </div>
              </header>
              {group.tasks.map((task) => (
                <button className="list-row" key={task.id} onClick={() => onOpen(task)}>
                  <span>
                    <span
                      className="status-icon"
                      style={{
                        background: data.statuses.find((s) => s.name === task.status)?.color,
                        borderColor: data.statuses.find((s) => s.name === task.status)?.color,
                      }}
                    />
                    <span className="list-task-content">
                      <span className="list-task-title">
                        <b>{task.title}</b>
                        {task.readOnly && (
                          <span
                            className="list-task-lock"
                            title={t('Managed by Jira')}
                            aria-label={t('Managed by Jira')}
                          >
                            <LockKeyhole size={13} />
                          </span>
                        )}
                      </span>
                      <CardLabels names={task.labels} definitions={data.labels} />
                    </span>
                    <small>{task.id}</small>
                  </span>
                  <span>{task.status}</span>
                  <span className={`priority ${task.priority.toLowerCase()}`}>{task.priority}</span>
                  <span className="assignee-stack">
                    {task.assignees.map((name) => (
                      <span key={name}>{avatar(name, true)}</span>
                    ))}
                  </span>
                  <span>{task.due ? task.due.slice(5).replace('-', ' / ') : '—'}</span>
                </button>
              ))}
            </section>
          );
        })}
        {!tasks.length && (
          <div className="empty-state">
            {t('No matching cards. Try another bucket, swimlane, or filter.')}
          </div>
        )}
      </div>
    </section>
  );
}
