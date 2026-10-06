import { Injectable } from '@angular/core';

/** The caller's effective dashboard capabilities (gates shell toolbar actions). */
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

/** A single overview KPI stat tile. */
export interface DashboardKpiTile {
  key: string;
  label: string;
  count: number;
  error: string | null;
  /** Named colour token (e.g. "blue"). */
  colorToken: string;
  /** Resolved hex colour. */
  color: string;
}

/** A chart dataset; labels/values/colors are parallel arrays. */
export interface DashboardChart {
  key: string;
  title: string;
  type: 'doughnut' | 'pie' | 'bar';
  disableTypeToggle: boolean;
  sliceOffset: number;
  labels: string[];
  values: number[];
  /** Hex colours. */
  colors: string[];
}

/** A navigation tile linking to a (Razor) sub-dashboard. */
export interface DashboardNavigationTile {
  key: string;
  title: string;
  subtitle: string;
  /** App-relative URL (prefix with basePath). */
  url: string;
  image: string;
}

export interface DashboardSegmentFilter {
  enabled: boolean;
  availableDomains: string[];
  selectedDomains: string[];
  availableTenants: string[];
  selectedTenants: string[];
}

export interface DashboardOverview {
  enabled: boolean;
  title: string;
  kpis: DashboardKpiTile[];
  charts: DashboardChart[];
}

/** Shape of GET /api/dashboard (mirrors DashboardController.DashboardPayload 1:1). */
export interface DashboardPayload {
  userName: string;
  autoRefreshMinutes: number;
  capabilities: DashboardCapabilities;
  overview: DashboardOverview;
  segmentFilter: DashboardSegmentFilter;
  tiles: DashboardNavigationTile[];
  generatedAtUtc: string;
}

export type SegmentDimension = 'Domain' | 'Tenant';

/**
 * Wraps the ASP.NET Core REST dashboard API (Controllers/DashboardController.cs).
 *
 * Contract:
 *  - GET  {base}/api/dashboard         -> DashboardPayload (401 if the session has no token).
 *  - POST {base}/api/dashboard/segment -> 200 { domains, tenants } (stored in session).
 *
 * Uses fetch() with same-origin credentials, matching SettingsService.
 */
@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly base: string = this.resolveBase();

  private resolveBase(): string {
    const href = document.querySelector('base')?.getAttribute('href') ?? '/';
    return href === '/' ? '' : href.replace(/\/$/, '');
  }

  get basePath(): string {
    return this.base;
  }

  /** Load the main dashboard payload. Returns null on failure; `unauthorized` on 401. */
  async getDashboard(): Promise<DashboardPayload | 'unauthorized' | null> {
    try {
      const response = await fetch(`${this.base}/api/dashboard`, {
        headers: { Accept: 'application/json' },
        credentials: 'same-origin',
      });
      if (response.status === 401) {
        return 'unauthorized';
      }
      if (!response.ok) {
        return null;
      }
      return (await response.json()) as DashboardPayload;
    } catch {
      return null;
    }
  }

  /** Persist the segment selection for one dimension. Returns true on success. */
  async setSegment(dimension: SegmentDimension, segments: string[]): Promise<boolean> {
    try {
      const response = await fetch(`${this.base}/api/dashboard/segment`, {
        method: 'POST',
        body: JSON.stringify({ dimension, segments }),
        headers: {
          'Content-Type': 'application/json',
          Accept: 'application/json',
        },
        credentials: 'same-origin',
      });
      return response.ok;
    } catch {
      return false;
    }
  }
}
