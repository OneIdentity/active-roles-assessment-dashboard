import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

export type IrisButtonVariant = 'primary' | 'secondary' | 'danger';
export type IrisButtonSize = 's' | 'default' | 'l';
export type IrisButtonType = 'button' | 'submit' | 'reset';

/**
 * Reusable IRIS button. Styled entirely from @oneidentity/iris-ui-tokens
 * (--oi-button-* / --oi-spacing-* / --oi-border-* variables) to match the
 * UX prototype's Button.module.css. Intended to be reused across all pages.
 */
@Component({
  selector: 'iris-button',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './iris-button.component.html',
  styleUrl: './iris-button.component.scss',
})
export class IrisButtonComponent {
  @Input() variant: IrisButtonVariant = 'primary';
  @Input() size: IrisButtonSize = 'default';
  @Input() type: IrisButtonType = 'button';
  @Input() disabled = false;

  /** Stretches the button to fill its container (used by full-width CTAs). */
  @Input() fullWidth = false;
}
