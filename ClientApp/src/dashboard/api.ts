export interface DashboardCapabilities {
  isActiveRolesAdmin: boolean;
  canAccessSettings: boolean;
  canViewSnapshots: boolean;
  canViewExposureReport: boolean;
  canViewAssessments: boolean;
  canExportDashboardData: boolean;
  canRebuildCache: boolean;
  cacheRebuildInProgress: boolean;
  performanceTestDashboards: string[];
}

export interface DashboardTab {
  key: string;
  title: string;
}

export interface SegmentFilterView {
  enabled: boolean;
  availableDomains: string[];
  selectedDomains: string[];
  availableTenants: string[];
  selectedTenants: string[];
}

export interface DashboardPayload {
  userName: string;
  autoRefreshMinutes: number;
  capabilities: DashboardCapabilities;
  segmentFilter: SegmentFilterView;
  tabs: DashboardTab[];
  generatedAtUtc: string;
}

export interface ChartDataset {
  key: string;
  title: string;
  type: string;
  disableTypeToggle: boolean;
  sliceOffset: number;
  labels: string[];
  values: number[];
  colors: string[];
  colorTokens: string[];
}

export interface ViewKpi {
  key: string;
  label: string;
  count: number;
  error: string | null;
  colorToken: string;
  color: string;
  sectionId: string;
  isRisk: boolean;
  severity?: 'none' | 'low' | 'medium' | 'high' | null;
  exclusionsNotApplied?: boolean;
}

export interface ViewCategory {
  key: string;
  title: string;
  isOverview: boolean;
  isRisk: boolean;
  kpis: ViewKpi[];
  charts: ChartDataset[];
}

export interface SourceSplitItem {
  source: string;
  count: number;
}

export interface DashboardView {
  key: string;
  title: string;
  categories: ViewCategory[];
  sourceSplit: SourceSplitItem[];
  managedObjects: ViewKpi | null;
  licensing: LicensingView | null;
  generatedAtUtc: string;
}

export interface LicensingSegment {
  name: string;
  kind: string;
  value: number;
}

export interface LicensingThreshold {
  key: string;
  total: number;
  threshold: number;
}

export interface LicensingTrendSeries {
  name: string;
  category: string;
  values: number[];
}

export interface LicensingView {
  error: string | null;
  trendLabels: string[];
  trend: LicensingTrendSeries[];
  thresholds: LicensingThreshold[];
  overThreshold: boolean;
  composition: LicensingSegment[];
  excluded: LicensingSegment[];
}

export interface KpiDetail {
  key: string;
  label: string;
  count: number;
  error: string | null;
  columns: string[];
  rows: string[][];
  groupTreeDn?: string | null;
  rowGroupTreeDns?: (string | null)[] | null;
  exclusionsNotApplied?: boolean;
}

export interface RefreshStatus {
  sequence: number;
  refreshing: boolean;
  failed: boolean;
  error: string | null;
}

export class HttpError extends Error {
  constructor(public readonly status: number) {
    super(`Request failed with status ${status}`);
  }
}

/**
 * Application path base (e.g. "/ActiveRolesDashboard" when hosted as an IIS sub-application,
 * "" at the server root), derived from the SPA location, which is always "{base}/app/...".
 */
export const appBase = (() => {
  const path = window.location.pathname;
  const idx = path.toLowerCase().indexOf('/app/');
  return idx > 0 ? path.slice(0, idx) : idx === 0 ? '' : path.toLowerCase().endsWith('/app') ? path.slice(0, -4) : '';
})();

/** Prefixes a root-relative application path ("/api/...") with the application path base. */
export const withBase = (path: string) => (path.startsWith('/') ? `${appBase}${path}` : path);

const loginUrl = () => withBase(`/Login?ReturnUrl=${encodeURIComponent(window.location.pathname)}`);

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(withBase(url), { credentials: 'same-origin', ...init });
  // A cookie-auth challenge is answered with a redirect to the Razor login page.
  if (response.redirected && new URL(response.url).pathname.toLowerCase().startsWith(withBase('/login').toLowerCase())) {
    window.location.href = loginUrl();
    return new Promise<T>(() => {});
  }
  if (response.status === 401) {
    window.location.href = loginUrl();
    return new Promise<T>(() => {});
  }
  if (!response.ok) throw new HttpError(response.status);
  return (await response.json()) as T;
}

export const getDashboard = () => request<DashboardPayload>('/api/dashboard');

export const getDashboardView = (key: string) =>
  request<DashboardView>(`/api/dashboard/view/${encodeURIComponent(key)}`);

export const rebuildCache = () =>
  request<{ refreshFrom: number }>('/api/dashboard/rebuild', { method: 'POST' });

export const getRefreshStatus = () => request<RefreshStatus>('/api/dashboard/refresh-status');

export const getKpiDetail = (key: string) =>
  request<KpiDetail>(`/api/dashboard/kpi/${encodeURIComponent(key)}`);

export const setSegments = (dimension: 'Domain' | 'Tenant', segments: string[]) =>
  request<{ domains: string[] | null; tenants: string[] | null }>('/api/dashboard/segment', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ dimension, segments }),
  });

export type PerfStatus = 'Ok' | 'Warn' | 'Fail' | 'Skipped';

export interface PerfTarget {
  id: string;
  name: string;
  serverType: string;
  applicableTests: string[];
}

export interface PerfProbe {
  targetId: string;
  targetName: string;
  serverType: string;
  testType: string;
  status: PerfStatus;
  latencyMs: number | null;
  message: string;
}

export interface PerfRunResult {
  error?: string;
  okCount: number;
  warnCount: number;
  failCount: number;
  probes: PerfProbe[];
}

export interface PerfTrendSeries {
  targetId: string;
  targetName: string;
  serverType: string;
  testType: string;
  samples: { timestampUtc: string; latencyMs: number | null; status: PerfStatus }[];
}

export interface PerfTrendResponse {
  intervalMinutes: number;
  retentionHours: number;
  series: PerfTrendSeries[];
}

export const getPerfTargets = (dashboard: string) =>
  request<PerfTarget[] | { error: string }>(`/api/diagnostics/targets?dashboard=${encodeURIComponent(dashboard)}`);

export const getPerfTrends = (dashboard: string, periodHours: number) =>
  request<PerfTrendResponse>(`/api/diagnostics/trends?dashboard=${encodeURIComponent(dashboard)}&periodHours=${periodHours}`);

export const runPerfTests = (dashboard: string, testType: string | null, targetIds: string[]) =>
  request<PerfRunResult>('/api/diagnostics/run', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ dashboard, testType, targetIds }),
  });

export interface ExportRequest {
  scope: 'Dashboard' | 'SubDashboard' | 'Category' | 'Kpi';
  format: 'Pdf' | 'Word' | 'Excel';
  dashboardKey: string;
  subDashboardKey?: string;
  categoryKey?: string;
  kpiKey?: string;
  includeDetails: boolean;
}

/** Posts to the existing server export endpoint and saves the returned file. */
export async function exportReport(req: ExportRequest): Promise<void> {
  const form = new FormData();
  form.set('Scope', req.scope);
  form.set('Format', req.format);
  form.set('DashboardKey', req.dashboardKey);
  if (req.subDashboardKey) form.set('SubDashboardKey', req.subDashboardKey);
  if (req.categoryKey) form.set('CategoryKey', req.categoryKey);
  if (req.kpiKey) form.set('KpiKey', req.kpiKey);
  form.set('IncludeDetails', String(req.includeDetails));
  const response = await fetch(withBase('/Export'), { method: 'POST', body: form, credentials: 'same-origin' });
  if (response.status === 401) {
    window.location.href = loginUrl();
    return;
  }
  if (!response.ok) throw new HttpError(response.status);
  const disposition = response.headers.get('Content-Disposition') ?? '';
  const match = /filename\*=UTF-8''([^;]+)|filename="?([^";]+)"?/i.exec(disposition);
  const fileName = match ? decodeURIComponent(match[1] ?? match[2]) : 'export';
  const url = URL.createObjectURL(await response.blob());
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
