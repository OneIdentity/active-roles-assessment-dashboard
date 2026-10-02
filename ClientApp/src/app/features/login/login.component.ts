import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  OnInit,
  signal,
} from '@angular/core';
import {
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { IrisButtonComponent } from '../../shared/ui/iris-button/iris-button.component';
import { IrisTextInputComponent } from '../../shared/ui/iris-text-input/iris-text-input.component';
import { IrisFormFieldComponent } from '../../shared/ui/iris-form-field/iris-form-field.component';
import {
  IrisDropdownComponent,
  IrisDropdownOption,
} from '../../shared/ui/iris-dropdown/iris-dropdown.component';

/** A language the login page can switch to, mirroring SupportedLanguage.All on the server. */
interface SupportedLanguage {
  code: string;
  displayName: string;
  flagImage: string;
}

@Component({
  selector: 'app-login',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    IrisButtonComponent,
    IrisTextInputComponent,
    IrisFormFieldComponent,
    IrisDropdownComponent,
  ],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class LoginComponent implements OnInit, OnDestroy {
  readonly form: FormGroup;

  /** Languages offered by the selector (kept in sync with server SupportedLanguage.All). */
  readonly languages: readonly SupportedLanguage[] = [
    { code: 'en', displayName: 'English', flagImage: 'img/flags/en.svg' },
    { code: 'fr', displayName: 'Français', flagImage: 'img/flags/fr.svg' },
    { code: 'it', displayName: 'Italiano', flagImage: 'img/flags/it.svg' },
    { code: 'es', displayName: 'Español', flagImage: 'img/flags/es.svg' },
    { code: 'de', displayName: 'Deutsch', flagImage: 'img/flags/de.svg' },
    { code: 'hu', displayName: 'Magyar', flagImage: 'img/flags/hu.svg' },
  ];

  /** Currently selected language code, read from the ASP.NET culture cookie. */
  readonly currentLanguage = signal<string>(this.resolveCurrentLanguage());

  /** Absolute URL to the One Identity watermark/background asset. */
  get watermarkUrl(): string {
    return `${this.auth.basePath}/images/oi-logo.svg`;
  }

  /** Absolute URL to the combined One Identity logo shown in the card header. */
  get logoUrl(): string {
    return `${this.auth.basePath}/images/oneidentity-logo-v2.svg`;
  }

  /** Builds a flag asset URL for the given relative image path. */
  flagUrl(flagImage: string): string {
    return `${this.auth.basePath}/${flagImage}`;
  }

  /** Dropdown options for the language switcher, including resolved flag URLs. */
  languageOptions(): IrisDropdownOption[] {
    return this.languages.map((lang) => ({
      value: lang.code,
      label: lang.displayName,
      imageUrl: this.flagUrl(lang.flagImage),
    }));
  }

  /** Inline auth/validation error shown above the form. */
  readonly errorMessage = signal<string | null>(null);

  /** True once the shared cache has faulted; disables Sign In. */
  readonly cacheFaulted = signal(false);

  /** True while a sign-in request / cache wait is in flight. */
  readonly submitting = signal(false);

  /** Message shown on the loading overlay. */
  readonly loadingMessage = signal('Signing in\u2026');

  private cacheWatchHandle: ReturnType<typeof setTimeout> | null = null;
  private destroyed = false;

  constructor(
    private readonly fb: FormBuilder,
    private readonly auth: AuthService,
    private readonly route: ActivatedRoute,
  ) {
    this.form = this.fb.group({
      username: ['', Validators.required],
      password: ['', Validators.required],
    });
  }

  ngOnInit(): void {
    this.watchCacheFault();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    if (this.cacheWatchHandle !== null) {
      clearTimeout(this.cacheWatchHandle);
    }
  }

  get usernameInvalid(): boolean {
    const control = this.form.get('username');
    return !!control && control.invalid && control.touched;
  }

  get passwordInvalid(): boolean {
    const control = this.form.get('password');
    return !!control && control.invalid && control.touched;
  }

  async onSubmit(): Promise<void> {
    this.errorMessage.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.errorMessage.set('Username and password are required.');
      return;
    }

    if (this.cacheFaulted()) {
      return;
    }

    this.submitting.set(true);
    this.loadingMessage.set('Signing in\u2026');

    const { username, password } = this.form.getRawValue();
    const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
    const result = await this.auth.login(username, password, returnUrl);

    if (!result.success) {
      this.submitting.set(false);
      this.errorMessage.set(result.error ?? 'Sign in failed.');
      return;
    }

    if (result.loadingMessage) {
      this.loadingMessage.set(result.loadingMessage);
    }

    const redirectUrl = result.redirectUrl ?? '/';
    const outcome = await this.auth.waitForCacheReady(() => {
      this.loadingMessage.set('Building cache\u2026');
    });

    if (outcome === 'faulted') {
      this.submitting.set(false);
      this.cacheFaulted.set(true);
      this.errorMessage.set(
        'The dashboard data could not be loaded. Please contact an administrator.',
      );
      return;
    }

    // ready or unreachable: proceed to the dashboard.
    this.loadingMessage.set('Filtering data\u2026');
    window.location.href = redirectUrl;
  }

  /**
   * Reads the current UI culture from the ASP.NET Core culture cookie
   * (format: c=<culture>|uic=<ui-culture>). Falls back to the <html lang>
   * attribute, then English, so the selector highlights the active language.
   */
  private resolveCurrentLanguage(): string {
    const match = document.cookie.match(/(?:^|;\s*)\.AspNetCore\.Culture=([^;]+)/);
    if (match) {
      const decoded = decodeURIComponent(match[1]);
      const uic = decoded.match(/uic=([^|]+)/);
      if (uic) {
        const code = uic[1].split('-')[0].toLowerCase();
        if (this.languages.some((l) => l.code === code)) {
          return code;
        }
      }
    }
    const htmlLang = document.documentElement.getAttribute('lang');
    if (htmlLang) {
      const code = htmlLang.split('-')[0].toLowerCase();
      if (this.languages.some((l) => l.code === code)) {
        return code;
      }
    }
    return 'en';
  }

  /**
   * Switches the UI language by writing the standard ASP.NET Core culture
   * cookie and reloading so the server re-renders all localized strings in the
   * new culture. Mirrors the previous Razor login behaviour.
   */
  setLanguage(code: string): void {
    if (!this.languages.some((l) => l.code === code) || code === this.currentLanguage()) {
      return;
    }
    const value = `c=${code}|uic=${code}`;
    const path = this.auth.basePath || '/';
    document.cookie =
      `.AspNetCore.Culture=${encodeURIComponent(value)}` +
      `;path=${path};max-age=31536000;samesite=lax`;
    window.location.reload();
  }

  /**
   * While the user sits on the login page, quietly poll the cache status. If
   * the cache faults, surface the error and disable Sign In proactively.
   */
  private watchCacheFault(): void {
    const poll = async (): Promise<void> => {
      if (this.destroyed) {
        return;
      }
      const status = await this.auth.getCacheStatus();
      if (this.destroyed) {
        return;
      }
      if (status?.faulted) {
        this.cacheFaulted.set(true);
        this.errorMessage.set(
          'The dashboard data could not be loaded. Please contact an administrator.',
        );
        return; // terminal until restart
      }
      this.cacheWatchHandle = setTimeout(() => void poll(), 3000);
    };
    void poll();
  }
}
