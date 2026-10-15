import { useLayoutEffect, useRef } from 'react';
import { withBase, type DashboardCapabilities } from './api';
import { t } from './strings';
import logo from '../ux/images/figma-active-roles.svg';

interface Props {
  capabilities: DashboardCapabilities;
}

/** Port of the UX `_Sidebar.cshtml` (Figma navigation variant). */
export function Sidebar({ capabilities }: Props) {
  const asideRef = useRef<HTMLElement>(null);
  const indicatorRef = useRef<HTMLSpanElement>(null);

  useLayoutEffect(() => {
    const aside = asideRef.current;
    const indicator = indicatorRef.current;
    const active = aside?.querySelector<HTMLElement>('a.sidebar-nav-item[aria-current="page"]');
    if (!aside || !indicator || !active) return;
    const top = active.getBoundingClientRect().top - aside.getBoundingClientRect().top + aside.scrollTop + 4;
    indicator.style.top = `${top}px`;
    aside.classList.add('has-sidebar-indicator');
    indicator.classList.add('no-transition', 'is-visible');
    indicator.style.transform = 'translateY(0)';
    requestAnimationFrame(() => indicator.classList.remove('no-transition'));
  }, []);

  return (
    <aside ref={asideRef} className="sidebar sidebar-figma" aria-label={t.mainNavigation}>
      <div className="sidebar-header">
        <span className="sidebar-logo"><img src={logo} alt="" /></span>
        <span className="sidebar-title">{t.activeRoles}</span>
      </div>
      <span ref={indicatorRef} className="sidebar-active-indicator" aria-hidden="true" />
      <nav className="sidebar-nav-group">
        <a href={withBase('/app/')} className="sidebar-nav-item" aria-current="page">
          <span className="sidebar-nav-asset sidebar-nav-asset-dashboard" aria-hidden="true" />
          <span>{t.dashboard}</span>
        </a>
        {capabilities.canViewAssessments && (
          <a href={withBase('/Assessments')} className="sidebar-nav-item">
            <span className="sidebar-nav-asset sidebar-nav-asset-assessment" aria-hidden="true" />
            <span>{t.assessment}</span>
          </a>
        )}
        {capabilities.canViewExposureReport && (
          <a href={withBase('/AttackExposure')} className="sidebar-nav-item">
            <span className="sidebar-nav-asset sidebar-nav-asset-exposure" aria-hidden="true" />
            <span>{t.attackExposure}</span>
          </a>
        )}
        {capabilities.canViewSnapshots && (
          <a href={withBase('/Snapshots')} className="sidebar-nav-item">
            <span className="sidebar-nav-asset sidebar-nav-asset-snapshots" aria-hidden="true" />
            <span>{t.snapshots}</span>
          </a>
        )}
      </nav>
      <div className="sidebar-nav-group">
        <div className="sidebar-nav-group-header">
          <span>{t.other}</span>
          <span className="sidebar-group-caret" aria-hidden="true" />
        </div>
        {capabilities.canAccessSettings && (
          <a href={withBase('/Settings')} className="sidebar-nav-item">
            <span className="sidebar-nav-asset sidebar-nav-asset-settings" aria-hidden="true" />
            <span>{t.settings}</span>
          </a>
        )}
        {/* Help page exists at /Help but is hidden from navigation until its content is finalized. */}
        <a href={withBase('/Help')} className="sidebar-nav-item" hidden>
          <span className="sidebar-nav-asset sidebar-nav-asset-help" aria-hidden="true" />
          <span>{t.helpWith}</span>
        </a>
        <a href={withBase('/Privacy')} className="sidebar-nav-item">
          <span className="sidebar-nav-asset sidebar-nav-asset-privacy" aria-hidden="true" />
          <span>{t.privacy}</span>
        </a>
        <a href={withBase('/Logout')} className="sidebar-nav-item">
          <span className="sidebar-nav-asset sidebar-nav-asset-signout" aria-hidden="true" />
          <span>{t.signOut}</span>
        </a>
      </div>
    </aside>
  );
}
