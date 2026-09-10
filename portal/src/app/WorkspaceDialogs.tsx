import { ArrowRight, Folder, Plus, Sparkles, Trash2, X } from 'lucide-react';
import React from 'react';
import { recordChanges } from '../domain/cardHistory';
import type { Member } from '../domain/models';
import { type Definition, type Project, type State, type Task } from '../domain/models';
import { isActivitiesProject } from '../domain/projectRules';
import { CardShareDialog } from '../features/cards/CardShareDialog';
import { Notifications } from '../features/notifications/Notifications';
import { ProfileForm } from '../features/profile/ProfileForm';
import { WorkflowManager } from '../features/workflow/WorkflowManager';
import { WorkspaceAppearance } from '../features/workspaces/WorkspaceAppearance';
import { WorkspaceSwitcher } from '../features/workspaces/WorkspaceSwitcher';
import { repository } from '../infrastructure/workspaceRepository';
import { ThemeSelect } from '../shared/Theme';

type WorkspaceDialogsProps = {
  modal: string | null;
  setModal: React.Dispatch<React.SetStateAction<string | null>>;
  t: (value: unknown, ...values: unknown[]) => string;
  removingMember: string | null;
  saving: boolean;
  currentMember: Member;
  data: State;
  commit: (next: State, message?: string, notify?: boolean) => Promise<boolean>;
  setRemovingMember: React.Dispatch<React.SetStateAction<string | null>>;
  draft: Task | null;
  openWorkspace: (loaded: State) => void;
  setToast: React.Dispatch<React.SetStateAction<string>>;
  project: Project;
  deleteProject: () => Promise<void>;
  toggleArchive: (archived: boolean, targetProject?: Project) => Promise<void>;
  saveLabels: (items: Definition[]) => Promise<void>;
  saveSwimlanes: (items: Definition[]) => Promise<void>;
  saveDefinitions: (kind: 'statuses' | 'buckets', items: Definition[]) => Promise<void>;
  setProjectId: React.Dispatch<React.SetStateAction<string>>;
  navigate: (name: string) => void;
};

export function WorkspaceDialogs({
  modal,
  setModal,
  t,
  removingMember,
  saving,
  currentMember,
  data,
  commit,
  setRemovingMember,
  draft,
  openWorkspace,
  setToast,
  project,
  deleteProject,
  toggleArchive,
  saveLabels,
  saveSwimlanes,
  saveDefinitions,
  setProjectId,
  navigate,
}: WorkspaceDialogsProps) {
  return (
    modal && (
      <div className="modal-overlay" onClick={() => setModal(null)}>
        <section
          role="dialog"
          aria-modal="true"
          aria-label={t(modal)}
          className={`modal ${modal === 'Profile' ? 'profile-modal' : ''}`}
          onClick={(e) => e.stopPropagation()}
        >
          <div className="modal-heading">
            <span>
              <Sparkles size={18} />
              {t(modal)}
            </span>
            <button
              aria-label={t('Close dialog')}
              className="icon-button"
              onClick={() => setModal(null)}
            >
              <X size={20} />
            </button>
          </div>
          {modal === 'Remove member' ? (
            <div className="simple-form">
              <h2>
                {t('Remove') + ' '}
                {removingMember}?
              </h2>
              <p>
                {t(
                  'This person will be removed from this workspace and from all card assignee lists. Cards, documents, and history will be preserved.',
                )}
              </p>
              <div className="confirmation-actions">
                <button className="secondary" onClick={() => setModal(null)}>
                  {t('Keep member')}
                </button>
                <button
                  className="primary destructive"
                  disabled={saving}
                  onClick={async () => {
                    if (!removingMember || removingMember === currentMember.name) return;
                    const next = {
                      ...data,
                      members: data.members.filter((m) => m.name !== removingMember),
                      tasks: data.tasks.map((task) =>
                        task.assignees.includes(removingMember)
                          ? recordChanges(
                              task,
                              {
                                ...task,
                                assignees: task.assignees.filter((name) => name !== removingMember),
                              },
                              currentMember.name,
                            )
                          : task,
                      ),
                      activity: [`Removed member ${removingMember}`, ...data.activity],
                    };
                    if (await commit(next, 'Member removed')) {
                      setModal(null);
                      setRemovingMember(null);
                    }
                  }}
                >
                  {t('Confirm removal')}
                </button>
              </div>
            </div>
          ) : modal === 'Notifications' ? (
            <Notifications
              items={data.notifications}
              busy={saving}
              onClear={() =>
                void commit({ ...data, notifications: [] }, 'Notifications cleared', false)
              }
              onDismiss={(id) =>
                void commit(
                  { ...data, notifications: data.notifications.filter((item) => item.id !== id) },
                  undefined,
                  false,
                )
              }
            />
          ) : modal === 'Share card' && draft ? (
            <CardShareDialog workspaceId={data.workspace.id} cardId={draft.id} />
          ) : modal === 'Workspace appearance' ? (
            <WorkspaceAppearance
              workspace={data.workspace}
              onSave={async (workspace) => {
                const saved = await commit({ ...data, workspace }, 'Workspace images updated');
                if (saved) setModal(null);
                return saved;
              }}
            />
          ) : modal === 'Workspace' ? (
            <WorkspaceSwitcher
              current={data.workspace}
              onDelete={async (id) => {
                const loaded = await repository.deleteWorkspace(id);
                if (id === data.workspace.id) openWorkspace(loaded);
                else setModal(null);
                setToast('Workspace deleted');
              }}
              onSwitch={async (id) => openWorkspace(await repository.switchWorkspace(id))}
              onCreate={async (name) =>
                openWorkspace(await repository.createWorkspace(name, currentMember))
              }
            />
          ) : modal === 'Archive project' || modal === 'Delete project' ? (
            <div className="simple-form">
              <h2>
                {modal === 'Archive project'
                  ? t('Save it for another day.')
                  : t('Delete this project?')}
              </h2>
              <p>
                <strong>{isActivitiesProject(project) ? t('My activities') : project.name}</strong>
                {' ' + t('contains') + ' '}
                {data.tasks.filter((t) => t.project === project.id).length}
                {' ' + t('cards.')}
              </p>
              <p>
                {modal === 'Archive project'
                  ? t(
                      'The project will move to Archived in Overview. Its cards, history, swimlanes, and files stay intact, and you can restore it anytime.',
                    )
                  : t(
                      'This permanently removes the project, its cards, history, swimlanes, and documents that are not attached elsewhere. This cannot be undone.',
                    )}
              </p>
              <div className="confirmation-actions">
                <button className="secondary" onClick={() => setModal(null)}>
                  {t('Keep project')}
                </button>
                <button
                  className={modal === 'Delete project' ? 'primary destructive' : 'primary'}
                  disabled={saving}
                  onClick={() =>
                    modal === 'Delete project' ? void deleteProject() : void toggleArchive(true)
                  }
                >
                  {modal === 'Delete project' ? <Trash2 size={15} /> : <Folder size={15} />}
                  {t('Confirm') + ' '}
                  {modal === 'Delete project' ? t('deletion') : t('archive')}
                </button>
              </div>
            </div>
          ) : modal === 'Manage labels' ? (
            <WorkflowManager
              kind="labels"
              definitions={data.labels}
              used={[...data.tasks.flatMap((task) => task.labels), ...(draft?.labels ?? [])]}
              onSave={saveLabels}
            />
          ) : modal === 'Manage swimlanes' ? (
            <WorkflowManager
              kind="swimlanes"
              definitions={data.swimlanes.filter((lane) => lane.project === project.id)}
              used={data.tasks
                .filter((task) => task.project === project.id)
                .map((task) => task.swimlane ?? '')}
              onSave={saveSwimlanes}
            />
          ) : modal === 'Manage statuses' || modal === 'Manage buckets' ? (
            <WorkflowManager
              kind={modal === 'Manage statuses' ? 'statuses' : 'buckets'}
              definitions={modal === 'Manage statuses' ? data.statuses : data.buckets}
              used={data.tasks.map((t) =>
                modal === 'Manage statuses' ? t.status : (t.bucket ?? ''),
              )}
              onSave={(items) =>
                saveDefinitions(modal === 'Manage statuses' ? 'statuses' : 'buckets', items)
              }
            />
          ) : modal === 'New project' ? (
            <form
              className="simple-form"
              onSubmit={(e) => {
                e.preventDefault();
                const f = new FormData(e.currentTarget);
                const id = 'project-' + Date.now();
                if (String(f.get('name')).trim().toLowerCase() === 'my activities') {
                  setToast('My activities already exists in this workspace.');
                  return;
                }
                void commit(
                  {
                    ...data,
                    projects: [
                      ...data.projects,
                      {
                        id,
                        name: String(f.get('name')).trim(),
                        description: String(f.get('description')),
                        color: String(f.get('color')),
                      },
                    ],
                  },
                  'Your new project is ready',
                );
                setProjectId(id);
                navigate('Projects');
                setModal(null);
              }}
            >
              <h2>{t('Make room for your next idea.')}</h2>
              <label>
                {t('Project name')}
                <input
                  name="name"
                  required
                  pattern=".*\S.*"
                  placeholder={t('Something great starts here')}
                  autoFocus
                />
              </label>
              <label>
                {t('Description')}
                <textarea name="description" placeholder={t('What are we working towards?')} />
              </label>
              <label>
                {t('Project color')}
                <input name="color" type="color" defaultValue="#a6bddb" />
              </label>
              <button className="primary" type="submit">
                <Plus size={16} />
                {t('Create project')}
              </button>
            </form>
          ) : modal === 'Invite members' ? (
            <form
              className="simple-form"
              onSubmit={(e) => {
                e.preventDefault();
                const f = new FormData(e.currentTarget);
                const name = String(f.get('name')).trim();
                const email = String(f.get('email')).trim();
                if (
                  data.members.some(
                    (m) =>
                      m.email.toLowerCase() === email.toLowerCase() ||
                      m.name.toLowerCase() === name.toLowerCase(),
                  )
                ) {
                  setToast('That teammate is already in your workspace');
                  return;
                }
                void commit(
                  {
                    ...data,
                    members: [
                      ...data.members,
                      {
                        name,
                        email,
                        initials: name
                          .split(' ')
                          .map((n) => n[0])
                          .join('')
                          .slice(0, 2),
                        color: '#bec9d8',
                      },
                    ],
                  },
                  `${name} added to your workspace`,
                );
                setModal(null);
              }}
            >
              <h2>{t('Good things happen together.')}</h2>
              <p>{t('Add a member to this workspace.')}</p>
              <label>
                {t('Full name')}
                <input
                  name="name"
                  required
                  pattern=".*\S.*"
                  placeholder={t('e.g. Taylor Brooks')}
                  autoFocus
                />
              </label>
              <label>
                {t('Email address')}
                <input name="email" type="email" required placeholder={t('taylor@studio.co')} />
              </label>
              <button className="primary" type="submit">
                <Plus size={16} />
                {t('Add teammate')}
              </button>
            </form>
          ) : modal === 'Activity' ? (
            <div className="activity-list">
              <h2>{t('A little momentum.')}</h2>
              {data.activity.map((a, i) => (
                <div key={i}>
                  <span className="activity-dot" />
                  <p>
                    {a}
                    <small>{i === 0 ? t('Latest update') : t('Workspace activity')}</small>
                  </p>
                </div>
              ))}
            </div>
          ) : modal === 'Help & shortcuts' ? (
            <div className="simple-form">
              <h2>{t('Find your flow.')}</h2>
              <p>
                {t(
                  'Drag cards between columns to move work forward. Open any card to edit its details, checklist, and conversation.',
                )}
              </p>
              <p>
                {t(
                  'Switch between Board, List, and Calendar to see work your way. Use filters to focus on a person or priority.',
                )}
              </p>
              <div className="shortcut">
                <span>{t('Search tasks')}</span>
                <kbd>{t('\u2318 / Ctrl K')}</kbd>
              </div>
              <div className="shortcut">
                <span>{t('Close a dialog')}</span>
                <kbd>{t('Esc')}</kbd>
              </div>
              <p>
                {t(
                  'Changes are saved in this browser. Export a project from the board\u2019s \u2022\u2022\u2022 menu.',
                )}
              </p>
            </div>
          ) : modal === 'Profile' ? (
            <ProfileForm
              member={currentMember}
              onSave={async (name, photo) => {
                if (
                  data.members
                    .filter((member) => member !== currentMember)
                    .some(
                      (m) =>
                        m.name.toLowerCase() === name.toLowerCase() ||
                        m.email.toLowerCase() === currentMember.email.toLowerCase(),
                    )
                ) {
                  setToast('This name or email belongs to another teammate');
                  return;
                }
                const old = currentMember.name;
                if (
                  await commit(
                    {
                      ...data,
                      members: data.members.map((m) =>
                        m === currentMember
                          ? {
                              ...m,
                              name,
                              photo,
                              initials: name
                                .split(' ')
                                .map((n) => n[0])
                                .join('')
                                .slice(0, 2),
                            }
                          : m,
                      ),
                      tasks: data.tasks.map((t) =>
                        t.assignees.includes(old)
                          ? recordChanges(
                              t,
                              { ...t, assignees: t.assignees.map((a) => (a === old ? name : a)) },
                              old,
                            )
                          : t,
                      ),
                    },
                    'Profile updated',
                  )
                )
                  setModal(null);
              }}
            />
          ) : (
            <div className="simple-form">
              <h2>
                {modal === 'Settings'
                  ? t('Your workspace, your way.')
                  : t('A space for all your possibilities.')}
              </h2>
              <div className="workspace-summary">
                <span className="workspace-icon">
                  {data.workspace.icon ? (
                    <img src={data.workspace.icon} alt={t('Workspace icon')} />
                  ) : (
                    data.workspace.name[0]
                  )}
                </span>
                <div>
                  <b>{data.workspace.name}</b>
                  <p>
                    {data.members.length}
                    {' ' + t('members \u00B7') + ' '}
                    {data.projects.length}
                    {' ' + t('projects')}
                  </p>
                </div>
              </div>
              <p>{t('Manage your workspace, members, and preferences.')}</p>
              <ThemeSelect />
              <button className="secondary" onClick={() => setModal('Workspace appearance')}>
                <Sparkles size={16} />
                {t('Workspace appearance')}
              </button>
              <button className="primary" onClick={() => setModal('New project')}>
                <Plus size={16} />
                {t('Create a project')}
              </button>
              <button className="secondary" onClick={() => setModal('Profile')}>
                {t('Edit your profile')}
                <ArrowRight size={16} />
              </button>
            </div>
          )}
        </section>
      </div>
    )
  );
}
