import { runInInjectionContext, EnvironmentInjector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { provideRouter } from '@angular/router';
import { authGuard, permissionGuard } from './auth.guard';
import { AuthService } from './auth.service';

/** Models only what the guards read off AuthService. */
class FakeAuthService {
  authenticated = false;
  restoreResult = false;
  restoreCalls = 0;
  permissions = new Set<string>();
  employeeLinked = false;
  landing = '/me';

  isAuthenticated(): boolean {
    return this.authenticated;
  }

  async tryRestore(): Promise<boolean> {
    this.restoreCalls++;
    if (this.restoreResult) this.authenticated = true;
    return this.restoreResult;
  }

  hasPermission(permission: string): boolean {
    return this.permissions.has(permission);
  }

  isEmployeeLinked(): boolean {
    return this.employeeLinked;
  }

  landingRoute(): string {
    return this.landing;
  }
}

describe('auth guards', () => {
  let auth: FakeAuthService;

  // The guards ignore both arguments; the casts keep the CanActivateFn signature honest.
  const route = {} as ActivatedRouteSnapshot;
  const state = { url: '/dashboard' } as RouterStateSnapshot;

  const run = <T>(fn: () => T): T =>
    runInInjectionContext(TestBed.inject(EnvironmentInjector), fn);

  beforeEach(() => {
    auth = new FakeAuthService();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: auth }],
    });
  });

  describe('authGuard', () => {
    it('allows an already-authenticated user without touching tryRestore', async () => {
      auth.authenticated = true;

      const result = await run(() => authGuard(route, state));

      expect(result).toBeTrue();
      expect(auth.restoreCalls).toBe(0);
    });

    it('allows the user when the session restores', async () => {
      auth.restoreResult = true;

      const result = await run(() => authGuard(route, state));

      expect(result).toBeTrue();
      expect(auth.restoreCalls).toBe(1);
    });

    it('redirects to /login when there is no session to restore', async () => {
      const result = await run(() => authGuard(route, state));

      expect(result instanceof UrlTree).toBeTrue();
      expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/login');
    });
  });

  describe('permissionGuard', () => {
    it('allows a user holding the permission', () => {
      auth.permissions.add('users.manage');

      const result = run(() => permissionGuard({ permission: 'users.manage' })(route, state));

      expect(result).toBeTrue();
    });

    it('bounces a user missing the permission to their landing route', () => {
      auth.landing = '/dashboard';

      const result = run(() => permissionGuard({ permission: 'users.manage' })(route, state));

      expect(result instanceof UrlTree).toBeTrue();
      expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/dashboard');
    });

    it('bounces a user with the permission but no employee link when one is required', () => {
      auth.permissions.add('attendance.view');
      auth.landing = '/dashboard';

      const result = run(() =>
        permissionGuard({ permission: 'attendance.view', requireEmployee: true })(route, state),
      );

      expect(result instanceof UrlTree).toBeTrue();
      expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/dashboard');
    });

    it('allows an employee-linked user through a requireEmployee gate', () => {
      auth.employeeLinked = true;

      const result = run(() => permissionGuard({ requireEmployee: true })(route, state));

      expect(result).toBeTrue();
    });
  });
});
