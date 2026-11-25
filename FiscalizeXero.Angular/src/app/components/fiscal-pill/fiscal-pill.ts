import { Component, ElementRef, HostListener, Input, OnDestroy, ViewChild, computed, inject, signal } from '@angular/core';
import { CdkConnectedOverlay, CdkOverlayOrigin, ConnectedPosition, Overlay } from '@angular/cdk/overlay';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FiscalFailure } from '../../core/models/xero.models';
import { absoluteTime, failureGuide, failureMeta, relativeTime } from '../../core/utils/fiscal-failure';

type FiscalState = 'success' | 'failed' | 'pending';

const POSITIONS: ConnectedPosition[] = [
  { originX: 'center', originY: 'bottom', overlayX: 'center', overlayY: 'top', offsetY: 8 },
  { originX: 'center', originY: 'top', overlayX: 'center', overlayY: 'bottom', offsetY: -8 },
  { originX: 'start', originY: 'bottom', overlayX: 'start', overlayY: 'top', offsetY: 8 },
  { originX: 'end', originY: 'bottom', overlayX: 'end', overlayY: 'top', offsetY: 8 },
];

/**
 * Fiscal status of a document: Fiscalized, Not Fiscalized, or Failed. A failed pill opens a popover with what went
 * wrong and how to fix it, on hover or keyboard focus (desktop) and on tap (touch). The popover is a CDK overlay in a
 * body-level layer, so blurred or scrolling containers can't clip or offset it; it flips above the pill near the
 * bottom of the screen, stays 12px inside the viewport, and closes when the page scrolls.
 */
@Component({
  selector: 'app-fiscal-pill',
  imports: [CommonModule, RouterLink, CdkOverlayOrigin, CdkConnectedOverlay],
  template: `
    @if (state() === 'failed') {
      <button #trigger cdkOverlayOrigin #origin="cdkOverlayOrigin" type="button"
              class="inline-flex items-center gap-1 rounded-full bg-rose-100 px-2.5 py-0.5 text-xs font-semibold text-rose-700 ring-1 ring-inset ring-rose-500/20 transition hover:bg-rose-200/80 focus:outline-none focus-visible:ring-2 focus-visible:ring-rose-500 dark:bg-rose-500/15 dark:text-rose-300 dark:hover:bg-rose-500/25"
              [attr.aria-expanded]="open()" [attr.aria-describedby]="open() ? popoverId : null"
              aria-haspopup="dialog" [attr.aria-label]="'Fiscalization failed. ' + failure()!.message"
              (mouseenter)="show()" (mouseleave)="scheduleHide()" (focus)="show()" (blur)="scheduleHide()"
              (click)="toggle($event)">
        <svg class="h-3 w-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"/><path d="M12 8v4M12 16h.01"/></svg>
        Failed
        @if (failure()!.attempts > 1) {
          <span class="rounded-full bg-rose-600/10 px-1.5 text-[10px] font-bold tabular-nums dark:bg-rose-300/15">{{ failure()!.attempts }}</span>
        }
      </button>

      <ng-template cdkConnectedOverlay [cdkConnectedOverlayOrigin]="origin" [cdkConnectedOverlayOpen]="open()"
                   [cdkConnectedOverlayPositions]="positions" [cdkConnectedOverlayPush]="true" [cdkConnectedOverlayViewportMargin]="12"
                   [cdkConnectedOverlayScrollStrategy]="scrollStrategy" [cdkConnectedOverlayWidth]="'min(340px, calc(100vw - 24px))'"
                   (detach)="close()">
        <div [id]="popoverId" role="dialog" aria-label="Fiscalization failure details"
             class="w-full overflow-hidden rounded-2xl border border-rose-200/80 bg-white text-left shadow-2xl shadow-slate-900/15 ring-1 ring-black/5 popover-in dark:border-rose-500/30 dark:bg-slate-900 dark:ring-white/10"
             (mouseenter)="cancelHide()" (mouseleave)="scheduleHide()" (click)="$event.stopPropagation()">
          <div class="flex items-start gap-3 border-b border-rose-100 bg-rose-50/80 px-4 py-3 dark:border-rose-500/20 dark:bg-rose-500/10">
            <span class="mt-0.5 flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-lg bg-rose-500 text-white shadow-sm">
              <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><path d="M12 9v4M12 17h.01"/></svg>
            </span>
            <div class="min-w-0 flex-1">
              <p class="text-sm font-semibold text-rose-800 dark:text-rose-200">Fiscalization failed</p>
              <p class="mt-0.5 text-[11px] text-rose-700/80 dark:text-rose-300/80" [title]="absolute(failure()!.lastFailedAt)">
                {{ relative(failure()!.lastFailedAt) }} · {{ meta(failure()!) }}
              </p>
            </div>
            <span class="flex-shrink-0 rounded-full bg-white/80 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-rose-700 ring-1 ring-rose-200 dark:bg-slate-900/60 dark:text-rose-300 dark:ring-rose-500/30">{{ guide().label }}</span>
          </div>

          <div class="max-h-64 space-y-3 overflow-y-auto px-4 py-3">
            <p class="whitespace-pre-line break-words text-xs leading-relaxed text-slate-700 dark:text-slate-200">{{ failure()!.message }}</p>
            @if (failure()!.details.length) {
              <ul class="space-y-1">
                @for (d of failure()!.details.slice(0, 6); track $index) {
                  <li class="flex items-start gap-2 text-xs text-slate-600 dark:text-slate-300">
                    <span class="mt-1.5 h-1.5 w-1.5 flex-shrink-0 rounded-full bg-rose-400"></span><span class="break-words">{{ d }}</span>
                  </li>
                }
                @if (failure()!.details.length > 6) {
                  <li class="pl-3.5 text-[11px] text-slate-400">+{{ failure()!.details.length - 6 }} more on the document page</li>
                }
              </ul>
            }
          </div>

          <div class="flex flex-col gap-2 border-t border-slate-100 bg-slate-50/70 px-4 py-3 dark:border-white/10 dark:bg-white/[0.03]">
            <p class="text-[11px] leading-snug text-slate-500 dark:text-slate-400">{{ guide().hint }}</p>
            @if (guide().action; as action) {
              <a [routerLink]="action.link" [queryParams]="action.queryParams ?? null" (click)="close()"
                 class="inline-flex flex-shrink-0 items-center gap-1 self-end rounded-lg bg-white px-2.5 py-1.5 text-[11px] font-semibold text-primary-600 shadow-sm ring-1 ring-slate-200 transition hover:bg-primary-50 dark:bg-slate-800 dark:text-brand-400 dark:ring-white/10 dark:hover:bg-white/10">
                {{ action.label }}
                <svg class="h-3 w-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M9 18l6-6-6-6"/></svg>
              </a>
            }
          </div>
        </div>
      </ng-template>
    } @else {
      <span [ngClass]="state() === 'success'
              ? 'bg-emerald-100 text-emerald-700 dark:bg-primary-600/15 dark:text-emerald-300'
              : 'bg-slate-100 text-slate-700 dark:bg-white/10 dark:text-slate-300'"
            class="inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-xs font-medium">
        <span class="h-1.5 w-1.5 rounded-full" [ngClass]="state() === 'success' ? 'bg-primary-600' : 'bg-slate-400'"></span>
        {{ state() === 'success' ? 'Fiscalized' : 'Not Fiscalized' }}
      </span>
    }
  `,
  styles: [`
    .popover-in { animation: popover-in 140ms ease-out; }
    @keyframes popover-in { from { opacity: 0; transform: translateY(-4px) scale(0.98); } to { opacity: 1; transform: none; } }
    @media (prefers-reduced-motion: reduce) { .popover-in { animation: none; } }
  `]
})
export class FiscalPill implements OnDestroy {
  @Input() set fiscalized(v: boolean | null | undefined) { this._fiscalized.set(!!v); }
  /** The document's latest failed attempt; ignored once the document is fiscalized. */
  @Input() set failureInfo(v: FiscalFailure | null | undefined) { this.failure.set(v ?? null); if (!v) this.close(); }

  @ViewChild('trigger') private trigger?: ElementRef<HTMLElement>;

  private _fiscalized = signal(false);
  readonly failure = signal<FiscalFailure | null>(null);
  readonly open = signal(false);
  readonly positions = POSITIONS;
  // A popover left behind while the page scrolls would point at nothing, so scrolling closes it.
  readonly scrollStrategy = inject(Overlay).scrollStrategies.close();
  readonly popoverId = `fiscal-failure-${Math.random().toString(36).slice(2, 9)}`;

  readonly state = computed<FiscalState>(() => this._fiscalized() ? 'success' : this.failure() ? 'failed' : 'pending');
  readonly guide = computed(() => failureGuide(this.failure()?.category, this.failure()?.message));

  private hideTimer: ReturnType<typeof setTimeout> | null = null;
  private pinned = false;

  constructor(private host: ElementRef<HTMLElement>) {}

  relative = relativeTime;
  absolute = absoluteTime;
  meta = failureMeta;

  show(): void {
    this.cancelHide();
    if (this.open()) return;
    this.open.set(true);
  }

  /** Tap or click pins it open (touch has no hover); a second tap closes it. Never triggers the row/card link. */
  toggle(ev: Event): void {
    ev.preventDefault();
    ev.stopPropagation();
    if (this.open() && this.pinned) { this.close(); return; }
    this.pinned = true;
    this.show();
  }

  scheduleHide(): void {
    if (this.pinned) return;
    this.cancelHide();
    this.hideTimer = setTimeout(() => this.close(), 150);
  }

  cancelHide(): void {
    if (this.hideTimer) { clearTimeout(this.hideTimer); this.hideTimer = null; }
  }

  close(): void {
    this.cancelHide();
    this.pinned = false;
    this.open.set(false);
  }

  @HostListener('document:keydown.escape')
  onEscape(): void { if (this.open()) { this.close(); this.trigger?.nativeElement.focus(); } }

  @HostListener('document:click', ['$event'])
  onDocumentClick(ev: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(ev.target as Node)) this.close();
  }

  @HostListener('window:resize')
  onViewportChange(): void { if (this.open()) this.close(); }

  ngOnDestroy(): void { this.close(); }
}
