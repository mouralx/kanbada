import { Download, FileText, Paperclip, Trash2, Upload } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { fileRepository, type Attachment } from '../../infrastructure/attachments';
import { useI18n } from '../../shared/i18n';
export function Attachments({
  items,
  onChange,
  onBusy,
}: {
  items: Attachment[];
  onChange: (items: Attachment[]) => void;
  onBusy: (busy: boolean) => void;
}) {
  const { t } = useI18n();
  const input = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const live = useRef(true);
  useEffect(() => {
    live.current = true;
    return () => {
      live.current = false;
    };
  }, []);
  async function upload(files: FileList | null) {
    if (!files?.length || busy) return;
    setError('');
    setBusy(true);
    onBusy(true);
    const added: Attachment[] = [];
    try {
      for (const file of Array.from(files)) {
        if (file.size > 25 * 1024 * 1024) throw new Error('Each file must be 25 MB or smaller.');
        added.push(await fileRepository.upload(file));
      }
      if (live.current) onChange([...items, ...added]);
      else await Promise.all(added.map((a) => fileRepository.remove(a.id)));
    } catch (e) {
      await Promise.all(added.map((a) => fileRepository.remove(a.id)));
      if (live.current) setError(e instanceof Error ? e.message : 'Unable to upload files.');
    } finally {
      if (live.current) {
        setBusy(false);
        onBusy(false);
        if (input.current) input.current.value = '';
      }
    }
  }
  return (
    <section className="attachments">
      <div className="checklist-heading">
        <h4>
          <Paperclip size={14} />
          {t('Attachments')}
        </h4>
        <span>
          {items.length} {items.length === 1 ? t('file') : t('files')}
        </span>
      </div>
      <input
        ref={input}
        type="file"
        multiple
        aria-label={t('Attach documents')}
        onChange={(e) => void upload(e.target.files)}
        hidden
      />
      <button
        type="button"
        className="upload-zone"
        disabled={busy}
        onClick={() => input.current?.click()}
        onDragOver={(e) => e.preventDefault()}
        onDrop={(e) => {
          e.preventDefault();
          void upload(e.dataTransfer.files);
        }}
      >
        <Upload size={19} />
        <span>
          {busy ? t('Uploading files\u2026') : t('Drop documents here or browse files')}
          <small>{t('Up to 25 MB per file \u00B7 saved with this card')}</small>
        </span>
      </button>
      {items.map((a) => (
        <div className="attachment-row" key={a.id}>
          <FileText size={19} />
          <div>
            <strong>{a.name}</strong>
            <small>
              {a.size < 1024
                ? t('{0} B', a.size)
                : a.size < 1024 * 1024
                  ? t('{0} KB', (a.size / 1024).toFixed(1))
                  : t('{0} MB', (a.size / 1024 / 1024).toFixed(1))}
            </small>
          </div>
          <button
            type="button"
            disabled={busy}
            className="icon-button"
            aria-label={t('Download {0}', a.name)}
            onClick={() => fileRepository.download(a).catch((e) => setError(e.message))}
          >
            <Download size={15} />
          </button>
          <button
            type="button"
            disabled={busy}
            className="icon-button danger"
            aria-label={t('Remove attachment {0}', a.name)}
            onClick={() => onChange(items.filter((item) => item.id !== a.id))}
          >
            <Trash2 size={15} />
          </button>
        </div>
      ))}
      {error && (
        <p role="alert" className="form-error">
          {t(error)}
        </p>
      )}
    </section>
  );
}
