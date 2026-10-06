import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  DashboardService,
  DashboardPayload,
  SegmentDimension,
} from '../../core/dashboard.service';
import { IrisBannerComponent } from '../../shared/ui/iris-banner/iris-banner.component';
import { IrisCardComponent } from '../../shared/ui/iris-card/iris-card.component';
import { IrisCheckboxComponent } from '../../shared/ui/iris-checkbox/iris-checkbox.component';
import { IrisStatCardComponent } from '../../shared/ui/iris-stat-card/iris-stat-card.component';
import { IrisDonutChartComponent } from '../../shared/ui/iris-donut-chart/iris-donut-chart.component';
import { IrisBarChartComponent } from '../../shared/ui/iris-bar-chart/iris-bar-chart.component';

/**
 * Angular replacement for the Razor main dashboard (Pages/Index.cshtml).
 * Renders the shell header actions, global segment filter, Overview KPI stat
 * cards and charts, and the navigation tiles to the (still Razor)
 * sub-dashboards. All data/permission logic lives server-side behind
 * GET /api/dashboard (DashboardController / DashboardDataService).
 */
@Component({
  selector: 'app-dashboard',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule,
    IrisBannerComponent,
    IrisCardComponent,
    IrisCheckboxComponent,
    IrisStatCardComponent,
    IrisDonutChartComponent,
    IrisBarChartComponent,
  ],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent implements OnInit {
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);
  readonly data = signal<DashboardPayload | null>(null);
  readonly savingSegment = signal(false);

  constructor(private readonly dashboard: DashboardService) {}

  get base(): string {
    return this.dashboard.basePath;
  }

  get logoUrl(): string {
    return `${this.base}/images/oneidentity-logo-v2.svg`;
  }

  url(path: string): string {
    const p = path.startsWith('/') ? path : `/${path}`;
    return `${this.base}${p}`;
  }

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    const result = await this.dashboard.getDashboard();
    if (result === 'unauthorized') {
      window.location.href = this.url('/login');
      return;
    }
    if (result === null) {
      this.errorMessage.set('The dashboard data could not be loaded. Please try again.');
    } else {
      this.errorMessage.set(null);
      this.data.set(result);
    }
    this.loading.set(false);
  }

  isSelected(dimension: SegmentDimension, segment: string): boolean {
    const f = this.data()?.segmentFilter;
    const list = dimension === 'Domain' ? f?.selectedDomains : f?.selectedTenants;
    return !!list?.some((s) => s.toLowerCase() === segment.toLowerCase());
  }

  async toggleSegment(dimension: SegmentDimension, segment: string, checked: boolean): Promise<void> {
    const f = this.data()?.segmentFilter;
    if (!f) return;
    const current = dimension === 'Domain' ? f.selectedDomains : f.selectedTenants;
    const next = checked
      ? [...current, segment]
      : current.filter((s) => s.toLowerCase() !== segment.toLowerCase());

    this.savingSegment.set(true);
    const ok = await this.dashboard.setSegment(dimension, next);
    this.savingSegment.set(false);
    if (!ok) {
      this.errorMessage.set('The segment filter could not be saved.');
      return;
    }
    await this.load();
  }
}
