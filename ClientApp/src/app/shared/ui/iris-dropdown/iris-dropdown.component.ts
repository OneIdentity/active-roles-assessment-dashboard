import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  HostListener,
  Input,
  Output,
  ViewChild,
  signal,
} from '@angular/core';
import { IrisIconComponent } from '../iris-icon/iris-icon.component';

export interface IrisDropdownOption {
  value: string;
  label: string;
  /** Optional leading image (e.g. a flag) shown before the label. */
  imageUrl?: string;
}

/**
 * Reusable IRIS dropdown — a styled trigger button + listbox panel replacing
 * a native <select>, following the same token/markup conventions as the
 * other iris-* ports (border-color-muted box, --oi-shadow-low panel).
 * Supports a transparent "onColor" variant for use over a brand/colour
 * background (e.g. the login page's language switcher).
 */
@Component({
  selector: 'iris-dropdown',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [IrisIconComponent],
  host: {
    '[class.full-width]': 'fullWidth',
  },
  templateUrl: './iris-dropdown.component.html',
  styleUrl: './iris-dropdown.component.scss',
})
export class IrisDropdownComponent {
  @Input() options: readonly IrisDropdownOption[] = [];
  @Input() value: string | null = null;
  @Input() ariaLabel: string | null = null;
  @Input() variant: 'default' | 'onColor' = 'default';
  @Input() fullWidth = false;
  @Input() showChevron = false;

  @Output() valueChange = new EventEmitter<string>();

  readonly open = signal(false);
  /** True once an open menu has been measured as needing to flip above the trigger. */
  readonly openUpward = signal(false);

  @ViewChild('menu') private menuRef?: ElementRef<HTMLUListElement>;

  constructor(private readonly host: ElementRef<HTMLElement>) {}

  get selected(): IrisDropdownOption | undefined {
    return this.options.find((o) => o.value === this.value);
  }

  toggle(): void {
    const next = !this.open();
    this.open.set(next);
    if (next) {
      this.openUpward.set(false);
      // Defer until the menu has rendered so its height can be measured.
      setTimeout(() => this.updatePlacement());
    }
  }

  select(optionValue: string): void {
    this.open.set(false);
    if (optionValue !== this.value) {
      this.valueChange.emit(optionValue);
    }
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) {
      this.open.set(false);
    }
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    this.open.set(false);
  }

  /** Flips the menu above the trigger when there isn't enough viewport space below it. */
  private updatePlacement(): void {
    const menuEl = this.menuRef?.nativeElement;
    if (!menuEl) {
      return;
    }
    const hostRect = this.host.nativeElement.getBoundingClientRect();
    const menuHeight = menuEl.getBoundingClientRect().height;
    const spaceBelow = window.innerHeight - hostRect.bottom;
    const spaceAbove = hostRect.top;
    this.openUpward.set(menuHeight > spaceBelow && spaceAbove > spaceBelow);
  }
}
