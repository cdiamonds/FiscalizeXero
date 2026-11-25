import { Component, Input, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';

export type DocumentStatus = 'paid' | 'overdue' | 'unpaid' | 'partial' | 'sent' | 'draft' | 'open' | 'void' | 'closed' | 'unknown';

@Component({
  selector: 'app-status-pill',
  imports: [CommonModule],
  template: `
    <span [ngClass]="cssClass()" class="inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-[11px] font-semibold uppercase tracking-wider">
      <span class="h-1.5 w-1.5 rounded-full" [ngClass]="dotClass()"></span>
      {{ resolved().label }}
    </span>
  `
})
export class StatusPill {
  @Input() set status(s: string | null | undefined) { this.raw.set(s ?? ''); }
  @Input() set dueDate(v: string | null | undefined) { this.due.set(v ?? null); }

  private raw = signal('');
  private due = signal<string | null>(null);

  resolved = computed(() => this.classify(this.raw().toLowerCase().trim(), this.due()));

  private classify(s: string, due: string | null): { kind: DocumentStatus; label: string; days?: number } {
    if (s === 'paid' || s === 'closed') return { kind: 'paid', label: 'Paid' };
    if (s === 'void')                   return { kind: 'void', label: 'Void' };
    if (s === 'partially_paid' || s === 'partial') return { kind: 'partial', label: 'Partial' };
    if (s === 'draft')                  return { kind: 'draft', label: 'Draft' };
    if (s === 'sent' || s === 'viewed') return { kind: 'sent', label: 'Sent' };
    if (s === 'open' || s === 'unpaid') {
      if (due) {
        const today = new Date(); today.setHours(0,0,0,0);
        const d = new Date(due); d.setHours(0,0,0,0);
        const days = Math.floor((today.getTime() - d.getTime()) / 86400000);
        if (days > 0) return { kind: 'overdue', label: `Overdue ${days}d`, days };
      }
      return { kind: 'unpaid', label: 'Unpaid' };
    }
    return { kind: 'unknown', label: s ? s : '—' };
  }

  cssClass = computed(() => {
    switch (this.resolved().kind) {
      case 'paid':    return 'bg-emerald-100 text-emerald-700 dark:bg-primary-600/15 dark:text-emerald-300';
      case 'overdue': return 'bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300';
      case 'unpaid':  return 'bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300';
      case 'partial': return 'bg-sky-100 text-sky-700 dark:bg-sky-500/15 dark:text-sky-300';
      case 'sent':    return 'bg-indigo-100 text-indigo-700 dark:bg-indigo-500/15 dark:text-indigo-300';
      case 'void':    return 'bg-slate-100 text-slate-500 dark:bg-white/10 dark:text-slate-400';
      default:        return 'bg-slate-100 text-slate-600 dark:bg-white/10 dark:text-slate-300';
    }
  });

  dotClass = computed(() => {
    switch (this.resolved().kind) {
      case 'paid':    return 'bg-primary-600';
      case 'overdue': return 'bg-rose-500';
      case 'unpaid':  return 'bg-amber-500';
      case 'partial': return 'bg-sky-500';
      case 'sent':    return 'bg-indigo-500';
      case 'void':    return 'bg-slate-400';
      default:        return 'bg-slate-400';
    }
  });
}
