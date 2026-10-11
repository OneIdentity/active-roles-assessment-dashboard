import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import {
  getDashboard, getDashboardView, getRefreshStatus, HttpError, rebuildCache, setSegments,
  type DashboardPayload, type DashboardView, type ViewKpi,
} from './api';
import { DashboardContent } from './DashboardContent';
import { ExportDialog } from './ExportDialog';
import { KpiDetailModal } from './KpiDetailModal';
import { SegmentFilter } from './SegmentFilter';
import { Sidebar } from './Sidebar';
import { t } from './strings';

const OVERVIEW = 'Overview';
const POLL_MS = 3000;

type Toast = { id: number; kind: 'success' | 'error' | 'info'; text: string };

/** Session key remembering the last dashboard tab, so Razor pages can return to it. */
const LAST_TAB_KEY = 'ard.lastDashboardTab';

/** Current tab from `?dashboard=` (matches the UX frame's query parameter). `?restore=1`
 *  (used by the Razor pages' "back to dashboard" links) reopens the last tab viewed. */
const readTabKey = () => {
  const params = new URLSearchParams(window.location.search);
  if (params.has('restore')) {
    let last: string | null = null;
    try { last = sessionStorage.getItem(LAST_TAB_KEY); } catch { /* storage unavailable */ }
    const url = new URL(window.location.pathname, window.location.origin);
    if (last && last !== OVERVIEW) url.searchParams.set('dashboard', last);
    history.replaceState(null, '', url);
    return last || OVERVIEW;
  }
  return params.get('dashboard') || OVERVIEW;
};

type ViewState =
  | { status: 'loading' }
  | { status: 'ready'; view: DashboardView }
  | { status: 'notFound' }
  | { status: 'error' };

/** React port of the UX `_AdDashboardFrame.cshtml` shell. */
export function DashboardFrame() {
  const [payload, setPayload] = useState<DashboardPayload | null>(null);
  const [payloadError, setPayloadError] = useState(false);
  const [tabKey, setTabKey] = useState(readTabKey);
  const [viewState, setViewState] = useState<ViewState>({ status: 'loading' });
  const [collapsed, setCollapsed] = useState(false);
  const [rebuilding, setRebuilding] = useState(false);
  const [detailKpi, setDetailKpi] = useState<ViewKpi | null>(null);
  const [exportState, setExportState] = useState<{ kpi?: string } | null>(null);
  const [toasts, setToasts] = useState<Toast[]>([]);
  const tabsRef = useRef<HTMLElement>(null);
  const indicatorRef = useRef<HTMLSpanElement>(null);
  const scrollRef = useRef<HTMLDivElement>(null);
  const pollRef = useRef<number | null>(null);

  const showToast = useCallback((kind: Toast['kind'], text: string) => {
    const id = Date.now() + Math.random();
    setToasts((list) => [...list, { id, kind, text }]);
    window.setTimeout(() => setToasts((list) => list.filter((x) => x.id !== id)), 5000);
  }, []);

  const loadPayload = useCallback(() =>
    getDashboard()
      .then((p) => { setPayload(p); return p; })
      .catch(() => { setPayloadError(true); return null; }), []);

  const stopPolling = () => {
    if (pollRef.current != null) window.clearInterval(pollRef.current);
    pollRef.current = null;
  };

  /** Polls until the cache sequence advances past "from" (or, with from = null, until not refreshing). */
  const pollRebuild = useCallback((from: number | null) => {
    stopPolling();
    setRebuilding(true);
    pollRef.current = window.setInterval(() => {
      getRefreshStatus()
        .then((s) => {
          const done = !s.refreshing && (from == null || s.sequence > from);
          if (!done) return;
          stopPolling();
          setRebuilding(false);
          if (s.failed) showToast('error', s.error ? `${t.rebuildFailed} ${s.error}` : t.rebuildFailed);
          else showToast('success', t.rebuildDone);
          loadPayload();
        })
        .catch(() => { stopPolling(); setRebuilding(false); });
    }, POLL_MS);
  }, [loadPayload, showToast]);

  useEffect(() => () => stopPolling(), []);

  useEffect(() => {
    loadPayload().then((p) => {
      if (p?.capabilities.cacheRebuildInProgress && p.capabilities.canRebuildCache) pollRebuild(null);
    });
  }, [loadPayload, pollRebuild]);

  useEffect(() => {
    try { sessionStorage.setItem(LAST_TAB_KEY, tabKey); } catch { /* storage unavailable */ }
  }, [tabKey]);

  useEffect(() => {
    const onPop = () => setTabKey(readTabKey());
    window.addEventListener('popstate', onPop);
    return () => window.removeEventListener('popstate', onPop);
  }, []);

  useEffect(() => {
    document.body.classList.add('ad-dashboard-design');
    return () => document.body.classList.remove('ad-dashboard-design');
  }, []);

  useEffect(() => {
    document.body.classList.toggle('ad-sidebar-collapsed', collapsed);
  }, [collapsed]);

  const loadView = useCallback((key: string) => {
    setViewState({ status: 'loading' });
    getDashboardView(key)
      .then((view) => {
        setViewState({ status: 'ready', view });
        if (scrollRef.current) scrollRef.current.scrollTop = 0;
      })
      .catch((e) => setViewState({ status: e instanceof HttpError && e.status === 404 ? 'notFound' : 'error' }));
  }, []);

  // Tabs the user may not see are never rendered; a key typed into the URL that is not
  // in the permitted list shows the in-frame "not found" state without calling the API.
  const tabAllowed = !!payload?.tabs.some((tab) => tab.key.toLowerCase() === tabKey.toLowerCase());

  useEffect(() => {
    if (!payload) return;
    if (tabAllowed) loadView(tabKey);
    else setViewState({ status: 'notFound' });
  }, [payload, tabKey, tabAllowed, loadView]);

  useEffect(() => {
    if (!payload?.autoRefreshMinutes) return;
    const id = window.setInterval(() => loadView(readTabKey()), payload.autoRefreshMinutes * 60_000);
    return () => window.clearInterval(id);
  }, [payload, loadView]);

  const moveIndicator = useCallback((animate: boolean) => {
    const tabs = tabsRef.current;
    const indicator = indicatorRef.current;
    const active = tabs?.querySelector<HTMLElement>('a.active');
    if (!indicator) return;
    if (!active) { indicator.classList.remove('is-visible'); return; }
    if (!animate) indicator.classList.add('no-transition');
    indicator.style.width = `${active.offsetWidth}px`;
    indicator.style.transform = `translateX(${active.offsetLeft}px)`;
    indicator.classList.add('is-visible');
    if (!animate) requestAnimationFrame(() => indicator.classList.remove('no-transition'));
  }, []);

  const firstLayout = useRef(true);
  useLayoutEffect(() => {
    moveIndicator(!firstLayout.current);
    firstLayout.current = false;
  }, [payload, tabKey, moveIndicator]);

  useEffect(() => {
    const onResize = () => moveIndicator(false);
    window.addEventListener('resize', onResize);
    return () => window.removeEventListener('resize', onResize);
  }, [moveIndicator]);

  const selectTab = (key: string) => (event: React.MouseEvent) => {
    event.preventDefault();
    if (key === tabKey) return;
    const url = new URL(window.location.pathname, window.location.origin);
    if (key !== OVERVIEW) url.searchParams.set('dashboard', key);
    history.pushState({ dashboard: key }, '', url);
    setTabKey(key);
  };

  const onRebuild = () => {
    setRebuilding(true);
    rebuildCache()
      .then((r) => { showToast('info', t.rebuildStarted); pollRebuild(r.refreshFrom); })
      .catch(() => { setRebuilding(false); showToast('error', t.rebuildFailed); });
  };

  const onKpi = useCallback((kpi: ViewKpi) => setDetailKpi(kpi), []);
  const closeDetail = useCallback(() => setDetailKpi(null), []);
  const closeExport = useCallback(() => setExportState(null), []);
  const onExportResult = useCallback((ok: boolean) => { if (!ok) showToast('error', t.exportFailed); }, [showToast]);

  const onSegments = (dimension: 'Domain' | 'Tenant', segments: string[]) => {
    setViewState({ status: 'loading' });
    setSegments(dimension, segments)
      .then(() => loadPayload())
      .catch(() => setViewState({ status: 'error' }));
  };

  const currentTab = payload?.tabs.find((tab) => tab.key.toLowerCase() === tabKey.toLowerCase());
  const title = !currentTab || currentTab.key === OVERVIEW
    ? (currentTab ? t.overview : t.notFoundTitle)
    : `${currentTab.title} ${t.dashboardTitleSuffix}`;

  useEffect(() => {
    document.title = currentTab ? `${t.footer} - ${currentTab.key === OVERVIEW ? t.overview : currentTab.title}` : t.footer;
  }, [currentTab]);

  if (payloadError) {
    return <div className="ad-content-scroll" role="alert">{t.loadFailed}</div>;
  }

  const showOverlay = !payload || viewState.status === 'loading';

  return (
    <>
      <div className={`loading-overlay ${showOverlay ? 'active' : ''}`} aria-hidden={!showOverlay}>
        <div className="loading-spinner">
          <div className="spinner" />
          <p>{t.loading}</p>
        </div>
      </div>
      {payload && (
        <div className="page ad-design-page">
          <Sidebar capabilities={payload.capabilities} />
          <div className="ad-main">
            <header className="ad-app-bar">
              <button className="ad-sidebar-toggle" type="button" aria-expanded={!collapsed}
                aria-label={collapsed ? t.expandNavigation : t.collapseNavigation}
                onClick={() => setCollapsed((c) => !c)}>
                <svg className="sidebar-toggle-icon" viewBox="0 0 18 14" width="18" height="14" aria-hidden="true">
                  <path d="M16 13V14H2V13H16ZM17 12V2C17 1.44771 16.5523 1 16 1H2C1.44772 1 1 1.44772 1 2V12C1 12.5523 1.44771 13 2 13V14C0.89543 14 0 13.1046 0 12V2C0 0.89543 0.89543 0 2 0H16C17.1046 0 18 0.89543 18 2V12C18 13.1046 17.1046 14 16 14V13C16.5523 13 17 12.5523 17 12Z" fill="currentColor" />
                  <rect className="sidebar-toggle-pane" x="2" y="2" width="8" height="10" rx="1" fill="currentColor" />
                </svg>
              </button>
              <nav className="ad-breadcrumb" aria-label={t.breadcrumb}>
                <span>{t.activeRoles}</span><span aria-hidden="true">/</span><span aria-current="page">{t.dashboard}</span>
              </nav>
            </header>

            <main className="ad-content">
              <div className="ad-content-sticky">
                <div className="ad-title-row">
                  <div className="ad-title-group">
                    <h1 id="ad-dashboard-title">{title}</h1>
                    <span className="ad-info-tip iris-icon-info" title={t.environmentOverview} aria-label={t.environmentOverview} />
                  </div>
                  <div className="ad-page-actions">
                    <button type="button" className="ad-action" title={t.refresh} onClick={() => tabAllowed && loadView(tabKey)}>
                      <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M20 7v5h-5M4 17v-5h5" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" /><path d="M5.6 9a7 7 0 0 1 11.8-2L20 9M4 15l2.6 2a7 7 0 0 0 11.8-2" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" /></svg>
                      <span>{t.refresh}</span>
                    </button>
                    {payload.capabilities.canRebuildCache && (
                      <button type="button" className={`ad-action ${rebuilding ? 'is-spinning' : ''}`} disabled={rebuilding}
                        aria-disabled={rebuilding} onClick={onRebuild}>
                        <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><ellipse cx="12" cy="5" rx="8.5" ry="3" fill="none" stroke="currentColor" strokeWidth="1.8" /><path d="M3.5 5v14c0 1.7 3.8 3 8.5 3s8.5-1.3 8.5-3V5M3.5 12c0 1.7 3.8 3 8.5 3s8.5-1.3 8.5-3" fill="none" stroke="currentColor" strokeWidth="1.8" /></svg>
                        <span>{t.rebuildCache}</span>
                      </button>
                    )}
                    {payload.capabilities.canExportDashboardData && (
                      <button type="button" className="ad-action" title={t.export}
                        disabled={viewState.status !== 'ready'} onClick={() => setExportState({})}>
                        <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M12 3v12m0 0 4-4m-4 4-4-4M5 16v4h14v-4" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" /></svg>
                        <span>{t.export}</span>
                      </button>
                    )}
                  </div>
                </div>

                <nav className="ad-tabs" aria-label={t.dashboards} ref={tabsRef}>
                  {payload.tabs.map((tab) => {
                    const active = tab.key.toLowerCase() === tabKey.toLowerCase();
                    const href = tab.key === OVERVIEW ? '/app/' : `/app/?dashboard=${encodeURIComponent(tab.key)}`;
                    return (
                      <a key={tab.key} href={href} className={active ? 'active' : ''}
                        aria-current={active ? 'page' : undefined} onClick={selectTab(tab.key)}>
                        {tab.key === OVERVIEW ? t.overview : tab.title}
                      </a>
                    );
                  })}
                  <span ref={indicatorRef} className="ad-tabs-indicator" aria-hidden="true" />
                </nav>
              </div>

              <div className="ad-content-scroll" ref={scrollRef} aria-busy={viewState.status === 'loading'}>
                <SegmentFilter filter={payload.segmentFilter} onApply={onSegments} />
                {viewState.status === 'ready' && <DashboardContent view={viewState.view} onKpi={onKpi} />}
                {viewState.status === 'notFound' && (
                  <section className="ad-source-card" role="alert">
                    <div className="ad-section-heading">
                      <h2>{t.notFoundTitle}</h2>
                      <p>{t.notFoundBody}</p>
                    </div>
                    <a href="/app/" onClick={selectTab(OVERVIEW)}>{t.backToOverview}</a>
                  </section>
                )}
                {viewState.status === 'error' && <p role="alert">{t.loadFailed}</p>}
                <footer className="footer">{t.footer}</footer>
              </div>
            </main>
          </div>
        </div>
      )}
      {payload && detailKpi && (
        <KpiDetailModal kpi={detailKpi} canExport={payload.capabilities.canExportDashboardData}
          onClose={closeDetail} onExport={(k) => { setDetailKpi(null); setExportState({ kpi: k.key }); }} />
      )}
      {payload && exportState && viewState.status === 'ready' && (
        <ExportDialog view={viewState.view} tabs={payload.tabs} initialKpi={exportState.kpi}
          onClose={closeExport} onResult={onExportResult} />
      )}
      <div className="toast-container react-toasts" role="status" aria-live="polite">
        {toasts.map((toast) => <div key={toast.id} className={`app-toast show ${toast.kind}`}>{toast.text}</div>)}
      </div>
    </>
  );
}
