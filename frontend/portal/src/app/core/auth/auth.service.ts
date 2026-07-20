import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  displayName: string;
  employeeId: string | null;
  permissions: string[];
}

const REFRESH_KEY = 'wm.refresh';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  // Access token lives only in memory; the refresh token is persisted so a
  // page reload survives. TODO(hardening): move refresh to an httpOnly cookie.
  private readonly accessToken = signal<string | null>(null);
  readonly displayName = signal<string>('');
  readonly permissions = signal<ReadonlySet<string>>(new Set());
  readonly employeeId = signal<string | null>(null);
  readonly isAuthenticated = computed(() => this.accessToken() !== null);
  /** True when this user is linked to an employee → gets the self-service surface. */
  readonly isEmployeeLinked = computed(() => this.employeeId() !== null);

  token(): string | null {
    return this.accessToken();
  }

  hasPermission(permission: string): boolean {
    return this.permissions().has(permission);
  }

  /** Where this user should land after login, based on what they can access. */
  landingRoute(): string {
    if (this.hasPermission('attendance.view')) return '/dashboard';
    if (this.isEmployeeLinked()) return '/me';
    if (this.hasPermission('users.manage')) return '/users';
    return '/me';
  }

  async login(userName: string, password: string): Promise<void> {
    const response = await firstValueFrom(
      this.http.post<AuthResponse>(`${environment.apiUrl}/api/auth/login`, { userName, password }),
    );
    this.apply(response);
  }

  /** Restores the session after a reload. Returns false if no valid refresh token. */
  async tryRestore(): Promise<boolean> {
    const refreshToken = localStorage.getItem(REFRESH_KEY);
    if (!refreshToken) return false;
    try {
      const response = await firstValueFrom(
        this.http.post<AuthResponse>(`${environment.apiUrl}/api/auth/refresh`, { refreshToken }),
      );
      this.apply(response);
      return true;
    } catch {
      localStorage.removeItem(REFRESH_KEY);
      return false;
    }
  }

  async refresh(): Promise<boolean> {
    return this.tryRestore();
  }

  async logout(): Promise<void> {
    const refreshToken = localStorage.getItem(REFRESH_KEY);
    if (refreshToken) {
      try {
        await firstValueFrom(
          this.http.post(`${environment.apiUrl}/api/auth/logout`, { refreshToken }),
        );
      } catch {
        // best effort — local state is cleared regardless
      }
    }
    localStorage.removeItem(REFRESH_KEY);
    this.accessToken.set(null);
    this.permissions.set(new Set());
    this.employeeId.set(null);
    await this.router.navigateByUrl('/login');
  }

  private apply(response: AuthResponse): void {
    this.accessToken.set(response.accessToken);
    this.displayName.set(response.displayName);
    this.permissions.set(new Set(response.permissions));
    this.employeeId.set(response.employeeId);
    localStorage.setItem(REFRESH_KEY, response.refreshToken);
  }
}
