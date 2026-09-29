import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import type { Task } from '../../domain/models';
import { apiEnabled } from '../../infrastructure/apiClient';
import {
  getCards,
  getSummary,
  queryString,
  type CardQuery,
  type CardSummary,
} from '../../infrastructure/cards';
import { useI18n } from '../../shared/i18n';

export function useCardSummary(
  workspace: string | undefined,
  version: number | undefined,
  query: CardQuery,
) {
  const key = queryString(query);
  const [result, setResult] = useState<{ key: string; summary: CardSummary } | null>(null);
  const [error, setError] = useState('');
  const [retry, setRetry] = useState(0);
  const identity = workspace + ':' + version + ':' + key;
  useEffect(() => {
    if (!apiEnabled || !workspace) return;
    const controller = new AbortController();
    getSummary(workspace, key, controller.signal)
      .then((summary) => {
        if (controller.signal.aborted) return;
        setResult({ key: identity, summary });
        setError('');
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted)
          setError(error instanceof Error ? error.message : 'Could not load cards.');
      });
    return () => controller.abort();
  }, [workspace, identity, key, retry]);
  return {
    summary: result?.key === identity ? result.summary : null,
    error,
    retry: () => setRetry((n) => n + 1),
  };
}

export function PagedCards({
  workspace,
  query,
  children,
  infinite = false,
  total,
}: {
  workspace: string;
  query: CardQuery;
  children: (items: Task[]) => ReactNode;
  infinite?: boolean;
  total?: number;
}) {
  const key = queryString(query);
  const loadPage = useCallback(
    (after: string | undefined, signal: AbortSignal) => getCards(workspace, key, after, signal),
    [workspace, key],
  );
  return (
    <PagedCollection
      key={workspace + ':' + key}
      loadPage={loadPage}
      infinite={infinite}
      total={total}
      counter="{0} of {1} cards loaded"
      loadLabel="Load more cards"
    >
      {children}
    </PagedCollection>
  );
}

export function PagedCollection<T extends { id: string }>({
  loadPage,
  children,
  infinite = false,
  total,
  counter = '{0} of {1} items loaded',
  loadLabel = 'Load more',
}: {
  loadPage: (
    after: string | undefined,
    signal: AbortSignal,
  ) => Promise<{ items: T[]; total: number; nextCursor: string | null }>;
  children: (items: T[], total: number | undefined) => ReactNode;
  infinite?: boolean;
  total?: number;
  counter?: string;
  loadLabel?: string;
}) {
  const { t } = useI18n();
  const [items, setItems] = useState<T[]>([]);
  const [cursor, setCursor] = useState<string | null | undefined>(undefined);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [count, setCount] = useState(total);
  const anchor = useRef<HTMLDivElement>(null);
  const request = useRef<AbortController | null>(null);
  const load = useCallback(
    async (restart = false) => {
      if (request.current || (!restart && cursor === null)) return;
      const controller = new AbortController();
      request.current = controller;
      setLoading(true);
      setError('');
      try {
        const page = await loadPage((restart ? undefined : cursor) ?? undefined, controller.signal);
        if (controller.signal.aborted) return;
        setItems((previous) => {
          const existing = restart ? [] : previous;
          const ids = new Set(existing.map((item) => item.id));
          return [...existing, ...page.items.filter((item) => !ids.has(item.id))];
        });
        setCursor(page.nextCursor);
        setCount(page.total);
      } catch (error) {
        if (!controller.signal.aborted)
          setError(error instanceof Error ? error.message : 'Could not load cards.');
      } finally {
        if (!controller.signal.aborted) {
          request.current = null;
          setLoading(false);
        }
      }
    },
    [loadPage, cursor],
  );
  useEffect(
    () => () => {
      request.current?.abort();
      request.current = null;
    },
    [],
  );
  useEffect(() => {
    if (!infinite && cursor === undefined && !error) void load();
  }, [infinite, cursor, error, load]);
  useEffect(() => {
    const node = anchor.current;
    if (!node || error || loading || cursor === null) return;
    if (!infinite) return;
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting)) void load();
      },
      { rootMargin: '160px' },
    );
    observer.observe(node);
    return () => observer.disconnect();
  }, [cursor, error, loading, infinite, load]);
  return (
    <>
      {children(items, count)}
      <div ref={anchor} className="card-pagination" aria-live="polite">
        {error ? (
          <>
            <p role="alert">{t(error)}</p>
            <button className="secondary" onClick={() => void load(true)}>
              {t('Retry')}
            </button>
          </>
        ) : loading ? (
          <span>{t('Loading cards…')}</span>
        ) : (
          cursor !== null && (
            <button className="secondary" onClick={() => void load()}>
              {t(loadLabel)}
            </button>
          )
        )}
        {count !== undefined && <small>{t(counter, items.length, count)}</small>}
      </div>
    </>
  );
}
