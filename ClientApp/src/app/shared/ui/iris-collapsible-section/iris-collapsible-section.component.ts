import {
  ChangeDetectionStrategy,
  Component,
  Input,
  signal,
} from '@angular/core';
import { IrisIconComponent } from '../iris-icon/iris-icon.component';

/**
 * Reusable IRIS collapsible section: an expandable group with a header
 * (title + optional helper) and a projected body. Mirrors the expandable
 * "section" rows of the former Razor Settings page. Styled from
 * @oneidentity/iris-ui-tokens for consistency with the rest of the dashboard.
 */
@Component({
  selector: 'iris-collapsible-section',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [IrisIconComponent],
  templateUrl: './iris-collapsible-section.component.html',
  styleUrl: './iris-collapsible-section.component.scss',
})
export class IrisCollapsibleSectionComponent {
  /** Bold title text shown in the section header. */
  @Input() heading = '';

  /** Tertiary helper copy below the title. */
  @Input() helper?: string;

  /** Initial expanded state. */
  @Input() set expanded(value: boolean) {
    this.open.set(value);
  }

  readonly open = signal(false);

  toggle(): void {
    this.open.update((v) => !v);
  }
}
