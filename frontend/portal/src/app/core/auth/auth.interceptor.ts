import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { from, switchMap, throwError, catchError } from 'rxjs';
import { AuthService } from './auth.service';

/** Attaches the bearer token; on a 401 tries one silent refresh, then logs out. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);

  const withToken = (token: string | null) =>
    token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  if (req.url.includes('/api/auth/')) {
    return next(req);
  }

  return next(withToken(auth.token())).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && auth.isAuthenticated()) {
        return from(auth.refresh()).pipe(
          switchMap(refreshed =>
            refreshed ? next(withToken(auth.token())) : throwError(() => error),
          ),
        );
      }
      return throwError(() => error);
    }),
  );
};
