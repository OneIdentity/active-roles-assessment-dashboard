import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  signal,
} from '@angular/core';
import { IrisIconComponent } from '../iris-icon/iris-icon.component';

export type IrisBannerType = 'info' | 'warning' | 'error' | 'success';

@Component({
  selector: 'iris-banner',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [IrisIconComponent],
  templateUrl: './iris-banner.component.html',
  styleUrl: './iris-banner.component.scss',
})
export class IrisBannerComponent {
  @Input() type: IrisBannerType = 'info';
  @Input() colored = false;
  @Input() dismissable = true;
  @Input() showActions = false;
  @Input() title = '';
  @Input() supportingText = '';
  @Input() primaryActionLabel = 'View';
  @Input() secondaryActionLabel = 'Dismiss';
  @Input() dismissAriaLabel = 'Dismiss';

  @Output() dismissed = new EventEmitter<void>();
  @Output() primaryActionClick = new EventEmitter<void>();
  @Output() secondaryActionClick = new EventEmitter<void>();

  readonly hidden = signal(false);

  get iconName(): string {
    switch (this.type) {
      case 'info': return 'Info';
      case 'warning':
      case 'error': return 'Warning';
      case 'success': return 'CheckCircle';
    }
  }

  get role(): 'alert' | 'status' {
    return this.type === 'error' || this.type === 'warning' ? 'alert' : 'status';
  }

  dismiss(): void {
    this.hidden.set(true);
    this.dismissed.emit();
  }
}