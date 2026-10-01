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
 * Wraps the existing ASP.NET Core authentication contract so the Angular
 * Login can perform a real cookie-based sign-in.
 *
 * Contract (mirrors Pages/Login.cshtml.cs + Login.cshtml):
 *  - POST {base}/Login with FormData (Username, Password) and
 *    Accept: application/json.
 *  - Success  -> 200 JSON { redirectUrl, loadingMessage }.
 *  - Failure  -> re-rendered HTML page (non-JSON); treated as auth failure.
 *  - GET {base}/cache/status -> { ready, faulted } drives the post-login
 *    "Building cache..." / "Filtering data..." overlay before redirect.
 *
 * Antiforgery is globally disabled for Razor Pages, so no token is required.
 * fetch() is used (not HttpClient) so the auth cookie round-trips exactly as
 * it does for the current Razor page.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  /** App base path (PathBase), e.g. '' or '/dashboard'. */
  private readonly base: string = this.resolveBase();

  private resolveBase(): string {
    const href = document.querySelector('base')?.getAttribute('href') ?? '/';
    return href === '/' ? '' : href.replace(/\/$/, '');
  }

  /** Attempt to sign in. Resolves with a structured LoginResult. */
  async login(username: string, password: string): Promise<LoginResult> {
    const form = new FormData();
    form.append('Username', username);
    form.append('Password', password);

    let response: Response;
    try {
      response = await fetch(`${this.base}/Login`, {
        method: 'POST',
        body: form,
        headers: { Accept: 'application/json' },
        credentials: 'same-origin',
      });
    } catch {
      return { success: false, error: 'Unable to reach the server. Please try again.' };
    }

    const contentType = response.headers.get('content-type') ?? '';
    if (response.ok && contentType.includes('application/json')) {
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

    // Non-JSON / non-ok response = validation or authentication failure.
    // The server re-renders the login HTML rather than returning a JSON error,
    // so surface a generic inline message.
    return {
      success: false,
      error: 'Sign in failed. Check your username and password and try again.',
    };
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
