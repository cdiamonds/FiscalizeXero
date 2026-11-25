import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth/auth.service';
import { LocalService } from '../../core/services/local/local.service';
import { FiscalizeLogo } from '../../components/branding/fiscalize-logo/fiscalize-logo';

type Status = 'pending' | 'success' | 'error';

@Component({
  selector: 'app-oauth-callback',
  imports: [CommonModule, FiscalizeLogo, RouterLink],
  template: `
    <div class="relative min-h-screen overflow-hidden bg-gradient-to-br from-slate-50 via-white to-brand-50/50 dark:from-slate-950 dark:via-slate-950 dark:to-black">
      <div class="bg-blob teal"    style="top: -120px; left: -100px; width: 460px; height: 460px;"></div>
      <div class="bg-blob emerald" style="bottom: -160px; right: -120px; width: 460px; height: 460px;"></div>

      <div class="relative z-10 mx-auto flex min-h-screen w-full max-w-xl flex-col items-center justify-center px-4 py-10 sm:px-6">
        <div class="mb-8 flex justify-center fade-in-down">
          <app-fiscalize-logo></app-fiscalize-logo>
        </div>

        <div class="w-full rounded-2xl border border-white/40 bg-white/80 p-8 shadow-xl backdrop-blur-md fade-in-down dark:border-white/10 dark:bg-slate-900/70">
          <ng-container [ngSwitch]="status()">
            <!-- Pending -->
            <div *ngSwitchCase="'pending'" class="flex flex-col items-center text-center">
              <div class="relative mb-6 flex h-20 w-20 items-center justify-center">
                <div class="absolute inset-0 rounded-full border-4 border-brand-200 opacity-50 dark:border-primary-500/30"></div>
                <div class="absolute inset-0 animate-spin rounded-full border-4 border-transparent border-t-primary-500"></div>
                <svg class="h-9 w-9 text-primary-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.8" d="M13.828 10.172a4 4 0 00-5.656 0l-4 4a4 4 0 105.656 5.656l1.102-1.101m-.758-4.899a4 4 0 005.656 0l4-4a4 4 0 00-5.656-5.656l-1.1 1.1"/>
                </svg>
              </div>
              <h1 class="text-xl font-bold tracking-tight text-slate-900 dark:text-white">Connecting to Xero…</h1>
              <p class="mt-2 text-sm text-slate-500 dark:text-slate-400">Finishing the connection with Xero. This usually takes a few seconds.</p>
            </div>

            <!-- Success -->
            <div *ngSwitchCase="'success'" class="flex flex-col items-center text-center">
              <div class="mb-6 flex h-20 w-20 items-center justify-center rounded-full bg-emerald-100 dark:bg-emerald-500/20">
                <svg class="h-10 w-10 text-emerald-600 dark:text-emerald-400" fill="none" stroke="currentColor" stroke-width="2.4" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" d="M5 13l4 4L19 7"/></svg>
              </div>
              <h1 class="text-2xl font-bold tracking-tight text-slate-900 dark:text-white">Xero connected</h1>
              <p class="mt-2 text-sm text-slate-500 dark:text-slate-400">You're all set. <strong class="text-slate-700 dark:text-slate-200" data-testid="tenant-name">{{ tenantName() || 'Your Xero organisation' }}</strong> is now linked to this Fiscalize company.</p>
              <a routerLink="/dashboard"
                 class="mt-6 inline-flex items-center gap-2 rounded-xl bg-gradient-to-r from-primary-500 to-primary-600 px-5 py-2.5 text-sm font-semibold text-white shadow-md shadow-primary-500/25 transition-all duration-200 hover:from-primary-600 hover:to-primary-700 hover:scale-[1.03]">
                Go to dashboard
                <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14M13 5l7 7-7 7"/></svg>
              </a>
            </div>

            <!-- Error -->
            <div *ngSwitchCase="'error'" class="flex flex-col items-center text-center">
              <div class="mb-6 flex h-20 w-20 items-center justify-center rounded-full bg-rose-100 dark:bg-rose-500/20">
                <svg class="h-10 w-10 text-rose-600 dark:text-rose-400" fill="none" stroke="currentColor" stroke-width="2.4" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" d="M12 9v4M12 17h.01M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"/></svg>
              </div>
              <h1 class="text-2xl font-bold tracking-tight text-slate-900 dark:text-white">{{ errorTitle() }}</h1>
              <p class="mt-2 text-sm text-slate-500 dark:text-slate-400" data-testid="callback-error">{{ errorMessage() }}</p>
              <p *ngIf="conflictOrganisation() as org" class="mt-3 rounded-xl border border-amber-200 bg-amber-50 px-3 py-2 text-left text-xs text-amber-800 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-200">
                Organisation: <strong>{{ org }}</strong>. To use a different one, start again and pick it on Xero's consent screen.
              </p>
              <a routerLink="/register-client"
                 class="mt-6 inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-5 py-2.5 text-sm font-semibold text-slate-700 hover:bg-slate-50 dark:border-white/10 dark:bg-slate-900 dark:text-slate-200">
                Try again
              </a>
            </div>
          </ng-container>
        </div>
      </div>
    </div>
  `
})
export class OAuthCallback implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private authService = inject(AuthService);
  private localService = inject(LocalService);

  readonly status = signal<Status>('pending');
  readonly errorTitle = signal('Connection failed');
  readonly errorMessage = signal('Could not complete the Xero connection.');
  readonly tenantName = signal<string | null>(null);
  /** On a 409: the organisation that already belongs to another Fiscalize company. */
  readonly conflictOrganisation = signal<string | null>(null);

  private friendlyMessage(err: any): string {
    const server = err?.error?.message;
    if (server) return server;
    switch (err?.status) {
      case 0:   return "We couldn't reach Fiscalize. Check your connection and try again.";
      case 401: return 'Xero did not accept the authorisation (it may have expired). Start the connection again.';
      case 403: return 'This Xero connection was started by a different Fiscalize company. Sign in to your own company and start the connection from there.';
      case 404: return "We couldn't match this authorisation to a pending connection. Start the connection again from the beginning.";
      case 410: return 'This Xero connection link has expired. Start the connection again; it only takes a few seconds.';
      case 409: return 'This Xero organisation is already connected to a different Fiscalize company.';
      case 429: return 'Too many attempts. Wait a minute, then start the connection again.';
      default:  return 'Could not complete the Xero connection. Start the connection again.';
    }
  }

  ngOnInit(): void {
    const qp = this.route.snapshot.queryParamMap;
    const state = qp.get('state');
    const code = qp.get('code');
    const errorParam = qp.get('error');

    // Xero sends the user back with ?error=access_denied when they cancel on the consent screen.
    if (errorParam) {
      const cancelled = errorParam === 'access_denied';
      this.errorTitle.set(cancelled ? 'Connection cancelled' : 'Xero did not connect');
      this.errorMessage.set(cancelled
        ? 'You cancelled on the Xero consent screen, so nothing was connected. You can start again whenever you are ready.'
        : 'Xero returned an error instead of connecting. Start the connection again.');
      this.status.set('error');
      // Let the API put its own wording on it (and record the attempt); the text above stays if it cannot.
      this.authService.reportOAuthError(state, errorParam).subscribe({
        next: () => {},
        error: (err) => { if (err?.error?.message) this.errorMessage.set(err.error.message); }
      });
      return;
    }

    if (!state || !code) {
      this.status.set('error');
      this.errorMessage.set('This page finishes a Xero connection, but Xero did not send the details it needs. Start the connection again.');
      return;
    }

    // Finishing a connection needs the person who started it: the API refuses a consent completed by anyone else.
    if (!this.localService.getToken()) {
      this.status.set('error');
      this.errorTitle.set('Sign in to finish connecting');
      this.errorMessage.set('Xero sent you back, but you are not signed in to Fiscalize in this browser. Sign in, then start the connection again from Xero Connection.');
      return;
    }

    this.authService.completeOAuth(state, code).subscribe({
      next: (res) => {
        this.tenantName.set(res?.tenantName ?? null);
        this.status.set('success');
        // Arriving through the Fiscalize platform's launcher leaves a return address: go back there with the
        // result so the platform can show it. Otherwise carry on to the dashboard.
        const returnUrl = sessionStorage.getItem('paringReturnUrl');
        if (returnUrl) {
          sessionStorage.removeItem('paringReturnUrl');
          setTimeout(() => {
            const url = new URL(returnUrl, window.location.origin);
            url.searchParams.set('paired', 'Xero');
            url.searchParams.set('status', 'success');
            if (res?.tenantName) url.searchParams.set('displayName', res.tenantName);
            window.location.href = url.toString();
          }, 1200);
        } else {
          setTimeout(() => this.router.navigate(['/dashboard']), 1800);
        }
      },
      error: (err) => {
        const message = this.friendlyMessage(err);
        this.errorMessage.set(message);
        if (err?.status === 409) {
          this.errorTitle.set('Already connected elsewhere');
          this.conflictOrganisation.set(err?.error?.tenantName ?? null);
        }
        this.status.set('error');
        // A conflict is explained best where the user started: send them back to the platform when they came from it.
        const returnUrl = sessionStorage.getItem('paringReturnUrl');
        if (err?.status === 409 && returnUrl) {
          sessionStorage.removeItem('paringReturnUrl');
          setTimeout(() => {
            const url = new URL(returnUrl, window.location.origin);
            url.searchParams.set('paired', 'Xero');
            url.searchParams.set('status', 'conflict');
            url.searchParams.set('message', message);
            window.location.href = url.toString();
          }, 4000);
        }
      }
    });
  }
}
