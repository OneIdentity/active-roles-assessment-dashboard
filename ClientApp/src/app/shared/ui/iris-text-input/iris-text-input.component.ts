import {
  ChangeDetectionStrategy,
  Component,
  Input,
  forwardRef,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

export type IrisTextInputSize = 's' | 'default' | 'l';
export type IrisTextInputType = 'text' | 'password' | 'email' | 'search';

/**
 * Reusable IRIS text input. Styled from @oneidentity/iris-ui-tokens to match
 * the UX prototype's TextInput.module.css (muted border, hover/focus-within
 * states, 1.5px inset focus ring, invalid state). Implements
 * ControlValueAccessor so it binds directly to reactive/template forms.
 */
@Component({
  selector: 'iris-text-input',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './iris-text-input.component.html',
  styleUrl: './iris-text-input.component.scss',
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => IrisTextInputComponent),
      multi: true,
    },
  ],
})
export class IrisTextInputComponent implements ControlValueAccessor {
  @Input() type: IrisTextInputType = 'text';
  @Input() size: IrisTextInputSize = 'default';
  @Input() placeholder = '';
  @Input() autocomplete: string | null = null;
  @Input() autofocus = false;
  @Input() inputId: string | null = null;
  @Input() invalid = false;

  value = '';
  disabled = false;

  private onChange: (value: string) => void = () => {};
  private onTouched: () => void = () => {};

  writeValue(value: string | null): void {
    this.value = value ?? '';
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled = isDisabled;
  }

  handleInput(event: Event): void {
    this.value = (event.target as HTMLInputElement).value;
    this.onChange(this.value);
  }

  handleBlur(): void {
    this.onTouched();
  }
}
