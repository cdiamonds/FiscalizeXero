import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FiscalizeLogo } from '../../components/branding/fiscalize-logo/fiscalize-logo';
import { OpenFiscalize } from '../../components/open-fiscalize/open-fiscalize';

@Component({
  selector: 'app-not-found',
  imports: [CommonModule, RouterLink, FiscalizeLogo, OpenFiscalize],
  template: `
    <div class="flex min-h-screen items-center justify-center bg-gradient-to-br from-slate-50 via-white to-brand-50/40 px-4 dark:from-slate-950 dark:via-slate-950 dark:to-black">
      <div class="w-full max-w-md rounded-2xl border border-slate-200/70 bg-white/90 p-8 text-center shadow-xl backdrop-blur-md dark:border-white/5 dark:bg-slate-900/80">
        <div class="mb-6 flex justify-center">
          <app-fiscalize-logo></app-fiscalize-logo>
        </div>
        <h1 class="text-3xl font-bold text-slate-900 dark:text-white">404</h1>
        <p class="mt-2 text-sm text-slate-600 dark:text-slate-300">That page doesn't exist.</p>
        <a routerLink="/dashboard" class="mt-6 inline-block rounded-xl bg-gradient-to-r from-primary-500 to-primary-600 px-5 py-2 text-sm font-semibold text-white shadow-md shadow-primary-500/25 hover:scale-[1.02] transition-transform">
          Back to dashboard
        </a>
        <div class="mt-3 flex justify-center">
          <app-open-fiscalize variant="page"></app-open-fiscalize>
        </div>
      </div>
    </div>
  `
})
export class NotFound {}
