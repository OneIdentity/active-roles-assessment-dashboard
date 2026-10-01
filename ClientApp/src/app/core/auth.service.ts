import { Injectable } from '@angular/core';

/** Result of a login attempt. */
export interface LoginResult {
  success: boolean;
  /** Where to navigate after the cache is ready (server-provided on success). */
  redirectUrl?: string;
  /** Localized "loading" message returned by the server on success. */
  loadingMessage?: string;
  /** Inline error message to display on failure. */
  error?: string;
}

/** Shape of GET /cache/status. */
export interface CacheStatus {
  state: string;
  ready: boolean;
  faulted: boolean;
  collectedAtUtc: string | null;
}

/**
 * Wraps the ASP.NET Core REST authentication API so the Angular Login can
 * perform a real cookie-based sign-in.
 *
 * Contract (Controllers/AuthController.cs):
 *  - POST {base}/api/auth/login  with JSON { username, password, returnUrl? }.
 *      Success -> 200 JSON { redirectUrl, loadingMessage }.
 *      Failure -> 401  JSON { error }.
 *  - POST {base}/api/auth/logout -> 200 JSON { redirectUrl }.
 *  - GET  {base}/cache/status    -> { ready, faulted } drives the post-login
 *    "Building cache..." / "Filtering data..." overlay before redirect.
 *
 * Antiforgery is globally disabled, so no token is required. fetch() is used so
 * the auth cookie round-trips exactly as the server expects.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  /** App base path (PathBase), e.g. '' or '/dashboard'. */
  private readonly base: string = this.resolveBase();

  private resolveBase(): string {
    const href = document.querySelector('base')?.getAttribute('href') ?? '/';
    return href === '/' ? '' : href.replace(/\/$/, '');
  }

  /** App base path (PathBase), e.g. '' or '/dashboard'. Used for asset URLs and cookie paths. */
  get basePath(): string {
    return this.base;
  }

  /** Attempt to sign in. Resolves with a structured LoginResult. */
  async login(
    username: string,
    password: string,
    returnUrl?: string | null,
  ): Promise<LoginResult> {
    let response: Response;
    try {
      response = await fetch(`${this.base}/api/auth/login`, {
        method: 'POST',
        body: JSON.stringify({ username, password, returnUrl: returnUrl ?? null }),
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
        redirectUrl?: string;
        loadingMessage?: string;
      };
      return {
        success: true,
        redirectUrl: data.redirectUrl ?? `${this.base}/`,
        loadingMessage: data.loadingMessage,
      };
    }

    // 401 (or other non-ok): the API returns { error } with a localized message.
    let error = 'Sign in failed. Check your username and password and try again.';
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

  /** Sign out: clears the server session/cookie and returns where to navigate. */
  async logout(): Promise<string> {
    try {
      const response = await fetch(`${this.base}/api/auth/logout`, {
        method: 'POST',
        headers: { Accept: 'application/json' },
        credentials: 'same-origin',
      });
      if (response.ok) {
        const data = (await response.json()) as { redirectUrl?: string };
        return data.redirectUrl ?? `${this.base}/login`;
      }
    } catch {
      // fall through to default
    }
    return `${this.base}/login`;
  }

  /** Fetch the shared cache status. */
  async getCacheStatus(): Promise<CacheStatus | null> {
    try {
      const response = await fetch(`${this.base}/cache/status`, {
        headers: { Accept: 'application/json' },
        credentials: 'same-origin',
      });
      if (!response.ok) {
        return null;
      }
      return (await response.json()) as CacheStatus;
    } catch {
      return null;
    }
  }

  /**
   * Poll /cache/status until the shared cache is ready (or faults). Mirrors the
   * existing login overlay behaviour.
   *
   * @param onBuilding invoked while the cache is still building.
   * @param pollIntervalMs delay between polls (default 1500ms).
   * @returns 'ready' | 'faulted' | 'unreachable'
   */
  async waitForCacheReady(
    onBuilding: () => void,
    pollIntervalMs = 1500,
  ): Promise<'ready' | 'faulted' | 'unreachable'> {
    for (;;) {
      const status = await this.getCacheStatus();
      if (status === null) {
        // Status endpoint unreachable: don't trap the user.
        return 'unreachable';
      }
      if (status.ready) {
        return 'ready';
      }
      if (status.faulted) {
        return 'faulted';
      }
      onBuilding();
      await this.delay(pollIntervalMs);
    }
  }

  private delay(ms: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }
}
