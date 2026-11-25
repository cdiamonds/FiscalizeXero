import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-page-header',
  imports: [CommonModule],
  template: `
    <div class="mb-4 rounded-2xl border border-white/40 bg-white/80 px-4 py-4 shadow-sm backdrop-blur-md fade-in-down dark:border-white/10 dark:bg-slate-900/70 sm:px-6 sm:py-5">
      <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div class="min-w-0">
          <h1 class="text-2xl font-bold tracking-tight text-slate-900 dark:text-white">{{ title }}</h1>
          <p *ngIf="subtitle" class="mt-1 max-w-2xl text-sm text-slate-600 dark:text-slate-400">{{ subtitle }}</p>
        </div>
        <div class="flex flex-wrap items-center gap-2">
          <ng-content></ng-content>
        </div>
      </div>
    </div>
  `
})
export class PageHeader {
  @Input() title = '';
  @Input() subtitle: string | null = null;
}
