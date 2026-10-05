import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  signal,
} from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import {
  SetupService,
  SetupLanguage,
  SetupPayload,
} from '../../core/setup.service';
import { IrisButtonComponent } from '../../shared/ui/iris-button/iris-button.component';
import { IrisTextInputComponent } from '../../shared/ui/iris-text-input/iris-text-input.component';
import { IrisFormFieldComponent } from '../../shared/ui/iris-form-field/iris-form-field.component';
import { IrisCheckboxComponent } from '../../shared/ui/iris-checkbox/iris-checkbox.component';
import { IrisCardComponent } from '../../shared/ui/iris-card/iris-card.component';
import { IrisBannerComponent } from '../../shared/ui/iris-banner/iris-banner.component';
import { IrisStepperComponent } from '../../shared/ui/iris-stepper/iris-stepper.component';
import {
  IrisDropdownComponent,
  IrisDropdownOption,
} from '../../shared/ui/iris-dropdown/iris-dropdown.component';

const FALLBACK_LANGUAGES: SetupLanguage[] = [
  { code: 'en', displayName: 'English', flagImage: '' },
  { code: 'fr', displayName: 'Français', flagImage: '' },
  { code: 'it', displayName: 'Italiano', flagImage: '' },
  { code: 'es', displayName: 'Español', flagImage: '' },
  { code: 'de', displayName: 'Deutsch', flagImage: '' },
  { code: 'hu', displayName: 'Magyar', flagImage: '' },
];

/**
 * Angular replacement for the former Razor setup wizard (Pages/Setup.cshtml).
 * Preserves the identical 7-step flow, fields, client-side validation and
 * persisted output; the server work is handled by SetupController via
 * SetupService. Built from reusable IRIS components, with the login page as the
 * visual baseline.
 */
@Component({
  selector: 'app-setup',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    IrisButtonComponent,
    IrisTextInputComponent,
    IrisFormFieldComponent,
    IrisCheckboxComponent,
    IrisCardComponent,
    IrisBannerComponent,
    IrisStepperComponent,
    IrisDropdownComponent,
  ],
  templateUrl: './setup.component.html',
  styleUrl: './setup.component.scss',
})
export class SetupComponent implements OnInit {
  /** Ordered step labels (7 steps, matching the Razor wizard order). */
  readonly stepLabels = [
    'REST API',
    'Service Account',
    'Web Interface',
    'Language',
    'Mapping & KPIs',
    'Licensing',
    'Analytics',
  ];

  private readonly totalPages = 7;

  readonly currentPage = signal(0);
  readonly errorMessage = signal<string | null>(null);
  readonly fieldErrors = signal<Record<string, string>>({});
  readonly submitting = signal(false);

  readonly languages = signal<SetupLanguage[]>(FALLBACK_LANGUAGES);
  readonly directoryTypes = signal<string[]>(['ActiveDirectory', 'Entra']);

  /** Placeholder hints surfaced by the server defaults for optional fields. */
  readonly placeholders = signal<{
    activeRolesAdmins: string;
    dashboardAdmins: string;
    auditors: string;
    powerUsers: string;
    noManagerUser: string;
    noManagerServiceAccount: string;
  }>({
    activeRolesAdmins: '',
    dashboardAdmins: '',
    auditors: '',
    powerUsers: '',
    noManagerUser: '',
    noManagerServiceAccount: '',
  });

  readonly form: FormGroup;

  get watermarkUrl(): string {
    return `${this.setup.basePath}/images/oi-logo.svg`;
  }

  get logoUrl(): string {
    return `${this.setup.basePath}/images/oneidentity-logo-v2.svg`;
  }

  constructor(
    private readonly fb: FormBuilder,
    private readonly setup: SetupService,
    private readonly router: Router,
  ) {
    this.form = this.fb.group({
      apiBaseUrl: [''],
      rstsUrl: [''],
      rstsScope: [''],
      serviceAccountUsername: [''],
      serviceAccountPassword: [''],
      webInterfaceUrl: [''],
      language: ['en'],
      roleGroupsDirectoryType: ['ActiveDirectory'],
      activeRolesAdminsGroup: [''],
      dashboardAdminsGroup: [''],
      auditorsGroup: [''],
      powerUsersGroup: [''],
      customNoManagerUserFilter: [''],
      customNoManagerServiceAccountFilter: [''],
      licensedDomainObjects: [''],
      licensedPartitionObjects: [''],
      licensedAzureObjects: [''],
      licensedSaasObjects: [''],
      licensedTotalObjects: [''],
      analyticsEnabled: [false],
      analyticsMeasurementId: [''],
    });
  }

  async ngOnInit(): Promise<void> {
    const options = await this.setup.getOptions();
    if (!options) {
      return;
    }
    if (options.configured) {
      // Already configured: leave the wizard.
      window.location.assign(options.redirectUrl ?? `${this.setup.basePath}/login`);
      return;
    }

    if (options.languages?.length) {
      this.languages.set(options.languages);
    }
    if (options.directoryTypes?.length) {
      this.directoryTypes.set(options.directoryTypes);
    }
    const d = options.defaults;
    if (d) {
      this.form.patchValue({
        language: d.language,
        roleGroupsDirectoryType: d.roleGroupsDirectoryType,
        activeRolesAdminsGroup: d.activeRolesAdminsGroup,
        dashboardAdminsGroup: d.dashboardAdminsGroup,
        auditorsGroup: d.auditorsGroup,
        powerUsersGroup: d.powerUsersGroup,
        analyticsEnabled: d.analyticsEnabled,
        analyticsMeasurementId: d.analyticsMeasurementId,
      });
      this.placeholders.set({
        activeRolesAdmins: d.roleGroupPlaceholders.activeRolesAdmins,
        dashboardAdmins: d.roleGroupPlaceholders.dashboardAdmins,
        auditors: d.roleGroupPlaceholders.auditors,
        powerUsers: d.roleGroupPlaceholders.powerUsers,
        noManagerUser: d.filterPlaceholders.noManagerUser,
        noManagerServiceAccount: d.filterPlaceholders.noManagerServiceAccount,
      });
    }
  }

  /** Language options for the IRIS dropdown (flag + name). */
  languageOptions(): IrisDropdownOption[] {
    return this.languages().map((l) => ({
      value: l.code,
      label: l.displayName,
      imageUrl: l.flagImage,
    }));
  }

  directoryTypeOptions(): IrisDropdownOption[] {
    return this.directoryTypes().map((t) => ({ value: t, label: t }));
  }

  setLanguage(code: string): void {
    this.form.get('language')?.setValue(code);
  }

  setDirectoryType(value: string): void {
    this.form.get('roleGroupsDirectoryType')?.setValue(value);
  }

  fieldError(controlName: string): string | null {
    return this.fieldErrors()[controlName] ?? null;
  }

  clearFieldError(controlName: string): void {
    const errors = { ...this.fieldErrors() };
    if (!(controlName in errors)) {
      return;
    }
    delete errors[controlName];
    this.fieldErrors.set(errors);
    if (Object.keys(errors).length === 0) {
      this.errorMessage.set(null);
    }
  }

  /**
   * Client-side gate matching the Razor validatePage(): step 0 requires the API
   * + RSTS URLs; step 1 requires the service-account username + password.
   */
  private validatePage(index: number): boolean {
    const v = this.form.value;
    if (index === 0) {
      if (!(v.apiBaseUrl ?? '').trim()) {
        return this.fail('REST API URL is required.', 'apiBaseUrl');
      }
      if (!(v.rstsUrl ?? '').trim()) {
        return this.fail('RSTS Token URL is required.', 'rstsUrl');
      }
    }
    if (index === 1) {
      if (!(v.serviceAccountUsername ?? '').trim()) {
        return this.fail('Service account username is required.', 'serviceAccountUsername');
      }
      if (!(v.serviceAccountPassword ?? '')) {
        return this.fail('Service account password is required.', 'serviceAccountPassword');
      }
    }
    return true;
  }

  private fail(message: string, controlName: string): false {
    this.errorMessage.set(message);
    this.fieldErrors.set({ [controlName]: message });
    return false;
  }

  next(): void {
    if (!this.validatePage(this.currentPage())) {
      return;
    }
    this.errorMessage.set(null);
    this.fieldErrors.set({});
    if (this.currentPage() < this.totalPages - 1) {
      this.currentPage.update((p) => p + 1);
    }
  }

  goToStep(index: number): void {
    if (index >= this.currentPage()) {
      return;
    }
    this.errorMessage.set(null);
    this.fieldErrors.set({});
    this.currentPage.set(index);
  }

  clearError(): void {
    this.errorMessage.set(null);
  }

  private toInt(value: unknown): number {
    const n = parseInt(String(value ?? '').trim(), 10);
    return Number.isFinite(n) ? n : 0;
  }

  async finish(): Promise<void> {
    // Re-run the same required-field validation the earlier steps enforced.
    if (!this.validatePage(0)) {
      this.currentPage.set(0);
      return;
    }
    if (!this.validatePage(1)) {
      this.currentPage.set(1);
      return;
    }

    const v = this.form.value;
    const payload: SetupPayload = {
      apiBaseUrl: (v.apiBaseUrl ?? '').trim(),
      rstsUrl: (v.rstsUrl ?? '').trim(),
      rstsScope: (v.rstsScope ?? '').trim(),
      serviceAccountUsername: (v.serviceAccountUsername ?? '').trim(),
      serviceAccountPassword: v.serviceAccountPassword ?? '',
      webInterfaceUrl: (v.webInterfaceUrl ?? '').trim(),
      language: v.language ?? 'en',
      roleGroupsDirectoryType: v.roleGroupsDirectoryType ?? 'ActiveDirectory',
      activeRolesAdminsGroup: (v.activeRolesAdminsGroup ?? '').trim(),
      dashboardAdminsGroup: (v.dashboardAdminsGroup ?? '').trim(),
      auditorsGroup: (v.auditorsGroup ?? '').trim(),
      powerUsersGroup: (v.powerUsersGroup ?? '').trim(),
      customNoManagerUserFilter: (v.customNoManagerUserFilter ?? '').trim(),
      customNoManagerServiceAccountFilter: (v.customNoManagerServiceAccountFilter ?? '').trim(),
      licensedDomainObjects: this.toInt(v.licensedDomainObjects),
      licensedPartitionObjects: this.toInt(v.licensedPartitionObjects),
      licensedAzureObjects: this.toInt(v.licensedAzureObjects),
      licensedSaasObjects: this.toInt(v.licensedSaasObjects),
      licensedTotalObjects: this.toInt(v.licensedTotalObjects),
      analyticsEnabled: !!v.analyticsEnabled,
      analyticsMeasurementId: (v.analyticsMeasurementId ?? '').trim(),
    };

    this.submitting.set(true);
    this.errorMessage.set(null);
    const result = await this.setup.save(payload);
    this.submitting.set(false);

    if (result.success) {
      window.location.assign(result.redirectUrl ?? `${this.setup.basePath}/login`);
      return;
    }
    this.errorMessage.set(result.error ?? 'Setup could not be saved.');
  }
}
