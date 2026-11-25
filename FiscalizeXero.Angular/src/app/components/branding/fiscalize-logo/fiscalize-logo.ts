import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-fiscalize-logo',
  imports: [CommonModule],
  template: `
    <div class="flex items-center gap-2.5" [class.justify-center]="iconOnly">
      <div class="flex h-9 w-9 items-center justify-center rounded-xl bg-gradient-to-br from-primary-500 to-primary-600 shadow-md shadow-primary-500/25">
        <span class="text-lg font-bold leading-none text-white" aria-hidden="true">F</span>
      </div>
      <ng-container *ngIf="!iconOnly">
        <div class="flex flex-col leading-tight">
          <span class="text-[15px] font-bold tracking-tight text-slate-900 dark:text-white">Fiscalize</span>
          <span *ngIf="subLabel" class="text-[10px] font-semibold uppercase tracking-wider text-primary-500 dark:text-brand-400">{{ subLabel }}</span>
        </div>
      </ng-container>
    </div>
  `
})
export class FiscalizeLogo {
  @Input() iconOnly = false;
  @Input() subLabel: string | null = 'for Xero';
}
