import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Dialog } from '../dialog/dialog';

@Component({
  selector: 'app-confirm-dialog',
  imports: [CommonModule, Dialog],
  template: `
    <app-dialog [open]="open" [title]="title" [subtitle]="message" [size]="'sm'" [hasFooter]="true" (close)="cancel.emit()">
      <div class="flex items-start gap-3">
        <div [class]="iconBgClass()" class="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-2xl shadow-md">
          <svg class="h-5 w-5 text-white" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
        </div>
        <p class="text-sm text-slate-700 dark:text-slate-200">{{ description }}</p>
      </div>

      <ng-container dialogFooter>
        <button type="button" (click)="cancel.emit()"
                class="inline-flex items-center rounded-xl border border-slate-200 bg-white/70 px-4 py-2 text-sm font-medium text-slate-700 transition-all duration-200 hover:bg-white hover:scale-[1.02] active:scale-[0.98] dark:border-white/10 dark:bg-white/5 dark:text-slate-200 dark:hover:bg-white/10">
          {{ cancelLabel }}
        </button>
        <button type="button" (click)="confirm.emit()" [disabled]="busy"
                [class]="confirmClass()"
                class="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-sm font-semibold text-white shadow-lg transition-all duration-200 hover:scale-[1.02] active:scale-[0.98] disabled:opacity-50 disabled:hover:scale-100">
          <svg *ngIf="busy" class="h-3.5 w-3.5 animate-spin" viewBox="0 0 24 24" fill="none"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
          {{ busy ? busyLabel : confirmLabel }}
        </button>
      </ng-container>
    </app-dialog>
  `
})
export class ConfirmDialog {
  @Input() open = false;
  @Input() title = 'Are you sure?';
  @Input() message: string | null = null;
  @Input() description = '';
  @Input() confirmLabel = 'Confirm';
  @Input() busyLabel = 'Working…';
  @Input() cancelLabel = 'Cancel';
  @Input() busy = false;
  @Input() variant: 'danger' | 'primary' | 'warning' = 'primary';
  @Output() confirm = new EventEmitter<void>();
  @Output() cancel = new EventEmitter<void>();

  iconBgClass(): string {
    switch (this.variant) {
      case 'danger':  return 'bg-gradient-to-br from-rose-500 to-orange-500 shadow-rose-500/30';
      case 'warning': return 'bg-gradient-to-br from-amber-500 to-orange-500 shadow-amber-500/30';
      default:        return 'bg-gradient-to-br from-primary-500 to-primary-600 shadow-primary-500/30';
    }
  }

  confirmClass(): string {
    switch (this.variant) {
      case 'danger':  return 'bg-gradient-to-r from-rose-500 to-orange-500 shadow-rose-500/25';
      case 'warning': return 'bg-gradient-to-r from-amber-500 to-orange-500 shadow-amber-500/25';
      default:        return 'bg-gradient-to-r from-primary-500 to-primary-600 shadow-primary-500/25';
    }
  }
}
