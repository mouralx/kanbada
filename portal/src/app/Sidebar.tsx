import { Brand } from '../shared/Brand';
import {
  CheckSquare,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  CircleHelp,
  Columns3,
  LayoutDashboard,
  Plus,
  Settings,
  Users,
} from 'lucide-react';
import React, { useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import type { Member } from '../domain/models';
import { type Project, type State, type Task } from '../domain/models';
import { isActivitiesProject } from '../domain/projectRules';
import { LogoutButton } from '../features/auth/AuthBoundary';

type SidebarProps = {
  sidebar: boolean;
  navigate: (name: string) => void;
  t: (value: unknown, ...values: unknown[]) => string;
  sidebarCollapsed: boolean;
  setSidebarCollapsed: React.Dispatch<React.SetStateAction<boolean>>;
  data: State;
  setModal: React.Dispatch<React.SetStateAction<string | null>>;
  page: string;
  activeTasks: Task[];
  currentMember: Member;
  isDone: (task: Task) => boolean;
  project: Project;
  setProjectId: React.Dispatch<React.SetStateAction<string>>;
  avatar: (name: string, small?: boolean) => React.JSX.Element;
};

export function Sidebar({
  sidebar,
  navigate,
  t,
  sidebarCollapsed,
  setSidebarCollapsed,
  data,
  setModal,
  page,
  activeTasks,
  currentMember,
  isDone,
  project,
  setProjectId,
  avatar,
}: SidebarProps) {
  const [tooltip, setTooltip] = useState<{ label: string; left: number; top: number } | null>(null);
  const showTooltip = (event: React.MouseEvent<HTMLElement> | React.FocusEvent<HTMLElement>) => {
    if (!sidebarCollapsed || !window.matchMedia('(min-width: 761px)').matches) return;
    const button = (event.target as Element).closest('button');
    const label = button?.getAttribute('aria-label');
    if (!button || !label) {
      setTooltip(null);
      return;
    }
    const rect = button.getBoundingClientRect();
    setTooltip({
      label,
      left: event.currentTarget.getBoundingClientRect().right + 12,
      top: Math.max(24, Math.min(window.innerHeight - 24, rect.top + rect.height / 2)),
    });
  };
  useEffect(() => {
    const dismiss = () => setTooltip(null);
    window.addEventListener('resize', dismiss);
    window.addEventListener('scroll', dismiss, true);
    return () => {
      window.removeEventListener('resize', dismiss);
      window.removeEventListener('scroll', dismiss, true);
    };
  }, []);
  return (
    <aside
      className={`sidebar ${sidebar ? 'open' : ''}`}
      onMouseOver={showTooltip}
      onMouseLeave={() => setTooltip(null)}
      onFocus={showTooltip}
      onBlur={() => setTooltip(null)}
      onClick={() => setTooltip(null)}
      onKeyDown={(event) => {
        if (event.key === 'Escape') setTooltip(null);
      }}
    >
      {sidebarCollapsed &&
        tooltip &&
        createPortal(
          <div
            className="sidebar-tooltip"
            role="tooltip"
            style={{ left: tooltip.left, top: tooltip.top }}
          >
            {tooltip.label}
          </div>,
          document.body,
        )}
      <a
        className="logo"
        href="#"
        onClick={(e) => {
          e.preventDefault();
          navigate('Projects');
        }}
      >
        <Brand />
      </a>
      <button
        className="sidebar-collapse-toggle"
        aria-label={sidebarCollapsed ? t('Expand sidebar') : t('Collapse sidebar')}
        title={
          sidebarCollapsed
            ? undefined
            : sidebarCollapsed
              ? t('Expand sidebar')
              : t('Collapse sidebar')
        }
        aria-expanded={!sidebarCollapsed}
        onClick={() => {
          const next = !sidebarCollapsed;
          setSidebarCollapsed(next);
          try {
            localStorage.setItem('kanbada-sidebar-collapsed', String(next));
          } catch {
            /* Keep the current session preference. */
          }
        }}
      >
        {sidebarCollapsed ? <ChevronRight size={15} /> : <ChevronLeft size={15} />}
      </button>
      <button
        aria-label={t('Switch workspace')}
        title={sidebarCollapsed ? undefined : data.workspace.name}
        className="workspace"
        onClick={() => setModal('Workspace')}
      >
        <span className="workspace-icon">
          {data.workspace.icon ? (
            <img src={data.workspace.icon} alt={t('Workspace icon')} />
          ) : (
            <>
              {data.workspace.name[0].toUpperCase()}
              <span>✳</span>
            </>
          )}
        </span>
        <span>
          {data.workspace.name}
          <small>
            {data.members.length} {t('members')}
          </small>
        </span>
        <ChevronDown size={15} />
      </button>
      <div className="nav-label">{t('WORKSPACE')}</div>
      <nav>
        {[
          { name: 'Overview', icon: LayoutDashboard },
          { name: 'My tasks', icon: CheckSquare },
          { name: 'Projects', icon: Columns3 },
          { name: 'Members', icon: Users },
        ].map(({ name, icon: Icon }) => (
          <button
            aria-label={t(name)}
            title={sidebarCollapsed ? undefined : t(name)}
            key={name}
            className={
              page === name || (name === 'Projects' && page === 'Project directory') ? 'active' : ''
            }
            onClick={() => navigate(name === 'Projects' ? 'Project directory' : name)}
          >
            <Icon size={18} />
            <span className="nav-text">{t(name)}</span>
            {name === 'My tasks' && (
              <span className="nav-count">
                {
                  activeTasks.filter((t) => t.assignees.includes(currentMember.name) && !isDone(t))
                    .length
                }
              </span>
            )}
          </button>
        ))}
      </nav>
      <div className="nav-label projects-label">
        {t('YOUR PROJECTS')}
        <button aria-label={t('Create project')} onClick={() => setModal('New project')}>
          <Plus size={16} />
        </button>
      </div>
      <div className="project-nav">
        {data.projects
          .filter((p) => !p.archived)
          .map((p) => (
            <button
              aria-label={isActivitiesProject(p) ? t('My activities') : p.name}
              title={
                sidebarCollapsed ? undefined : isActivitiesProject(p) ? t('My activities') : p.name
              }
              key={p.id}
              className={project.id === p.id && page === 'Projects' ? 'selected' : ''}
              onClick={() => {
                setProjectId(p.id);
                navigate('Projects');
              }}
            >
              <span style={{ background: p.color }} />
              <span className="project-nav-text">
                {isActivitiesProject(p) ? t('My activities') : p.name}
              </span>
              {project.id === p.id && page === 'Projects' && <span className="project-indicator" />}
            </button>
          ))}
      </div>
      <button
        aria-label={t('New project')}
        title={sidebarCollapsed ? undefined : t('New project')}
        className="new-project"
        onClick={() => setModal('New project')}
      >
        <Plus size={16} />
        <span className="nav-text">{t('New project')}</span>
      </button>
      <div className="sidebar-bottom">
        <LogoutButton />
        <button
          aria-label={t('Settings')}
          title={sidebarCollapsed ? undefined : t('Settings')}
          onClick={() => setModal('Settings')}
        >
          <Settings size={17} />
          <span className="nav-text">{t('Settings')}</span>
        </button>
        <button
          aria-label={t('Help')}
          title={sidebarCollapsed ? undefined : t('Help')}
          onClick={() => navigate('Help')}
        >
          <CircleHelp size={17} />
          <span className="nav-text">{t('Help')}</span>
        </button>
        <button
          aria-label={t('Edit profile')}
          title={sidebarCollapsed ? undefined : currentMember.name}
          className="profile"
          onClick={() => setModal('Profile')}
        >
          {avatar(currentMember.name)}
          <span>
            {currentMember.name}
            <small>{t('Personal account')}</small>
          </span>
          <ChevronDown size={15} />
        </button>
      </div>
    </aside>
  );
}
