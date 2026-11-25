import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AuthService } from '../../core/services/auth/auth.service';
import { FiscalizeLogo } from '../../components/branding/fiscalize-logo/fiscalize-logo';
import { ToastService } from '../../core/services/toast/toast.service';
import { extractErrorMessage } from '../../core/utils/http-error';

@Component({
  selector: 'app-login',
  imports: [CommonModule, ReactiveFormsModule, RouterLink, FiscalizeLogo],
  template: `
    <div class="relative flex min-h-screen items-center justify-center overflow-hidden bg-gradient-to-br from-slate-50 via-white to-brand-50/50 px-4 dark:from-slate-950 dark:via-slate-950 dark:to-black">
      <div class="bg-blob teal"    style="top: -120px; left: -100px; width: 460px; height: 460px;"></div>
      <div class="bg-blob emerald" style="bottom: -160px; right: -120px; width: 480px; height: 480px;"></div>

      <div class="relative z-10 w-full max-w-md scale-in">
        <div class="mb-6 flex items-center justify-between fade-in-down">
          <a routerLink="/" class="inline-flex items-center gap-1.5 rounded-lg px-2 py-1.5 text-sm font-medium text-slate-500 transition-colors hover:bg-slate-100 hover:text-slate-700 dark:text-slate-400 dark:hover:bg-white/5 dark:hover:text-slate-200">
            <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M15 19l-7-7 7-7"/></svg>
            Back
          </a>
          <app-fiscalize-logo></app-fiscalize-logo>
          <div class="w-16"></div>
        </div>

        <div class="card-hover rounded-2xl border border-white/40 bg-white/70 p-6 shadow-xl shadow-slate-900/[0.04] backdrop-blur-md md:p-8 dark:border-white/10 dark:bg-slate-900/60 dark:shadow-black/30">
          <div class="text-center fade-in-down" style="animation-delay: 0.05s">
            <div class="mx-auto flex h-12 w-12 items-center justify-center rounded-2xl bg-gradient-to-br from-primary-500 to-primary-600 shadow-lg shadow-primary-500/30">
              <svg class="h-6 w-6 text-white" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>
            </div>
            <h1 class="mt-4 text-2xl font-bold tracking-tight text-slate-900 dark:text-white">Welcome back</h1>
            <p class="mt-1 text-sm text-slate-600 dark:text-slate-300">Sign in to fiscalize your Xero invoices and credit notes with ZIMRA.</p>
          </div>

          @if (problem) {
            <div class="mt-6 rounded-xl border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-200" role="alert">
              <p class="font-semibold">Sign-in didn’t complete</p>
              <p class="mt-1 break-words text-xs">{{ problem }}</p>
              <p class="mt-1 text-xs">Try once more. If it happens again, send this message to support, or open the app from Fiscalize meanwhile.</p>
            </div>
          }

          <form [formGroup]="form" (ngSubmit)="submit()" class="mt-8 space-y-4">
            <div class="fade-in-down" style="animation-delay: 0.10s">
              <label class="mb-1.5 block text-xs font-semibold uppercase tracking-wider text-slate-600 dark:text-slate-400">Email</label>
              <div class="relative">
                <svg class="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"/><polyline points="22,6 12,13 2,6"/></svg>
                <input id="login-email" type="email" formControlName="email" autocomplete="email" placeholder="you@example.com"
                       class="w-full rounded-xl border border-slate-200 bg-white/70 py-2.5 pl-9 pr-3 text-sm text-slate-900 outline-none transition-all duration-200 placeholder:text-slate-400 focus:border-primary-500 focus:bg-white focus:ring-2 focus:ring-primary-500/30 focus:scale-[1.01] dark:border-white/10 dark:bg-slate-950/70 dark:text-white dark:focus:bg-slate-950" />
              </div>
            </div>

            <div class="fade-in-down" style="animation-delay: 0.15s">
              <div class="mb-1.5 flex items-center justify-between">
                <label class="block text-xs font-semibold uppercase tracking-wider text-slate-600 dark:text-slate-400">Password</label>
                <a class="text-xs font-medium text-primary-600 hover:text-primary-700 dark:text-brand-400">Forgot?</a>
              </div>
              <div class="relative">
                <svg class="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="11" width="18" height="11" rx="2"/><path d="M7 11V7a5 5 0 0110 0v4"/></svg>
                <input [type]="showPassword() ? 'text' : 'password'" formControlName="password" autocomplete="current-password" placeholder="••••••••"
                       class="w-full rounded-xl border border-slate-200 bg-white/70 py-2.5 pl-9 pr-10 text-sm text-slate-900 outline-none transition-all duration-200 placeholder:text-slate-400 focus:border-primary-500 focus:bg-white focus:ring-2 focus:ring-primary-500/30 focus:scale-[1.01] dark:border-white/10 dark:bg-slate-950/70 dark:text-white dark:focus:bg-slate-950" />
                <button type="button" (click)="togglePassword()" class="absolute right-2 top-1/2 -translate-y-1/2 rounded-md p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-700 dark:hover:bg-white/5 dark:hover:text-slate-200" tabindex="-1" aria-label="Toggle password">
                  <svg *ngIf="!showPassword()" class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/></svg>
                  <svg *ngIf="showPassword()" class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17.94 17.94A10.07 10.07 0 0112 20c-7 0-11-8-11-8a18.45 18.45 0 015.06-5.94M9.9 4.24A9.12 9.12 0 0112 4c7 0 11 8 11 8a18.5 18.5 0 01-2.16 3.19m-6.72-1.07a3 3 0 11-4.24-4.24"/><line x1="1" y1="1" x2="23" y2="23"/></svg>
                </button>
              </div>
            </div>

            <button type="submit" [disabled]="form.invalid || loading()"
                    class="group fade-in-down inline-flex w-full items-center justify-center gap-2 rounded-xl bg-gradient-to-r from-primary-500 to-primary-600 px-4 py-3 text-sm font-semibold text-white shadow-lg shadow-primary-500/25 transition-all duration-200 hover:from-primary-600 hover:to-primary-700 hover:scale-[1.02] active:scale-[0.98] disabled:cursor-not-allowed disabled:opacity-50 disabled:hover:scale-100"
                    style="animation-delay: 0.20s">
              <svg *ngIf="loading()" class="h-4 w-4 animate-spin" viewBox="0 0 24 24" fill="none"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
              {{ loading() ? 'Signing in…' : 'Sign in' }}
              <svg *ngIf="!loading()" class="h-4 w-4 transition-transform group-hover:translate-x-0.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14M13 5l7 7-7 7"/></svg>
            </button>
          </form>

          <div class="mt-6 text-center text-sm text-slate-500 fade-in-down dark:text-slate-400" style="animation-delay: 0.25s">
            New to Fiscalize?
            <a href="https://fiscalize.co.zw/sign-up" target="_blank" rel="noopener noreferrer" class="font-semibold text-primary-600 hover:text-primary-700 dark:text-brand-400">Create an account →</a>
          </div>
        </div>

        <p class="mt-6 text-center text-xs text-slate-500 dark:text-slate-500">
          Protected by Fiscalize · v{{ year }} ·
          <a routerLink="/terms" class="hover:text-primary-600 dark:hover:text-brand-400">Terms</a> ·
          <a routerLink="/privacy" class="hover:text-primary-600 dark:hover:text-brand-400">Privacy</a> ·
          <a href="mailto:legal@fiscalize.co.zw?subject=Fiscalize%20for%20Xero%20support" class="hover:text-primary-600 dark:hover:text-brand-400">Support</a>
        </p>
      </div>
    </div>
  `
})
export class Login {
  private fb = inject(FormBuilder);
  private auth = inject(AuthService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private toast = inject(ToastService);

  readonly year = new Date().getFullYear();
  readonly loading = signal(false);
  readonly showPassword = signal(false);

  readonly form = this.fb.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(6)]]
  });

  /** Why the last sign-in didn't complete (set when the app was rejected straight after signing in). */
  readonly problem = (this.route.snapshot.queryParamMap.get('problem') ?? '').slice(0, 400) || null;

  togglePassword(): void { this.showPassword.update((v) => !v); }

  submit(): void {
    if (this.form.invalid) return;
    this.loading.set(true);

    const { email, password } = this.form.getRawValue();
    this.auth.login({ email: email!, password: password! }).subscribe({
      next: (res) => {
        this.loading.set(false);
        const queryReturn = this.route.snapshot.queryParamMap.get('returnUrl');
        const back = queryReturn && queryReturn.startsWith('/') && !queryReturn.startsWith('//') ? queryReturn : null;
        // No company from the platform (someone with several companies): choose one first, then carry on.
        if (!res.activeCompany?.id) {
          const returnUrl = back ?? this.auth.peekReturnTo();
          this.router.navigate(['/select-company'], { queryParams: returnUrl ? { returnUrl } : {} });
          return;
        }
        this.router.navigateByUrl(back || this.auth.defaultRouteAfterLogin(res));
      },
      error: (err) => {
        this.loading.set(false);
        this.toast.error(extractErrorMessage(err, 'Sign-in failed. Please try again.'));
      }
    });
  }
}
