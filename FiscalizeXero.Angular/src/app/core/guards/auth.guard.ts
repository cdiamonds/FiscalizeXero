import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { LocalService } from '../services/local/local.service';

export const authGuard: CanActivateFn = (_route, state) => {
  const localService = inject(LocalService);
  const router = inject(Router);

  if (localService.getToken()) return true;

  return router.createUrlTree(['/login'], {
    queryParams: { returnUrl: state.url }
  });
};
