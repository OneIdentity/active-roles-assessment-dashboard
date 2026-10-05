import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

/**
 * Reusable IRIS stepper: a horizontal progress indicator with numbered
 * indicators that render a completed check, the current step fill, or an
 * upcoming outline. Faithful Angular port of the UX prototype's Stepper,
 * styled from @oneidentity/iris-ui-tokens. Presentational only — navigation is
 * driven by the host (the setup wizard) via `current`.
 */
@Component({
  selector: 'iris-stepper',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './iris-stepper.component.html',
  styleUrl: './iris-stepper.component.scss',
})
export class IrisStepperComponent {
  /** Ordered step labels. One indicator is rendered per label. */
  @Input() steps: string[] = [];

  /** Zero-based index of the current step. */
  @Input() current = 0;

  state(index: number): 'completed' | 'current' | 'upcoming' {
    if (index < this.current) return 'completed';
    if (index === this.current) return 'current';
    return 'upcoming';
  }
}
