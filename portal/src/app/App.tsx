import {
  ArrowRight,
  ArrowUpRight,
  Bell,
  Check,
  ChevronDown,
  ChevronRight,
  Columns3,
  Folder,
  Menu,
  Plus,
  Search,
  SlidersHorizontal,
  Tag,
  Trash2,
  Users,
  X,
} from 'lucide-react';
import React, { useCallback, useEffect, useState } from 'react';
import { recordChanges } from '../domain/cardHistory';
import {
  type Definition,
  type Project,
  type State,
  type Status,
  type Task,
} from '../domain/models';
import { isActivitiesProject } from '../domain/projectRules';
import { BoardToolbar } from '../features/board/BoardToolbar';
import { CalendarView } from '../features/board/CalendarView';
import { TaskList } from '../features/board/TaskList';
import { BoardCard } from '../features/cards/BoardCard';
import { CardDrawer } from '../features/cards/CardDrawer';
import { Dashboard } from '../features/dashboard/Dashboard';
import { HelpCenter } from '../features/help/HelpCenter';
import { ProjectDirectory } from '../features/projects/ProjectDirectory';
import { currentStorageAccount } from '../infrastructure/accountStorage';
import { apiEnabled } from '../infrastructure/apiClient';
import { fileRepository } from '../infrastructure/attachments';
import { repository } from '../infrastructure/workspaceRepository';
import { useI18n, type Locale } from '../shared/i18n';
import { ThemeSelect } from '../shared/Theme';
import { useWorkspaceLocation } from './hooks/useWorkspaceLocation';
import { useWorkspaceSave } from './hooks/useWorkspaceSave';
import { Sidebar } from './Sidebar';
import { WorkspaceDialogs } from './WorkspaceDialogs';
export function App() {
  const { t, locale, setLocale } = useI18n();
  const [projectId, setProjectId] = useState('website');
  const [page, setPage] = useState('Projects');
  const [view, setView] = useState('Board');
  const [search, setSearch] = useState('');
  const [priority, setPriority] = useState('All');
  const [person, setPerson] = useState('All');
  const [filterOpen, setFilterOpen] = useState(false);
  const [modal, setModal] = useState<string | null>(null);
  const [draft, setDraft] = useState<Task | null>(null);
  const [toast, setToast] = useState('');
  const [menu, setMenu] = useState(false);
  const [sidebar, setSidebar] = useState(false);
  const [month, setMonth] = useState(() => new Date().getMonth());
  const [year, setYear] = useState(() => new Date().getFullYear());
  const [bucketFilter, setBucketFilter] = useState('All');
  const [groupBy, setGroupBy] = useState('Status');
  const [uploading, setUploading] = useState(false);
  const [collapsedLanes, setCollapsedLanes] = useState<Set<string>>(new Set());
  const [showArchived, setShowArchived] = useState(false);
  const [swimlaneFilter, setSwimlaneFilter] = useState('All');
  const [statusFilter, setStatusFilter] = useState('All');
  const [completionFilter, setCompletionFilter] = useState('All');
  const [removingMember, setRemovingMember] = useState<string | null>(null);
  const [sidebarCollapsed, setSidebarCollapsed] = useState(() => {
    try {
      return localStorage.getItem('kanbada-sidebar-collapsed') === 'true';
    } catch {
      return false;
    }
  });
  useEffect(() => setCollapsedLanes(new Set()), [projectId, page]);
  const { data, setData, loadError } = useWorkspaceLocation({
    setProjectId,
    setPage,
    setDraft,
    setToast,
  });
  const { saving, commit } = useWorkspaceSave(data, setData, setToast);
  const closeDraft = useCallback(() => {
    if (uploading) return;
    for (const file of draft?.attachments ?? [])
      if (!data?.tasks.some((t) => t.attachments?.some((a) => a.id === file.id)))
        void fileRepository.remove(file.id).catch(() => {});
    setDraft(null);
  }, [data, draft, uploading]);
  useEffect(() => {
    setUploading(false);
  }, [draft?.id]);
  useEffect(() => {
    if (!apiEnabled || !data || draft || modal || saving) return;
    let cancelled = false;
    const timer = setInterval(() => {
      repository
        .load()
        .then((next) => {
          if (
            !cancelled &&
            next.workspace.id === data.workspace.id &&
            next.version !== data.version
          )
            setData(next);
        })
        .catch(() => {});
    }, 15000);
    return () => {
      cancelled = true;
      clearInterval(timer);
    };
  }, [data, draft, modal, saving, setData]);
  useEffect(() => {
    if (!data) return;
    const url = new URL(window.location.href);
    const id = draft && data.tasks.some((task) => task.id === draft.id) ? draft.id : null;
    if (id) {
      if (
        url.searchParams.get('card') === id &&
        url.searchParams.get('workspace') === data.workspace.id
      )
        return;
      url.searchParams.set('card', id);
      url.searchParams.set('workspace', data.workspace.id);
    } else {
      if (!url.searchParams.has('card')) return;
      url.searchParams.delete('card');
    }
    window.history.pushState(null, '', url);
  }, [draft, data]);
  useEffect(() => {
    if (!draft && !modal) return;
    const previous = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.body.style.overflow = previous;
    };
  }, [draft, modal]);
  useEffect(() => {
    if (toast) {
      const t = setTimeout(() => setToast(''), 3200);
      return () => clearTimeout(t);
    }
  }, [toast]);
  useEffect(() => {
    function key(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        if (modal) setModal(null);
        else closeDraft();
        setMenu(false);
      }
      if ((e.metaKey || e.ctrlKey) && e.key === 'k') {
        e.preventDefault();
        document.querySelector<HTMLInputElement>('.search input')?.focus();
      }
    }
    window.addEventListener('keydown', key);
    return () => window.removeEventListener('keydown', key);
  }, [closeDraft, modal]);
  if (!data && loadError)
    return (
      <div className="loading">
        <p role="alert">{t(loadError)}</p>
        <button onClick={() => window.location.reload()}>{t('Retry')}</button>
      </div>
    );
  if (!data)
    return (
      <div className="loading">
        {t('kanbada')}
        <span>{t('Making room for great work\u2026')}</span>
      </div>
    );
  const currentMember =
    data.members.find((member) => member.userId === currentStorageAccount()?.id) ?? data.members[0];
  const project = data.projects.find((p) => p.id === projectId) ||
    data.projects.find((p) => !p.archived) ||
    data.projects[0] || {
      id: '',
      name: 'Your next project',
      description: 'Create a project to make room for your next idea.',
      color: '#a6bddb',
    };
  const activeTasks = data.tasks.filter((task) =>
    data.projects.some((p) => p.id === task.project && !p.archived),
  );
  const statuses = data.statuses.map((s) => s.name);
  const isDone = (task: Task) => !!data.statuses.find((s) => s.name === task.status)?.complete;
  const lanes = [
    { id: '', name: '', project: '', color: '#879eb9', complete: false },
    ...data.swimlanes.filter((lane) => page === 'My tasks' || lane.project === project.id),
  ];
  const inLane = (task: Task, lane: (typeof lanes)[number]) =>
    (task.swimlane ?? '') === lane.name && (!lane.project || task.project === lane.project);
  const groups = groupBy === 'Status' ? statuses : ['', ...data.buckets.map((b) => b.name)];
  const inGroup = (task: Task, value: string) =>
    groupBy === 'Status' ? task.status === value : (task.bucket ?? '') === value;
  const groupName = (value: string) => value || 'No bucket';
  const updateTask = async (task: Task) => {
    if (uploading || saving) return;
    const previous = data.tasks.find((t) => t.id === task.id);
    const updated = recordChanges(previous, task, currentMember.name);
    const next = {
      ...data,
      tasks: previous
        ? data.tasks.map((t) => (t.id === task.id ? updated : t))
        : [...data.tasks, updated],
      activity: [`${task.title} · ${task.status}`, ...data.activity].slice(0, 30),
    };
    if (await commit(next, 'Task saved')) {
      setDraft(null);
      for (const file of previous?.attachments ?? [])
        if (!next.tasks.some((t) => t.attachments?.some((a) => a.id === file.id)))
          void fileRepository.remove(file.id).catch(() => {});
    }
  };
  const saveDefinitions = async (kind: 'statuses' | 'buckets', items: Definition[]) => {
    const next = {
      ...data,
      [kind]: items,
      tasks: data.tasks.map((task) => {
        const field = kind === 'statuses' ? 'status' : 'bucket';
        const old = data[kind].find((d) => d.name === task[field]);
        const replacement = items.find((d) => d.id === old?.id);
        if (!replacement) return task;
        const updated = recordChanges(
          task,
          { ...task, [field]: replacement.name },
          currentMember.name,
        );
        if (kind === 'statuses' && old?.complete !== replacement.complete)
          updated.history = [
            ...(updated.history ?? []),
            {
              id: crypto.randomUUID(),
              at: new Date().toISOString(),
              actor: currentMember.name,
              changes: [
                replacement.complete
                  ? 'Status now counts as completed'
                  : 'Status no longer counts as completed',
              ],
            },
          ];
        return updated;
      }),
    };
    if (await commit(next, kind === 'statuses' ? 'Statuses updated' : 'Buckets updated')) {
      setBucketFilter('All');
      setSwimlaneFilter('All');
      setStatusFilter('All');
      setCompletionFilter('All');
      setModal(null);
    }
  };
  const saveLabels = async (items: Definition[]) => {
    const rename = (names: string[]) =>
      names.map((name) => {
        const old = data.labels.find((label) => label.name === name);
        return items.find((label) => label.id === old?.id)?.name ?? name;
      });
    const next = {
      ...data,
      labels: items,
      tasks: data.tasks.map((task) =>
        recordChanges(task, { ...task, labels: rename(task.labels) }, currentMember.name),
      ),
    };
    if (await commit(next, 'Labels updated')) {
      setDraft((current) =>
        current
          ? {
              ...current,
              labels: rename(current.labels),
              history:
                next.tasks.find((task) => task.id === current.id)?.history ?? current.history,
            }
          : current,
      );
      setModal(null);
    }
  };
  const saveSwimlanes = async (items: Definition[]) => {
    const previous = data.swimlanes.filter((lane) => lane.project === project.id);
    const next = {
      ...data,
      swimlanes: [
        ...data.swimlanes.filter((lane) => lane.project !== project.id),
        ...items.map((lane) => ({ ...lane, project: project.id })),
      ],
      tasks: data.tasks.map((task) => {
        if (task.project !== project.id) return task;
        const old = previous.find((lane) => lane.name === task.swimlane);
        const replacement = items.find((lane) => lane.id === old?.id);
        return replacement && replacement.name !== task.swimlane
          ? recordChanges(task, { ...task, swimlane: replacement.name }, currentMember.name)
          : task;
      }),
    };
    if (await commit(next, 'Swimlanes updated')) setModal(null);
  };
  const openWorkspace = (loaded: State) => {
    const url = new URL(window.location.href);
    url.searchParams.delete('card');
    url.searchParams.delete('workspace');
    window.history.replaceState(null, '', url);
    setData(loaded);
    setProjectId(loaded.projects.find((p) => !p.archived)?.id ?? loaded.projects[0]?.id ?? '');
    setModal(null);
    setMenu(false);
    setShowArchived(false);
    setFilterOpen(false);
    setGroupBy('Status');
    setView('Board');
    navigate('Overview');
    setToast('Workspace ready');
  };
  const toggleArchive = async (archived: boolean, targetProject: Project = project) => {
    if (isActivitiesProject(targetProject)) {
      setToast('My activities cannot be archived, deleted, or renamed.');
      return;
    }
    const next = {
      ...data,
      projects: data.projects.map((p) => (p.id === targetProject.id ? { ...p, archived } : p)),
      activity: [
        `${archived ? 'Archived' : 'Restored'} project “${targetProject.name}”`,
        ...data.activity,
      ],
    };
    if (await commit(next, archived ? 'Project archived' : 'Project restored')) {
      setModal(null);
      if (archived) {
        setShowArchived(true);
        navigate('Project directory');
      }
    }
  };
  const deleteProject = async () => {
    if (isActivitiesProject(project)) {
      setToast('My activities cannot be archived, deleted, or renamed.');
      return;
    }
    const removed = data.tasks.filter((t) => t.project === project.id);
    const next = {
      ...data,
      projects: data.projects.filter((p) => p.id !== project.id),
      tasks: data.tasks.filter((t) => t.project !== project.id),
      swimlanes: data.swimlanes.filter((lane) => lane.project !== project.id),
      activity: [
        `Deleted project “${isActivitiesProject(project) ? t('My activities') : project.name}”`,
        ...data.activity,
      ],
    };
    if (await commit(next, 'Project deleted')) {
      setModal(null);
      setProjectId(next.projects.find((p) => !p.archived)?.id ?? next.projects[0]?.id ?? '');
      setShowArchived(false);
      navigate('Overview');
      for (const file of removed.flatMap((t) => t.attachments ?? []))
        if (!next.tasks.some((t) => t.attachments?.some((a) => a.id === file.id)))
          void fileRepository.remove(file.id).catch(() => {});
    }
  };
  const newTask = (
    status: Status = statuses[0],
    bucket = '',
    swimlane = '',
    targetProject = page === 'My tasks'
      ? (data.projects.find(isActivitiesProject)?.id ?? project.id)
      : project.id,
  ) => {
    if (!targetProject) {
      setModal('New project');
      return;
    }
    setDraft({
      id: `KB-${crypto.randomUUID().slice(0, 8).toUpperCase()}`,
      bucket,
      swimlane,
      project: targetProject,
      title: '',
      description: '',
      status,
      priority: 'Medium',
      labels: [],
      assignees: [currentMember.name],
      due: '',
      comments: [],
      checklist: [],
    });
  };
  const projectTasks = (page === 'My tasks' ? activeTasks : data.tasks).filter((t) =>
    page === 'My tasks' ? t.assignees.includes(currentMember.name) : t.project === project.id,
  );
  const currentDate = new Date();
  const today = `${currentDate.getFullYear()}-${String(currentDate.getMonth() + 1).padStart(2, '0')}-${String(currentDate.getDate()).padStart(2, '0')}`;
  const selectedLane = data.swimlanes.find((lane) => lane.id === swimlaneFilter);
  const filtered = projectTasks.filter(
    (t) =>
      (t.title + ' ' + t.labels.join(' ') + ' ' + t.id)
        .toLowerCase()
        .includes(search.toLowerCase()) &&
      (priority === 'All' || t.priority === priority) &&
      (person === 'All' || t.assignees.includes(person)) &&
      (bucketFilter === 'All' || (t.bucket ?? '') === bucketFilter) &&
      (statusFilter === 'All' || t.status === statusFilter) &&
      (swimlaneFilter === 'All' ||
        (swimlaneFilter === ''
          ? !t.swimlane
          : t.swimlane === selectedLane?.name && t.project === selectedLane?.project)) &&
      (completionFilter === 'All' ||
        (completionFilter === 'Completed'
          ? isDone(t)
          : completionFilter === 'Open'
            ? !isDone(t)
            : !isDone(t) && !!t.due && t.due < today)),
  );
  const completed = projectTasks.filter((t) => isDone(t)).length;
  const progress = projectTasks.length ? Math.round((completed / projectTasks.length) * 100) : 0;
  const avatar = (name: string, small = false) => {
    const m = data.members.find((m) => m.name === name);
    return (
      <span
        title={t(name)}
        className={`avatar ${small ? 'small' : ''}`}
        style={{ background: m?.color || '#bfccdd' }}
      >
        {m?.initials || name.slice(0, 2).toUpperCase()}
        {m?.photo && (
          <img
            src={m.photo}
            alt={t('{0} profile', name)}
            onError={(e) => {
              e.currentTarget.style.display = 'none';
            }}
          />
        )}
      </span>
    );
  };
  const navigate = (name: string) => {
    setPage(name);
    setSidebar(false);
    setSearch('');
    setPerson('All');
    setPriority('All');
    setBucketFilter('All');
    setSwimlaneFilter('All');
    setStatusFilter('All');
    setCompletionFilter('All');
    setFilterOpen(false);
    setMenu(false);
    if (name === 'My tasks') setView('List');
  };
  const move = (id: string, value: string, lane: (typeof lanes)[number]) => {
    const task = data.tasks.find((t) => t.id === id);
    if (!task) return;
    if (lane.project && task.project !== lane.project) {
      setToast('Choose a swimlane in this card’s project');
      return;
    }
    if (!inGroup(task, value) || !inLane(task, lane)) {
      const updated = recordChanges(
        task,
        {
          ...task,
          swimlane: lane.name,
          ...(groupBy === 'Status' ? { status: value } : { bucket: value }),
        },
        currentMember.name,
      );
      void commit(
        {
          ...data,
          tasks: data.tasks.map((t) => (t.id === id ? updated : t)),
          activity: [
            `Moved “${task.title}” to ${groupName(value)}${lane.name ? ' / ' + lane.name : ''}`,
            ...data.activity,
          ],
        },
        `Moved to ${groupName(value)}${lane.name ? ' / ' + lane.name : ''}`,
      );
    }
  };
  const card = (task: Task) => (
    <BoardCard
      key={task.id}
      task={task}
      isDone={isDone}
      setDraft={setDraft}
      t={t}
      data={data}
      locale={locale}
      avatar={avatar}
    />
  );
  return (
    <div className={`app ${sidebarCollapsed ? 'sidebar-collapsed' : ''}`}>
      {sidebar && (
        <button
          className="sidebar-scrim"
          aria-label={t('Close navigation')}
          onClick={() => setSidebar(false)}
        />
      )}
      <Sidebar
        sidebar={sidebar}
        navigate={navigate}
        t={t}
        sidebarCollapsed={sidebarCollapsed}
        setSidebarCollapsed={setSidebarCollapsed}
        data={data}
        setModal={setModal}
        page={page}
        activeTasks={activeTasks}
        currentMember={currentMember}
        isDone={isDone}
        project={project}
        setProjectId={setProjectId}
        avatar={avatar}
      />
      <main>
        <header className="topbar">
          <div className="breadcrumbs">
            <button
              className="mobile-menu icon-button"
              aria-label={t('Toggle navigation')}
              onClick={() => setSidebar(!sidebar)}
            >
              <Menu size={20} />
            </button>
            <span>{t('Workspace')}</span>
            <ChevronRight size={14} />
            <span>{t(page === 'Project directory' ? 'Projects' : page)}</span>
            {page === 'Projects' && (
              <>
                <ChevronRight size={14} />
                <strong>{isActivitiesProject(project) ? t('My activities') : project.name}</strong>
              </>
            )}
          </div>
          <div className="top-actions">
            <ThemeSelect compact />
            <select
              className="language-switch"
              aria-label={t('Interface language')}
              value={locale}
              onChange={(e) => setLocale(e.target.value as Locale)}
            >
              <option value="en-US">EN</option>
              <option value="pt-PT">PT</option>
            </select>
            <label className="search">
              <Search size={15} />
              <input
                aria-label={t('Search tasks')}
                placeholder={t('Search anything\u2026')}
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
              <kbd>{t('\u2318 K')}</kbd>
            </label>
            <button
              className="notification icon-button"
              aria-label={t('Notifications')}
              onClick={() => setModal('Notifications')}
            >
              <Bell size={19} />
              {data.notifications.length > 0 && <i className="unread-indicator" />}
            </button>
            <button
              className="avatar-button"
              aria-label={t('Your profile')}
              onClick={() => setModal('Profile')}
            >
              {avatar(currentMember.name, true)}
            </button>
          </div>
        </header>
        <div className="main-content">
          <div className="project-heading">
            <div>
              <div className="eyebrow">
                <span />
                {' ' + t('A LITTLE STRUCTURE. A LOT OF POSSIBILITY.')}
              </div>
              <h1>
                {page === 'Projects'
                  ? isActivitiesProject(project)
                    ? t('My activities')
                    : project.name
                  : page === 'Project directory'
                    ? t('Projects')
                    : page === 'Overview'
                      ? t('A good day to make progress.')
                      : page === 'Members'
                        ? t('Your workspace members.')
                        : page === 'Help'
                          ? t('Help')
                          : t('My tasks')}
                <span className="title-dot">.</span>
              </h1>
              <p>
                {page === 'Projects'
                  ? isActivitiesProject(project)
                    ? t(project.description)
                    : project.description
                  : page === 'Project directory'
                    ? t(
                        'Every project, in one place. Manage what\u2019s live and what\u2019s saved for later.',
                      )
                    : page === 'Members'
                      ? t('The people who make this workspace yours.')
                      : page === 'My tasks'
                        ? t('Your assigned cards across all active projects in this workspace.')
                        : t('Here\u2019s what\u2019s moving across your workspace.')}
              </p>
            </div>
            <button className="invite-button" onClick={() => setModal('Invite members')}>
              <Users size={16} />
              {t('Invite members')}
              <Plus size={14} />
            </button>
          </div>
          {page === 'Projects' && project.archived && (
            <div className="archive-banner">
              <Folder size={16} />
              <span>{t('This project is archived. Its cards and documents are preserved.')}</span>
              <button onClick={() => void toggleArchive(false)}>
                {t('Restore project')}
                <ArrowRight size={14} />
              </button>
            </div>
          )}
          {(page === 'Projects' || page === 'My tasks') && (
            <>
              {page === 'Projects' && (
                <section
                  className={`project-banner ${data.workspace.banner ? 'has-workspace-banner' : ''}`}
                >
                  {data.workspace.banner && (
                    <img
                      className="workspace-banner-image"
                      src={data.workspace.banner}
                      alt={t('Workspace banner')}
                      style={{ objectPosition: `center ${data.workspace.bannerPosition ?? 50}%` }}
                    />
                  )}
                  <div className="banner-text">
                    <span className="banner-eyebrow">{t('THE NEXT CHAPTER')}</span>
                    <h2>
                      {t('Good ideas.')}
                      <br />
                      {t('Great things ahead.')}
                    </h2>
                    <p>{t('A shared space to turn what if into what\u2019s next.')}</p>
                    <div className="banner-bottom">
                      <div className="avatar-stack">
                        {data.members.slice(0, 4).map((m) => (
                          <React.Fragment key={m.name}>{avatar(m.name, true)}</React.Fragment>
                        ))}
                      </div>
                      <span>
                        {data.members.length}
                        {' ' + t('creative minds, one direction')}
                      </span>
                    </div>
                  </div>
                  <div className="banner-art" aria-hidden="true">
                    <div className="orbit orbit-one" />
                    <div className="orbit orbit-two" />
                    <div className="art-star">✳</div>
                    <div className="art-dot" />
                    <div className="art-ring" />
                    <span className="art-caption">
                      {t('MAKE ROOM FOR')}
                      <br />
                      {t('SOMETHING GREAT \u2197')}
                    </span>
                  </div>
                  <div className="banner-progress">
                    <span>
                      {t('PROJECT PROGRESS')}
                      <ArrowUpRight size={16} />
                    </span>
                    <div>
                      <strong>{progress}%</strong>
                      <span>{t('keep it going \u2197')}</span>
                    </div>
                    <div className="progress-track">
                      <i style={{ width: `${progress}%` }} />
                    </div>
                    <p>
                      <b>{completed}</b>
                      {' ' + t('of') + ' '}
                      {projectTasks.length}
                      {' ' + t('tasks completed')}
                    </p>
                  </div>
                </section>
              )}
              <BoardToolbar
                view={view}
                setView={setView}
                t={t}
                priority={priority}
                person={person}
                bucketFilter={bucketFilter}
                swimlaneFilter={swimlaneFilter}
                statusFilter={statusFilter}
                completionFilter={completionFilter}
                setFilterOpen={setFilterOpen}
                filterOpen={filterOpen}
                setPriority={setPriority}
                setPerson={setPerson}
                data={data}
                setBucketFilter={setBucketFilter}
                projectTasks={projectTasks}
                setSwimlaneFilter={setSwimlaneFilter}
                lanes={lanes}
                inLane={inLane}
                setStatusFilter={setStatusFilter}
                statuses={statuses}
                setCompletionFilter={setCompletionFilter}
                setSearch={setSearch}
                newTask={newTask}
                page={page}
                setMenu={setMenu}
                menu={menu}
                project={project}
                setToast={setToast}
                setModal={setModal}
                toggleArchive={toggleArchive}
              />
              {page === 'Projects' && (
                <div className="workflow-toolbar">
                  <label>
                    {t('Group by')}
                    <select
                      aria-label={t('Group board by')}
                      value={groupBy}
                      onChange={(e) => setGroupBy(e.target.value)}
                    >
                      <option value={'Status'}>{t('Status')}</option>
                      <option value={'Bucket'}>{t('Bucket')}</option>
                    </select>
                  </label>
                  <div>
                    <button onClick={() => setModal('Manage swimlanes')}>
                      <Columns3 size={14} />
                      {t('Manage swimlanes')}
                    </button>
                    <button onClick={() => setModal('Manage buckets')}>
                      <Folder size={14} />
                      {t('Manage buckets')}
                    </button>
                    <button onClick={() => setModal('Manage labels')}>
                      <Tag size={14} />
                      {t('Manage labels')}
                    </button>
                    <button onClick={() => setModal('Manage statuses')}>
                      <SlidersHorizontal size={14} />
                      {t('Manage statuses')}
                    </button>
                  </div>
                </div>
              )}
              <div className="board-caption">
                <span>
                  <span className="live-dot" />
                  {saving ? t('Saving changes\u2026') : t('Everything\u2019s up to date')}
                </span>
                <span>
                  {filtered.length}
                  {' ' + t('tasks') + ' '}
                  <span className="caption-dot">·</span>
                  {' ' + t('September 2026')}
                </span>
              </div>
              {view === 'Dashboard' ? (
                <Dashboard
                  key={page + project.id}
                  data={data}
                  tasks={projectTasks}
                  projectId={page === 'Projects' ? project.id : undefined}
                  title={page === 'My tasks' ? t('My tasks dashboard') : t('Project dashboard')}
                  onOpen={(task) => setDraft(structuredClone(task))}
                />
              ) : view === 'Board' ? (
                <div className="swimlanes">
                  {lanes.map((lane) => (
                    <section
                      className="swimlane"
                      key={lane.id}
                      aria-label={lane.name || 'No swimlane'}
                    >
                      <button
                        className="swimlane-heading"
                        aria-expanded={!collapsedLanes.has(lane.id)}
                        aria-controls={'swimlane-' + (lane.id || 'default')}
                        onClick={() =>
                          setCollapsedLanes((previous) => {
                            const next = new Set(previous);
                            if (next.has(lane.id)) next.delete(lane.id);
                            else next.add(lane.id);
                            return next;
                          })
                        }
                      >
                        <ChevronDown
                          size={16}
                          className={collapsedLanes.has(lane.id) ? 'collapsed' : ''}
                        />
                        <span className="lane-color" style={{ background: lane.color }} />
                        <strong>{lane.name || 'No swimlane'}</strong>
                        <span className="lane-count">
                          {filtered.filter((t) => inLane(t, lane)).length}
                        </span>
                        {page === 'My tasks' && lane.project && (
                          <small>{data.projects.find((p) => p.id === lane.project)?.name}</small>
                        )}
                        <small className="lane-hint">
                          {collapsedLanes.has(lane.id) ? t('Expand') : t('Collapse')}
                        </small>
                      </button>
                      {!collapsedLanes.has(lane.id) && (
                        <div
                          id={'swimlane-' + (lane.id || 'default')}
                          className="kanban-board"
                          style={{
                            gridTemplateColumns: `repeat(${groups.length}, minmax(230px, 1fr))`,
                          }}
                        >
                          {groups.map((status, i) => (
                            <section
                              className={`kanban-column column-${i}`}
                              key={status}
                              onDragOver={(e) => {
                                e.preventDefault();
                                e.dataTransfer.dropEffect = 'move';
                                e.currentTarget.classList.add('drag-over');
                              }}
                              onDragLeave={(e) => e.currentTarget.classList.remove('drag-over')}
                              onDrop={(e) => {
                                e.preventDefault();
                                e.currentTarget.classList.remove('drag-over');
                                move(e.dataTransfer.getData('text/plain'), status, lane);
                              }}
                            >
                              <div className="column-heading">
                                <span
                                  className="status-icon"
                                  style={{
                                    borderColor: (groupBy === 'Status'
                                      ? data.statuses
                                      : data.buckets
                                    ).find((d) => d.name === status)?.color,
                                    background: (groupBy === 'Status'
                                      ? data.statuses
                                      : data.buckets
                                    ).find((d) => d.name === status)?.color,
                                  }}
                                >
                                  {groupBy === 'Status' &&
                                  data.statuses.find((d) => d.name === status)?.complete ? (
                                    <Check size={11} />
                                  ) : null}
                                </span>
                                <h2>{t(groupName(status))}</h2>
                                <span className="column-count">
                                  {
                                    filtered.filter((t) => inGroup(t, status) && inLane(t, lane))
                                      .length
                                  }
                                </span>
                                <button
                                  aria-label={t('Add task to {0}', status)}
                                  onClick={() =>
                                    newTask(
                                      groupBy === 'Status' ? status : statuses[0],
                                      groupBy === 'Bucket' ? status : '',
                                      lane.name,
                                      lane.project || project.id,
                                    )
                                  }
                                >
                                  <Plus size={17} />
                                </button>
                              </div>
                              <div className="column-cards">
                                {filtered
                                  .filter((t) => inGroup(t, status) && inLane(t, lane))
                                  .map(card)}
                              </div>
                              <button
                                className="add-card"
                                onClick={() =>
                                  newTask(
                                    groupBy === 'Status' ? status : statuses[0],
                                    groupBy === 'Bucket' ? status : '',
                                    lane.name,
                                    lane.project || project.id,
                                  )
                                }
                              >
                                <Plus size={15} />
                                {t('Add task')}
                              </button>
                            </section>
                          ))}
                        </div>
                      )}
                    </section>
                  ))}
                </div>
              ) : view === 'List' ? (
                <TaskList
                  key={page}
                  personal={page === 'My tasks'}
                  tasks={filtered}
                  data={data}
                  onOpen={(task) => setDraft(structuredClone(task))}
                  avatar={avatar}
                />
              ) : (
                <CalendarView
                  year={year}
                  month={month}
                  locale={locale}
                  t={t}
                  setMonth={setMonth}
                  setYear={setYear}
                  filtered={filtered}
                  setDraft={setDraft}
                  data={data}
                />
              )}
            </>
          )}
          {page === 'Help' && <HelpCenter />}
          {page === 'Overview' && (
            <>
              {data.workspace.banner && (
                <div className="workspace-overview-cover">
                  <img
                    src={data.workspace.banner}
                    alt={t('Workspace banner')}
                    style={{ objectPosition: `center ${data.workspace.bannerPosition ?? 50}%` }}
                  />
                  <button className="secondary" onClick={() => setModal('Workspace appearance')}>
                    {t('Change banner')}
                  </button>
                </div>
              )}
              <Dashboard
                data={data}
                tasks={activeTasks}
                title={t('Workspace dashboard')}
                onOpen={(task) => setDraft(structuredClone(task))}
              />
              <div className="project-section-heading">
                <h2 className="section-title">
                  {showArchived
                    ? t('Saved for another day.')
                    : t('A little closer to what\u2019s next')}
                </h2>
                <div className="project-scope-tabs">
                  <button
                    className={!showArchived ? 'active' : ''}
                    onClick={() => setShowArchived(false)}
                  >
                    {t('Active projects (')}
                    {data.projects.filter((p) => !p.archived).length})
                  </button>
                  <button
                    className={showArchived ? 'active' : ''}
                    onClick={() => setShowArchived(true)}
                  >
                    {t('Archived (')}
                    {data.projects.filter((p) => p.archived).length})
                  </button>
                </div>
              </div>
              <div className="project-grid">
                {data.projects
                  .filter((p) => !!p.archived === showArchived)
                  .map((p) => {
                    const ts = data.tasks.filter((t) => t.project === p.id);
                    const n = ts.filter((t) => isDone(t)).length;
                    return (
                      <button
                        key={p.id}
                        className="project-tile"
                        onClick={() => {
                          setProjectId(p.id);
                          navigate('Projects');
                        }}
                      >
                        <span className="project-tile-art" style={{ background: p.color }}>
                          ✳<ArrowUpRight size={24} />
                        </span>
                        <h3>{isActivitiesProject(p) ? t('My activities') : p.name}</h3>
                        <p>{p.description}</p>
                        <div className="progress-track">
                          <i style={{ width: `${ts.length ? (n / ts.length) * 100 : 0}%` }} />
                        </div>
                        <small>
                          {n}
                          {' ' + t('of') + ' '}
                          {ts.length}
                          {' ' + t('tasks completed')}
                          {p.archived ? t(' \u00B7 Archived') : ''}
                        </small>
                      </button>
                    );
                  })}
                <button className="project-tile new-tile" onClick={() => setModal('New project')}>
                  <Plus size={28} />
                  <h3>{t('Room for your next idea')}</h3>
                  <span>{t('Create a project')}</span>
                </button>
              </div>
            </>
          )}
          {page === 'Project directory' && (
            <ProjectDirectory
              projects={data.projects}
              tasks={data.tasks}
              statuses={data.statuses}
              archived={showArchived}
              setArchived={setShowArchived}
              search={search}
              onCreate={() => setModal('New project')}
              onOpen={(p) => {
                setProjectId(p.id);
                navigate('Projects');
              }}
              onArchive={(p) => {
                setProjectId(p.id);
                setModal('Archive project');
              }}
              onRestore={(p) => void toggleArchive(false, p)}
              onDelete={(p) => {
                setProjectId(p.id);
                setModal('Delete project');
              }}
            />
          )}
          {page === 'Members' && (
            <>
              <h2 className="section-title">{t('Workspace members')}</h2>
              <div className="team-grid">
                {data.members
                  .filter((m) => m.name.toLowerCase().includes(search.toLowerCase()))
                  .map((m, i) => (
                    <div className="member-card" key={m.email}>
                      {avatar(m.name)}
                      <span className="member-role">
                        {i === 0 ? t('Workspace owner') : t('Team member')}
                      </span>
                      <h3>{m.name}</h3>
                      <p>{m.email}</p>
                      {m.invitationToken && (
                        <button
                          className="text-button"
                          onClick={async () => {
                            try {
                              const url = new URL(window.location.origin);
                              url.searchParams.set('invite', m.invitationToken!);
                              await navigator.clipboard.writeText(url.href);
                              setToast('Invitation link copied');
                            } catch {
                              setToast('Could not copy invitation link.');
                            }
                          }}
                        >
                          {t('Copy invitation link')}
                        </button>
                      )}
                      <div>
                        <span>
                          {
                            data.tasks.filter((t) => t.assignees.includes(m.name) && !isDone(t))
                              .length
                          }
                          {' ' + t('active tasks')}
                        </span>
                        {i > 0 && data.workspace.canManage !== false && (
                          <button
                            className="text-button danger"
                            aria-label={t('Remove member {0}', m.name)}
                            onClick={() => {
                              setRemovingMember(m.name);
                              setModal('Remove member');
                            }}
                          >
                            <Trash2 size={13} />
                            {t('Remove')}
                          </button>
                        )}
                        <button
                          className="text-button"
                          onClick={() => {
                            navigate('Projects');
                            setPerson(m.name);
                            setView('List');
                          }}
                        >
                          {t('View tasks') + ' '}
                          <ArrowRight size={14} />
                        </button>
                      </div>
                    </div>
                  ))}
                <button className="member-card new-tile" onClick={() => setModal('Invite members')}>
                  <Plus size={28} />
                  <h3>{t('Better, together.')}</h3>
                  <span>{t('Invite a teammate')}</span>
                </button>
              </div>
            </>
          )}
          <footer className="page-footer">
            <span className="tiny-logo">▥</span>
            {' ' + t('A little less busy. A little more progress.')}
            <span>
              {t('Made for the way you work') + ' '}
              <span>✳</span>
            </span>
          </footer>
        </div>
      </main>
      {draft && (
        <CardDrawer
          key={draft.id}
          data={data}
          draft={draft}
          setDraft={setDraft}
          currentMember={currentMember}
          modal={modal}
          setModal={setModal}
          setToast={setToast}
          closeDraft={closeDraft}
          uploading={uploading}
          setUploading={setUploading}
          saving={saving}
          updateTask={updateTask}
          commit={commit}
          avatar={avatar}
        />
      )}
      <WorkspaceDialogs
        modal={modal}
        setModal={setModal}
        t={t}
        removingMember={removingMember}
        saving={saving}
        currentMember={currentMember}
        data={data}
        commit={commit}
        setRemovingMember={setRemovingMember}
        draft={draft}
        openWorkspace={openWorkspace}
        setToast={setToast}
        project={project}
        deleteProject={deleteProject}
        toggleArchive={toggleArchive}
        saveLabels={saveLabels}
        saveSwimlanes={saveSwimlanes}
        saveDefinitions={saveDefinitions}
        setProjectId={setProjectId}
        navigate={navigate}
      />
      {toast && (
        <div role="status" className="toast">
          <span>
            <Check size={15} />
          </span>
          {t(toast)}
          <button aria-label={t('Dismiss notification')} onClick={() => setToast('')}>
            <X size={15} />
          </button>
        </div>
      )}
    </div>
  );
}
