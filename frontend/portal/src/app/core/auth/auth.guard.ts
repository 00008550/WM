import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) return true;
  if (await auth.tryRestore()) return true;
  return router.createUrlTree(['/login']);
};

/**
 * Route guard factory: allow only if the user holds `permission`, else bounce to
 * their landing route. `requireEmployee` gates the self-service pages.
 */
export function permissionGuard(options: { permission?: string; requireEmployee?: boolean }): CanActivateFn {
  return () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    const ok =
      (!options.permission || auth.hasPermission(options.permission)) &&
      (!options.requireEmployee || auth.isEmployeeLinked());
    return ok ? true : router.parseUrl(auth.landingRoute());
  };
}
