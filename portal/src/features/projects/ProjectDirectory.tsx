import { Archive, ArrowUpRight, Folder, LockKeyhole, Plus, RotateCcw, Trash2 } from 'lucide-react';
import { type Definition, type Project, type Task } from '../../domain/models';
import { isActivitiesProject } from '../../domain/projectRules';
import { useI18n } from '../../shared/i18n';
export function ProjectDirectory({
  projects,
  tasks,
  statuses,
  archived,
  setArchived,
  search,
  onCreate,
  onOpen,
  onArchive,
  onRestore,
  onDelete,
}: {
  projects: Project[];
  tasks: Task[];
  statuses: Definition[];
  archived: boolean;
  setArchived: (archived: boolean) => void;
  search: string;
  onCreate: () => void;
  onOpen: (project: Project) => void;
  onArchive: (project: Project) => void;
  onRestore: (project: Project) => void;
  onDelete: (project: Project) => void;
}) {
  const { t } = useI18n();
  const matching = projects.filter(
    (p) =>
      !!p.archived === archived &&
      (p.name + ' ' + p.description).toLowerCase().includes(search.toLowerCase()),
  );
  return (
    <section className="project-directory">
      <div className="directory-toolbar">
        <div className="project-scope-tabs">
          <button className={!archived ? 'active' : ''} onClick={() => setArchived(false)}>
            {t('Live (')}
            {projects.filter((p) => !p.archived).length})
          </button>
          <button className={archived ? 'active' : ''} onClick={() => setArchived(true)}>
            {t('Archived (')}
            {projects.filter((p) => p.archived).length})
          </button>
        </div>
        <button className="primary" onClick={onCreate}>
          <Plus size={15} />
          {t('Create project')}
        </button>
      </div>
      <div className="directory-list">
        {matching.map((project) => {
          const cards = tasks.filter((t) => t.project === project.id);
          const completed = cards.filter(
            (t) => statuses.find((s) => s.name === t.status)?.complete,
          ).length;
          const progress = cards.length ? Math.round((completed / cards.length) * 100) : 0;
          return (
            <article className="directory-project" key={project.id}>
              <span className="directory-project-icon" style={{ background: project.color }}>
                <Folder size={23} />
              </span>
              <div className="directory-project-info">
                <button onClick={() => onOpen(project)}>
                  <h2>{isActivitiesProject(project) ? t('My activities') : project.name}</h2>
                  <ArrowUpRight size={16} />
                </button>
                <p>{isActivitiesProject(project) ? t(project.description) : project.description}</p>
                <small>
                  {cards.length}
                  {' ' + t('cards \u00B7') + ' '}
                  {completed}
                  {' ' + t('completed')}
                  {project.archived ? t(' \u00B7 Archived') : ''}
                </small>
              </div>
              <div className="directory-progress">
                <span>{progress}%</span>
                <div className="progress-track">
                  <i style={{ width: `${progress}%` }} />
                </div>
              </div>
              <div className="directory-actions">
                <button
                  aria-label={t(
                    'Open project {0}',
                    isActivitiesProject(project) ? t('My activities') : project.name,
                  )}
                  title={t('Open project')}
                  onClick={() => onOpen(project)}
                >
                  <ArrowUpRight size={16} />
                </button>
                {isActivitiesProject(project) ? (
                  <span
                    className="protected-project"
                    title={t('My activities is a permanent project.')}
                    aria-label={t('Protected project')}
                  >
                    <LockKeyhole size={16} />
                  </span>
                ) : (
                  <>
                    {project.archived ? (
                      <button
                        aria-label={t('Restore project {0}', project.name)}
                        title={t('Restore project')}
                        onClick={() => onRestore(project)}
                      >
                        <RotateCcw size={16} />
                      </button>
                    ) : (
                      <button
                        aria-label={t('Archive project {0}', project.name)}
                        title={t('Archive project')}
                        onClick={() => onArchive(project)}
                      >
                        <Archive size={16} />
                      </button>
                    )}
                    <button
                      className="danger"
                      aria-label={t('Delete project {0}', project.name)}
                      title={t('Delete project')}
                      onClick={() => onDelete(project)}
                    >
                      <Trash2 size={16} />
                    </button>
                  </>
                )}
              </div>
            </article>
          );
        })}
      </div>
      {!matching.length && (
        <div className="directory-empty">
          <Folder size={32} />
          <h3>{archived ? t('Nothing archived yet.') : t('Make room for your next idea.')}</h3>
          <p>
            {search
              ? t('No projects match your search.')
              : archived
                ? t('Archived projects will appear here, ready to restore.')
                : t('Create a project to get started.')}
          </p>
          {!archived && (
            <button className="primary" onClick={onCreate}>
              <Plus size={15} />
              {t('Create project')}
            </button>
          )}
        </div>
      )}
    </section>
  );
}
