import { Injectable } from '@angular/core';

/** A language option offered by the setup wizard. */
export interface SetupLanguage {
  code: string;
  displayName: string;
  /** PathBase-prefixed flag asset URL. */
  flagImage: string;
}

/** Current defaults and placeholder hints for the wizard fields. */
export interface SetupDefaults {
  language: string;
  roleGroupsDirectoryType: string;
  activeRolesAdminsGroup: string;
  dashboardAdminsGroup: string;
  auditorsGroup: string;
  powerUsersGroup: string;
  analyticsEnabled: boolean;
  analyticsMeasurementId: string;
  roleGroupPlaceholders: {
    activeRolesAdmins: string;
    dashboardAdmins: string;
    auditors: string;
    powerUsers: string;
  };
  filterPlaceholders: {
    noManagerUser: string;
    noManagerServiceAccount: string;
  };
}

/** Shape of GET /api/setup. */
export interface SetupOptions {
  configured: boolean;
  /** Present when already configured; where to send the user instead. */
  redirectUrl?: string;
  languages?: SetupLanguage[];
  directoryTypes?: string[];
  defaults?: SetupDefaults;
}

/** Full payload POSTed to /api/setup. Mirrors the server SetupRequest 1:1. */
export interface SetupPayload {
  apiBaseUrl: string;
  rstsUrl: string;
  rstsScope: string;
  serviceAccountUsername: string;
  serviceAccountPassword: string;
  webInterfaceUrl: string;
  language: string;
  roleGroupsDirectoryType: string;
  activeRolesAdminsGroup: string;
  dashboardAdminsGroup: string;
  auditorsGroup: string;
  powerUsersGroup: string;
  customNoManagerUserFilter: string;
  customNoManagerServiceAccountFilter: string;
  licensedDomainObjects: number;
  licensedPartitionObjects: number;
  licensedAzureObjects: number;
  licensedSaasObjects: number;
  licensedTotalObjects: number;
  analyticsEnabled: boolean;
  analyticsMeasurementId: string;
}

/** Result of a save attempt. */
export interface SetupSaveResult {
  success: boolean;
  /** Where to navigate after a successful save (server-provided). */
  redirectUrl?: string;
  /** Inline error message to display on failure. */
  error?: string;
}

/**
 * Wraps the ASP.NET Core REST setup API (Controllers/SetupController.cs) so the
 * Angular first-run wizard can load defaults and persist configuration.
 *
 * Contract:
 *  - GET  {base}/api/setup -> { configured, languages, directoryTypes, defaults }.
 *  - POST {base}/api/setup -> 200 { redirectUrl } on success, 400/500 { error }.
 *
 * Antiforgery is globally disabled; fetch() with same-origin credentials matches
 * the server's expectations (identical to AuthService).
 */
@Injectable({ providedIn: 'root' })
export class SetupService {
  private readonly base: string = this.resolveBase();

  private resolveBase(): string {
    const href = document.querySelector('base')?.getAttribute('href') ?? '/';
    return href === '/' ? '' : href.replace(/\/$/, '');
  }

  get basePath(): string {
    return this.base;
  }

  /** Load the wizard options/defaults. */
  async getOptions(): Promise<SetupOptions | null> {
    try {
      const response = await fetch(`${this.base}/api/setup`, {
        headers: { Accept: 'application/json' },
        credentials: 'same-origin',
      });
      if (!response.ok) {
        return null;
      }
      return (await response.json()) as SetupOptions;
    } catch {
      return null;
    }
  }

  /** Validate and persist the wizard configuration. */
  async save(payload: SetupPayload): Promise<SetupSaveResult> {
    let response: Response;
    try {
      response = await fetch(`${this.base}/api/setup`, {
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
      const data = (await response.json()) as { redirectUrl?: string };
      return { success: true, redirectUrl: data.redirectUrl ?? `${this.base}/login` };
    }

    let error = 'Setup could not be saved. Please review your entries and try again.';
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
