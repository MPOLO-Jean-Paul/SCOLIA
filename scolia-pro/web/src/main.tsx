import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { App } from './App';
import { SessionProvider, applyTheme, storedTheme } from './lib/session';
import { ToastProvider } from './components/ui';
import { ApiError } from './api/client';
import '@fontsource-variable/google-sans-flex';
import '@fontsource-variable/google-sans-code';
import './styles.css';
import './theme.css';

applyTheme(storedTheme());
// La préférence s'applique aussi à la connexion, avant que la coque authentifiée ne soit montée.
try { document.documentElement.dataset.motion = localStorage.getItem('scolia.animations') === '0' ? 'reduced' : 'normal'; } catch { /* stockage indisponible */ }

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 15_000,
      refetchOnWindowFocus: true,
      retry: (count, error) => !(error instanceof ApiError && error.status >= 400 && error.status < 500) && count < 2,
    },
  },
});

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <SessionProvider>
          <ToastProvider>
            <App />
          </ToastProvider>
        </SessionProvider>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
);
