import { ArrowDown, ArrowUp, Check, Plus, Trash2 } from 'lucide-react';
import { useState } from 'react';
import type { Definition } from '../../domain/models';
import { useI18n } from '../../shared/i18n';
export function WorkflowManager({
  kind,
  definitions,
  used,
  onSave,
}: {
  kind: 'statuses' | 'buckets' | 'swimlanes' | 'labels';
  definitions: Definition[];
  used: string[];
  onSave: (items: Definition[]) => Promise<void>;
}) {
  const { t } = useI18n();
  const singular =
    kind === 'statuses'
      ? 'status'
      : kind === 'buckets'
        ? 'bucket'
        : kind === 'labels'
          ? 'label'
          : 'swimlane';
  const [items, setItems] = useState(() => structuredClone(definitions));
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);
  const update = (id: string, patch: Partial<Definition>) =>
    setItems(items.map((item) => (item.id === id ? { ...item, ...patch } : item)));
  const reorder = (index: number, offset: number) => {
    const next = [...items];
    [next[index], next[index + offset]] = [next[index + offset], next[index]];
    setItems(next);
  };
  return (
    <form
      className="workflow-manager simple-form"
      onSubmit={async (e) => {
        e.preventDefault();
        const clean = items.map((item) => ({ ...item, name: item.name.trim() }));
        if (clean.some((item) => !item.name)) {
          setError('Give each ' + singular + ' a name.');
          return;
        }
        if (
          kind !== 'labels' &&
          clean.some((item) =>
            ['all', 'no bucket', 'no swimlane'].includes(item.name.toLowerCase()),
          )
        ) {
          setError('Choose a name other than All, No bucket, or No swimlane.');
          return;
        }
        if (new Set(clean.map((item) => item.name.toLowerCase())).size !== clean.length) {
          setError('Names must be unique.');
          return;
        }
        setError('');
        setSaving(true);
        try {
          await onSave(clean);
        } finally {
          setSaving(false);
        }
      }}
    >
      <h2>
        {kind === 'statuses'
          ? t('A workflow that works for you.')
          : kind === 'labels'
            ? t('A little color. A lot of clarity.')
            : t('Give your work a place.')}
      </h2>
      <p>
        {kind === 'statuses'
          ? t(
              'Statuses describe where a card is in your process. Mark completed stages to keep progress accurate.',
            )
          : kind === 'buckets'
            ? t(
                'Buckets group related cards independently of their status. They\u2019re shared across your workspace.',
              )
            : kind === 'labels'
              ? t(
                  'Labels highlight what matters. Create, color, and organize labels shared across your workspace.',
                )
              : t(
                  'Swimlanes organize this project into horizontal rows. New lanes start expanded.',
                )}{' '}
        {kind === 'swimlanes'
          ? t('Changes apply to the selected project.')
          : t('Changes apply across all projects.')}
      </p>
      <div className="definition-list">
        {items.map((item, index) => {
          const original = definitions.find((d) => d.id === item.id);
          const inUse = !!original && used.includes(original.name);
          return (
            <div className="definition-row" key={item.id}>
              <div className="definition-main">
                <input
                  type="color"
                  aria-label={t('Color for {0}', item.name)}
                  value={item.color}
                  onChange={(e) => update(item.id, { color: e.target.value })}
                />
                <input
                  aria-label={t(
                    '{0} name {1}',
                    kind === 'statuses'
                      ? 'Status'
                      : kind === 'buckets'
                        ? 'Bucket'
                        : kind === 'labels'
                          ? t('Label')
                          : 'Swimlane',
                    index + 1,
                  )}
                  value={item.name}
                  placeholder={t('Name')}
                  onChange={(e) => update(item.id, { name: e.target.value })}
                  required
                  maxLength={40}
                />
                <button
                  type="button"
                  className="icon-button"
                  aria-label={t('Move {0} up', item.name)}
                  disabled={index === 0}
                  onClick={() => reorder(index, -1)}
                >
                  <ArrowUp size={14} />
                </button>
                <button
                  type="button"
                  className="icon-button"
                  aria-label={t('Move {0} down', item.name)}
                  disabled={index === items.length - 1}
                  onClick={() => reorder(index, 1)}
                >
                  <ArrowDown size={14} />
                </button>
                <button
                  type="button"
                  className="icon-button danger"
                  aria-label={t('Delete {0}', item.name)}
                  disabled={inUse || (kind === 'statuses' && items.length === 1)}
                  title={
                    inUse && kind === 'labels'
                      ? t('Remove this label from its cards before deleting it.')
                      : inUse
                        ? 'Move cards out of this ' + singular + ' before deleting it.'
                        : t('Delete')
                  }
                  onClick={() => setItems(items.filter((d) => d.id !== item.id))}
                >
                  <Trash2 size={14} />
                </button>
              </div>
              <div className="definition-meta">
                {kind === 'statuses' && (
                  <label>
                    <input
                      type="checkbox"
                      checked={item.complete}
                      onChange={(e) => update(item.id, { complete: e.target.checked })}
                    />
                    {t('Counts as completed')}
                  </label>
                )}
                {inUse && (
                  <span>
                    {kind === 'labels'
                      ? t('In use · remove from cards before deleting')
                      : t('In use \u00B7 move cards before deleting')}
                  </span>
                )}
              </div>
            </div>
          );
        })}
      </div>
      <button
        type="button"
        className="secondary definition-add"
        onClick={() =>
          setItems([
            ...items,
            { id: crypto.randomUUID(), name: '', color: '#7490b0', complete: false },
          ])
        }
      >
        <Plus size={15} />
        {t('Add') + ' '}
        {t(singular)}
      </button>
      {error && (
        <p role="alert" className="form-error">
          {t(error)}
        </p>
      )}
      <button className="primary" disabled={saving}>
        <Check size={16} />
        {saving ? t('Saving\u2026') : t('Save ' + kind)}
      </button>
    </form>
  );
}
