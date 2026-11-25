import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { LocalService } from '../services/local/local.service';

/**
 * Blocks dashboard access for companies whose Xero connection isn't fully set up yet.
 * Redirects to /register-client so the user finishes the OAuth/setup flow first.
 *
 * Runs after the auth guard, so we can assume an authenticated user.
 */
export const setupGuard: CanActivateFn = (_route, state) => {
  const localService = inject(LocalService);
  const router = inject(Router);

  const user = localService.getAuthUser();
  if (!user) return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });

  const isSetup = !!user.xeroSetupComplete;
  if (!isSetup) {
    return router.createUrlTree(['/register-client']);
  }
  return true;
};
