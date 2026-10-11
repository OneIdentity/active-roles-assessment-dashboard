import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';

// UX IRIS dashboard stylesheets (same stack the UX Razor dashboard pages load:
// dashboard.css imports iris-tokens.css, then ad-dashboard.css for the Figma frame).
import './ux/css/dashboard.css';
import './ux/css/ad-dashboard.css';
import './dashboard/react-additions.css';

import { DashboardFrame } from './dashboard/DashboardFrame';

const rootEl = document.getElementById('root');
if (!rootEl) throw new Error('Root element #root not found');

createRoot(rootEl).render(
  <StrictMode>
    <DashboardFrame />
  </StrictMode>,
);
