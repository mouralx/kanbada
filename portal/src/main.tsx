import React from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './app/App';
import { AuthBoundary } from './features/auth/AuthBoundary';
import { SharedCardRouter } from './features/cards/SharedCard';
import { I18nProvider } from './shared/i18n';
import { ThemeProvider } from './shared/Theme';
import { PlatformBrandingProvider } from './shared/PlatformBranding';
import './styles/dark.css';
import './styles/styles.css';
import './styles/branding.css';
import './styles/theme-components.css';
import './styles/platform-layout.css';
createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <I18nProvider>
      <PlatformBrandingProvider>
        <ThemeProvider>
          <AuthBoundary>
            <SharedCardRouter>
              <App />
            </SharedCardRouter>
          </AuthBoundary>
        </ThemeProvider>
      </PlatformBrandingProvider>
    </I18nProvider>
  </React.StrictMode>,
);
