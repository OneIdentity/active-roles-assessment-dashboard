import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

const VB_WIDTH = 400;
const PAD = { top: 8, right: 8, bottom: 28, left: 44 };

function niceMax(max: number, ticks: number): number {
  if (max <= 0) return 1;
  const raw = max / ticks;
  const mag = Math.pow(10, Math.floor(Math.log10(raw)));
  const step = [1, 2, 5, 10].map((m) => m * mag).find((s) => s >= raw) ?? raw;
  return Math.ceil(max / step) * step;
}

function formatTick(n: number): string {
  return n >= 1000 ? `${Math.round(n / 1000)}k` : String(n);
}

/**
 * Angular port of the IRIS BarChart: SVG column chart with gridlines,
 * y-axis ticks and x-axis labels. Pure SVG math, no charting dependency.
 */
@Component({
  selector: 'iris-bar-chart',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './iris-bar-chart.component.html',
  styleUrl: './iris-bar-chart.component.scss',
})
export class IrisBarChartComponent {
  readonly labels = input.required<string[]>();
  readonly values = input.required<number[]>();
  readonly colors = input.required<string[]>();
  readonly height = input(200);
  readonly tickCount = input(4);

  readonly width = VB_WIDTH;
  readonly padLeft = PAD.left;
  readonly padRight = PAD.right;

  readonly geometry = computed(() => {
    const h = this.height();
    const plotH = h - PAD.top - PAD.bottom;
    const baseline = PAD.top + plotH;
    const max = niceMax(Math.max(0, ...this.values()), this.tickCount());
    const ticks = Array.from({ length: this.tickCount() + 1 }, (_, i) => {
      const v = (max / this.tickCount()) * i;
      return { y: baseline - (v / max) * plotH, label: formatTick(v) };
    });

    const n = this.values().length || 1;
    const band = (VB_WIDTH - PAD.left - PAD.right) / n;
    const w = band * 0.5;
    const bars = this.values().map((v, i) => {
      const bh = (v / max) * plotH;
      return {
        x: PAD.left + band * i + (band - w) / 2,
        y: baseline - bh,
        w,
        h: bh,
        cx: PAD.left + band * i + band / 2,
        label: this.labels()[i],
        value: v,
        color: this.colors()[i],
      };
    });
    return { h, ticks, bars };
  });
}
