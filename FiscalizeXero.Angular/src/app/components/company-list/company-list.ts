import { Component, computed, EventEmitter, Input, Output, input, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { UserCompany } from '../../core/models/xero.models';
import { SearchBar } from '../search-bar/search-bar';

/**
 * Searchable list of the user's companies with their Xero connection status and per-company
 * actions. Presentational only: the parent owns switching/connecting/disconnecting and says which
 * company is busy via `busyId`.
 */
@Component({
  selector: 'app-company-list',
  imports: [CommonModule, SearchBar],
  template: `
    <div class="space-y-3">
      <app-search-bar *ngIf="companies().length > 4 || query()"
                      [debounceMs]="120"
                      placeholder="Search companies or Xero organisations…"
                      (valueChange)="query.set($event)" />

      <ul class="divide-y divide-slate-100 overflow-hidden rounded-xl border border-slate-200 bg-white/60 dark:divide-white/10 dark:border-white/10 dark:bg-slate-950/30"
          role="list" aria-label="Your companies">
        <li *ngFor="let c of filtered(); trackBy: trackById"
            class="flex flex-col gap-3 px-4 py-3.5 transition-colors hover:bg-slate-50/70 sm:flex-row sm:items-center dark:hover:bg-white/5"
            [class.bg-primary-500/5]="c.isActive">
          <div class="flex min-w-0 flex-1 items-center gap-3">
            <span class="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-primary-500 to-primary-600 text-[11px] font-bold text-white shadow-sm"
                  aria-hidden="true">{{ initials(c.companyName) }}</span>
            <div class="min-w-0">
              <div class="flex flex-wrap items-center gap-2">
                <span class="truncate text-sm font-semibold text-slate-900 dark:text-white">{{ c.companyName }}</span>
                <span *ngIf="c.isActive" class="rounded-full bg-primary-500/15 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-primary-700 dark:text-brand-300">Active</span>
              </div>
              <div class="mt-0.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs">
                <span class="inline-flex items-center gap-1.5" [ngClass]="status(c).text">
                  <span class="h-1.5 w-1.5 rounded-full" [ngClass]="status(c).dot"></span>{{ status(c).label }}
                </span>
                <span *ngIf="c.xeroTenantName" class="text-slate-500 dark:text-slate-400">Xero: {{ c.xeroTenantName }}</span>
                <a *ngIf="c.xeroUrl && c.isSetupComplete" [href]="c.xeroUrl" target="_blank" rel="noopener"
                   class="font-medium text-primary-600 hover:underline dark:text-brand-400">Open Xero</a>
              </div>
              <p *ngIf="c.needsReauth && c.connectionProblem" class="mt-1 text-xs text-amber-700 dark:text-amber-300">{{ c.connectionProblem }}</p>
            </div>
          </div>

          <div class="flex flex-shrink-0 flex-wrap items-center gap-2 sm:justify-end">
            <button *ngIf="!c.isActive" type="button" (click)="switch.emit(c)" [disabled]="busyId !== null"
                    class="rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-xs font-semibold text-slate-700 transition hover:bg-slate-50 active:scale-95 disabled:cursor-not-allowed disabled:opacity-50 dark:border-white/10 dark:bg-slate-900 dark:text-slate-200 dark:hover:bg-white/5">
              {{ busyId === c.companyId && busyAction === 'switch' ? 'Switching…' : 'Switch to' }}
            </button>
            <button *ngIf="!c.isSetupComplete" type="button" (click)="connect.emit(c)" [disabled]="busyId !== null"
                    class="rounded-lg bg-primary-500 px-3 py-1.5 text-xs font-semibold text-white shadow-sm transition hover:bg-primary-600 active:scale-95 disabled:cursor-not-allowed disabled:opacity-50">
              Connect Xero
            </button>
            <button *ngIf="c.isSetupComplete && c.needsReauth" type="button" (click)="connect.emit(c)" [disabled]="busyId !== null"
                    class="rounded-lg bg-amber-500 px-3 py-1.5 text-xs font-semibold text-white shadow-sm transition hover:bg-amber-600 active:scale-95 disabled:cursor-not-allowed disabled:opacity-50">
              Re-authorize
            </button>
            <button *ngIf="c.isSetupComplete" type="button" (click)="disconnect.emit(c)" [disabled]="busyId !== null"
                    class="rounded-lg border border-rose-200 px-3 py-1.5 text-xs font-semibold text-rose-700 transition hover:bg-rose-50 active:scale-95 disabled:cursor-not-allowed disabled:opacity-50 dark:border-rose-500/30 dark:text-rose-300 dark:hover:bg-rose-500/10">
              {{ busyId === c.companyId && busyAction === 'disconnect' ? 'Disconnecting…' : 'Disconnect' }}
            </button>
          </div>
        </li>

        <li *ngIf="companies().length === 0" class="px-4 py-8 text-center text-sm text-slate-500 dark:text-slate-400">
          No companies on your account yet.
        </li>
        <li *ngIf="companies().length > 0 && filtered().length === 0" class="px-4 py-8 text-center text-sm text-slate-500 dark:text-slate-400">
          No companies match “{{ query() }}”.
        </li>
      </ul>

      <p *ngIf="companies().length > 4" class="text-right text-[11px] text-slate-400" aria-live="polite">
        Showing {{ filtered().length }} of {{ companies().length }}
      </p>
    </div>
  `
})
export class CompanyList {
  /** Signal input so `filtered` recomputes whenever the parent hands over a new list. */
  readonly companies = input<UserCompany[]>([]);
  /** Company currently being acted on (disables all actions while set). */
  @Input() busyId: number | null = null;
  @Input() busyAction: 'switch' | 'disconnect' | 'connect' | null = null;

  @Output() switch = new EventEmitter<UserCompany>();
  @Output() connect = new EventEmitter<UserCompany>();
  @Output() disconnect = new EventEmitter<UserCompany>();

  readonly query = signal('');

  readonly filtered = computed(() => {
    const q = this.query().trim().toLowerCase();
    const list = [...this.companies()].sort((a, b) =>
      Number(b.isActive) - Number(a.isActive) || a.companyName.localeCompare(b.companyName));
    if (!q) return list;
    return list.filter((c) =>
      c.companyName.toLowerCase().includes(q) || (c.xeroTenantName ?? '').toLowerCase().includes(q));
  });

  trackById = (_: number, c: UserCompany) => c.id;

  initials(name: string): string {
    return (name || '?').split(/\s+/).map((p) => p[0]).filter(Boolean).slice(0, 2).join('').toUpperCase();
  }

  status(c: UserCompany): { label: string; text: string; dot: string } {
    if (!c.isSetupComplete)
      return { label: 'Not connected', text: 'text-slate-500 dark:text-slate-400', dot: 'bg-slate-400' };
    if (c.needsReauth)
      return { label: c.connectionProblem?.includes('expired') ? 'Connection expired' : 'Needs re-authorization', text: 'text-amber-600 dark:text-amber-400', dot: 'bg-amber-500' };
    return {
      label: c.isUsingSharedConnection ? 'Connected by a colleague' : 'Connected',
      text: 'text-emerald-600 dark:text-emerald-400',
      dot: 'bg-emerald-500'
    };
  }
}
