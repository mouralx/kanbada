import { ArrowRight, Check, Layers, LockKeyhole, Plus, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { type Workspace } from '../../domain/models';
import { repository } from '../../infrastructure/workspaceRepository';
import { useI18n } from '../../shared/i18n';
export function WorkspaceSwitcher({
  current,
  onSwitch,
  onCreate,
  onDelete,
}: {
  current: Workspace;
  onSwitch: (id: string) => Promise<void>;
  onCreate: (name: string) => Promise<void>;
  onDelete: (id: string) => Promise<void>;
}) {
  const { t } = useI18n();
  const [workspaces, setWorkspaces] = useState<Workspace[]>([]);
  const [name, setName] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [deleting, setDeleting] = useState<Workspace | null>(null);
  useEffect(() => {
    repository
      .listWorkspaces()
      .then(setWorkspaces)
      .catch(() => setError('Could not load workspaces.'));
  }, []);
  const run = async (action: () => Promise<void>) => {
    setBusy(true);
    setError('');
    try {
      await action();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not update workspaces.');
    } finally {
      setBusy(false);
    }
  };
  if (deleting)
    return (
      <div className="simple-form workspace-delete-confirmation">
        <h2>{t('Delete workspace?')}</h2>
        <strong>{deleting.name}</strong>
        <p>
          {t(
            'This permanently deletes this workspace and all its projects, cards, history, labels, and documents. This cannot be undone.',
          )}
        </p>
        {deleting.id === current.id && (
          <p>{t('You will return to My Workspace. Your personal workspace will stay intact.')}</p>
        )}
        {error && (
          <p className="form-error" role="alert">
            {t(error)}
          </p>
        )}
        <div className="confirmation-actions">
          <button
            className="secondary"
            disabled={busy}
            onClick={() => {
              setDeleting(null);
              setError('');
            }}
          >
            {t('Keep workspace')}
          </button>
          <button
            className="primary destructive"
            disabled={busy}
            onClick={() => void run(() => onDelete(deleting.id))}
          >
            <Trash2 size={15} />
            {t('Confirm deletion')}
          </button>
        </div>
      </div>
    );
  return (
    <div className="simple-form workspace-switcher">
      <h2>{t('A space for every ambition.')}</h2>
      <p>
        {t(
          'Switch workspaces or create a new one. Each has its own projects, members, workflow, and cards.',
        )}
      </p>
      <div className="workspace-options">
        {workspaces.map((workspace) => (
          <div className="workspace-option-row" key={workspace.id}>
            <button
              className="workspace-open"
              disabled={busy}
              onClick={() => void run(() => onSwitch(workspace.id))}
              aria-label={t('Switch to {0}', workspace.name)}
            >
              <span className="workspace-option-icon">
                <>{workspace.icon ? <img src={workspace.icon} alt="" /> : <Layers size={19} />}</>
              </span>
              <span>
                <b>{workspace.name}</b>
                <small>
                  {current.id === workspace.id ? t('Current workspace') : t('Open workspace')}
                </small>
              </span>
              {current.id === workspace.id ? <Check size={16} /> : <ArrowRight size={16} />}
            </button>
            {workspace.id === 'studio' || workspace.personal || workspace.canManage === false ? (
              <span
                className="workspace-protected"
                title={t('My Workspace cannot be deleted.')}
                aria-label={t('Protected workspace')}
              >
                <LockKeyhole size={15} />
              </span>
            ) : (
              <button
                className="icon-button danger workspace-delete"
                aria-label={t('Delete workspace {0}', workspace.name)}
                title={t('Delete workspace')}
                disabled={busy}
                onClick={() => {
                  setDeleting(workspace);
                  setError('');
                }}
              >
                <Trash2 size={16} />
              </button>
            )}
          </div>
        ))}
      </div>
      <p className="workspace-protection-note">
        <LockKeyhole size={12} />
        {t('My Workspace is your permanent personal workspace.')}
      </p>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          void run(() => onCreate(name));
        }}
      >
        <label>
          {t('New workspace name')}
          <input
            aria-label={t('New workspace name')}
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder={t('e.g. Product studio')}
            required
            maxLength={60}
          />
        </label>
        <button className="primary" disabled={busy}>
          <Plus size={15} />
          {busy ? t('Working…') : t('Create workspace')}
        </button>
      </form>
      {error && (
        <p className="form-error" role="alert">
          {t(error)}
        </p>
      )}
    </div>
  );
}
