import {
  ChangeDetectionStrategy,
  Component,
  OnChanges,
  OnDestroy,
  input,
  signal,
} from '@angular/core';

const COUNT_UP_MS = 900;

function prefersReducedMotion(): boolean {
  return (
    typeof window !== 'undefined' &&
    !!window.matchMedia &&
    window.matchMedia('(prefers-reduced-motion: reduce)').matches
  );
}

/**
 * Angular port of the IRIS StatCard: a single hero metric tile with a
 * count-up roll on load (honours reduced motion).
 */
@Component({
  selector: 'iris-stat-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './iris-stat-card.component.html',
  styleUrl: './iris-stat-card.component.scss',
})
export class IrisStatCardComponent implements OnChanges, OnDestroy {
  readonly label = input.required<string>();
  readonly value = input.required<number>();
  readonly error = input<string | null>(null);
  /** Accent colour (hex) shown as the card's top rule. */
  readonly accent = input<string | null>(null);

  readonly display = signal('0');
  private raf = 0;

  ngOnChanges(): void {
    cancelAnimationFrame(this.raf);
    const target = this.value() ?? 0;
    if (prefersReducedMotion() || target <= 0) {
      this.display.set(target.toLocaleString('en-US'));
      return;
    }
    let start = 0;
    const tick = (t: number) => {
      if (!start) start = t;
      const p = Math.min((t - start) / COUNT_UP_MS, 1);
      const eased = 1 - Math.pow(1 - p, 3);
      this.display.set(Math.round(target * eased).toLocaleString('en-US'));
      if (p < 1) this.raf = requestAnimationFrame(tick);
    };
    this.raf = requestAnimationFrame(tick);
  }

  ngOnDestroy(): void {
    cancelAnimationFrame(this.raf);
  }
}
