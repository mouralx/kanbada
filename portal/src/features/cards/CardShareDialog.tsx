import { Copy, Link, ShieldCheck, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { cardShareUrl, shareRepository, type CardShare } from '../../infrastructure/sharing';
import { useI18n } from '../../shared/i18n';
export function CardShareDialog({ workspaceId, cardId }: { workspaceId: string; cardId: string }) {
  const { t, locale } = useI18n();
  const [share, setShare] = useState<CardShare | null>(null);
  const [access, setAccess] = useState<CardShare['access']>('signed-in');
  const [days, setDays] = useState(0);
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState('');
  const [copied, setCopied] = useState(false);
  useEffect(() => {
    shareRepository
      .current(workspaceId, cardId)
      .then((value) => {
        setShare(value);
        if (value) {
          setAccess(value.access);
          setDays(
            value.expiresAt
              ? Math.round((Date.parse(value.expiresAt) - Date.parse(value.createdAt)) / 86400000)
              : 0,
          );
        }
      })
      .catch(() => setError('Could not load the sharing settings.'))
      .finally(() => setBusy(false));
  }, [workspaceId, cardId]);
  const run = async (fn: () => Promise<void>) => {
    setBusy(true);
    setError('');
    try {
      await fn();
    } catch {
      setError('Could not update or copy the sharing link. Please try again.');
    } finally {
      setBusy(false);
    }
  };
  return (
    <div className="simple-form share-form">
      <span className="share-symbol">
        <Link size={25} />
      </span>
      <h2>{t('Share this card')}</h2>
      <p>{t('Give others a direct view of this card, its files, and its history.')}</p>
      <div className="share-access-note">
        <ShieldCheck size={18} />
        <span>{t('Sign-in required · View only')}</span>
      </div>
      <label>
        {t('Who can open this link?')}
        <select
          aria-label={t('Link access')}
          value={access}
          disabled={busy}
          onChange={(e) => {
            setAccess(e.target.value as CardShare['access']);
            setCopied(false);
          }}
        >
          <option value="signed-in">{t('Anyone signed in with the link')}</option>
          <option value="members">{t('Workspace members only')}</option>
        </select>
      </label>
      <label>
        {t('Link expiration')}
        <select
          value={days}
          disabled={busy}
          onChange={(e) => {
            setDays(Number(e.target.value));
            setCopied(false);
          }}
        >
          <option value={0}>{t('No expiration')}</option>
          <option value={7}>{t('7 days')}</option>
          <option value={30}>{t('30 days')}</option>
        </select>
      </label>
      {share && (
        <>
          <label>
            {t('Share link')}
            <input
              aria-label={t('Share link')}
              readOnly
              value={cardShareUrl(share.token)}
              onFocus={(e) => e.target.select()}
            />
          </label>
          <p>
            {t(
              share.access === 'members'
                ? 'Workspace members only'
                : 'Anyone signed in with the link',
            )}{' '}
            ·{' '}
            {share.expiresAt
              ? new Date(share.expiresAt).toLocaleDateString(locale)
              : t('No expiration')}
          </p>
        </>
      )}
      <button
        className="primary"
        disabled={busy}
        onClick={() =>
          void run(async () => {
            const value = await shareRepository.create(workspaceId, cardId, access, days);
            setShare(value);
            setCopied(false);
          })
        }
      >
        <Link size={15} />
        {t(share ? 'Replace link with these settings' : 'Create share link')}
      </button>
      {share && (
        <div className="share-actions">
          <button
            className="secondary"
            disabled={busy}
            onClick={() =>
              void run(async () => {
                await navigator.clipboard.writeText(cardShareUrl(share.token));
                setCopied(true);
              })
            }
          >
            <Copy size={15} />
            {t(copied ? 'Link copied' : 'Copy link')}
          </button>
          <button
            className="text-button danger"
            disabled={busy}
            onClick={() =>
              void run(async () => {
                await shareRepository.revoke(share.token);
                setShare(null);
                setCopied(false);
              })
            }
          >
            <Trash2 size={14} />
            {t('Revoke link')}
          </button>
        </div>
      )}
      {share && <p>{t('Replacing or revoking a link stops the previous link from working.')}</p>}
      {copied && <p role="status">{t('Link copied')}</p>}
      {error && (
        <p className="form-error" role="alert">
          {t(error)}
        </p>
      )}
    </div>
  );
}
