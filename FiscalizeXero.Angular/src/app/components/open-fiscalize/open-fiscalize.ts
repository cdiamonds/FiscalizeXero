import { Component, Input, computed, inject, signal } from '@angular/core';
import { AuthService } from '../../core/services/auth/auth.service';
import { environment } from '../../../environments/environment';

/**
 * "Back to the Fiscalize platform", on every page, so nobody has to type the address. Signed in with a company, the
 * platform is opened already signed in (a one-time launch link). Without a session, or if that link can't be made,
 * it goes to the platform's own sign-in page instead of leaving the user stuck.
 */
@Component({
  selector: 'app-open-fiscalize',
  template: `
    <span class="relative inline-flex">
      @if (canLaunch()) {
        <button type="button" (click)="open()" [disabled]="opening()" [class]="buttonClass()"
                title="Back to the Fiscalize platform" aria-label="Back to the Fiscalize platform" data-testid="open-fiscalize">
          @if (opening()) {
            <svg class="h-4 w-4 animate-spin text-primary-500" viewBox="0 0 24 24" fill="none" aria-hidden="true"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
          } @else {
            @if (variant === 'bar') {
            <span class="flex h-4 w-4 flex-shrink-0 items-center justify-center rounded bg-gradient-to-br from-primary-500 to-primary-600 text-[9px] font-bold leading-none text-white" aria-hidden="true">F</span>
          } @else {
            <svg class="h-4 w-4 text-primary-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="15 3 21 3 21 9"/><path d="M10 14L21 3"/><path d="M21 14v5a2 2 0 01-2 2H5a2 2 0 01-2-2V5a2 2 0 012-2h5"/></svg>
          }
          }
          <span [class]="labelClass()">{{ opening() ? 'Opening…' : 'Fiscalize' }}</span>
        </button>
      } @else {
        <a [href]="platformUrl" [class]="buttonClass()" title="Go to the Fiscalize platform" aria-label="Go to the Fiscalize platform" data-testid="open-fiscalize">
          @if (variant === 'bar') {
            <span class="flex h-4 w-4 flex-shrink-0 items-center justify-center rounded bg-gradient-to-br from-primary-500 to-primary-600 text-[9px] font-bold leading-none text-white" aria-hidden="true">F</span>
          } @else {
            <svg class="h-4 w-4 text-primary-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="15 3 21 3 21 9"/><path d="M10 14L21 3"/><path d="M21 14v5a2 2 0 01-2 2H5a2 2 0 01-2-2V5a2 2 0 012-2h5"/></svg>
          }
          <span [class]="labelClass()">Fiscalize</span>
        </a>
      }

      @if (error()) {
        <div class="absolute right-0 top-full z-50 mt-2 w-64 rounded-xl border border-rose-200 bg-rose-50 px-3 py-2 text-xs text-rose-700 shadow-lg fade-in-down dark:border-rose-500/30 dark:bg-rose-500/10 dark:text-rose-300"
             role="alert">
          <p>{{ error() }}</p>
          <a [href]="platformUrl" class="mt-1.5 inline-block font-semibold underline underline-offset-2">Open the Fiscalize sign-in page</a>
        </div>
      }
    </span>
  `,
})
export class OpenFiscalize {
  private auth = inject(AuthService);

  /** `bar`: the compact top-bar chip (label hidden on phones). `page`: the outlined button on standalone pages. */
  @Input() variant: 'bar' | 'page' = 'bar';

  readonly platformUrl = environment.fiscalizeWebUrl;
  readonly opening = signal(false);
  readonly error = signal<string | null>(null);

  /** A launch link needs a session with a company; otherwise the platform's sign-in page is the way back. */
  readonly canLaunch = computed(() => this.auth.isAuthenticated() && !!this.auth.currentUser()?.companyId);

  buttonClass(): string {
    return this.variant === 'page'
      ? 'inline-flex items-center gap-1.5 rounded-xl border border-slate-200 bg-white/80 px-3 py-2 text-xs font-medium text-slate-700 transition-all duration-200 hover:bg-white hover:scale-[1.03] disabled:cursor-not-allowed disabled:opacity-60 dark:border-white/10 dark:bg-slate-900/60 dark:text-slate-200 dark:hover:bg-slate-900'
      : 'flex items-center gap-1.5 rounded-xl border border-slate-200/70 bg-white/70 px-2.5 py-1.5 text-xs font-semibold text-slate-700 transition-all duration-200 hover:bg-white hover:scale-[1.03] disabled:cursor-not-allowed disabled:opacity-60 dark:border-white/5 dark:bg-white/5 dark:text-slate-200 dark:hover:bg-white/10';
  }

  labelClass(): string {
    return this.variant === 'page' ? '' : 'hidden sm:inline';
  }

  open(): void {
    if (this.opening()) return;
    this.opening.set(true);
    this.error.set(null);
    this.auth.openFiscalize().subscribe({
      next: (result) => {
        this.opening.set(false);
        if (result?.launchUrl) window.location.href = result.launchUrl;
        else this.showError('Fiscalize could not be opened signed in.');
      },
      error: (err) => {
        this.opening.set(false);
        this.showError(err?.error?.message || 'Fiscalize could not be opened signed in.');
      }
    });
  }

  private showError(message: string): void {
    this.error.set(message);
    setTimeout(() => this.error.set(null), 8000);
  }
}
