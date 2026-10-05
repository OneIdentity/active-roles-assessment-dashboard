import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

/**
 * Reusable IRIS card: a bordered content surface with an optional header
 * (title + helper). Faithful Angular port of the UX prototype's Card.module.css,
 * styled entirely from @oneidentity/iris-ui-tokens. Projected content goes in the
 * card body; an optional `[card-actions]` slot renders trailing header controls.
 */
@Component({
  selector: 'iris-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './iris-card.component.html',
  styleUrl: './iris-card.component.scss',
})
export class IrisCardComponent {
  /** Bold title text shown in the header. */
  @Input() heading?: string;

  /** Tertiary helper copy below the title. */
  @Input() helper?: string;

  /** Removes the surface treatment when the content is already inside a card. */
  @Input() flat = false;
}
