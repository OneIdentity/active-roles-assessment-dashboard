export interface DashboardCapabilities {
  isActiveRolesAdmin: boolean;
  canAccessSettings: boolean;
  canViewSnapshots: boolean;
  canViewExposureReport: boolean;
  canViewAssessments: boolean;
  canExportDashboardData: boolean;
  canRebuildCache: boolean;
  cacheRebuildInProgress: boolean;
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

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, { credentials: 'same-origin', ...init });
  // A cookie-auth challenge is answered with a redirect to the Razor login page.
  if (response.redirected && new URL(response.url).pathname.toLowerCase().startsWith('/login')) {
    window.location.href = `/Login?ReturnUrl=${encodeURIComponent(window.location.pathname)}`;
    return new Promise<T>(() => {});
  }
  if (response.status === 401) {
    window.location.href = `/Login?ReturnUrl=${encodeURIComponent(window.location.pathname)}`;
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
  const response = await fetch('/Export', { method: 'POST', body: form, credentials: 'same-origin' });
  if (response.status === 401) {
    window.location.href = `/Login?ReturnUrl=${encodeURIComponent(window.location.pathname)}`;
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
