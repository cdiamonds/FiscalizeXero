import { Component, DestroyRef, Input, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subscription } from 'rxjs';
import { QrCodeService } from '../../core/services/xero/qr-code.service';

type Source = 'api' | 'external';

/**
 * The fiscal QR code. Drawn by our own API first (same library as the stamped PDF, so nothing outside can block it),
 * falling back to a public QR service only if the API can't be reached. If both fail it says so and offers a retry,
 * instead of leaving a dead box. The FDMS link itself is shown next to it by the page, so verification never
 * depends on the picture.
 */
@Component({
  selector: 'app-qr-code',
  imports: [CommonModule],
  template: `
    <div class="inline-flex flex-col items-center gap-2">
      <div class="rounded-xl bg-white p-2 shadow-sm ring-1 ring-slate-200 dark:ring-white/10"
           [class.ring-2]="hasError" [class.ring-red-500]="hasError">
        @if (!link()) {
          <div class="flex items-center justify-center text-[10px] font-medium text-slate-400" [style.width.px]="size" [style.height.px]="size">No QR</div>
        } @else if (src()) {
          <img [src]="src()" [width]="size" [height]="size" alt="Fiscal QR code: scan to verify on ZIMRA FDMS"
               class="block [image-rendering:pixelated]" decoding="async" (error)="onImageError()" />
        } @else if (failed()) {
          <div class="flex flex-col items-center justify-center gap-2 text-center" [style.width.px]="size" [style.height.px]="size" role="status">
            <span class="text-[11px] font-medium text-slate-500">QR code couldn't be drawn</span>
            <button type="button" (click)="retry()"
                    class="rounded-lg border border-slate-200 px-2.5 py-1 text-[11px] font-semibold text-slate-700 transition hover:bg-slate-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary-500">
              Retry
            </button>
          </div>
        } @else {
          <div class="animate-pulse rounded-md bg-slate-100" [style.width.px]="size" [style.height.px]="size" aria-label="Loading QR code"></div>
        }
      </div>
      @if (hasError) {
        <div class="inline-flex items-center gap-1 rounded-full bg-red-100 px-2 py-1 text-[10px] font-semibold text-red-700 dark:bg-red-500/15 dark:text-red-300">
          <svg class="h-3 w-3" fill="currentColor" viewBox="0 0 20 20" aria-hidden="true"><path fill-rule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z" clip-rule="evenodd"/></svg>
          Fiscalized with errors
        </div>
      }
    </div>
  `
})
export class QrCode {
  private qr = inject(QrCodeService);

  /** The FDMS verification link to encode. Changing it redraws the code. */
  @Input() set value(v: string | null | undefined) {
    const next = (v ?? '').trim() || null;
    if (next === this.link()) return;
    this.link.set(next);
    this.load();
  }
  @Input() size = 160;
  @Input() hasError = false;

  readonly link = signal<string | null>(null);
  readonly src = signal<string | null>(null);
  readonly failed = signal(false);
  private source: Source = 'api';
  private sub?: Subscription;

  constructor() { inject(DestroyRef).onDestroy(() => this.sub?.unsubscribe()); }

  retry(): void { this.load(); }

  /** API first; on failure, the public service; on its failure, the retry state. */
  private load(): void {
    this.sub?.unsubscribe();
    this.src.set(null);
    this.failed.set(false);
    const link = this.link();
    if (!link) return;
    this.source = 'api';
    this.sub = this.qr.image(link).subscribe({
      next: (uri) => this.src.set(uri),
      error: () => this.useExternal(link)
    });
  }

  private useExternal(link: string): void {
    this.source = 'external';
    this.src.set(`https://api.qrserver.com/v1/create-qr-code/?size=${this.size}x${this.size}&data=${encodeURIComponent(link)}&ecc=M&margin=0`);
  }

  onImageError(): void {
    const link = this.link();
    if (this.source === 'api' && link) { this.useExternal(link); return; }
    this.src.set(null);
    this.failed.set(true);
  }
}
