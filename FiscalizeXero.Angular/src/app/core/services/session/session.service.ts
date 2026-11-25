import { Injectable, signal } from '@angular/core';

export const FISCALIZE_RETURN_TO_KEY = 'FISCALIZE_RETURN_TO_KEY';
export const FISCALIZE_SESSION_EXPIRED_EVENT = 'fiscalize-session-expired';
const SIGNED_IN_AT_KEY = 'FISCALIZE_SIGNED_IN_AT';

@Injectable({ providedIn: 'root' })
export class SessionService {
  readonly sessionExpired = signal(false);

  constructor() {
    if (typeof window !== 'undefined') {
      window.addEventListener(FISCALIZE_SESSION_EXPIRED_EVENT, () => this.sessionExpired.set(true));
    }
  }

  dispatchExpired(): void {
    if (typeof window !== 'undefined') {
      window.dispatchEvent(new CustomEvent(FISCALIZE_SESSION_EXPIRED_EVENT));
    }
    this.sessionExpired.set(true);
  }

  dismiss(): void {
    this.sessionExpired.set(false);
  }

  /** Remembers when this tab last signed in, so a rejection straight afterwards is explained instead of looping. */
  markSignedIn(): void {
    this.rejectionExplained = false;
    try { window.sessionStorage.setItem(SIGNED_IN_AT_KEY, String(Date.now())); } catch { /* storage unavailable */ }
  }

  /** True within two minutes of signing in on this tab. */
  justSignedIn(): boolean {
    try {
      const at = Number(window.sessionStorage.getItem(SIGNED_IN_AT_KEY) || 0);
      return at > 0 && Date.now() - at < 120_000;
    } catch { return false; }
  }

  /** Set once the sign-in page has been told why the session was rejected; later rejections stay quiet. */
  rejectionExplained = false;

  storeReturnTo(url: string): void {
    if (typeof window === 'undefined') return;
    window.sessionStorage.setItem(FISCALIZE_RETURN_TO_KEY, url);
  }

  takeReturnTo(): string | null {
    if (typeof window === 'undefined') return null;
    const v = window.sessionStorage.getItem(FISCALIZE_RETURN_TO_KEY);
    if (v) window.sessionStorage.removeItem(FISCALIZE_RETURN_TO_KEY);
    return v;
  }
}
