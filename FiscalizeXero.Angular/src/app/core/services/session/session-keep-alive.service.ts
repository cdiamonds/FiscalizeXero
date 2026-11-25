import { Injectable, NgZone, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../../environments/environment';
import { LocalService } from '../local/local.service';
import { SessionService } from './session.service';

/** How often the Fiscalize platform session is kept alive while the user is working. */
const INTERVAL_MS = 5 * 60_000;
/** Counted as "working" when the user did something this recently. */
const ACTIVE_WITHIN_MS = 10 * 60_000;
/** Back on the tab after this long: check the session straight away, before the user meets an error. */
const RETURN_CHECK_AFTER_MS = 2 * 60_000;

/**
 * Keeps the user's Fiscalize platform session alive while they work in this app, because the platform ends idle
 * sessions and many pages here never touch it. When the session is already over, the API answers with the
 * "fiscalizeSessionExpired" marker and the interceptor shows the "sign in again" box, so the user is asked to sign in
 * instead of meeting errors on their next click. Silent otherwise: a failed check never shows an error.
 */
@Injectable({ providedIn: 'root' })
export class SessionKeepAliveService {
  private http = inject(HttpClient);
  private local = inject(LocalService);
  private session = inject(SessionService);
  private zone = inject(NgZone);

  private lastActivity = Date.now();
  private hiddenAt: number | null = null;
  private inFlight = false;
  private started = false;

  start(): void {
    if (this.started || typeof window === 'undefined') return;
    this.started = true;
    // Outside Angular's zone: activity tracking and the timer shouldn't trigger change detection.
    this.zone.runOutsideAngular(() => {
      const mark = () => { this.lastActivity = Date.now(); };
      for (const ev of ['pointerdown', 'keydown', 'scroll', 'touchstart']) window.addEventListener(ev, mark, { passive: true, capture: true });
      document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'hidden') { this.hiddenAt = Date.now(); return; }
        const away = this.hiddenAt ? Date.now() - this.hiddenAt : 0;
        this.hiddenAt = null;
        if (away >= RETURN_CHECK_AFTER_MS) { this.lastActivity = Date.now(); this.ping(); }
      });
      setInterval(() => {
        if (document.visibilityState === 'visible' && Date.now() - this.lastActivity < ACTIVE_WITHIN_MS) this.ping();
      }, INTERVAL_MS);
    });
  }

  /** Asks the API to keep the platform session alive; the interceptor handles "session over". */
  ping(): void {
    if (this.inFlight || !this.local.getToken() || this.session.sessionExpired()) return;
    if (!this.local.getAuthUser()?.companyId) return; // nothing to keep alive before a company is chosen
    this.inFlight = true;
    this.zone.run(() => this.http.post(`${environment.apiURL}Auths/KeepAlive`, {}).subscribe({
      next: () => { this.inFlight = false; },
      error: () => { this.inFlight = false; }
    }));
  }
}
