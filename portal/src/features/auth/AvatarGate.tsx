import { useEffect, useState } from 'react';
import { apiRequest } from '../../infrastructure/apiClient';
import { Brand } from '../../shared/Brand';
import { useI18n } from '../../shared/i18n';

export function AvatarGate({
  onComplete,
  onLogout,
}: {
  onComplete: () => void;
  onLogout: () => void;
}) {
  const { t } = useI18n();
  const [gravatar, setGravatar] = useState<string | null>(null);
  const [lookup, setLookup] = useState('Checking Gravatar…');
  const [custom, setCustom] = useState<string | null>(null);
  const [source, setSource] = useState<'gravatar' | 'custom' | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  useEffect(() => {
    let active = true;
    void apiRequest<{ photo: string | null }>('/auth/avatar/gravatar')
      .then((result) => {
        if (!active) return;
        setGravatar(result.photo);
        setLookup(
          result.photo
            ? 'A Gravatar photo is available for your email.'
            : 'No Gravatar photo was found. Upload your own photo.',
        );
      })
      .catch(() => {
        if (active) setLookup('Gravatar is unavailable. You can upload your own photo.');
      });
    return () => {
      active = false;
    };
  }, []);
  return (
    <main className="enrollment-page">
      <section className="enrollment-card avatar-enrollment">
        <div className="logo">
          <Brand />
        </div>
        <h1>{t('Choose your profile photo')}</h1>
        <p>
          {t(
            'A profile photo is required to finish registration. Choose your Gravatar or upload your own image.',
          )}
        </p>
        <p>
          {t('Put a face to your name — a photo helps your team recognise you and feel closer.')}
        </p>
        <p role="status">{t(lookup)}</p>
        <div className="avatar-choices">
          {gravatar && (
            <button
              type="button"
              className="avatar-choice"
              aria-pressed={source === 'gravatar'}
              disabled={busy}
              onClick={() => setSource('gravatar')}
            >
              <img src={gravatar} alt={t('Gravatar photo')} />
              <span>{t('Use Gravatar')}</span>
            </button>
          )}
          {custom && (
            <button
              type="button"
              className="avatar-choice"
              aria-pressed={source === 'custom'}
              disabled={busy}
              onClick={() => setSource('custom')}
            >
              <img src={custom} alt={t('Uploaded photo preview')} />
              <span>{t('Use my photo')}</span>
            </button>
          )}
        </div>
        <label className="avatar-upload">
          {t('Upload photo')}
          <input
            type="file"
            accept="image/jpeg,image/png,image/webp,image/gif"
            disabled={busy}
            onChange={async (event) => {
              const file = event.target.files?.[0];
              event.target.value = '';
              if (!file) return;
              setError('');
              setBusy(true);
              try {
                if (
                  !['image/jpeg', 'image/png', 'image/webp', 'image/gif'].includes(file.type) ||
                  file.size > 2 * 1024 * 1024
                )
                  throw new Error('Choose a JPG, PNG, WebP, or GIF image smaller than 2 MB.');
                const bitmap = await createImageBitmap(file);
                try {
                  const canvas = document.createElement('canvas');
                  canvas.width = 512;
                  canvas.height = 512;
                  const context = canvas.getContext('2d');
                  if (!context) throw new Error('Could not read this image.');
                  const side = Math.min(bitmap.width, bitmap.height);
                  context.drawImage(
                    bitmap,
                    (bitmap.width - side) / 2,
                    (bitmap.height - side) / 2,
                    side,
                    side,
                    0,
                    0,
                    512,
                    512,
                  );
                  setCustom(canvas.toDataURL('image/png'));
                  setSource('custom');
                } finally {
                  bitmap.close();
                }
              } catch (e) {
                setError(e instanceof Error ? e.message : 'Could not read this image.');
              } finally {
                setBusy(false);
              }
            }}
          />
        </label>
        <p>{t('JPG, PNG, WebP or GIF · up to 2 MB')}</p>
        {error && (
          <p role="alert" className="form-error">
            {t(error)}
          </p>
        )}
        <div className="two-factor-actions">
          <button
            className="primary"
            disabled={busy || !source}
            onClick={async () => {
              setBusy(true);
              setError('');
              try {
                await apiRequest('/auth/avatar', {
                  method: 'POST',
                  body: JSON.stringify(
                    source === 'gravatar' ? { useGravatar: true } : { photo: custom },
                  ),
                });
                onComplete();
              } catch (e) {
                setError(e instanceof Error ? e.message : 'Could not save your photo.');
              } finally {
                setBusy(false);
              }
            }}
          >
            {t(busy ? 'Working…' : 'Continue')}
          </button>
          <button className="secondary" disabled={busy} onClick={onLogout}>
            {t('Sign out')}
          </button>
        </div>
      </section>
    </main>
  );
}
