import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/services/auth/auth.service';
import { ClientConfigService } from '../../core/services/xero/client-config.service';
import { FiscalizeLogo } from '../../components/branding/fiscalize-logo/fiscalize-logo';

/**
 * Entry point hit by Fiscalyze's app launcher (POST /api/AppLaunchers/Launch
 * returns a launchUrl pointing here). We exchange the one-time launch token for a
 * QB-app session, persist the `return_url` for the OAuth callback, then route to
 * /register-client (if Xero is not connected yet) or /dashboard (if already connected).
 */
@Component({
  selector: 'app-launch',
  standalone: true,
  imports: [CommonModule, FiscalizeLogo],
  template: `
    <div class="relative min-h-screen overflow-hidden bg-gradient-to-br from-slate-50 via-white to-brand-50/50 dark:from-slate-950 dark:via-slate-950 dark:to-black">
      <div class="bg-blob teal" style="top: -120px; left: -100px; width: 460px; height: 460px;"></div>
      <div class="bg-blob emerald" style="bottom: -160px; right: -120px; width: 460px; height: 460px;"></div>

      <div class="relative z-10 mx-auto flex min-h-screen w-full max-w-xl flex-col items-center justify-center px-4 py-10 sm:px-6">
        <div class="mb-8 flex justify-center fade-in-down">
          <app-fiscalize-logo></app-fiscalize-logo>
        </div>

        <div class="w-full rounded-2xl border border-white/40 bg-white/80 p-8 shadow-xl backdrop-blur-md fade-in-down dark:border-white/10 dark:bg-slate-900/70">
          @if (error()) {
            <div class="flex flex-col items-center text-center">
              <div class="mb-6 flex h-20 w-20 items-center justify-center rounded-full bg-rose-100 dark:bg-rose-500/20">
                <svg class="h-10 w-10 text-rose-600 dark:text-rose-400" fill="none" stroke="currentColor" stroke-width="2.4" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" d="M12 9v4M12 17h.01M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"/></svg>
              </div>
              <h1 class="text-2xl font-bold tracking-tight text-slate-900 dark:text-white">Launch failed</h1>
              <p class="mt-2 text-sm text-slate-500 dark:text-slate-400">{{ error() }}</p>
              <p class="mt-4 text-xs text-slate-500 dark:text-slate-400">Return to Fiscalyze and try again, or contact support if the issue persists.</p>
            </div>
          } @else {
            <div class="flex flex-col items-center text-center">
              <div class="relative mb-6 flex h-20 w-20 items-center justify-center">
                <div class="absolute inset-0 rounded-full border-4 border-brand-200 opacity-50 dark:border-primary-500/30"></div>
                <div class="absolute inset-0 animate-spin rounded-full border-4 border-transparent border-t-primary-500"></div>
                <svg class="h-9 w-9 text-primary-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.8" d="M13.828 10.172a4 4 0 00-5.656 0l-4 4a4 4 0 105.656 5.656l1.102-1.101m-.758-4.899a4 4 0 005.656 0l4-4a4 4 0 00-5.656-5.656l-1.1 1.1"/>
                </svg>
              </div>
              <h1 class="text-xl font-bold tracking-tight text-slate-900 dark:text-white">Signing you in…</h1>
              <p class="mt-2 text-sm text-slate-500 dark:text-slate-400">Opening Fiscalize for Xero. One moment.</p>
            </div>
          }
        </div>
      </div>
    </div>
  `,
})
export class LaunchComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private auth = inject(AuthService);
  private clientConfig = inject(ClientConfigService);

  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    const qp = this.route.snapshot.queryParamMap;
    const token = qp.get('token');
    const returnUrl = qp.get('return_url');

    if (!token) {
      this.error.set('No launch token in URL. Did you arrive here via the Fiscalyze app launcher?');
      return;
    }

    // Persist return_url so OAuthCallback can redirect back to Fiscalyze after pairing.
    if (returnUrl) sessionStorage.setItem('paringReturnUrl', returnUrl);

    this.auth.exchangeLaunchToken(token).subscribe({
      next: () => this.routeAfterLogin(),
      error: (err) => {
        this.error.set(err?.error?.message || err?.message || 'Could not validate the launch token.');
      },
    });
  }

  /**
   * If Xero is not connected yet, send the user into the "Connect Xero"
   * flow. The new SaaS path skips the BYOC form entirely. Once they finish OAuth and
   * the callback creates the pairing, OAuthCallback redirects back to return_url.
   * If a Xero connection already exists, drop straight to the dashboard.
   */
  private routeAfterLogin(): void {
    this.clientConfig.current().subscribe({
      next: (cfg) => {
        if (cfg?.tenantId) {
          this.router.navigateByUrl('/dashboard');
        } else {
          this.router.navigateByUrl('/register-client');
        }
      },
      error: () => this.router.navigateByUrl('/register-client'),
    });
  }
}
