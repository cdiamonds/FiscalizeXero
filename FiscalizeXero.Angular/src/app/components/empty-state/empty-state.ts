import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-empty-state',
  imports: [CommonModule],
  template: `
    <div class="empty-state-fade-in flex flex-col items-center justify-center px-6 py-16 text-center">
      <div class="flex h-14 w-14 items-center justify-center rounded-2xl bg-gradient-to-br from-slate-100 to-slate-200 text-slate-400 dark:from-white/5 dark:to-white/10 dark:text-slate-500">
        <svg class="h-6 w-6" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path [attr.d]="icon"></path>
        </svg>
      </div>
      <h3 class="mt-4 text-sm font-semibold text-slate-900 dark:text-white">{{ title }}</h3>
      <p *ngIf="description" class="mt-1 max-w-sm text-xs text-slate-500 dark:text-slate-400">{{ description }}</p>
    </div>
  `
})
export class EmptyState {
  @Input() title = 'Nothing here yet';
  @Input() description: string | null = null;
  @Input() icon = 'M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2';
}
