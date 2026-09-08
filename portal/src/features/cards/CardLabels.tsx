import type { Definition } from '../../domain/models';
import { useI18n } from '../../shared/i18n';

export function CardLabels({ names, definitions }: { names: string[]; definitions: Definition[] }) {
  const { t } = useI18n();
  if (!names.length) return null;
  return (
    <span className="card-labels" aria-label={t('Labels')}>
      {definitions
        .filter((label) => names.includes(label.name))
        .map((label) => (
          <span
            key={label.id}
            className="label-pill"
            title={label.name}
            style={{ backgroundColor: `${label.color}20`, borderColor: `${label.color}45` }}
          >
            <i style={{ backgroundColor: label.color }} />
            {label.name}
          </span>
        ))}
    </span>
  );
}
