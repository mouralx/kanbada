import { CheckCheck, Clock3, Folder, Rows3 } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import type { State, Task } from '../../domain/models';
import { isActivitiesProject } from '../../domain/projectRules';
import { useI18n } from '../../shared/i18n';
import { CardLabels } from '../cards/CardLabels';
export function TaskList({
  tasks,
  data,
  onOpen,
  avatar,
  personal = false,
}: {
  tasks: Task[];
  personal?: boolean;
  data: State;
  onOpen: (task: Task) => void;
  avatar: (name: string, small?: boolean) => ReactNode;
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
          }))
          .filter((group) => group.tasks.length)
      : groupBy === 'None'
        ? [{ name: 'All matching cards', tasks }]
        : groupBy === 'Bucket'
          ? [
              ...data.buckets.map((bucket) => ({
                name: bucket.name,
                tasks: tasks.filter((t) => t.bucket === bucket.name),
              })),
              { name: 'No bucket', tasks: tasks.filter((t) => !t.bucket) },
            ].filter((group) => group.tasks.length)
          : [
              ...data.swimlanes.map((lane) => ({
                name: lane.name + ' · ' + (projectName(lane.project) ?? ''),
                tasks: tasks.filter((t) => t.project === lane.project && t.swimlane === lane.name),
              })),
              { name: 'No swimlane', tasks: tasks.filter((t) => !t.swimlane) },
            ].filter((group) => group.tasks.length);
  return (
    <section className="list-workspace">
      <div className="list-metrics-toolbar">
        <span>
          <Rows3 size={15} />
          {tasks.length}
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
          const complete = group.tasks.filter(done).length;
          const overdue = group.tasks.filter((t) => !done(t) && !!t.due && t.due < today).length;
          return (
            <section className="list-group" key={group.name} aria-label={group.name}>
              <header className="list-group-heading">
                <strong>
                  <Folder size={13} />
                  {group.name}
                </strong>
                <div>
                  <span>
                    {group.tasks.length}
                    {' ' + t('total')}
                  </span>
                  <span>
                    {group.tasks.length - complete}
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
              {group.tasks.map((t) => (
                <button className="list-row" key={t.id} onClick={() => onOpen(t)}>
                  <span>
                    <span
                      className="status-icon"
                      style={{
                        background: data.statuses.find((s) => s.name === t.status)?.color,
                        borderColor: data.statuses.find((s) => s.name === t.status)?.color,
                      }}
                    />
                    <span className="list-task-content">
                      <b>{t.title}</b>
                      <CardLabels names={t.labels} definitions={data.labels} />
                    </span>
                    <small>{t.id}</small>
                  </span>
                  <span>{t.status}</span>
                  <span className={`priority ${t.priority.toLowerCase()}`}>{t.priority}</span>
                  <span className="assignee-stack">
                    {t.assignees.map((name) => (
                      <span key={name}>{avatar(name, true)}</span>
                    ))}
                  </span>
                  <span>{t.due ? t.due.slice(5).replace('-', ' / ') : '—'}</span>
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
