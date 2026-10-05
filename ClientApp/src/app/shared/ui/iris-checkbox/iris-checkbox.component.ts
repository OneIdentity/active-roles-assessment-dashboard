import { ChangeDetectionStrategy, Component, Input, forwardRef } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

/**
 * Reusable IRIS checkbox: a controlled boolean checkbox implementing
 * ControlValueAccessor so it binds with ngModel / reactive forms. Faithful
 * Angular port of the UX prototype's Checkbox, styled from
 * @oneidentity/iris-ui-tokens with a draw-in checkmark. Projected content is the
 * label shown beside the box.
 */
@Component({
  selector: 'iris-checkbox',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './iris-checkbox.component.html',
  styleUrl: './iris-checkbox.component.scss',
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => IrisCheckboxComponent),
      multi: true,
    },
  ],
})
export class IrisCheckboxComponent implements ControlValueAccessor {
  @Input() disabled = false;
  @Input() ariaLabel?: string;

  checked = false;

  private onChange: (value: boolean) => void = () => {};
  private onTouched: () => void = () => {};

  writeValue(value: boolean): void {
    this.checked = !!value;
  }

  registerOnChange(fn: (value: boolean) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled = isDisabled;
  }

  toggle(next: boolean): void {
    this.checked = next;
    this.onChange(next);
    this.onTouched();
  }
}
