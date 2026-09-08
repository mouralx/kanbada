import {
  Bell,
  CalendarDays,
  Columns3,
  Download,
  Folder,
  LayoutDashboard,
  List,
  MoreHorizontal,
  Plus,
  SlidersHorizontal,
  Trash2,
} from 'lucide-react';
import React from 'react';
import { type Project, type State, type Status, type Task } from '../../domain/models';
import { isActivitiesProject } from '../../domain/projectRules';

type BoardToolbarProps = {
  view: string;
  setView: React.Dispatch<React.SetStateAction<string>>;
  t: (value: unknown, ...values: unknown[]) => string;
  priority: string;
  person: string;
  bucketFilter: string;
  swimlaneFilter: string;
  statusFilter: string;
  completionFilter: string;
  setFilterOpen: React.Dispatch<React.SetStateAction<boolean>>;
  filterOpen: boolean;
  setPriority: React.Dispatch<React.SetStateAction<string>>;
  setPerson: React.Dispatch<React.SetStateAction<string>>;
  data: State;
  setBucketFilter: React.Dispatch<React.SetStateAction<string>>;
  projectTasks: Task[];
  setSwimlaneFilter: React.Dispatch<React.SetStateAction<string>>;
  lanes: { id: string; name: string; project: string; color: string; complete: boolean }[];
  inLane: (
    task: Task,
    lane: { id: string; name: string; project: string; color: string; complete: boolean },
  ) => boolean;
  setStatusFilter: React.Dispatch<React.SetStateAction<string>>;
  statuses: string[];
  setCompletionFilter: React.Dispatch<React.SetStateAction<string>>;
  setSearch: React.Dispatch<React.SetStateAction<string>>;
  newTask: (status?: Status, bucket?: string, swimlane?: string, targetProject?: string) => void;
  page: 'Projects' | 'My tasks';
  setMenu: React.Dispatch<React.SetStateAction<boolean>>;
  menu: boolean;
  project: Project;
  setToast: React.Dispatch<React.SetStateAction<string>>;
  setModal: React.Dispatch<React.SetStateAction<string | null>>;
  toggleArchive: (archived: boolean, targetProject?: Project) => Promise<void>;
};

export function BoardToolbar({
  view,
  setView,
  t,
  priority,
  person,
  bucketFilter,
  swimlaneFilter,
  statusFilter,
  completionFilter,
  setFilterOpen,
  filterOpen,
  setPriority,
  setPerson,
  data,
  setBucketFilter,
  projectTasks,
  setSwimlaneFilter,
  lanes,
  inLane,
  setStatusFilter,
  statuses,
  setCompletionFilter,
  setSearch,
  newTask,
  page,
  setMenu,
  menu,
  project,
  setToast,
  setModal,
  toggleArchive,
}: BoardToolbarProps) {
  return (
    <div className="board-toolbar">
      <div className="view-tabs">
        {[
          { name: 'Board', icon: Columns3 },
          { name: 'List', icon: List },
          { name: 'Calendar', icon: CalendarDays },
          { name: 'Dashboard', icon: LayoutDashboard },
        ].map(({ name, icon: Icon }) => (
          <button
            className={view === name ? 'active' : ''}
            key={name}
            onClick={() => setView(name)}
          >
            <Icon size={16} />
            {t(name)}
          </button>
        ))}
      </div>
      <div className="board-actions">
        <div className="filter-wrap">
          <button
            className={`filter-button ${priority !== 'All' || person !== 'All' || bucketFilter !== 'All' || swimlaneFilter !== 'All' || statusFilter !== 'All' || completionFilter !== 'All' ? 'selected' : ''}`}
            onClick={() => setFilterOpen(!filterOpen)}
          >
            <SlidersHorizontal size={15} />
            {t('Filter')}
            {(priority !== 'All' ||
              person !== 'All' ||
              bucketFilter !== 'All' ||
              swimlaneFilter !== 'All' ||
              statusFilter !== 'All' ||
              completionFilter !== 'All') && <span className="filter-dot" />}
          </button>
          {filterOpen && (
            <div className="popover">
              <h4>{t('Make space for focus')}</h4>
              <label>
                {t('Priority')}
                <select
                  aria-label={t('Priority')}
                  value={priority}
                  onChange={(e) => setPriority(e.target.value)}
                >
                  {['All', 'High', 'Medium', 'Low'].map((v) => (
                    <option key={v} value={v}>
                      {t(v)}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                {t('Assignee')}
                <select
                  aria-label={t('Assignee')}
                  value={person}
                  onChange={(e) => setPerson(e.target.value)}
                >
                  <option value={'All'}>{t('All')}</option>
                  {data.members.map((m) => (
                    <option key={m.name} value={m.name}>
                      {m.name}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                {t('Bucket')}
                <select
                  aria-label={t('Filter by bucket')}
                  value={bucketFilter}
                  onChange={(e) => setBucketFilter(e.target.value)}
                >
                  <option value="All">{t('All buckets')}</option>
                  <option value="">{t('No bucket')}</option>
                  {data.buckets.map((b) => (
                    <option value={b.name} key={b.id}>
                      {b.name} ({projectTasks.filter((t) => t.bucket === b.name).length})
                    </option>
                  ))}
                </select>
              </label>
              <label>
                {t('Swimlane')}
                <select
                  aria-label={t('Filter by swimlane')}
                  value={swimlaneFilter}
                  onChange={(e) => setSwimlaneFilter(e.target.value)}
                >
                  <option value="All">{t('All swimlanes')}</option>
                  <option value="">
                    {t('No swimlane (')}
                    {projectTasks.filter((t) => !t.swimlane).length})
                  </option>
                  {lanes
                    .filter((lane) => lane.id)
                    .map((lane) => (
                      <option value={lane.id} key={lane.id}>
                        {lane.name} ({projectTasks.filter((t) => inLane(t, lane)).length})
                      </option>
                    ))}
                </select>
              </label>
              <label>
                {t('Status')}
                <select
                  aria-label={t('Filter by status')}
                  value={statusFilter}
                  onChange={(e) => setStatusFilter(e.target.value)}
                >
                  <option value={'All'}>{t('All')}</option>
                  {statuses.map((status) => (
                    <option key={status} value={status}>
                      {t(status)}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                {t('Card metrics')}
                <select
                  aria-label={t('Filter by card metrics')}
                  value={completionFilter}
                  onChange={(e) => setCompletionFilter(e.target.value)}
                >
                  {['All', 'Open', 'Completed', 'Overdue'].map((value) => (
                    <option key={value} value={value}>
                      {t(value)}
                    </option>
                  ))}
                </select>
              </label>
              <button
                className="text-button"
                onClick={() => {
                  setBucketFilter('All');
                  setSwimlaneFilter('All');
                  setStatusFilter('All');
                  setCompletionFilter('All');
                  setPriority('All');
                  setPerson('All');
                  setSearch('');
                }}
              >
                {t('Clear filters')}
              </button>
            </div>
          )}
        </div>
        <span className="toolbar-divider" />
        <button className="primary" onClick={() => newTask()}>
          <Plus size={16} />
          {t('Add task')}
        </button>
        {page === 'Projects' && (
          <div className="filter-wrap">
            <button
              aria-label={t('Project options')}
              className="icon-button"
              onClick={() => setMenu(!menu)}
            >
              <MoreHorizontal size={20} />
            </button>
            {menu && (
              <div className="popover options">
                <button
                  onClick={() => {
                    const blob = new Blob(
                      [JSON.stringify({ project, tasks: projectTasks }, null, 2)],
                      { type: 'application/json' },
                    );
                    const url = URL.createObjectURL(blob);
                    const a = document.createElement('a');
                    a.href = url;
                    a.download = project.id + '.json';
                    a.click();
                    URL.revokeObjectURL(url);
                    setMenu(false);
                    setToast('Project exported');
                  }}
                >
                  <Download size={15} />
                  {t('Export project')}
                </button>
                <button
                  onClick={() => {
                    setMenu(false);
                    setModal('Activity');
                  }}
                >
                  <Bell size={15} />
                  {t('View activity')}
                </button>
                {project.id && !isActivitiesProject(project) && (
                  <>
                    <button
                      onClick={() => {
                        setMenu(false);
                        if (project.archived) void toggleArchive(false);
                        else setModal('Archive project');
                      }}
                    >
                      <Folder size={15} />
                      {project.archived ? t('Restore project') : t('Archive project')}
                    </button>
                    <button
                      className="danger"
                      onClick={() => {
                        setMenu(false);
                        setModal('Delete project');
                      }}
                    >
                      <Trash2 size={15} />
                      {t('Delete project')}
                    </button>
                  </>
                )}
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
