import { ArrowLeft, ArrowRight, Check, LogOut, Plus } from 'lucide-react';
import { useContext, useEffect, useState, type ReactNode } from 'react';
import { setStorageAccount } from '../../infrastructure/accountStorage';
import { apiEnabled } from '../../infrastructure/apiClient';
import {
  authRepository,
  authSessionKey,
  type Account,
  type Provider,
} from '../../infrastructure/auth';
import { useI18n, type Locale } from '../../shared/i18n';
import { ThemeSelect } from '../../shared/Theme';
import { ApiAuthBoundary } from './ApiAuthBoundary';
import { LogoutContext } from './LogoutContext';
function ProviderMark({ provider }: { provider: Provider }) {
  return provider === 'google' ? (
    <span className="google-mark" aria-hidden="true">
      G
    </span>
  ) : (
    <span className="microsoft-mark" aria-hidden="true">
      <i />
      <i />
      <i />
      <i />
    </span>
  );
}
export function LogoutButton() {
  const session = useContext(LogoutContext);
  const { t } = useI18n();
  if (!session) return null;
  return (
    <>
      <button
        type="button"
        className="logout-button"
        aria-label={t('Sign out')}
        title={t('Sign out')}
        disabled={session.busy}
        onClick={() => void session.logout()}
      >
        <LogOut size={17} />
        <span className="logout-label">{t(session.busy ? 'Working…' : 'Sign out')}</span>
      </button>
      {session.error && (
        <p className="logout-error" role="alert">
          {t(session.error)}
        </p>
      )}
    </>
  );
}
function LocalAuthBoundary({ children }: { children: ReactNode }) {
  const { t, locale, setLocale } = useI18n();
  const [user, setUser] = useState<Account | null>(null);
  const [loading, setLoading] = useState(true);
  const [provider, setProvider] = useState<Provider | null>(null);
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [register, setRegister] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const accept = (value: Account | null) => {
    setStorageAccount(value);
    setUser(value);
  };
  useEffect(() => {
    const refresh = () =>
      void authRepository.session().then((value) => {
        accept(value);
        setLoading(false);
      });
    refresh();
    const changed = (e: StorageEvent) => {
      if (e.key === authSessionKey || e.key === null) refresh();
    };
    window.addEventListener('storage', changed);
    return () => window.removeEventListener('storage', changed);
  }, []);
  async function choose(value: Provider) {
    setBusy(true);
    setError('');
    try {
      setAccounts(await authRepository.accounts(value));
      setProvider(value);
    } catch {
      setError('Could not load accounts. Please try again.');
    } finally {
      setBusy(false);
    }
  }
  async function signIn(name: string, email: string) {
    if (!provider) return;
    setBusy(true);
    setError('');
    try {
      accept(await authRepository.signIn(provider, name, email));
      setProvider(null);
      setRegister(false);
    } catch (e) {
      setError(
        e instanceof Error && e.message === 'Enter your name and a valid email address.'
          ? e.message
          : 'Could not sign in. Please try again.',
      );
    } finally {
      setBusy(false);
    }
  }
  if (loading)
    return (
      <div className="loading">
        kanbada<span>{t('Loading…')}</span>
      </div>
    );
  const logout = async () => {
    setBusy(true);
    setError('');
    try {
      await authRepository.signOut();
      accept(null);
      setProvider(null);
      setRegister(false);
    } catch {
      setError('Could not sign out. Please try again.');
    } finally {
      setBusy(false);
    }
  };
  if (user)
    return (
      <LogoutContext.Provider key={user.id} value={{ logout, busy, error }}>
        {children}
      </LogoutContext.Provider>
    );

  return (
    <main className="auth-page">
      <section className="auth-story">
        <a className="auth-logo" href="/">
          ▥ kanbada
        </a>
        <div>
          <span className="auth-eyebrow">{t('A LITTLE STRUCTURE. A LOT OF POSSIBILITY.')}</span>
          <h1>{t('Your next great thing starts here.')}</h1>
          <p>{t('Bring your people, projects, and everyday progress together.')}</p>
          <div className="auth-art" aria-hidden="true">
            <span>✳</span>
            <div>
              <i />
              <i />
              <i />
            </div>
          </div>
        </div>
        <small>{t('Less busywork. More progress.')}</small>
      </section>
      <section className="auth-content">
        <ThemeSelect compact />
        <select
          className="language-switch"
          aria-label={t('Interface language')}
          value={locale}
          onChange={(e) => setLocale(e.target.value as Locale)}
        >
          <option value="en-US">EN</option>
          <option value="pt-PT">PT</option>
        </select>
        <div className="auth-form">
          {provider && (
            <button
              className="auth-back"
              disabled={busy}
              onClick={() => {
                setProvider(null);
                setRegister(false);
                setError('');
              }}
            >
              <ArrowLeft size={16} />
              {t('Back')}
            </button>
          )}
          <span className="auth-eyebrow">KANBADA</span>
          <h2>
            {t(
              provider
                ? register
                  ? 'Create your account'
                  : 'Choose an account'
                : 'Welcome to your workspace.',
            )}
          </h2>
          <p>
            {t(
              provider
                ? 'Continue to Kanbada with your selected account.'
                : 'Sign in or create an account to get started.',
            )}
          </p>
          {!provider ? (
            <div className="provider-buttons">
              {(['google', 'microsoft'] as Provider[]).map((value) => (
                <button key={value} disabled={busy} onClick={() => void choose(value)}>
                  <ProviderMark provider={value} />
                  {t(value === 'google' ? 'Continue with Google' : 'Continue with Microsoft')}
                  <ArrowRight size={16} />
                </button>
              ))}
            </div>
          ) : (
            <>
              <div className="selected-provider">
                <ProviderMark provider={provider} />
                {provider === 'google' ? 'Google' : 'Microsoft'}
              </div>
              {register ? (
                <form
                  onSubmit={(e) => {
                    e.preventDefault();
                    const form = new FormData(e.currentTarget);
                    void signIn(String(form.get('name')), String(form.get('email')));
                  }}
                >
                  <label>
                    {t('Full name')}
                    <input name="name" autoComplete="name" required maxLength={80} />
                  </label>
                  <label>
                    {t('Email address')}
                    <input
                      name="email"
                      type="email"
                      autoComplete="email"
                      required
                      maxLength={254}
                    />
                  </label>
                  <button className="auth-submit" disabled={busy}>
                    <Check size={16} />
                    {t(busy ? 'Working…' : 'Continue')}
                  </button>
                </form>
              ) : (
                <div className="account-choices">
                  {accounts.map((account) => (
                    <button
                      key={account.id}
                      disabled={busy}
                      onClick={() => void signIn(account.name, account.email)}
                    >
                      <span className="account-initials">
                        {account.name
                          .split(' ')
                          .map((n) => n[0])
                          .slice(0, 2)
                          .join('')}
                      </span>
                      <span>
                        <b>{account.name}</b>
                        <small>{account.email}</small>
                      </span>
                      <ArrowRight size={16} />
                    </button>
                  ))}
                  <button disabled={busy} onClick={() => setRegister(true)}>
                    <Plus size={18} />
                    {t('Use another account')}
                  </button>
                </div>
              )}
            </>
          )}
          {error && (
            <p className="form-error" role="alert">
              {t(error)}
            </p>
          )}
          <p className="auth-footer">{t('One account. A little more clarity every day.')}</p>
        </div>
      </section>
    </main>
  );
}

export function AuthBoundary({ children }: { children: ReactNode }) {
  return apiEnabled ? (
    <ApiAuthBoundary>{children}</ApiAuthBoundary>
  ) : (
    <LocalAuthBoundary>{children}</LocalAuthBoundary>
  );
}
