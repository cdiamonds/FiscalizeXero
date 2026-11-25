import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-dialog',
  imports: [CommonModule],
  template: `
    <div *ngIf="open" class="fixed inset-0 z-[80] flex items-center justify-center px-4">
      <div class="absolute inset-0 bg-slate-900/50 backdrop-blur-sm fade-in" (click)="onBackdrop()"></div>
      <div class="relative w-full" [ngClass]="widthClass()" role="dialog" aria-modal="true">
        <div class="scale-in flex max-h-[calc(100vh-2rem)] flex-col overflow-hidden rounded-2xl border border-white/40 bg-white/95 shadow-2xl shadow-slate-900/20 backdrop-blur-md dark:border-white/10 dark:bg-slate-900/95 dark:shadow-black/60">

          <!-- ─── Plain header (default — used by ConfirmDialog etc.) ─── -->
          <div *ngIf="title && variant === 'plain'" class="flex flex-shrink-0 items-start justify-between border-b border-slate-200/70 px-5 py-4 dark:border-white/5">
            <div>
              <h2 class="text-base font-semibold text-slate-900 dark:text-white">{{ title }}</h2>
              <p *ngIf="subtitle" class="mt-0.5 text-xs text-slate-500 dark:text-slate-400">{{ subtitle }}</p>
            </div>
            <button type="button" (click)="close.emit()" class="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 dark:hover:bg-white/5 dark:hover:text-slate-200" aria-label="Close">
              <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M18 6L6 18M6 6l12 12"/></svg>
            </button>
          </div>

          <!-- ─── Gradient (branded) header — matches FiscalyzeAngular dialog style ─── -->
          <div *ngIf="title && variant === 'gradient'"
               class="flex-shrink-0 bg-gradient-to-r from-primary-500 to-primary-600 px-6 py-4 shadow-md shadow-primary-500/20">
            <div class="flex items-center justify-between gap-3">
              <div class="flex min-w-0 items-center gap-3">
                <div class="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-xl bg-white/20 backdrop-blur-sm">
                  <svg *ngIf="iconPath" class="h-5 w-5 text-white" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <path [attr.d]="iconPath"></path>
                  </svg>
                  <svg *ngIf="!iconPath" class="h-5 w-5 text-white" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg>
                </div>
                <div class="min-w-0">
                  <h2 class="truncate text-lg font-bold text-white">{{ title }}</h2>
                  <p *ngIf="subtitle" class="text-sm text-white/85">{{ subtitle }}</p>
                </div>
              </div>
              <button type="button" (click)="close.emit()"
                      class="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-xl bg-white/20 text-white transition-all duration-200 hover:bg-white/30 hover:scale-[1.05]"
                      aria-label="Close">
                <svg class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M18 6L6 18M6 6l12 12"/></svg>
              </button>
            </div>
          </div>

          <div class="flex-1 overflow-y-auto px-5 py-4">
            <ng-content></ng-content>
          </div>
          <div *ngIf="hasFooter" class="flex flex-shrink-0 flex-wrap items-center justify-end gap-2 border-t border-slate-200/70 px-5 py-3 dark:border-white/5">
            <ng-content select="[dialogFooter]"></ng-content>
          </div>
        </div>
      </div>
    </div>
  `
})
export class Dialog {
  @Input() open = false;
  @Input() title: string | null = null;
  @Input() subtitle: string | null = null;
  @Input() size: 'sm' | 'md' | 'lg' | 'xl' = 'md';
  @Input() closeOnBackdrop = true;
  @Input() hasFooter = false;
  /**
   * Header style. `plain` keeps the existing border-bottom title bar (used by ConfirmDialog
   * which already paints its own coloured icon in the body). `gradient` switches to the
   * teal/emerald branded header matching FiscalyzeAngular's dialogs.
   */
  @Input() variant: 'plain' | 'gradient' = 'plain';
  /** Optional SVG path drawn inside the gradient header's white circle. */
  @Input() iconPath: string | null = null;
  @Output() close = new EventEmitter<void>();

  widthClass(): string {
    switch (this.size) {
      case 'sm': return 'max-w-md';
      case 'lg': return 'max-w-3xl';
      case 'xl': return 'max-w-5xl';
      default:   return 'max-w-xl';
    }
  }

  onBackdrop(): void {
    if (this.closeOnBackdrop) this.close.emit();
  }
}
