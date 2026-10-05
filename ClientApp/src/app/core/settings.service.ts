import { Injectable } from '@angular/core';

/** A language option offered by the settings page. */
export interface SettingsLanguage {
  code: string;
  displayName: string;
  /** PathBase-prefixed flag asset URL. */
  flagImage: string;
}

/** The caller's effective settings capabilities. */
export interface SettingsCapabilities {
  canAccessSettings: boolean;
  canManageUserSettings: boolean;
  canViewSystemSettings: boolean;
  canManageSystemSettings: boolean;
  isActiveRolesAdmin: boolean;
}

/** A single KPI toggle inside a visibility category. */
export interface KpiVisibilityItem {
  key: string;
  label: string;
  enabled: boolean;
}

/** A top-level KPI visibility category with its child toggles. */
export interface KpiVisibilityCategory {
  key: string;
  label: string;
  enabled: boolean;
  adminOnly: boolean;
  items: KpiVisibilityItem[];
}

export interface RoleMatrixRole {
  key: string;
  label: string;
  fixed: boolean;
}

export interface RoleMatrixPermission {
  key: string;
  label: string;
}

/** Placeholder/default hints surfaced for input fields. */
export interface SettingsDefaults {
  noManagerUser: string;
  noManagerServiceAccount: string;
  roleGroupActiveRolesAdmins: string;
  roleGroupDashboardAdmins: string;
  roleGroupAuditors: string;
  roleGroupPowerUsers: string;
}

/** Shape of GET /api/settings (mirrors SettingsController.SettingsOptions 1:1). */
export interface SettingsOptions {
  capabilities: SettingsCapabilities;
  languages: SettingsLanguage[];
  directoryTypes: string[];

  // User settings
  autoRefreshMinutes: number;
  language: string;
  kpiVisibility: KpiVisibilityCategory[];

  // System settings
  webInterfaceUrl: string;
  customNoManagerUserFilter: string;
  customNoManagerServiceAccountFilter: string;
  entraLargeGroupMemberThreshold: number;
  dynamicGroupExpensiveRuleThreshold: number;
  customADUserAccountAttributes: string;

  apiBaseUrl: string;
  rstsUrl: string;
  resource: string;
  ignoreSslErrors: boolean;

  analyticsEnabled: boolean;
  analyticsMeasurementId: string;

  defaultNoGroupOwnerFilter: string;
  defaultNoManagerUserFilter: string;
  defaultNoManagerServiceAccountFilter: string;
  defaultUserAccountExpiredFilter: string;
  defaultUserAccountLockedOutFilter: string;
  defaultEmptyGroupsFilter: string;
  defaultADUserAccountsFilter: string;
  defaultADGroupsFilter: string;

  roleGroupsDirectoryType: string;
  activeRolesAdminsGroup: string;
  dashboardAdminsGroup: string;
  auditorsGroup: string;
  powerUsersGroup: string;

  defaultLanguage: string;

  serviceAccountUsername: string;

  dailyRefreshTime: string;
  loadOnStartup: boolean;

  perfTrendingEnabled: boolean;
  perfTrendingIntervalMinutes: number;
  perfTrendingRetentionHours: number;
  perfTrendingMinimumInterval: number;

  licensedDomainObjects: number;
  licensedPartitionObjects: number;
  licensedAzureObjects: number;
  licensedSaasObjects: number;
  licensedTotalObjects: number;

  matrixRoles: RoleMatrixRole[];
  matrixPermissions: RoleMatrixPermission[];
  matrixGrants: string[];

  defaults: SettingsDefaults;
}

/**
 * The complete flat KpiSettings bag. Round-tripped verbatim so flags the UI
 * does not surface are preserved. Indexed to allow the KPI tree to read/write
 * arbitrary keys returned by the server without hardcoding every property.
 */
export type KpiSettings = Record<string, boolean>;

/** Full payload POSTed to /api/settings (mirrors SettingsController.SettingsRequest 1:1). */
export interface SettingsPayload {
  // User settings
  autoRefreshMinutes: number;
  language: string;
  kpiSettings: KpiSettings;

  // System settings
  webInterfaceUrl: string;
  customNoManagerUserFilter: string;
  customNoManagerServiceAccountFilter: string;
  entraLargeGroupMemberThreshold: number;
  dynamicGroupExpensiveRuleThreshold: number;
  customADUserAccountAttributes: string;

  apiBaseUrl: string;
  rstsUrl: string;
  resource: string;
  ignoreSslErrors: boolean;

  analyticsEnabled: boolean;
  analyticsMeasurementId: string;

  defaultNoGroupOwnerFilter: string;
  defaultNoManagerUserFilter: string;
  defaultNoManagerServiceAccountFilter: string;
  defaultUserAccountExpiredFilter: string;
  defaultUserAccountLockedOutFilter: string;
  defaultEmptyGroupsFilter: string;
  defaultADUserAccountsFilter: string;
  defaultADGroupsFilter: string;

  roleGroupsDirectoryType: string;
  activeRolesAdminsGroup: string;
  dashboardAdminsGroup: string;
  auditorsGroup: string;
  powerUsersGroup: string;

  defaultLanguage: string;

  serviceAccountUsername: string;
  /** Blank leaves the existing protected password unchanged. */
  serviceAccountPassword: string;

  dailyRefreshTime: string;
  loadOnStartup: boolean;

  perfTrendingEnabled: boolean;
  perfTrendingIntervalMinutes: number;
  perfTrendingRetentionHours: number;

  licensedDomainObjects: number;
  licensedPartitionObjects: number;
  licensedAzureObjects: number;
  licensedSaasObjects: number;
  licensedTotalObjects: number;

  /** Granted "Role:Permission" tokens (one per selected checkbox). */
  rolePermissionGrants: string[];
}

/** Result of a save attempt. */
export interface SettingsSaveResult {
  success: boolean;
  settingsChanged?: boolean;
  restartRequired?: boolean;
  error?: string;
}

/**
 * Wraps the ASP.NET Core REST settings API (Controllers/SettingsController.cs) so
 * the Angular settings page can load current values/capabilities and persist
 * changes.
 *
 * Contract:
 *  - GET  {base}/api/settings -> SettingsOptions (403 if the caller lacks access).
 *  - POST {base}/api/settings -> 200 { settingsChanged, restartRequired }, 403/500 { error }.
 *
 * Uses fetch() with same-origin credentials, matching SetupService/AuthService.
 */
@Injectable({ providedIn: 'root' })
export class SettingsService {
  private readonly base: string = this.resolveBase();

  private resolveBase(): string {
    const href = document.querySelector('base')?.getAttribute('href') ?? '/';
    return href === '/' ? '' : href.replace(/\/$/, '');
  }

  get basePath(): string {
    return this.base;
  }

  /** Load the current settings values, options and capabilities. */
  async getOptions(): Promise<SettingsOptions | null> {
    try {
      const response = await fetch(`${this.base}/api/settings`, {
        headers: { Accept: 'application/json' },
        credentials: 'same-origin',
      });
      if (!response.ok) {
        return null;
      }
      return (await response.json()) as SettingsOptions;
    } catch {
      return null;
    }
  }

  /** Validate and persist the settings. */
  async save(payload: SettingsPayload): Promise<SettingsSaveResult> {
    let response: Response;
    try {
      response = await fetch(`${this.base}/api/settings`, {
        method: 'POST',
        body: JSON.stringify(payload),
        headers: {
          'Content-Type': 'application/json',
          Accept: 'application/json',
        },
        credentials: 'same-origin',
      });
    } catch {
      return { success: false, error: 'Unable to reach the server. Please try again.' };
    }

    if (response.ok) {
      const data = (await response.json()) as {
        settingsChanged?: boolean;
        restartRequired?: boolean;
      };
      return {
        success: true,
        settingsChanged: data.settingsChanged ?? true,
        restartRequired: data.restartRequired ?? false,
      };
    }

    let error = 'Settings could not be saved. Please review your entries and try again.';
    try {
      const body = (await response.json()) as { error?: string };
      if (body?.error) {
        error = body.error;
      }
    } catch {
      // No JSON body; keep the generic message.
    }
    return { success: false, error };
  }
}
