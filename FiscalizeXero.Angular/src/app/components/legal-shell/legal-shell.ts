import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FiscalizeLogo } from '../branding/fiscalize-logo/fiscalize-logo';

/**
 * Page frame for the public legal documents (Terms, Privacy). Header with the logo and a way back,
 * a readable single column, and the links customers expect in the footer.
 */
@Component({
  selector: 'app-legal-shell',
  imports: [RouterLink, FiscalizeLogo],
  template: `
    <div class="relative min-h-screen overflow-hidden bg-gradient-to-br from-slate-50 via-white to-brand-50/40 dark:from-slate-950 dark:via-slate-950 dark:to-black">
      <div class="bg-blob teal"    style="top: -160px; right: -120px; width: 480px; height: 480px;"></div>
      <div class="bg-blob emerald" style="bottom: -180px; left: -100px; width: 460px; height: 460px;"></div>

      <div class="relative z-10 mx-auto w-full max-w-3xl px-4 py-8 sm:px-6 lg:py-12">
        <div class="mb-8 flex items-center justify-between fade-in-down">
          <a routerLink="/" class="inline-flex items-center gap-1.5 rounded-lg px-2 py-1.5 text-sm font-medium text-slate-500 transition-colors hover:bg-slate-100 hover:text-slate-700 dark:text-slate-400 dark:hover:bg-white/5 dark:hover:text-slate-200">
            <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M15 19l-7-7 7-7"/></svg>
            Back
          </a>
          <app-fiscalize-logo></app-fiscalize-logo>
          <div class="w-16"></div>
        </div>

        <article class="legal rounded-2xl border border-white/40 bg-white/80 p-6 shadow-sm backdrop-blur-md fade-in-down sm:p-10 dark:border-white/10 dark:bg-slate-900/70" style="animation-delay: 0.05s">
          <header class="mb-8 border-b border-slate-200/70 pb-6 dark:border-white/10">
            <p class="text-xs font-semibold uppercase tracking-wider text-primary-600 dark:text-brand-400">{{ kicker() }}</p>
            <h1 class="mt-2 text-3xl font-bold tracking-tight text-slate-900 dark:text-white">{{ title() }}</h1>
            <p class="mt-3 text-sm text-slate-500 dark:text-slate-400">
              Effective {{ effective() }} · Last updated {{ updated() }}
            </p>
          </header>
          <ng-content />
        </article>

        <footer class="mt-8 flex flex-wrap items-center justify-center gap-x-6 gap-y-2 text-xs text-slate-500 fade-in-down dark:text-slate-400" style="animation-delay: 0.10s">
          <a routerLink="/terms" class="hover:text-primary-600 dark:hover:text-brand-400">Terms of Service</a>
          <a routerLink="/privacy" class="hover:text-primary-600 dark:hover:text-brand-400">Privacy Policy</a>
          <a href="https://fiscalize.co.zw/terms-and-conditions" target="_blank" rel="noopener noreferrer" class="hover:text-primary-600 dark:hover:text-brand-400">Fiscalize platform terms</a>
          <a href="https://fiscalize.co.zw/privacy-policy" target="_blank" rel="noopener noreferrer" class="hover:text-primary-600 dark:hover:text-brand-400">Fiscalize platform privacy</a>
          <span>© {{ year }} Fiscalize. All rights reserved.</span>
        </footer>
      </div>
    </div>
  `,
  styles: [`
    :host ::ng-deep .legal h2 { margin-top: 2rem; font-size: 1.125rem; font-weight: 700; letter-spacing: -0.01em; color: rgb(15 23 42); }
    :host ::ng-deep .legal h3 { margin-top: 1.25rem; font-size: 0.95rem; font-weight: 600; color: rgb(30 41 59); }
    :host ::ng-deep .legal p, :host ::ng-deep .legal li { margin-top: 0.6rem; font-size: 0.9rem; line-height: 1.65; color: rgb(71 85 105); }
    :host ::ng-deep .legal ul { margin-top: 0.4rem; padding-left: 1.25rem; list-style: disc; }
    :host ::ng-deep .legal ol { margin-top: 0.4rem; padding-left: 1.25rem; list-style: decimal; }
    :host ::ng-deep .legal li { margin-top: 0.3rem; }
    :host ::ng-deep .legal strong { color: rgb(15 23 42); font-weight: 600; }
    :host ::ng-deep .legal a { color: rgb(1 114 199); font-weight: 500; }
    :host ::ng-deep .legal a:hover { text-decoration: underline; }
    :host ::ng-deep .legal table { margin-top: 0.75rem; width: 100%; border-collapse: collapse; font-size: 0.85rem; }
    :host ::ng-deep .legal th, :host ::ng-deep .legal td { border: 1px solid rgb(226 232 240); padding: 0.5rem 0.65rem; text-align: left; vertical-align: top; color: rgb(71 85 105); }
    :host ::ng-deep .legal th { background: rgb(248 250 252); color: rgb(15 23 42); font-weight: 600; }
    :host-context(.dark) ::ng-deep .legal h2, :host-context(.dark) ::ng-deep .legal strong, :host-context(.dark) ::ng-deep .legal th { color: #fff; }
    :host-context(.dark) ::ng-deep .legal h3 { color: rgb(226 232 240); }
    :host-context(.dark) ::ng-deep .legal p, :host-context(.dark) ::ng-deep .legal li, :host-context(.dark) ::ng-deep .legal td { color: rgb(203 213 225); }
    :host-context(.dark) ::ng-deep .legal th, :host-context(.dark) ::ng-deep .legal td { border-color: rgb(255 255 255 / 0.1); }
    :host-context(.dark) ::ng-deep .legal th { background: rgb(255 255 255 / 0.04); }
    :host-context(.dark) ::ng-deep .legal a { color: rgb(45 212 191); }
  `]
})
export class LegalShell {
  readonly kicker = input('Legal');
  readonly title = input.required<string>();
  readonly effective = input('17 November 2026');
  readonly updated = input('17 November 2026');
  readonly year = new Date().getFullYear();
}
