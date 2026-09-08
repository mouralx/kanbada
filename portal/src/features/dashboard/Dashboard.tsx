import {
  Activity,
  ArrowUpRight,
  CheckCheck,
  Clock3,
  Download,
  Flag,
  Layers,
  Users,
  X,
} from 'lucide-react';
import { useEffect, useState } from 'react';
import type { State, Task } from '../../domain/models';
import { isActivitiesProject } from '../../domain/projectRules';
import { useI18n } from '../../shared/i18n';
type MetricGroup = {
  label: string;
  tasks: Task[];
  color?: string;
};
export function Dashboard({
  data,
  tasks: allTasks,
  title,
  onOpen,
  projectId,
}: {
  data: State;
  tasks: Task[];
  title: string;
  onOpen: (task: Task) => void;
  projectId?: string;
}) {
  const { t, locale } = useI18n();
  const projectName = (id: string) => {
    const p = data.projects.find((p) => p.id === id);
    return p ? (isActivitiesProject(p) ? t('My activities') : p.name) : '';
  };
  const [exporting, setExporting] = useState(false);
  const [exportError, setExportError] = useState('');
  const [now, setNow] = useState(() => new Date());
  const [selected, setSelected] = useState<string | null>(null);
  const [bucket, setBucket] = useState('All');
  const [lane, setLane] = useState('All');
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), 60000);
    return () => clearInterval(timer);
  }, []);
  const dateKey = (date: Date) =>
    `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
  const today = dateKey(now);
  const done = (t: Task) => !!data.statuses.find((s) => s.name === t.status)?.complete;
  const availableLanes = data.swimlanes.filter((l) =>
    projectId
      ? l.project === projectId
      : data.projects.some((p) => p.id === l.project && !p.archived),
  );
  const chosenBucket = data.buckets.find((b) => b.id === bucket);
  const chosenLane = data.swimlanes.find((l) => l.id === lane);
  const tasks = allTasks.filter(
    (t) =>
      (bucket === 'All' || (bucket === '' ? !t.bucket : t.bucket === chosenBucket?.name)) &&
      (lane === 'All' ||
        (lane === ''
          ? !t.swimlane
          : t.swimlane === chosenLane?.name && t.project === chosenLane?.project)),
  );
  const open = tasks.filter((t) => !done(t));
  const completed = tasks.filter(done);
  const overdue = open.filter((t) => !!t.due && t.due < today);
  const high = open.filter((t) => t.priority === 'High');
  const completion = tasks.length ? Math.round((completed.length / tasks.length) * 100) : 0;
  const metrics = [
    {
      label: 'Total cards',
      value: tasks.length,
      icon: Layers,
      tasks,
      hint: 'Across this dashboard',
    },
    {
      label: 'Completed',
      value: completed.length,
      icon: CheckCheck,
      tasks: completed,
      hint: `${completion}% completion rate`,
    },
    {
      label: 'Open cards',
      value: open.length,
      icon: Activity,
      tasks: open,
      hint: 'Ready for the next step',
    },
    {
      label: 'Overdue',
      value: overdue.length,
      icon: Clock3,
      tasks: overdue,
      hint: 'Open cards past their due date',
    },
    {
      label: 'High priority',
      value: high.length,
      icon: Flag,
      tasks: high,
      hint: 'Open cards needing focus',
    },
    {
      label: 'Unassigned',
      value: open.filter((t) => !t.assignees.length).length,
      icon: Users,
      tasks: open.filter((t) => !t.assignees.length),
      hint: 'Open cards without an owner',
    },
  ];
  const bucketGroups: MetricGroup[] = [
    ...data.buckets.map((b) => ({
      label: b.name,
      color: b.color,
      tasks: tasks.filter((t) => t.bucket === b.name),
    })),
    { label: 'No bucket', color: '#bcc9d7', tasks: tasks.filter((t) => !t.bucket) },
  ];
  const laneGroups: MetricGroup[] = [
    ...availableLanes.map((l) => ({
      label: l.name + (projectId ? '' : ' · ' + projectName(l.project)),
      color: l.color,
      tasks: tasks.filter((t) => t.swimlane === l.name && t.project === l.project),
    })),
    { label: 'No swimlane', color: '#bcc9d7', tasks: tasks.filter((t) => !t.swimlane) },
  ];
  const dueDays = Array.from({ length: 7 }, (_, i) => {
    const date = new Date(now);
    date.setDate(date.getDate() + i);
    const key = dateKey(date);
    return {
      key,
      date,
      label: 'Due ' + date.toLocaleDateString(locale, { month: 'short', day: 'numeric' }),
      tasks: tasks.filter((t) => t.due === key),
    };
  });
  const groups = [
    ...metrics,
    ...bucketGroups.map((g) => ({ ...g, label: 'Bucket: ' + g.label })),
    ...laneGroups.map((g) => ({ ...g, label: 'Swimlane: ' + g.label })),
    ...data.statuses.map((s) => ({
      label: 'Status: ' + s.name,
      tasks: tasks.filter((t) => t.status === s.name),
    })),
    ...dueDays,
  ];
  const exportPdf = async () => {
    setExporting(true);
    setExportError('');
    try {
      const { exportDashboardPdf } = await import('./dashboardPdf');
      exportDashboardPdf(
        data,
        tasks,
        title,
        locale,
        `${t('Bucket')}: ${bucket === 'All' ? t('All buckets') : (chosenBucket?.name ?? t('No bucket'))} / ${t('Swimlane')}: ${lane === 'All' ? t('All swimlanes') : (chosenLane?.name ?? t('No swimlane'))}`,
        projectId,
      );
    } catch {
      setExportError(t('Could not export the dashboard. Please try again.'));
    } finally {
      setExporting(false);
    }
  };
  const selectedTasks = groups.find((m) => m.label === selected)?.tasks ?? [];
  const activity = tasks
    .flatMap((task) => (task.history ?? []).map((entry) => ({ task, entry })))
    .sort((a, b) => b.entry.at.localeCompare(a.entry.at))
    .slice(0, 5);
  const checklist = tasks.flatMap((t) => t.checklist);
  const checklistDone = checklist.filter((c) => c.done).length;
  const maxDue = Math.max(1, ...dueDays.map((d) => d.tasks.length));
  const bars = (
    rows: {
      name: string;
      color: string;
      count: number;
    }[],
    denominator: number,
    clickable = false,
  ) =>
    rows.map((row) => (
      <button
        className="metric-bar-row"
        key={row.name}
        disabled={!clickable}
        onClick={() => setSelected('Status: ' + row.name)}
        title={t('{0}: {1} cards', row.name, row.count)}
      >
        <span className="bar-label">
          <span>
            <i style={{ background: row.color }} />
            {t(row.name)}
          </span>
          <b>{row.count}</b>
        </span>
        <span className="metric-track">
          <i
            style={{
              width: `${denominator ? (row.count / denominator) * 100 : 0}%`,
              background: row.color,
            }}
          />
        </span>
      </button>
    ));
  const scopeMetrics = (kind: string, rows: MetricGroup[]) => (
    <div className="scope-metrics">
      <div className="scope-metric-labels">
        <span>{t(kind)}</span>
        <span>{t('Total')}</span>
        <span>{t('Open')}</span>
        <span>{t('Done')}</span>
        <span>{t('Late')}</span>
      </div>
      {rows.map((group) => {
        const countDone = group.tasks.filter(done).length;
        const late = group.tasks.filter((t) => !done(t) && !!t.due && t.due < today).length;
        return (
          <button
            className="scope-metric-row"
            key={group.label}
            onClick={() => setSelected(kind + ': ' + group.label)}
            aria-label={t(
              '{0} {1}: {2} total, {3} open, {4} completed, {5} overdue',
              kind,
              group.label,
              group.tasks.length,
              group.tasks.length - countDone,
              countDone,
              late,
            )}
          >
            <span>
              <i style={{ background: group.color }} />
              {t(group.label)}
            </span>
            <b>{group.tasks.length}</b>
            <span>{group.tasks.length - countDone}</span>
            <span>{countDone}</span>
            <span className={late ? 'overdue' : ''}>{late}</span>
            <i
              className="scope-progress"
              style={{
                width: `${tasks.length ? (group.tasks.length / tasks.length) * 100 : 0}%`,
                background: group.color,
              }}
            />
          </button>
        );
      })}
    </div>
  );
  return (
    <section className="dashboard" aria-label={t(title)}>
      <div className="dashboard-heading">
        <div>
          <span className="eyebrow">{t('THE BIG PICTURE')}</span>
          <h2>{t(title)}</h2>
          <p>{t('Every card counts. Here\u2019s where things stand.')}</p>
        </div>
        <button
          className="secondary dashboard-export"
          disabled={exporting}
          onClick={() => void exportPdf()}
        >
          <Download size={16} />
          {t(exporting ? 'Preparing PDF…' : 'Export PDF')}
        </button>
        <span className="dashboard-live">
          <i />
          {t('Live \u00B7 updates with your work')}
        </span>
      </div>
      <div className="dashboard-filters">
        <label>
          {t('Bucket')}
          <select
            aria-label={t('Dashboard bucket')}
            value={bucket}
            onChange={(e) => {
              setBucket(e.target.value);
              setSelected(null);
            }}
          >
            <option value="All">{t('All buckets')}</option>
            <option value="">{t('No bucket')}</option>
            {data.buckets.map((b) => (
              <option value={b.id} key={b.id}>
                {b.name}
              </option>
            ))}
          </select>
        </label>
        <label>
          {t('Swimlane')}
          <select
            aria-label={t('Dashboard swimlane')}
            value={lane}
            onChange={(e) => {
              setLane(e.target.value);
              setSelected(null);
            }}
          >
            <option value="All">{t('All swimlanes')}</option>
            <option value="">{t('No swimlane')}</option>
            {availableLanes.map((l) => (
              <option key={l.id} value={l.id}>
                {l.name}
                {projectId ? '' : ' · ' + projectName(l.project)}
              </option>
            ))}
          </select>
        </label>
        <span>
          {tasks.length}
          {' ' + t('of') + ' '}
          {allTasks.length}
          {' ' + t('cards')}
        </span>
        {(bucket !== 'All' || lane !== 'All') && (
          <button
            className="text-button"
            onClick={() => {
              setBucket('All');
              setLane('All');
              setSelected(null);
            }}
          >
            {t('Clear dashboard filters')}
          </button>
        )}
      </div>
      {exportError && <p role="alert">{exportError}</p>}
      <div className="kpi-grid">
        {metrics.map(({ label, value, icon: Icon, hint }) => (
          <button
            key={label}
            className={`kpi-card ${selected === label ? 'selected' : ''} ${label === 'Overdue' && value ? 'attention' : ''}`}
            onClick={() => setSelected(selected === label ? null : label)}
          >
            <span>
              <Icon size={17} />
              <ArrowUpRight size={14} />
            </span>
            <strong>{t(value)}</strong>
            <b>{t(label)}</b>
            <small>{t(hint)}</small>
          </button>
        ))}
      </div>
      {selected && (
        <section className="dashboard-drilldown">
          <header>
            <h3>
              {t(selected)} <span>{selectedTasks.length}</span>
            </h3>
            <button
              className="icon-button"
              aria-label={t('Close metric details')}
              onClick={() => setSelected(null)}
            >
              <X size={16} />
            </button>
          </header>
          {selectedTasks.length ? (
            selectedTasks.map((t) => (
              <button className="metric-task" key={t.id} onClick={() => onOpen(t)}>
                <span>
                  {t.title}
                  <small>{projectName(t.project)}</small>
                </span>
                <span>{t.status}</span>
                <ArrowUpRight size={15} />
              </button>
            ))
          ) : (
            <p>{t('No cards in this category. A little breathing room.')}</p>
          )}
        </section>
      )}
      <div className="dashboard-charts">
        <section className="dashboard-panel">
          <h3>
            {t('A little closer to done')}
            <span>{t('Completion')}</span>
          </h3>
          <div className="donut-layout">
            <button
              className="completion-donut"
              style={{
                background: `conic-gradient(#7494ba 0% ${completion}%, var(--chart-rest, #e8eef6) ${completion}% 100%)`,
              }}
              aria-label={t(
                'Completion chart: {0} completed out of {1} cards',
                completed.length,
                tasks.length,
              )}
              onClick={() => setSelected('Completed')}
            >
              <span>
                <strong>{completion}%</strong>
                <small>{t('completed')}</small>
              </span>
            </button>
            <div className="chart-legend">
              <button onClick={() => setSelected('Completed')}>
                <i />
                {completed.length}
                {' ' + t('completed')}
              </button>
              <button onClick={() => setSelected('Open cards')}>
                <i />
                {open.length}
                {' ' + t('open')}
              </button>
              <p>
                {t('Small steps.')}
                <br />
                {t('Visible progress.')}
              </p>
            </div>
          </div>
        </section>
        <section className="dashboard-panel">
          <h3>
            {t('The week ahead')}
            <span>{t('Cards by due date')}</span>
          </h3>
          <div className="due-chart" aria-label={t('Seven-day due date chart')}>
            {dueDays.map((day) => {
              const complete = day.tasks.filter(done).length;
              return (
                <button
                  key={day.key}
                  className="due-chart-day"
                  onClick={() => setSelected(day.label)}
                  aria-label={t(
                    '{0}: {1} cards, {2} completed',
                    day.label,
                    day.tasks.length,
                    complete,
                  )}
                  title={t('{0}: {1} cards', day.label, day.tasks.length)}
                >
                  <b>{day.tasks.length}</b>
                  <span className="due-chart-track">
                    <span
                      className="due-chart-bar"
                      style={{ height: `${(day.tasks.length / maxDue) * 100}%` }}
                    >
                      <i
                        style={{
                          height: `${day.tasks.length ? (complete / day.tasks.length) * 100 : 0}%`,
                        }}
                      />
                    </span>
                  </span>
                  <small>{day.date.toLocaleDateString(locale, { weekday: 'short' })}</small>
                  <em>{day.date.getDate()}</em>
                </button>
              );
            })}
          </div>
          <div className="due-legend">
            <span>
              <i />
              {t('Open')}
            </span>
            <span>
              <i />
              {t('Completed')}
            </span>
            <small>{t('Click a day to explore its cards')}</small>
          </div>
        </section>
      </div>
      <div className="dashboard-panels">
        <section className="dashboard-panel">
          <h3>
            {t('Workflow at a glance')}
            <span>
              {tasks.length}
              {' ' + t('cards')}
            </span>
          </h3>
          {bars(
            data.statuses.map((s) => ({
              name: s.name,
              color: s.color,
              count: tasks.filter((t) => t.status === s.name).length,
            })),
            tasks.length,
            true,
          )}
          {!tasks.length && (
            <p className="metric-note">{t('Create your first card to get things moving.')}</p>
          )}
        </section>
        <section className="dashboard-panel">
          <h3>
            {t('Team workload')}
            <span>
              {open.length}
              {' ' + t('open cards')}
            </span>
          </h3>
          {bars(
            data.members.map((m) => ({
              name: m.name,
              color: '#839fbe',
              count: open.filter((t) => t.assignees.includes(m.name)).length,
            })),
            Math.max(
              ...data.members.map((m) => open.filter((t) => t.assignees.includes(m.name)).length),
              1,
            ),
          )}
          <p className="metric-note">{t('Shared cards count once for each assigned teammate.')}</p>
        </section>
        <section className="dashboard-panel">
          <h3>
            {t('Bucket performance')}
            <span>
              {data.buckets.length}
              {' ' + t('buckets')}
            </span>
          </h3>
          {scopeMetrics('Bucket', bucketGroups)}
        </section>
        <section className="dashboard-panel">
          <h3>
            {t('Swimlane performance')}
            <span>
              {availableLanes.length}
              {' ' + t('swimlanes')}
            </span>
          </h3>
          {scopeMetrics('Swimlane', laneGroups)}
        </section>
        <section className="dashboard-panel">
          <h3>
            {t('The smaller steps')}
            <span>{t('Checklist progress')}</span>
          </h3>
          <div className="checklist-metric">
            <CheckCheck size={18} />
            <div>
              <b>
                {checklistDone} / {checklist.length}
              </b>
              <small>{t('Checklist items completed')}</small>
            </div>
            <strong>
              {checklist.length ? Math.round((checklistDone / checklist.length) * 100) : 0}%
            </strong>
          </div>
          <p className="metric-note">{t('Every checked item moves the work forward.')}</p>
        </section>
        <section className="dashboard-panel">
          <h3>
            {t('Latest movement')}
            <span>{t('Card history')}</span>
          </h3>
          {activity.map(({ task, entry }) => (
            <button className="dashboard-event" key={entry.id} onClick={() => onOpen(task)}>
              <i />
              <span>
                <b>{task.title}</b>
                <small>{t(entry.changes[0])}</small>
                <time>
                  {entry.actor} ·{' '}
                  {new Date(entry.at).toLocaleDateString(locale, {
                    month: 'short',
                    day: 'numeric',
                  })}
                </time>
              </span>
              <ArrowUpRight size={13} />
            </button>
          ))}
          {!activity.length && (
            <p className="metric-note">{t('Your card updates will appear here.')}</p>
          )}
        </section>
      </div>
      <p className="dashboard-footnote">
        {t(
          'Charts and metrics follow the dashboard\u2019s bucket and swimlane filters. Overdue means an unfinished card due before today. Workspace metrics exclude archived projects.',
        )}
      </p>
    </section>
  );
}
