import { Camera, Check, Trash2 } from 'lucide-react';
import { useState } from 'react';
import type { Workspace } from '../../domain/models';
import { useI18n } from '../../shared/i18n';

async function readImage(file: File, banner: boolean): Promise<string> {
  if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type))
    throw new Error('Choose a JPG, PNG, or WebP image.');
  if (file.size > 10 * 1024 * 1024) throw new Error('Choose an image smaller than 10 MB.');
  const url = URL.createObjectURL(file);
  try {
    const image = new Image();
    image.src = url;
    await image.decode();
    const canvas = document.createElement('canvas');
    const scale = Math.min(1, (banner ? 1600 : 256) / Math.max(image.width, image.height));
    canvas.width = Math.max(1, Math.round(image.width * scale));
    canvas.height = Math.max(1, Math.round(image.height * scale));
    const ctx = canvas.getContext('2d');
    if (!ctx) throw new Error('Could not load this image.');
    ctx.fillStyle = '#e3ecf7';
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    ctx.drawImage(image, 0, 0, canvas.width, canvas.height);
    return canvas.toDataURL('image/jpeg', 0.82);
  } catch (error) {
    if (error instanceof Error && error.message === 'Could not load this image.') throw error;
    throw new Error('This file could not be opened as an image.', { cause: error });
  } finally {
    URL.revokeObjectURL(url);
  }
}
export function WorkspaceAppearance({
  workspace,
  onSave,
}: {
  workspace: Workspace;
  onSave: (workspace: Workspace) => Promise<boolean>;
}) {
  const { t } = useI18n();
  const [icon, setIcon] = useState(workspace.icon);
  const [banner, setBanner] = useState(workspace.banner);
  const [position, setPosition] = useState(workspace.bannerPosition ?? 50);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  async function upload(file: File | undefined, isBanner: boolean) {
    if (!file) return;
    setBusy(true);
    setError('');
    try {
      const image = await readImage(file, isBanner);
      if (isBanner) {
        setBanner(image);
        setPosition(50);
      } else setIcon(image);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load this image.');
    } finally {
      setBusy(false);
    }
  }
  return (
    <form
      className="simple-form workspace-appearance"
      onSubmit={async (e) => {
        e.preventDefault();
        if (busy) return;
        setBusy(true);
        setError('');
        try {
          if (!(await onSave({ ...workspace, icon, banner, bannerPosition: position })))
            setError('Could not save workspace images. Please try again.');
        } catch {
          setError('Could not save workspace images. Please try again.');
        } finally {
          setBusy(false);
        }
      }}
    >
      <h2>{t('Workspace appearance')}</h2>
      <p>{t('Make this space yours with an icon and a banner.')}</p>
      <div className="workspace-image-row">
        <div className="workspace-icon-preview">
          {icon ? <img src={icon} alt={t('Workspace icon preview')} /> : workspace.name[0]}
        </div>
        <div>
          <h3>{t('Workspace icon')}</h3>
          <label className="image-upload-button">
            <Camera size={15} />
            {t('Change icon')}
            <input
              aria-label={t('Workspace icon')}
              type="file"
              accept="image/jpeg,image/png,image/webp"
              disabled={busy}
              onChange={(e) => {
                void upload(e.target.files?.[0], false);
                e.target.value = '';
              }}
            />
          </label>
          {icon && (
            <button
              className="text-button danger"
              type="button"
              disabled={busy}
              onClick={() => setIcon(undefined)}
            >
              <Trash2 size={13} />
              {t('Remove icon')}
            </button>
          )}
        </div>
      </div>
      <h3>{t('Workspace banner')}</h3>
      <div className="workspace-banner-preview">
        {banner ? (
          <img
            src={banner}
            style={{ objectPosition: `center ${position}%` }}
            alt={t('Workspace banner preview')}
          />
        ) : (
          <span>✳</span>
        )}
      </div>
      <div className="workspace-image-actions">
        <label className="image-upload-button">
          <Camera size={15} />
          {t('Change banner')}
          <input
            aria-label={t('Workspace banner')}
            type="file"
            accept="image/jpeg,image/png,image/webp"
            disabled={busy}
            onChange={(e) => {
              void upload(e.target.files?.[0], true);
              e.target.value = '';
            }}
          />
        </label>
        {banner && (
          <button
            className="text-button danger"
            type="button"
            disabled={busy}
            onClick={() => setBanner(undefined)}
          >
            <Trash2 size={13} />
            {t('Remove banner')}
          </button>
        )}
      </div>
      {banner && (
        <label>
          {t('Banner position')}
          <input
            type="range"
            min="0"
            max="100"
            value={position}
            disabled={busy}
            onChange={(e) => setPosition(Number(e.target.value))}
          />
        </label>
      )}
      <p>{t('JPG, PNG or WebP · up to 10 MB. Square icons and wide banners work best.')}</p>
      {error && (
        <p className="form-error" role="alert">
          {t(error)}
        </p>
      )}
      <button className="primary" disabled={busy}>
        <Check size={15} />
        {t(busy ? 'Working…' : 'Save workspace images')}
      </button>
    </form>
  );
}
