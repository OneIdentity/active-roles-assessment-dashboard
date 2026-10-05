import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  signal,
} from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import {
  SettingsService,
  SettingsOptions,
  SettingsLanguage,
  SettingsCapabilities,
  KpiVisibilityCategory,
  RoleMatrixRole,
  RoleMatrixPermission,
  KpiSettings,
  SettingsPayload,
} from '../../core/settings.service';
import { IrisButtonComponent } from '../../shared/ui/iris-button/iris-button.component';
import { IrisTextInputComponent } from '../../shared/ui/iris-text-input/iris-text-input.component';
import { IrisFormFieldComponent } from '../../shared/ui/iris-form-field/iris-form-field.component';
import { IrisCheckboxComponent } from '../../shared/ui/iris-checkbox/iris-checkbox.component';
import { IrisCardComponent } from '../../shared/ui/iris-card/iris-card.component';
import { IrisBannerComponent } from '../../shared/ui/iris-banner/iris-banner.component';
import { IrisCollapsibleSectionComponent } from '../../shared/ui/iris-collapsible-section/iris-collapsible-section.component';
import {
  IrisDropdownComponent,
  IrisDropdownOption,
} from '../../shared/ui/iris-dropdown/iris-dropdown.component';

const EMPTY_CAPABILITIES: SettingsCapabilities = {
  canAccessSettings: false,
  canManageUserSettings: false,
  canViewSystemSettings: false,
  canManageSystemSettings: false,
  isActiveRolesAdmin: false,
};

/**
 * Angular replacement for the former Razor Settings page (Pages/Settings.cshtml).
 * Preserves the identical behaviour: user vs system categories, permission
 * gating, collapsible sections, the data-driven KPI-visibility tree, the
 * admin-only role/permission matrix, the write-only service-account password,
 * and restart-required detection. Server work is handled by SettingsController
 * via SettingsService. Built from reusable IRIS components.
 */
@Component({
  selector: 'app-settings',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    IrisButtonComponent,
    IrisTextInputComponent,
    IrisFormFieldComponent,
    IrisCheckboxComponent,
    IrisCardComponent,
    IrisBannerComponent,
    IrisCollapsibleSectionComponent,
    IrisDropdownComponent,
  ],
  templateUrl: './settings.component.html',
  styleUrl: './settings.component.scss',
})
export class SettingsComponent implements OnInit {
  readonly loading = signal(true);
  readonly accessDenied = signal(false);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly savedMessage = signal<string | null>(null);
  readonly restartRequired = signal(false);

  readonly capabilities = signal<SettingsCapabilities>(EMPTY_CAPABILITIES);
  readonly languages = signal<SettingsLanguage[]>([]);
  readonly directoryTypes = signal<string[]>(['ActiveDirectory', 'Entra']);
  readonly perfTrendingMinimumInterval = signal(1);

  /** The data-driven KPI visibility tree returned by the server. */
  readonly kpiVisibility = signal<KpiVisibilityCategory[]>([]);

  /** The full flat KpiSettings bag, round-tripped verbatim on save. */
  private kpiSettings: KpiSettings = {};

  readonly matrixRoles = signal<RoleMatrixRole[]>([]);
  readonly matrixPermissions = signal<RoleMatrixPermission[]>([]);
  /** Currently-granted "Role:Permission" tokens. */
  readonly matrixGrants = signal<Set<string>>(new Set());

  readonly defaults = signal<SettingsOptions['defaults']>({
    noManagerUser: '',
    noManagerServiceAccount: '',
    roleGroupActiveRolesAdmins: '',
    roleGroupDashboardAdmins: '',
    roleGroupAuditors: '',
    roleGroupPowerUsers: '',
  });

  readonly form: FormGroup;

  get logoUrl(): string {
    return `${this.settings.basePath}/images/oneidentity-logo-v2.svg`;
  }

  constructor(
    private readonly fb: FormBuilder,
    private readonly settings: SettingsService,
  ) {
    this.form = this.fb.group({
      // User settings
      autoRefreshMinutes: [0],
      language: ['en'],

      // System settings
      webInterfaceUrl: [''],
      customNoManagerUserFilter: [''],
      customNoManagerServiceAccountFilter: [''],
      entraLargeGroupMemberThreshold: [1],
      dynamicGroupExpensiveRuleThreshold: [1],
      customADUserAccountAttributes: [''],

      apiBaseUrl: [''],
      rstsUrl: [''],
      resource: [''],
      ignoreSslErrors: [false],

      analyticsEnabled: [false],
      analyticsMeasurementId: [''],

      defaultNoGroupOwnerFilter: [''],
      defaultNoManagerUserFilter: [''],
      defaultNoManagerServiceAccountFilter: [''],
      defaultUserAccountExpiredFilter: [''],
      defaultUserAccountLockedOutFilter: [''],
      defaultEmptyGroupsFilter: [''],
      defaultADUserAccountsFilter: [''],
      defaultADGroupsFilter: [''],

      roleGroupsDirectoryType: ['ActiveDirectory'],
      activeRolesAdminsGroup: [''],
      dashboardAdminsGroup: [''],
      auditorsGroup: [''],
      powerUsersGroup: [''],

      defaultLanguage: [''],

      serviceAccountUsername: [''],
      serviceAccountPassword: [''],

      dailyRefreshTime: [''],
      loadOnStartup: [false],

      perfTrendingEnabled: [false],
      perfTrendingIntervalMinutes: [1],
      perfTrendingRetentionHours: [1],

      licensedDomainObjects: [0],
      licensedPartitionObjects: [0],
      licensedAzureObjects: [0],
      licensedSaasObjects: [0],
      licensedTotalObjects: [0],
    });
  }

  async ngOnInit(): Promise<void> {
    const options = await this.settings.getOptions();
    this.loading.set(false);

    if (!options) {
      // 403 (no access) or a transport error; show the access-denied state.
      this.accessDenied.set(true);
      return;
    }

    this.capabilities.set(options.capabilities);
    if (!options.capabilities.canAccessSettings) {
      this.accessDenied.set(true);
      return;
    }

    this.languages.set(options.languages ?? []);
    this.directoryTypes.set(options.directoryTypes ?? ['ActiveDirectory', 'Entra']);
    this.perfTrendingMinimumInterval.set(options.perfTrendingMinimumInterval);
    this.kpiVisibility.set(options.kpiVisibility ?? []);
    this.matrixRoles.set(options.matrixRoles ?? []);
    this.matrixPermissions.set(options.matrixPermissions ?? []);
    this.matrixGrants.set(new Set(options.matrixGrants ?? []));
    this.defaults.set(options.defaults);

    // Seed the full KpiSettings bag from the tree so flags not surfaced in the
    // UI are preserved on save. Category and item keys map directly to bool keys.
    this.kpiSettings = {};
    for (const cat of options.kpiVisibility ?? []) {
      this.kpiSettings[`${cat.key}Enabled`] = cat.enabled;
      for (const item of cat.items) {
        this.kpiSettings[item.key] = item.enabled;
      }
    }

    this.form.patchValue({
      autoRefreshMinutes: options.autoRefreshMinutes,
      language: options.language,
      webInterfaceUrl: options.webInterfaceUrl,
      customNoManagerUserFilter: options.customNoManagerUserFilter,
      customNoManagerServiceAccountFilter: options.customNoManagerServiceAccountFilter,
      entraLargeGroupMemberThreshold: options.entraLargeGroupMemberThreshold,
      dynamicGroupExpensiveRuleThreshold: options.dynamicGroupExpensiveRuleThreshold,
      customADUserAccountAttributes: options.customADUserAccountAttributes,
      apiBaseUrl: options.apiBaseUrl,
      rstsUrl: options.rstsUrl,
      resource: options.resource,
      ignoreSslErrors: options.ignoreSslErrors,
      analyticsEnabled: options.analyticsEnabled,
      analyticsMeasurementId: options.analyticsMeasurementId,
      defaultNoGroupOwnerFilter: options.defaultNoGroupOwnerFilter,
      defaultNoManagerUserFilter: options.defaultNoManagerUserFilter,
      defaultNoManagerServiceAccountFilter: options.defaultNoManagerServiceAccountFilter,
      defaultUserAccountExpiredFilter: options.defaultUserAccountExpiredFilter,
      defaultUserAccountLockedOutFilter: options.defaultUserAccountLockedOutFilter,
      defaultEmptyGroupsFilter: options.defaultEmptyGroupsFilter,
      defaultADUserAccountsFilter: options.defaultADUserAccountsFilter,
      defaultADGroupsFilter: options.defaultADGroupsFilter,
      roleGroupsDirectoryType: options.roleGroupsDirectoryType,
      activeRolesAdminsGroup: options.activeRolesAdminsGroup,
      dashboardAdminsGroup: options.dashboardAdminsGroup,
      auditorsGroup: options.auditorsGroup,
      powerUsersGroup: options.powerUsersGroup,
      defaultLanguage: options.defaultLanguage,
      serviceAccountUsername: options.serviceAccountUsername,
      serviceAccountPassword: '',
      dailyRefreshTime: options.dailyRefreshTime,
      loadOnStartup: options.loadOnStartup,
      perfTrendingEnabled: options.perfTrendingEnabled,
      perfTrendingIntervalMinutes: options.perfTrendingIntervalMinutes,
      perfTrendingRetentionHours: options.perfTrendingRetentionHours,
      licensedDomainObjects: options.licensedDomainObjects,
      licensedPartitionObjects: options.licensedPartitionObjects,
      licensedAzureObjects: options.licensedAzureObjects,
      licensedSaasObjects: options.licensedSaasObjects,
      licensedTotalObjects: options.licensedTotalObjects,
    });

    // Read-only system view when the user can view but not manage system settings.
    if (!options.capabilities.canManageSystemSettings) {
      this.disableSystemControls();
    }
  }

  // ---------------------------------------------------------------------
  // Dropdown options
  // ---------------------------------------------------------------------

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

  setDefaultLanguage(code: string): void {
    this.form.get('defaultLanguage')?.setValue(code);
  }

  setDirectoryType(value: string): void {
    this.form.get('roleGroupsDirectoryType')?.setValue(value);
  }

  // ---------------------------------------------------------------------
  // KPI visibility tree
  // ---------------------------------------------------------------------

  toggleKpiCategory(category: KpiVisibilityCategory, enabled: boolean): void {
    this.kpiSettings[`${category.key}Enabled`] = enabled;
    this.kpiVisibility.update((cats) =>
      cats.map((c) => (c.key === category.key ? { ...c, enabled } : c)),
    );
  }

  toggleKpiItem(
    category: KpiVisibilityCategory,
    itemKey: string,
    enabled: boolean,
  ): void {
    this.kpiSettings[itemKey] = enabled;
    this.kpiVisibility.update((cats) =>
      cats.map((c) =>
        c.key === category.key
          ? {
              ...c,
              items: c.items.map((i) =>
                i.key === itemKey ? { ...i, enabled } : i,
              ),
            }
          : c,
      ),
    );
  }

  // ---------------------------------------------------------------------
  // Role/permission matrix
  // ---------------------------------------------------------------------

  grantToken(role: RoleMatrixRole, permission: RoleMatrixPermission): string {
    return `${role.key}:${permission.key}`;
  }

  isGranted(role: RoleMatrixRole, permission: RoleMatrixPermission): boolean {
    // Fixed roles (Dashboard Administrator) always have every permission.
    return role.fixed || this.matrixGrants().has(this.grantToken(role, permission));
  }

  toggleGrant(
    role: RoleMatrixRole,
    permission: RoleMatrixPermission,
    granted: boolean,
  ): void {
    if (role.fixed) {
      return;
    }
    const token = this.grantToken(role, permission);
    this.matrixGrants.update((set) => {
      const next = new Set(set);
      if (granted) {
        next.add(token);
      } else {
        next.delete(token);
      }
      return next;
    });
  }

  // ---------------------------------------------------------------------
  // Save
  // ---------------------------------------------------------------------

  private toInt(value: unknown): number {
    const n = parseInt(String(value ?? '').trim(), 10);
    return Number.isFinite(n) ? n : 0;
  }

  async save(): Promise<void> {
    const v = this.form.value;
    const payload: SettingsPayload = {
      autoRefreshMinutes: this.toInt(v.autoRefreshMinutes),
      language: v.language ?? 'en',
      kpiSettings: this.kpiSettings,

      webInterfaceUrl: (v.webInterfaceUrl ?? '').trim(),
      customNoManagerUserFilter: (v.customNoManagerUserFilter ?? '').trim(),
      customNoManagerServiceAccountFilter: (v.customNoManagerServiceAccountFilter ?? '').trim(),
      entraLargeGroupMemberThreshold: this.toInt(v.entraLargeGroupMemberThreshold),
      dynamicGroupExpensiveRuleThreshold: this.toInt(v.dynamicGroupExpensiveRuleThreshold),
      customADUserAccountAttributes: v.customADUserAccountAttributes ?? '',

      apiBaseUrl: (v.apiBaseUrl ?? '').trim(),
      rstsUrl: (v.rstsUrl ?? '').trim(),
      resource: (v.resource ?? '').trim(),
      ignoreSslErrors: !!v.ignoreSslErrors,

      analyticsEnabled: !!v.analyticsEnabled,
      analyticsMeasurementId: (v.analyticsMeasurementId ?? '').trim(),

      defaultNoGroupOwnerFilter: (v.defaultNoGroupOwnerFilter ?? '').trim(),
      defaultNoManagerUserFilter: (v.defaultNoManagerUserFilter ?? '').trim(),
      defaultNoManagerServiceAccountFilter: (v.defaultNoManagerServiceAccountFilter ?? '').trim(),
      defaultUserAccountExpiredFilter: (v.defaultUserAccountExpiredFilter ?? '').trim(),
      defaultUserAccountLockedOutFilter: (v.defaultUserAccountLockedOutFilter ?? '').trim(),
      defaultEmptyGroupsFilter: (v.defaultEmptyGroupsFilter ?? '').trim(),
      defaultADUserAccountsFilter: (v.defaultADUserAccountsFilter ?? '').trim(),
      defaultADGroupsFilter: (v.defaultADGroupsFilter ?? '').trim(),

      roleGroupsDirectoryType: v.roleGroupsDirectoryType ?? 'ActiveDirectory',
      activeRolesAdminsGroup: (v.activeRolesAdminsGroup ?? '').trim(),
      dashboardAdminsGroup: (v.dashboardAdminsGroup ?? '').trim(),
      auditorsGroup: (v.auditorsGroup ?? '').trim(),
      powerUsersGroup: (v.powerUsersGroup ?? '').trim(),

      defaultLanguage: (v.defaultLanguage ?? '').trim(),

      serviceAccountUsername: (v.serviceAccountUsername ?? '').trim(),
      serviceAccountPassword: v.serviceAccountPassword ?? '',

      dailyRefreshTime: (v.dailyRefreshTime ?? '').trim(),
      loadOnStartup: !!v.loadOnStartup,

      perfTrendingEnabled: !!v.perfTrendingEnabled,
      perfTrendingIntervalMinutes: this.toInt(v.perfTrendingIntervalMinutes),
      perfTrendingRetentionHours: this.toInt(v.perfTrendingRetentionHours),

      licensedDomainObjects: this.toInt(v.licensedDomainObjects),
      licensedPartitionObjects: this.toInt(v.licensedPartitionObjects),
      licensedAzureObjects: this.toInt(v.licensedAzureObjects),
      licensedSaasObjects: this.toInt(v.licensedSaasObjects),
      licensedTotalObjects: this.toInt(v.licensedTotalObjects),

      rolePermissionGrants: Array.from(this.matrixGrants()),
    };

    this.submitting.set(true);
    this.errorMessage.set(null);
    this.savedMessage.set(null);
    const result = await this.settings.save(payload);
    this.submitting.set(false);

    if (result.success) {
      this.savedMessage.set('Settings saved successfully.');
      this.restartRequired.set(!!result.restartRequired);
      // The password is write-only; clear it after a successful save.
      this.form.get('serviceAccountPassword')?.setValue('');
      return;
    }
    this.errorMessage.set(result.error ?? 'Settings could not be saved.');
  }

  clearError(): void {
    this.errorMessage.set(null);
  }

  clearSaved(): void {
    this.savedMessage.set(null);
  }

  /** Disables every system-settings control for the view-only (no manage) case. */
  private disableSystemControls(): void {
    const systemControls = [
      'webInterfaceUrl',
      'customNoManagerUserFilter',
      'customNoManagerServiceAccountFilter',
      'entraLargeGroupMemberThreshold',
      'dynamicGroupExpensiveRuleThreshold',
      'customADUserAccountAttributes',
      'apiBaseUrl',
      'rstsUrl',
      'resource',
      'ignoreSslErrors',
      'analyticsEnabled',
      'analyticsMeasurementId',
      'defaultNoGroupOwnerFilter',
      'defaultNoManagerUserFilter',
      'defaultNoManagerServiceAccountFilter',
      'defaultUserAccountExpiredFilter',
      'defaultUserAccountLockedOutFilter',
      'defaultEmptyGroupsFilter',
      'defaultADUserAccountsFilter',
      'defaultADGroupsFilter',
      'roleGroupsDirectoryType',
      'activeRolesAdminsGroup',
      'dashboardAdminsGroup',
      'auditorsGroup',
      'powerUsersGroup',
      'defaultLanguage',
      'serviceAccountUsername',
      'serviceAccountPassword',
      'dailyRefreshTime',
      'loadOnStartup',
      'perfTrendingEnabled',
      'perfTrendingIntervalMinutes',
      'perfTrendingRetentionHours',
      'licensedDomainObjects',
      'licensedPartitionObjects',
      'licensedAzureObjects',
      'licensedSaasObjects',
      'licensedTotalObjects',
    ];
    for (const name of systemControls) {
      this.form.get(name)?.disable();
    }
  }
}
