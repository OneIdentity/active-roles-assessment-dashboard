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

@Component({
  selector: 'app-login',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    IrisButtonComponent,
    IrisTextInputComponent,
    IrisFormFieldComponent,
  ],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class LoginComponent implements OnInit, OnDestroy {
  readonly form: FormGroup;

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
