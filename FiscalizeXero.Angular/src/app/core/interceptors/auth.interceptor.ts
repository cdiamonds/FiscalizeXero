import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { LocalService } from '../services/local/local.service';
import { SessionService } from '../services/session/session.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const localService = inject(LocalService);
  const sessionService = inject(SessionService);
  const router = inject(Router);
  const token = localService.getToken();

  const authReq = token
    ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : req;

  return next(authReq).pipe(
    catchError((err: HttpErrorResponse) => {
      // Backend signal — fiscalize session/token expired (separate from 401)
      const body: any = err.error;
      const isAuthCall = req.url.includes('/Auths/');
      // Rejected straight after signing in: going back to the sign-in page would just repeat. Say what happened
      // instead, on the sign-in page, so the user (and support) can see the reason.
      const rejectsSession = err.status === 401 || body?.fiscalizeSessionExpired === true;
      if (rejectsSession && !isAuthCall && sessionService.justSignedIn()) {
        // Pages send several requests at once: explain the first rejection, swallow the rest (no "session expired" box).
        if (sessionService.rejectionExplained) return throwError(() => err);
        sessionService.rejectionExplained = true;
        const path = req.url.replace(/^.*\/api\//, '').split('?')[0];
        const reason = (body && typeof body === 'object' && body.message) ? String(body.message).replace(/\.$/, '') : 'no reason given';
        console.warn('Signed in, then rejected by', path, err.status, reason);
        localService.clearAuth();
        router.navigate(['/login'], { queryParams: { problem: `Signed in, but the app couldn't open your session (${path} answered ${err.status}: ${reason}).` } });
        return throwError(() => err);
      }
      if (body && body.fiscalizeSessionExpired === true) {
        sessionService.dispatchExpired();
      }

      if (err.status === 401) {
        const isAuthEndpoint = req.url.includes('/Auths/Login');
        // Only this app rejecting the sign-in signs the user out: that 401 comes with no message. A 401 that explains
        // itself (a device or API key refused, the platform session needing a fresh sign-in) is shown to the user
        // instead; an expired platform session already raised the "sign in again" prompt above.
        const explained = !!body && typeof body === 'object' && (body.fiscalizeSessionExpired === true || !!body.message);
        if (!isAuthEndpoint && !explained) {
          if (router.url && !router.url.startsWith('/login') && !router.url.startsWith('/redirect-url')) {
            sessionService.storeReturnTo(router.url);
          }
          localService.clearAuth();
          router.navigate(['/login']);
        }
      }
      // 424 (Xero, the device or the platform refused), 423 (locked) and 429 (rate-limited) are the caller's to
      // show: the user's own sign-in is fine, so nothing is cleared and nobody is sent to sign in.
      return throwError(() => err);
    })
  );
};
