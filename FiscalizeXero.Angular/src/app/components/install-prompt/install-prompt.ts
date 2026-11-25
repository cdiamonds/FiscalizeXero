import { Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PwaService } from '../../core/services/pwa/pwa.service';

@Component({
  selector: 'app-install-prompt',
  imports: [CommonModule],
  template: `
    <!-- Native install banner (Chromium / Edge / Android) -->
    <div *ngIf="showNativeBanner()"
         class="fixed bottom-4 left-4 right-4 z-[60] mx-auto max-w-md rounded-2xl border border-white/40 bg-white/95 p-4 shadow-xl backdrop-blur-md fade-in-down dark:border-white/10 dark:bg-slate-900/95 sm:bottom-6">
      <div class="flex items-start gap-3">
        <div class="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-xl bg-gradient-to-br from-primary-500 to-primary-600 shadow-md shadow-primary-500/25">
          <svg class="h-5 w-5 text-white" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M12 3v12m0 0l-4-4m4 4l4-4M5 21h14"/>
          </svg>
        </div>
        <div class="min-w-0 flex-1">
          <p class="text-sm font-semibold text-slate-900 dark:text-white">Install Fiscalize</p>
          <p class="mt-0.5 text-xs text-slate-600 dark:text-slate-400">
            Add it to your device for one-tap launches and offline access.
          </p>
          <div class="mt-3 flex items-center gap-2">
            <button type="button" (click)="install()"
                    class="inline-flex items-center gap-1.5 rounded-xl bg-gradient-to-r from-primary-500 to-primary-600 px-3 py-1.5 text-xs font-semibold text-white shadow-md shadow-primary-500/20 transition-all duration-200 hover:from-primary-600 hover:to-primary-700 hover:scale-[1.04] active:scale-[0.96]">
              Install
            </button>
            <button type="button" (click)="dismiss()"
                    class="rounded-xl px-3 py-1.5 text-xs font-medium text-slate-600 transition-colors hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-white/5">
              Not now
            </button>
          </div>
        </div>
        <button type="button" (click)="dismiss()" aria-label="Dismiss"
                class="-mr-1 -mt-1 rounded-md p-1 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 dark:hover:bg-white/5">
          <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M18 6L6 18M6 6l12 12"/></svg>
        </button>
      </div>
    </div>

    <!-- iOS Safari has no API — show manual instructions -->
    <div *ngIf="showIosBanner()"
         class="fixed bottom-4 left-4 right-4 z-[60] mx-auto max-w-md rounded-2xl border border-white/40 bg-white/95 p-4 shadow-xl backdrop-blur-md fade-in-down dark:border-white/10 dark:bg-slate-900/95 sm:bottom-6">
      <div class="flex items-start gap-3">
        <div class="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-xl bg-gradient-to-br from-primary-500 to-primary-600 shadow-md shadow-primary-500/25">
          <svg class="h-5 w-5 text-white" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M12 3v12m0 0l-4-4m4 4l4-4M5 21h14"/>
          </svg>
        </div>
        <div class="min-w-0 flex-1">
          <p class="text-sm font-semibold text-slate-900 dark:text-white">Install on iPhone or iPad</p>
          <p class="mt-0.5 text-xs text-slate-600 dark:text-slate-400">
            Tap the
            <svg class="mx-0.5 inline h-4 w-4 align-text-bottom text-brand-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 16V4m0 0L8 8m4-4l4 4M4 20h16"/></svg>
            Share button, then "Add to Home Screen".
          </p>
          <div class="mt-3 flex items-center gap-2">
            <button type="button" (click)="dismiss()"
                    class="rounded-xl px-3 py-1.5 text-xs font-medium text-slate-600 transition-colors hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-white/5">
              Got it
            </button>
          </div>
        </div>
        <button type="button" (click)="dismiss()" aria-label="Dismiss"
                class="-mr-1 -mt-1 rounded-md p-1 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 dark:hover:bg-white/5">
          <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M18 6L6 18M6 6l12 12"/></svg>
        </button>
      </div>
    </div>

    <!-- Update-available toast -->
    <div *ngIf="pwa.updateReady()"
         class="fixed bottom-4 left-4 right-4 z-[60] mx-auto max-w-sm rounded-2xl border border-white/40 bg-white/95 p-3 shadow-xl backdrop-blur-md fade-in-down dark:border-white/10 dark:bg-slate-900/95 sm:bottom-6">
      <div class="flex items-center gap-3">
        <div class="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-xl bg-brand-500/15 text-brand-600 dark:text-brand-400">
          <svg class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"/></svg>
        </div>
        <div class="flex-1">
          <p class="text-sm font-semibold text-slate-900 dark:text-white">Update available</p>
          <p class="text-xs text-slate-600 dark:text-slate-400">A new version of Fiscalize is ready.</p>
        </div>
        <button type="button" (click)="reload()"
                class="rounded-xl bg-gradient-to-r from-primary-500 to-primary-600 px-3 py-1.5 text-xs font-semibold text-white hover:from-primary-600 hover:to-primary-700">
          Reload
        </button>
      </div>
    </div>
  `
})
export class InstallPrompt {
  readonly pwa = inject(PwaService);

  // We give the iOS hint a one-time per-tab gate so it doesn't reappear after dismiss.
  private iosDismissed = signal(false);

  readonly showNativeBanner = computed(() => this.pwa.canInstall());
  readonly showIosBanner = computed(() => this.pwa.isIosSafari() && !this.iosDismissed() && !this.pwa.isStandalone());

  install(): void { void this.pwa.promptInstall(); }

  dismiss(): void {
    this.iosDismissed.set(true);
    this.pwa.dismissForNow();
  }

  reload(): void { this.pwa.applyUpdate(); }
}
