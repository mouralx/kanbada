import {
  CalendarDays,
  Check,
  CheckSquare,
  Flag,
  Folder,
  MessageSquare,
  MoreHorizontal,
} from 'lucide-react';
import React from 'react';
import { type State, type Task } from '../../domain/models';
import { type Locale } from '../../shared/i18n';
import { CardLabels } from './CardLabels';

type BoardCardProps = {
  task: Task;
  isDone: (task: Task) => boolean;
  setDraft: React.Dispatch<React.SetStateAction<Task | null>>;
  t: (value: unknown, ...values: unknown[]) => string;
  data: State;
  locale: Locale;
  avatar: (name: string, small?: boolean) => React.JSX.Element;
};

export function BoardCard({ task, isDone, setDraft, t, data, locale, avatar }: BoardCardProps) {
  return (
    <article
      key={task.id}
      className={`task-card ${isDone(task) ? 'completed' : ''}`}
      draggable
      onDragStart={(e) => {
        e.dataTransfer.setData('text/plain', task.id);
        e.dataTransfer.effectAllowed = 'move';
      }}
      onClick={() => setDraft(structuredClone(task))}
      tabIndex={0}
      onKeyDown={(e) => {
        if (e.key === 'Enter') setDraft(structuredClone(task));
      }}
      aria-label={t('Open {0}', task.title)}
    >
      {task.cover && (
        <div className={`task-cover ${task.cover}`} aria-hidden="true">
          {task.cover === 'landing' ? (
            <>
              <div className="mini-nav">
                {t('forma')}
                <span>{t('About &nbsp; Work &nbsp; Contact \u2197')}</span>
              </div>
              <div className="mini-heading">
                {t('A little different.')}
                <br />
                {t('A lot more you.')}
              </div>
              <div className="mini-orbit" />
              <span className="mini-link">{t('Discover a new perspective \u2197')}</span>
            </>
          ) : task.cover === 'mobile' ? (
            <>
              <div className="phone">
                <span>9:41</span>
                <div className="phone-flower">✳</div>
                <b>
                  {t('Good things')}
                  <br />
                  {t('start here.')}
                </b>
                <i>{t('Let\u2019s get started \u2192')}</i>
              </div>
              <div className="phone second">
                <span>{t('Your everyday, elevated.')}</span>
                <div className="phone-block" />
                <b>
                  {t('Make it')}
                  <br />
                  {t('your own.')}
                </b>
              </div>
            </>
          ) : (
            <>
              <span className="note n1">
                {t('Discover')}
                <br />
                <b>✧</b>
              </span>
              <span className="note n2">
                {t('Explore')}
                <br />
                <b>↗</b>
              </span>
              <span className="note n3">
                {t('Connect')}
                <br />
                <b>♡</b>
              </span>
              <div className="journey-line" />
            </>
          )}
        </div>
      )}
      <div className="card-top">
        <CardLabels names={task.labels} definitions={data.labels} />
        <span className="card-ellipsis">
          <MoreHorizontal size={16} />
        </span>
      </div>
      <h3>
        {isDone(task) && <Check size={15} />} {task.title}
      </h3>
      <div className="card-meta">
        <span className={`priority ${task.priority.toLowerCase()}`}>
          <Flag size={11} />
          {t(task.priority)}
        </span>
        <span className="due">
          <CalendarDays size={12} />
          {task.due
            ? new Date(task.due + 'T12:00:00').toLocaleDateString(locale, {
                month: 'short',
                day: 'numeric',
              })
            : t('No due date')}
        </span>
      </div>
      {task.bucket && (
        <span className="card-bucket">
          <Folder size={10} />
          {task.bucket}
        </span>
      )}
      <div className="card-footer">
        <span>{task.id}</span>
        <div>
          {task.comments.length > 0 && (
            <span>
              <MessageSquare size={12} />
              {task.comments.length}
            </span>
          )}
          {task.checklist.length > 0 && (
            <span>
              <CheckSquare size={12} />
              {task.checklist.filter((c) => c.done).length}/{task.checklist.length}
            </span>
          )}
          <div className="assignee-stack">
            {task.assignees.map((name) => (
              <React.Fragment key={name}>{avatar(name, true)}</React.Fragment>
            ))}
          </div>
        </div>
      </div>
    </article>
  );
}
