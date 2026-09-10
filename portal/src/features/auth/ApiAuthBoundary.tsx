import { AvatarGate } from './AvatarGate';
import { TwoFactorGate } from './TwoFactorGate';
import { Brand } from '../../shared/Brand';
import { ArrowRight, LockKeyhole, Mail } from 'lucide-react';
import { useEffect, useState, type ReactNode } from 'react';
import { setStorageAccount } from '../../infrastructure/accountStorage';
import { apiRequest } from '../../infrastructure/apiClient';
import type { Account } from '../../infrastructure/auth';
import { useI18n, type Locale } from '../../shared/i18n';
import { ThemeSelect } from '../../shared/Theme';
import { LogoutContext } from './LogoutContext';
type Session = {
  user: Account | null;
  providers: { google: boolean; microsoft: boolean };
  avatarRequired?: boolean;
  twoFactorSetupRequired?: boolean;
  twoFactorVerificationRequired?: boolean;
};
export function ApiAuthBoundary({ children }: { children: ReactNode }) {
  const { t, locale, setLocale } = useI18n();
  const [enrollmentActive, setEnrollmentActive] = useState(false);
  const [session, setSession] = useState<Session | null>(null);
  const [register, setRegister] = useState(false);
  const [emailForm, setEmailForm] = useState(false);
  const [twoFactorRequired, setTwoFactorRequired] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const refresh = async () => {
    try {
      const value = await apiRequest<Session>('/auth/session');
      if (value.twoFactorSetupRequired) setEnrollmentActive(true);
      setStorageAccount(value.user);
      setSession(value);
    } catch {
      setError('Could not connect to Kanbada. Please try again.');
      setSession({ user: null, providers: { google: false, microsoft: false } });
    }
  };
  useEffect(() => {
    void refresh();
    const update = (e: StorageEvent) => {
      if (e.key === 'kanbada-auth-change') void refresh();
    };
    const expired = () => {
      setStorageAccount(null);
      setSession((previous) => (previous ? { ...previous, user: null } : null));
    };
    window.addEventListener('storage', update);
    window.addEventListener('focus', refresh);
    window.addEventListener('kanbada-session-expired', expired);
    const timer = setInterval(refresh, 60000);
    return () => {
      clearInterval(timer);
      window.removeEventListener('storage', update);
      window.removeEventListener('focus', refresh);
      window.removeEventListener('kanbada-session-expired', expired);
    };
  }, []);
  const signal = () => {
    try {
      localStorage.setItem('kanbada-auth-change', String(Date.now()));
    } catch {
      /* Cookies still control the session. */
    }
  };
  const logout = async () => {
    setBusy(true);
    setError('');
    try {
      await apiRequest('/auth/logout', { method: 'POST' });
      setEnrollmentActive(false);
      setStorageAccount(null);
      setSession((previous) => (previous ? { ...previous, user: null } : null));
      signal();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not sign out. Please try again.');
    } finally {
      setBusy(false);
    }
  };
  if (!session)
    return (
      <div className="loading">
        kanbada<span>{t('Loading…')}</span>
      </div>
    );
  if (session.user && session.avatarRequired)
    return <AvatarGate onComplete={() => void refresh()} onLogout={() => void logout()} />;
  if (
    session.user &&
    (enrollmentActive || session.twoFactorSetupRequired || session.twoFactorVerificationRequired)
  )
    return (
      <TwoFactorGate
        setup={enrollmentActive || !!session.twoFactorSetupRequired}
        onComplete={() => {
          setEnrollmentActive(false);
          void refresh();
        }}
        onLogout={() => void logout()}
      />
    );
  if (session.user)
    return (
      <LogoutContext.Provider key={session.user.id} value={{ logout, busy, error }}>
        {children}
      </LogoutContext.Provider>
    );
  return (
    <main className="auth-page">
      <section className="auth-story">
        <a className="logo auth-logo" href="/">
          <Brand />
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
        <div className="auth-preferences">
          <ThemeSelect compact iconOnly />
          <select
            className="language-switch"
            aria-label={t('Interface language')}
            value={locale}
            onChange={(e) => setLocale(e.target.value as Locale)}
          >
            <option value="en-US">EN</option>
            <option value="pt-PT">PT</option>
          </select>
        </div>
        <div className="auth-form">
          <span className="auth-eyebrow">KANBADA</span>
          <h2>{t(register ? 'Create your account' : 'Welcome to your workspace.')}</h2>
          <p>{t('Sign in or create an account to get started.')}</p>
          <div className="provider-buttons">
            {(['google', 'microsoft'] as const).map((provider) => (
              <button
                key={provider}
                disabled={!session.providers[provider] || busy}
                title={
                  !session.providers[provider]
                    ? t('This sign-in provider is not configured.')
                    : undefined
                }
                onClick={() =>
                  window.location.assign(
                    '/api/auth/' +
                      provider +
                      '/start?returnUrl=' +
                      encodeURIComponent(window.location.pathname + window.location.search),
                  )
                }
              >
                <LockKeyhole size={18} />
                {t(provider === 'google' ? 'Continue with Google' : 'Continue with Microsoft')}
                <ArrowRight size={15} />
              </button>
            ))}
            <button onClick={() => setEmailForm(!emailForm)}>
              <Mail size={18} />
              {t('Continue with email')}
              <ArrowRight size={15} />
            </button>
          </div>
          {emailForm && (
            <form
              onSubmit={async (e) => {
                e.preventDefault();
                const element = e.currentTarget;
                const form = new FormData(element);
                setBusy(true);
                setError('');
                try {
                  const result = await apiRequest<{ twoFactorRequired?: boolean } | undefined>(
                    '/auth/' + (register ? 'register' : 'login'),
                    {
                      method: 'POST',
                      body: JSON.stringify({
                        name: form.get('name') || '',
                        email: form.get('email'),
                        password: form.get('password'),
                        code: form.get('code') || undefined,
                      }),
                    },
                  );
                  if (result?.twoFactorRequired) {
                    setTwoFactorRequired(true);
                    return;
                  }
                  element.reset();
                  setRegister(false);
                  setEmailForm(false);
                  setTwoFactorRequired(false);
                  await refresh();
                  signal();
                } catch (e) {
                  setError(e instanceof Error ? e.message : 'Could not sign in. Please try again.');
                } finally {
                  setBusy(false);
                }
              }}
            >
              {register && (
                <label>
                  {t('Full name')}
                  <input name="name" autoComplete="name" required maxLength={120} />
                </label>
              )}
              <label>
                {t('Email address')}
                <input name="email" type="email" autoComplete="username" required />
              </label>
              <label>
                {t('Password')}
                <input
                  name="password"
                  type="password"
                  autoComplete={register ? 'new-password' : 'current-password'}
                  minLength={register ? 12 : 1}
                  maxLength={200}
                  required
                />
              </label>
              {twoFactorRequired && !register && (
                <>
                  <label>
                    {t('Authenticator or recovery code')}
                    <input
                      name="code"
                      aria-describedby="auth-code-help"
                      autoComplete="one-time-code"
                      autoFocus
                      required
                      maxLength={100}
                    />
                  </label>
                  <p id="auth-code-help">
                    {t(
                      'Enter a code from your authenticator app, or one of your saved recovery codes.',
                    )}
                  </p>
                </>
              )}
              {register && (
                <>
                  <p>{t('Use at least 12 characters.')}</p>
                  <p>{t('An authenticator app is required to finish registration.')}</p>
                </>
              )}
              <button className="auth-submit" disabled={busy}>
                {t(busy ? 'Working…' : register ? 'Create account' : 'Sign in')}
              </button>
              <button
                type="button"
                className="auth-back"
                onClick={() => {
                  setRegister(!register);
                  setTwoFactorRequired(false);
                  setError('');
                }}
              >
                {t(register ? 'Already have an account? Sign in' : 'New here? Create an account')}
              </button>
            </form>
          )}
          {error && (
            <p role="alert" className="form-error">
              {t(error)}{' '}
              <button
                onClick={() => {
                  setError('');
                  void refresh();
                }}
              >
                {t('Retry')}
              </button>
            </p>
          )}
          {new URLSearchParams(location.search).has('auth_error') && (
            <p role="alert">{t('Provider sign-in did not complete. Please try again.')}</p>
          )}
        </div>
      </section>
    </main>
  );
}
