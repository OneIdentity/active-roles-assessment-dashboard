import {
  ChangeDetectionStrategy,
  Component,
  Input,
  OnChanges,
  isDevMode,
} from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { manifest } from '@oneidentity/iris-ui-icons';

/**
 * Index icons by name once. Each entry is the raw `<svg…>` string from the
 * official @oneidentity/iris-ui-icons package (the same library Active Roles
 * uses), so the icon set stays identical across the products.
 */
const ICONS_BY_NAME: Record<string, string> = (() => {
  const map: Record<string, string> = Object.create(null);
  for (const i of manifest.icons) {
    map[i.name] = i.svg;
  }
  return map;
})();

/**
 * IRIS icon — renders a named SVG from the design-system icon library.
 *
 * Faithful Angular port of the UX team's `Icon` component. The SVG sources use
 * `currentColor` for strokes/fills, so colour is driven purely by surrounding
 * text colour. Size is a CSS length applied to the host element's width/height.
 */
@Component({
  selector: 'iris-icon',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './iris-icon.component.html',
  styleUrl: './iris-icon.component.scss',
  host: {
    '[style.width]': 'size',
    '[style.height]': 'size',
  },
})
export class IrisIconComponent implements OnChanges {
  /** Icon name, e.g. "MagnifyingGlass". */
  @Input({ required: true }) name!: string;

  /** CSS length applied to width/height. Defaults to 20px (UX default). */
  @Input() size = '20px';

  /** Accessible title; omit for decorative use. */
  @Input() title?: string;

  html: SafeHtml = '';

  constructor(private readonly sanitizer: DomSanitizer) {}

  ngOnChanges(): void {
    const svg = ICONS_BY_NAME[this.name];

    if (!svg) {
      if (isDevMode()) {
        console.warn(`<iris-icon name="${this.name}"> not found in manifest.`);
      }
      this.html = '';
      return;
    }

    // Inject role/aria attributes into the raw svg string.
    const markup = this.title
      ? svg
          .replace('<svg ', '<svg role="img" focusable="false" ')
          .replace('>', `><title>${escapeXml(this.title)}</title>`)
      : svg.replace('<svg ', '<svg aria-hidden="true" focusable="false" ');

    // Trusted: markup originates from the vendored design-system package.
    this.html = this.sanitizer.bypassSecurityTrustHtml(markup);
  }
}

function escapeXml(s: string): string {
  return String(s)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&apos;');
}
