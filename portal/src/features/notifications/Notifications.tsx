import { Bell, CheckCheck, X } from 'lucide-react';
import type { Notification } from '../../domain/models';
import { useI18n } from '../../shared/i18n';
import { useCallback, useState } from 'react';
import { apiRequest } from '../../infrastructure/apiClient';
import { cardPath } from '../../infrastructure/cards';
import { PagedCollection } from '../board/PagedCards';
export function Notifications({
  items,
  busy,
  onClear,
  onDismiss,
  total = items.length,
}: {
  items: Notification[];
  busy: boolean;
  onClear: () => void;
  onDismiss: (id: string) => void;
  total?: number;
}) {
  const { t, locale } = useI18n();
  return (
    <section className="notifications-panel">
      <div className="notifications-toolbar">
        <span>{t('{0} unread notifications', total)}</span>
        <button className="text-button" disabled={busy || !total} onClick={onClear}>
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
                <p>
                  {item.cardId
                    ? t('You were assigned to {0} ({1}).', item.message, item.cardId)
                    : t(item.message)}
                </p>
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

export function RemoteNotifications({
  workspace,
  version,
  onChanged,
}: {
  workspace: string;
  version: number;
  onChanged: () => Promise<void>;
}) {
  const { t } = useI18n();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const loadPage = useCallback(
    (after: string | undefined, signal: AbortSignal) =>
      apiRequest<{ items: Notification[]; total: number; nextCursor: string | null }>(
        cardPath(workspace) +
          '/notification-feed' +
          (after ? '?after=' + encodeURIComponent(after) : ''),
        { signal },
      ),
    [workspace],
  );
  const dismiss = async (id?: string) => {
    setBusy(true);
    setError('');
    try {
      await apiRequest(
        cardPath(workspace) +
          '/notification-feed' +
          (id ? '?notificationId=' + encodeURIComponent(id) : ''),
        {
          method: 'DELETE',
          headers: { 'If-Match': String(version) },
        },
      );
      await onChanged();
    } catch (error) {
      setError(error instanceof Error ? error.message : 'Could not save changes.');
    } finally {
      setBusy(false);
    }
  };
  return (
    <>
      {error && <p role="alert">{t(error)}</p>}
      <PagedCollection key={workspace + ':' + version} loadPage={loadPage}>
        {(items, total) => (
          <Notifications
            items={items}
            total={total}
            busy={busy}
            onClear={() => void dismiss()}
            onDismiss={(id) => void dismiss(id)}
          />
        )}
      </PagedCollection>
    </>
  );
}
