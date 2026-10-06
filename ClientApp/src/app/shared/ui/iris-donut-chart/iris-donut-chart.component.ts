import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

interface Arc {
  path: string;
  label: string;
  value: number;
  color: string;
}

const SIZE = 200;
const OUTER = 96;
const INNER = 64;

function point(r: number, angle: number): string {
  const a = angle - Math.PI / 2;
  return `${(SIZE / 2 + r * Math.cos(a)).toFixed(3)} ${(SIZE / 2 + r * Math.sin(a)).toFixed(3)}`;
}

function arcPath(start: number, end: number, inner: number): string {
  // A full circle cannot be drawn as a single arc; nudge the end.
  if (end - start >= Math.PI * 2) end = start + Math.PI * 2 - 0.0001;
  const large = end - start > Math.PI ? 1 : 0;
  const outerPath = `M ${point(OUTER, start)} A ${OUTER} ${OUTER} 0 ${large} 1 ${point(OUTER, end)}`;
  if (inner <= 0) return `${outerPath} L ${SIZE / 2} ${SIZE / 2} Z`;
  return `${outerPath} L ${point(inner, end)} A ${inner} ${inner} 0 ${large} 0 ${point(inner, start)} Z`;
}

/**
 * Angular port of the IRIS DonutChart: SVG donut (or pie) with a centre total
 * and a value legend. Pure SVG math, no charting dependency.
 */
@Component({
  selector: 'iris-donut-chart',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './iris-donut-chart.component.html',
  styleUrl: './iris-donut-chart.component.scss',
})
export class IrisDonutChartComponent {
  readonly labels = input.required<string[]>();
  readonly values = input.required<number[]>();
  readonly colors = input.required<string[]>();
  /** Render a solid pie instead of a donut. */
  readonly pie = input(false);
  readonly centerLabel = input('Total');

  readonly size = SIZE;
  readonly total = computed(() => this.values().reduce((a, b) => a + b, 0));

  readonly arcs = computed<Arc[]>(() => {
    const total = this.total();
    if (total <= 0) return [];
    let angle = 0;
    const inner = this.pie() ? 0 : INNER;
    return this.values().map((v, i) => {
      const sweep = (v / total) * Math.PI * 2;
      const path = arcPath(angle, angle + sweep, inner);
      angle += sweep;
      return { path, label: this.labels()[i], value: v, color: this.colors()[i] };
    });
  });
}
