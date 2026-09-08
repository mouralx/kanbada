import { ArrowUpRight, Check, CheckSquare, Copy, Folder, Link, Tag, Trash2, X } from 'lucide-react';
import { useState, type Dispatch, type ReactNode, type SetStateAction } from 'react';
import { type Member, type State, type Status, type Task } from '../../domain/models';
import { isActivitiesProject } from '../../domain/projectRules';
import { fileRepository } from '../../infrastructure/attachments';
import { useI18n } from '../../shared/i18n';
import { Attachments } from './CardAttachments';
import { CardLabels } from './CardLabels';

type CardDrawerProps = {
  data: State;
  draft: Task;
  setDraft: Dispatch<SetStateAction<Task | null>>;
  currentMember: Member;
  modal: string | null;
  setModal: Dispatch<SetStateAction<string | null>>;
  setToast: Dispatch<SetStateAction<string>>;
  closeDraft: () => void;
  uploading: boolean;
  setUploading: Dispatch<SetStateAction<boolean>>;
  saving: boolean;
  updateTask: (task: Task) => Promise<void>;
  commit: (next: State, message?: string, notify?: boolean) => Promise<boolean>;
  avatar: (name: string, small?: boolean) => ReactNode;
};

export function CardDrawer({
  data,
  draft,
  setDraft,
  currentMember,
  modal,
  setModal,
  setToast,
  closeDraft,
  uploading,
  setUploading,
  saving,
  updateTask,
  commit,
  avatar,
}: CardDrawerProps) {
  const { t, locale } = useI18n();
  const [cardTab, setCardTab] = useState('Details');
  const [comment, setComment] = useState('');
  const statuses = data.statuses.map((status) => status.name);
  return (
    <div className="modal-overlay card-drawer-overlay" onClick={closeDraft}>
      <section
        role="dialog"
        aria-modal="true"
        aria-label={t('Task details')}
        className="modal task-modal card-drawer"
        onClick={(e) => e.stopPropagation()}
        onKeyDown={(e) => {
          if (e.key !== 'Tab' || modal) return;
          const items = Array.from(
            e.currentTarget.querySelectorAll<HTMLElement>(
              'button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),a[href]',
            ),
          ).filter((item) => item.getClientRects().length > 0);
          const first = items[0],
            last = items.at(-1);
          if (e.shiftKey && document.activeElement === first) {
            e.preventDefault();
            last?.focus();
          } else if (!e.shiftKey && document.activeElement === last) {
            e.preventDefault();
            first?.focus();
          }
        }}
      >
        <div className="modal-heading">
          <span>
            <CheckSquare size={17} />
            {draft.id}
          </span>
          {data.tasks.some((task) => task.id === draft.id) && (
            <button className="card-share-link" onClick={() => setModal('Share card')}>
              {t('Share link')}
            </button>
          )}
          {data.tasks.some((task) => task.id === draft.id) && (
            <button
              className="card-copy-link"
              aria-label={t('Copy card link')}
              onClick={async () => {
                try {
                  const url = new URL(window.location.href);
                  url.searchParams.set('card', draft.id);
                  url.searchParams.set('workspace', data.workspace.id);
                  await navigator.clipboard.writeText(url.href);
                  setToast('Card link copied');
                } catch {
                  setToast('Could not copy the link. Copy it from your browser address bar.');
                }
              }}
            >
              <Link size={15} />
              {t('Copy link')}
            </button>
          )}
          <button aria-label={t('Close task')} className="icon-button" onClick={closeDraft}>
            <X size={20} />
          </button>
        </div>
        <div className="card-tabs" role="tablist" aria-label={t('Card sections')}>
          {['Details', 'History'].map((tab) => (
            <button
              key={tab}
              role="tab"
              id={'card-tab-' + tab}
              aria-controls={'card-panel-' + tab}
              aria-selected={cardTab === tab}
              disabled={uploading}
              onClick={() => setCardTab(tab)}
            >
              {t(tab)}
              {tab === 'History' && <span>{draft.history?.length ?? 0}</span>}
            </button>
          ))}
        </div>
        {cardTab === 'History' && (
          <div
            className="card-history"
            role="tabpanel"
            id="card-panel-History"
            aria-labelledby="card-tab-History"
          >
            <h2>{t('Every step of the story.')}</h2>
            <p>{t('Saved changes, newest first.')}</p>
            {[...(draft.history ?? [])].reverse().map((entry) => (
              <article key={entry.id} className="history-entry">
                {avatar(entry.actor, true)}
                <div>
                  <header>
                    <strong>{entry.actor}</strong>
                    <time dateTime={entry.at}>{new Date(entry.at).toLocaleString(locale)}</time>
                  </header>
                  <ul>
                    {entry.changes.map((change, i) => (
                      <li key={i}>{t(change)}</li>
                    ))}
                  </ul>
                </div>
              </article>
            ))}
            {!draft.history?.length && (
              <div className="empty-state">{t('Save this card to start its history.')}</div>
            )}
          </div>
        )}
        <form
          hidden={cardTab !== 'Details'}
          role="tabpanel"
          id="card-panel-Details"
          aria-labelledby="card-tab-Details"
          onSubmit={(e) => {
            e.preventDefault();
            if (draft.title.trim()) updateTask({ ...draft, title: draft.title.trim() });
          }}
        >
          <div className="card-project-association" role="group" aria-label={t('Project')}>
            <Folder size={16} />
            <span>
              <small>{t('Project')}</small>
              <strong>
                {isActivitiesProject(
                  data.projects.find((p) => p.id === draft.project) ?? {
                    id: '',
                    name: '',
                    color: '',
                    description: '',
                  },
                )
                  ? t('My activities')
                  : data.projects.find((p) => p.id === draft.project)?.name}
              </strong>
            </span>
          </div>
          <input
            className="task-title-input"
            placeholder={t('Give your idea a name\u2026')}
            aria-label={t('Task title')}
            required
            value={draft.title}
            onChange={(e) => setDraft({ ...draft, title: e.target.value })}
            autoFocus
          />
          <div className="task-fields">
            <label>
              {t('Status')}
              <select
                aria-label={t('Status')}
                value={draft.status}
                onChange={(e) => setDraft({ ...draft, status: e.target.value as Status })}
              >
                {statuses.map((s) => (
                  <option key={s} value={s}>
                    {t(s)}
                  </option>
                ))}
              </select>
            </label>
            <label>
              {t('Priority')}
              <select
                aria-label={t('Priority')}
                value={draft.priority}
                onChange={(e) => setDraft({ ...draft, priority: e.target.value })}
              >
                {['Low', 'Medium', 'High'].map((s) => (
                  <option key={s} value={s}>
                    {t(s)}
                  </option>
                ))}
              </select>
            </label>
            <fieldset className="assignee-picker">
              <legend>{t('Assignees')}</legend>
              {data.members.map((m) => (
                <label key={m.name}>
                  <input
                    type="checkbox"
                    aria-label={t('Assign {0}', m.name)}
                    checked={draft.assignees.includes(m.name)}
                    onChange={(e) =>
                      setDraft({
                        ...draft,
                        assignees: e.target.checked
                          ? [...draft.assignees, m.name]
                          : draft.assignees.filter((name) => name !== m.name),
                      })
                    }
                  />
                  {avatar(m.name, true)}
                  <span>{m.name}</span>
                </label>
              ))}
            </fieldset>
            <label>
              {t('Due date')}
              <input
                type="date"
                value={draft.due}
                onChange={(e) => setDraft({ ...draft, due: e.target.value })}
              />
            </label>
            <label>
              {t('Swimlane')}
              <select
                aria-label={t('Card swimlane')}
                value={draft.swimlane ?? ''}
                onChange={(e) => setDraft({ ...draft, swimlane: e.target.value })}
              >
                <option value="">{t('No swimlane')}</option>
                {data.swimlanes
                  .filter((lane) => lane.project === draft.project)
                  .map((lane) => (
                    <option key={lane.id} value={lane.name}>
                      {lane.name}
                    </option>
                  ))}
              </select>
            </label>
            <label>
              {t('Bucket')}
              <select
                aria-label={t('Card bucket')}
                value={draft.bucket ?? ''}
                onChange={(e) => setDraft({ ...draft, bucket: e.target.value })}
              >
                <option value="">{t('No bucket')}</option>
                {data.buckets.map((b) => (
                  <option key={b.id} value={b.name}>
                    {b.name}
                  </option>
                ))}
              </select>
            </label>
            <fieldset className="label-picker">
              <legend>{t('Labels')}</legend>
              <div className="label-picker-heading">
                <span>{t('Choose one or more labels')}</span>
                <button
                  type="button"
                  disabled={uploading}
                  onClick={() => setModal('Manage labels')}
                >
                  <Tag size={13} />
                  {t('Manage labels')}
                </button>
              </div>
              <CardLabels names={draft.labels} definitions={data.labels} />
              {!draft.labels.length && <p className="no-labels">{t('No labels selected')}</p>}
              <div className="label-choices">
                {data.labels.map((label) => (
                  <label key={label.id}>
                    <input
                      type="checkbox"
                      aria-label={t('Apply label {0}', label.name)}
                      checked={draft.labels.includes(label.name)}
                      onChange={(e) =>
                        setDraft({
                          ...draft,
                          labels: e.target.checked
                            ? [...draft.labels, label.name]
                            : draft.labels.filter((name) => name !== label.name),
                        })
                      }
                    />
                    <CardLabels names={[label.name]} definitions={data.labels} />
                  </label>
                ))}
              </div>
              {!data.labels.length && (
                <p className="no-labels">{t('Create your first label with Manage labels.')}</p>
              )}
            </fieldset>
          </div>
          <label className="description-label">
            {t('Description')}
            <textarea
              placeholder={t('A little context goes a long way\u2026')}
              value={draft.description}
              onChange={(e) => setDraft({ ...draft, description: e.target.value })}
            />
          </label>
          <div className="checklist-heading">
            <h4>{t('Checklist')}</h4>
            <span>
              {draft.checklist.filter((c) => c.done).length}/{draft.checklist.length}
            </span>
          </div>
          {draft.checklist.map((c, i) => (
            <div className="checklist-item" key={i}>
              <input
                type="checkbox"
                checked={c.done}
                aria-label={c.text}
                onChange={() =>
                  setDraft({
                    ...draft,
                    checklist: draft.checklist.map((x, j) =>
                      i === j ? { ...x, done: !x.done } : x,
                    ),
                  })
                }
              />
              <span className={c.done ? 'checked' : ''}>{c.text}</span>
              <button
                type="button"
                aria-label={t('Remove {0}', c.text)}
                onClick={() =>
                  setDraft({ ...draft, checklist: draft.checklist.filter((_, j) => j !== i) })
                }
              >
                <X size={13} />
              </button>
            </div>
          ))}
          <input
            className="checklist-add"
            placeholder={t('+ Add a checklist item, then press Enter')}
            aria-label={t('New checklist item')}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault();
                if (e.currentTarget.value.trim()) {
                  setDraft({
                    ...draft,
                    checklist: [
                      ...draft.checklist,
                      { text: e.currentTarget.value.trim(), done: false },
                    ],
                  });
                  e.currentTarget.value = '';
                }
              }
            }}
          />
          <Attachments
            items={draft.attachments ?? []}
            onChange={(attachments) => {
              for (const file of draft.attachments ?? [])
                if (
                  !attachments.some((a) => a.id === file.id) &&
                  !data.tasks.some((t) => t.attachments?.some((a) => a.id === file.id))
                )
                  void fileRepository.remove(file.id).catch(() => {});
              setDraft((current) => (current ? { ...current, attachments } : current));
            }}
            onBusy={setUploading}
          />
          <h4>{t('Conversation')}</h4>
          {draft.comments.map((c, i) => (
            <div className="comment" key={i}>
              {avatar(currentMember.name, true)}
              <p>{c}</p>
            </div>
          ))}
          <div className="comment-input">
            <input
              placeholder={t('Share a thought\u2026')}
              aria-label={t('Comment')}
              value={comment}
              onChange={(e) => setComment(e.target.value)}
            />
            <button
              type="button"
              aria-label={t('Add comment')}
              onClick={() => {
                if (comment.trim()) {
                  setDraft({ ...draft, comments: [...draft.comments, comment.trim()] });
                  setComment('');
                }
              }}
            >
              <ArrowUpRight size={18} />
            </button>
          </div>
          <div className="modal-footer">
            <div>
              {data.tasks.some((t) => t.id === draft.id) && (
                <>
                  <button
                    type="button"
                    className="icon-button danger"
                    aria-label={t('Delete task')}
                    disabled={uploading || saving}
                    onClick={async () => {
                      const next = {
                        ...data,
                        tasks: data.tasks.filter((t) => t.id !== draft.id),
                      };
                      if (await commit(next, 'Task deleted')) {
                        for (const file of [
                          ...(draft.attachments ?? []),
                          ...(data.tasks.find((t) => t.id === draft.id)?.attachments ?? []),
                        ])
                          if (!next.tasks.some((t) => t.attachments?.some((a) => a.id === file.id)))
                            void fileRepository.remove(file.id).catch(() => {});
                        setDraft(null);
                      }
                    }}
                  >
                    <Trash2 size={17} />
                  </button>
                  <button
                    type="button"
                    className="icon-button"
                    aria-label={t('Duplicate task')}
                    onClick={() =>
                      updateTask({
                        ...draft,
                        id: `KB-${crypto.randomUUID().slice(0, 8).toUpperCase()}`,
                        history: [],
                        title: draft.title + ' (copy)',
                      })
                    }
                  >
                    <Copy size={16} />
                  </button>
                </>
              )}
            </div>
            <button type="submit" className="primary" disabled={uploading || saving}>
              <Check size={16} />
              {uploading ? t('Uploading\u2026') : t('Save task')}
            </button>
          </div>
        </form>
      </section>
    </div>
  );
}
