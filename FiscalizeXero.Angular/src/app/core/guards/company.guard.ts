import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { LocalService } from '../services/local/local.service';

/**
 * Company pages need a chosen company. A sign-in that didn't bring one (someone with several companies signing in
 * here directly) goes to /select-company first, and comes back to the page it asked for once a company is chosen.
 * Runs after the auth guard.
 */
export const companyGuard: CanActivateFn = (_route, state) => {
  const user = inject(LocalService).getAuthUser();
  if (user?.companyId) return true;
  return inject(Router).createUrlTree(['/select-company'], { queryParams: { returnUrl: state.url } });
};
