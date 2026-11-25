import { Component, EventEmitter, Input, Output, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { FiscalFailure } from '../../core/models/xero.models';
import { absoluteTime, failureGuide, relativeTime } from '../../core/utils/fiscal-failure';

/**
 * Why a document failed to fiscalise, on the document page: the full error, each individual problem, when and how
 * it was attempted, what to do about it, and a button to try again.
 */
@Component({
  selector: 'app-fiscal-failure-panel',
  imports: [CommonModule, RouterLink],
  template: `
    @if (failure(); as f) {
      <section role="alert" aria-live="polite"
               class="overflow-hidden rounded-2xl border border-rose-200/80 bg-white shadow-sm fade-in dark:border-rose-500/30 dark:bg-slate-900">
        <div class="flex flex-col gap-3 border-b border-rose-100 bg-gradient-to-r from-rose-50 to-white px-5 py-4 sm:flex-row sm:items-start sm:justify-between dark:border-rose-500/20 dark:from-rose-500/10 dark:to-slate-900">
          <div class="flex min-w-0 items-start gap-3">
            <span class="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-xl bg-rose-500 text-white shadow-md shadow-rose-500/25">
              <svg class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><path d="M12 9v4M12 17h.01"/></svg>
            </span>
            <div class="min-w-0">
              <div class="flex flex-wrap items-center gap-2">
                <h2 class="text-sm font-semibold text-rose-800 dark:text-rose-200">This {{ documentLabel }} failed to fiscalize</h2>
                <span class="rounded-full bg-rose-100 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-rose-700 dark:bg-rose-500/20 dark:text-rose-300">{{ guide().label }}</span>
              </div>
              <p class="mt-1 text-xs text-slate-500 dark:text-slate-400">
                Last attempt <span class="font-medium text-slate-700 dark:text-slate-300" [title]="absolute(f.lastFailedAt)">{{ relative(f.lastFailedAt) }}</span>
                · {{ f.source === 'Manual' ? 'manual' : 'automatic' }}
                @if (f.attempts > 1) { · {{ f.attempts }} attempts since {{ relative(f.firstFailedAt) }} }
              </p>
            </div>
          </div>
          @if (canRetry) {
            <button type="button" (click)="retry.emit()" [disabled]="busy"
                    class="inline-flex flex-shrink-0 items-center justify-center gap-1.5 rounded-xl bg-rose-600 px-3.5 py-2 text-xs font-semibold text-white shadow-sm transition hover:bg-rose-700 active:scale-[0.98] disabled:cursor-not-allowed disabled:opacity-60 focus:outline-none focus-visible:ring-2 focus-visible:ring-rose-500 focus-visible:ring-offset-2 dark:focus-visible:ring-offset-slate-900">
              <svg class="h-3.5 w-3.5" [class.animate-spin]="busy" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M21 12a9 9 0 1 1-3-6.7L21 8"/><path d="M21 3v5h-5"/></svg>
              {{ busy ? 'Trying…' : 'Fiscalize again' }}
            </button>
          }
        </div>

        <div class="space-y-4 px-5 py-4">
          <div>
            <p class="mb-1 text-[11px] font-semibold uppercase tracking-wider text-slate-400 dark:text-slate-500">Error</p>
            <p class="whitespace-pre-line break-words text-sm leading-relaxed text-slate-700 dark:text-slate-200">{{ f.message }}</p>
          </div>

          @if (f.details.length) {
            <div>
              <p class="mb-1.5 text-[11px] font-semibold uppercase tracking-wider text-slate-400 dark:text-slate-500">What needs fixing</p>
              <ul class="grid gap-1.5 sm:grid-cols-2">
                @for (d of f.details; track $index) {
                  <li class="flex items-start gap-2 rounded-lg bg-rose-50/70 px-3 py-2 text-xs text-rose-800 ring-1 ring-inset ring-rose-100 dark:bg-rose-500/10 dark:text-rose-200 dark:ring-rose-500/20">
                    <svg class="mt-0.5 h-3.5 w-3.5 flex-shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M18 6 6 18M6 6l12 12"/></svg>
                    <span class="break-words">{{ d }}</span>
                  </li>
                }
              </ul>
            </div>
          }

          <div class="flex flex-col gap-3 rounded-xl border border-slate-200/80 bg-slate-50/70 px-4 py-3 sm:flex-row sm:items-center sm:justify-between dark:border-white/10 dark:bg-white/[0.03]">
            <div class="flex items-start gap-2.5">
              <svg class="mt-0.5 h-4 w-4 flex-shrink-0 text-primary-500 dark:text-brand-400" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4M12 8h.01"/></svg>
              <p class="text-xs leading-relaxed text-slate-600 dark:text-slate-300">{{ guide().hint }}</p>
            </div>
            <div class="flex flex-shrink-0 items-center gap-2">
              @if (guide().action; as action) {
                <a [routerLink]="action.link" [queryParams]="action.queryParams ?? null"
                   class="inline-flex items-center gap-1 rounded-lg bg-white px-3 py-1.5 text-xs font-semibold text-primary-600 shadow-sm ring-1 ring-slate-200 transition hover:bg-primary-50 dark:bg-slate-800 dark:text-brand-400 dark:ring-white/10 dark:hover:bg-white/10">
                  {{ action.label }}
                  <svg class="h-3 w-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M9 18l6-6-6-6"/></svg>
                </a>
              }
              <button type="button" (click)="copy(f)"
                      class="inline-flex items-center gap-1 rounded-lg px-2.5 py-1.5 text-xs font-medium text-slate-500 transition hover:bg-white hover:text-slate-700 dark:text-slate-400 dark:hover:bg-white/10 dark:hover:text-slate-200"
                      [attr.aria-label]="copied() ? 'Copied' : 'Copy error details'">
                <svg class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/></svg>
                {{ copied() ? 'Copied' : 'Copy' }}
              </button>
            </div>
          </div>
        </div>
      </section>
    }
  `
})
export class FiscalFailurePanel {
  @Input() set failureInfo(v: FiscalFailure | null | undefined) { this.failure.set(v ?? null); }
  /** "invoice" or "credit note". */
  @Input() documentLabel = 'document';
  @Input() canRetry = true;
  /** The document's Xero contact, so a buyer problem links straight to that customer. */
  @Input() set customerId(v: string | null | undefined) { this.customer.set(v ?? null); }
  @Input() busy = false;
  @Output() retry = new EventEmitter<void>();

  private router = inject(Router);
  readonly failure = signal<FiscalFailure | null>(null);
  private readonly customer = signal<string | null>(null);
  readonly guide = computed(() => failureGuide(this.failure()?.category, this.failure()?.message,
    { id: this.customer(), returnTo: this.router.url.split('?')[0] }));
  readonly copied = signal(false);

  relative = relativeTime;
  absolute = absoluteTime;

  /** Plain-text summary for support tickets. */
  copy(f: FiscalFailure): void {
    const text = [
      `Fiscalization failed (${this.guide().label})`,
      `Document: ${this.documentLabel} ${f.documentId}`,
      `Last attempt: ${absoluteTime(f.lastFailedAt)} (${f.source}, ${f.attempts} attempt${f.attempts === 1 ? '' : 's'})`,
      '',
      f.message,
      ...f.details.map((d) => `- ${d}`)
    ].join('\n');
    navigator.clipboard?.writeText(text).then(() => {
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 1800);
    }).catch(() => { /* clipboard blocked: nothing to do */ });
  }
}
