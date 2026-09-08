import { Bell, CheckCheck, X } from 'lucide-react';
import type { Notification } from '../../domain/models';
import { useI18n } from '../../shared/i18n';
export function Notifications({
  items,
  busy,
  onClear,
  onDismiss,
}: {
  items: Notification[];
  busy: boolean;
  onClear: () => void;
  onDismiss: (id: string) => void;
}) {
  const { t, locale } = useI18n();
  return (
    <section className="notifications-panel">
      <div className="notifications-toolbar">
        <span>{t('{0} unread notifications', items.length)}</span>
        <button className="text-button" disabled={busy || !items.length} onClick={onClear}>
          <CheckCheck size={15} />
          {t('Clear notifications')}
        </button>
      </div>
      {items.length ? (
        <div className="notifications-list">
          {items.map((item) => (
            <article className="notification-item" key={item.id}>
              <span className="notification-item-icon">
                <Bell size={15} />
              </span>
              <div>
                <p>{t(item.message)}</p>
                <time dateTime={item.at}>{new Date(item.at).toLocaleString(locale)}</time>
              </div>
              <button
                className="icon-button"
                disabled={busy}
                aria-label={t('Dismiss notification {0}', item.message)}
                onClick={() => onDismiss(item.id)}
              >
                <X size={14} />
              </button>
            </article>
          ))}
        </div>
      ) : (
        <div className="notifications-empty">
          <CheckCheck size={30} />
          <h2>{t('You’re all caught up.')}</h2>
          <p>{t('New workspace updates will appear here.')}</p>
        </div>
      )}
      <p className="notifications-footnote">
        {t('Clearing notifications keeps card history and workspace activity intact.')}
      </p>
    </section>
  );
}
