import React from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './app/App';
import { AuthBoundary } from './features/auth/AuthBoundary';
import { SharedCardRouter } from './features/cards/SharedCard';
import { I18nProvider } from './shared/i18n';
import { ThemeProvider } from './shared/Theme';
import './styles/dark.css';
import './styles/styles.css';
createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <I18nProvider>
      <ThemeProvider>
        <AuthBoundary>
          <SharedCardRouter>
            <App />
          </SharedCardRouter>
        </AuthBoundary>
      </ThemeProvider>
    </I18nProvider>
  </React.StrictMode>,
);
