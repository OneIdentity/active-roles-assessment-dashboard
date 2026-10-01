import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

/**
 * Reusable IRIS form field wrapper: label (with optional required marker) +
 * projected control + optional error message. Ported from the UX prototype's
 * FormField.module.css and styled from @oneidentity/iris-ui-tokens.
 *
 * Usage:
 *   <iris-form-field label="Username" [for]="'username'" [error]="usernameError">
 *     <iris-text-input inputId="username" ... />
 *   </iris-form-field>
 */
@Component({
  selector: 'iris-form-field',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './iris-form-field.component.html',
  styleUrl: './iris-form-field.component.scss',
})
export class IrisFormFieldComponent {
  @Input() label = '';
  @Input() for: string | null = null;
  @Input() required = false;
  /** Error text to display; when non-empty the error region is shown. */
  @Input() error: string | null = null;
}
