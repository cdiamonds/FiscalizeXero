import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { Dialog } from '../dialog/dialog';
import { SessionService } from '../../core/services/session/session.service';
import { AuthService } from '../../core/services/auth/auth.service';

@Component({
  selector: 'app-session-expired-modal',
  imports: [CommonModule, Dialog],
  template: `
    <app-dialog [open]="sessionService.sessionExpired()" [closeOnBackdrop]="false" title="Your Fiscalize session has timed out" subtitle="Sign in again to carry on." [size]="'sm'" [hasFooter]="true" (close)="signIn()">
      <div class="flex items-start gap-3">
        <div class="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-2xl bg-gradient-to-br from-amber-500 to-orange-500 shadow-md shadow-amber-500/30">
          <svg class="h-5 w-5 text-white" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M12 6v6l4 2"/></svg>
        </div>
        <p class="text-sm text-slate-700 dark:text-slate-200">For security, Fiscalize ends sessions after a period of inactivity. Sign in again and you'll come straight back to this page.</p>
      </div>
      <ng-container dialogFooter>
        <button type="button" (click)="signIn()"
                class="inline-flex items-center gap-1.5 rounded-xl bg-gradient-to-r from-primary-500 to-primary-600 px-4 py-2 text-sm font-semibold text-white shadow-lg shadow-primary-500/25 transition-all duration-200 hover:from-primary-600 hover:to-primary-700 hover:scale-[1.02] active:scale-[0.98]">
          Sign in again
        </button>
      </ng-container>
    </app-dialog>
  `
})
export class SessionExpiredModal {
  sessionService = inject(SessionService);
  private auth = inject(AuthService);
  private router = inject(Router);

  signIn(): void {
    this.sessionService.dismiss();
    if (this.router.url && !this.router.url.startsWith('/login')) {
      this.sessionService.storeReturnTo(this.router.url);
    }
    this.auth.logout(false);
    this.router.navigate(['/login']);
  }
}
