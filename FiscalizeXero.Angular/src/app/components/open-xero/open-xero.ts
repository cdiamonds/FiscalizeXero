import { Component, Input, inject } from '@angular/core';
import { CompaniesService } from '../../core/services/xero/companies.service';

/**
 * "Open Xero" for the active company, in a new tab so this app stays open. Goes to the connected organisation when
 * Xero has told us its short code, otherwise to Xero, which shows the organisation the user last used.
 * Shows nothing while the company has no Xero connection.
 */
@Component({
  selector: 'app-open-xero',
  template: `
    @if (url(); as href) {
      <a [href]="href" target="_blank" rel="noopener" [class]="buttonClass()"
         title="Open Xero in a new tab" aria-label="Open Xero in a new tab" data-testid="open-xero">
        @if (variant === 'bar') {
          <span class="flex h-4 w-4 flex-shrink-0 items-center justify-center rounded-full bg-[#13B5EA] text-[9px] font-bold leading-none text-white" aria-hidden="true">X</span>
        } @else {
          <svg class="h-4 w-4 flex-shrink-0 text-[#13B5EA]" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M18 13v6a2 2 0 01-2 2H5a2 2 0 01-2-2V8a2 2 0 012-2h6M15 3h6v6M10 14L21 3"/></svg>
        }
        <span [class]="variant === 'bar' ? 'hidden sm:inline' : ''">{{ variant === 'page' ? 'Open in Xero' : 'Xero' }}</span>
      </a>
    }
  `,
})
export class OpenXero {
  private companies = inject(CompaniesService);

  /** `bar`: the compact top-bar chip. `page`: a full-height page-header button. */
  @Input() variant: 'bar' | 'page' = 'bar';

  readonly url = this.companies.activeXeroUrl;

  buttonClass(): string {
    return this.variant === 'page'
      ? 'inline-flex h-10 items-center gap-2 rounded-xl border border-slate-200 bg-white px-3.5 text-sm font-semibold text-slate-700 shadow-sm transition hover:bg-slate-50 focus:outline-none focus:ring-2 focus:ring-primary-500 dark:border-white/10 dark:bg-slate-900 dark:text-slate-200 dark:hover:bg-white/5'
      : 'flex items-center gap-1.5 rounded-xl border border-slate-200/70 bg-white/70 px-2.5 py-1.5 text-xs font-semibold text-slate-700 transition-all duration-200 hover:bg-white hover:scale-[1.03] dark:border-white/5 dark:bg-white/5 dark:text-slate-200 dark:hover:bg-white/10';
  }
}
