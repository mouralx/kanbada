import { Camera, Check, Trash2 } from 'lucide-react';
import { useRef, useState } from 'react';
import type { Member } from '../../domain/models';
import { apiEnabled } from '../../infrastructure/apiClient';
import { useI18n } from '../../shared/i18n';
import { LogoutButton } from '../auth/AuthBoundary';
export function ProfileForm({
  member,
  onSave,
}: {
  member: Member;
  onSave: (name: string, email: string, photo?: string) => Promise<void>;
}) {
  const { t } = useI18n();
  const [photo, setPhoto] = useState(member.photo);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const input = useRef<HTMLInputElement>(null);
  async function selectPhoto(file?: File) {
    if (!file) return;
    setError('');
    if (!['image/jpeg', 'image/png', 'image/webp', 'image/gif'].includes(file.type)) {
      setError('Choose a JPG, PNG, WebP, or GIF image.');
      return;
    }
    if (file.size > 2 * 1024 * 1024) {
      setError('Choose an image smaller than 2 MB.');
      return;
    }
    setBusy(true);
    try {
      const url = await new Promise<string>((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(String(reader.result));
        reader.onerror = () => reject(new Error('Could not read this image.'));
        reader.readAsDataURL(file);
      });
      await new Promise<void>((resolve, reject) => {
        const image = new Image();
        image.onload = () => resolve();
        image.onerror = () => reject(new Error('This file could not be opened as an image.'));
        image.src = url;
      });
      setPhoto(url);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load this image.');
    } finally {
      setBusy(false);
      if (input.current) input.current.value = '';
    }
  }
  return (
    <form
      className="simple-form"
      onSubmit={async (e) => {
        e.preventDefault();
        if (busy) return;
        const form = new FormData(e.currentTarget);
        setBusy(true);
        try {
          await onSave(String(form.get('name')).trim(), String(form.get('email')).trim(), photo);
        } finally {
          setBusy(false);
        }
      }}
    >
      <h2>{t('A little about you.')}</h2>
      <div className="profile-photo-editor">
        <div className="profile-photo-preview" style={{ background: member.color }}>
          {photo ? (
            <img src={photo} alt={t('Profile photo preview')} />
          ) : (
            <span>{member.initials}</span>
          )}
          <span className="photo-camera">
            <Camera size={15} />
          </span>
        </div>
        <div>
          <button
            type="button"
            className="secondary photo-select"
            disabled={busy}
            onClick={() => input.current?.click()}
          >
            <Camera size={15} />
            {busy ? t('Loading\u2026') : t('Change photo')}
          </button>
          <p>{t('JPG, PNG, WebP or GIF \u00B7 up to 2 MB')}</p>
          {photo && (
            <button
              type="button"
              className="text-button danger"
              disabled={busy}
              onClick={() => setPhoto(undefined)}
            >
              <Trash2 size={12} />
              {t('Remove photo')}
            </button>
          )}
        </div>
      </div>
      <input
        type="file"
        ref={input}
        aria-label={t('Profile picture')}
        accept="image/jpeg,image/png,image/webp,image/gif"
        hidden
        onChange={(e) => void selectPhoto(e.target.files?.[0])}
      />
      {error && (
        <p className="form-error" role="alert">
          {t(error)}
        </p>
      )}
      <label>
        {t('Name')}
        <input name="name" defaultValue={member.name} required pattern=".*\S.*" />
      </label>
      <label>
        {t('Email')}
        <input
          name="email"
          type="email"
          readOnly={apiEnabled}
          defaultValue={member.email}
          required
        />
      </label>
      <button className="primary" disabled={busy}>
        <Check size={15} />
        {t('Save profile')}
      </button>
      <div className="profile-signout">
        <LogoutButton />
      </div>
    </form>
  );
}
