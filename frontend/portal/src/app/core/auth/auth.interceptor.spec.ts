import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

/**
 * Fake stand-in for AuthService: the interceptor only reads `token()` /
 * `isAuthenticated()` and calls `refresh()`, so the spec models exactly that
 * and nothing of the real service's HTTP or storage behaviour.
 */
class FakeAuthService {
  accessToken: string | null = 'token-1';
  refreshResult = true;
  refreshCalls = 0;

  token(): string | null {
    return this.accessToken;
  }

  isAuthenticated(): boolean {
    return this.accessToken !== null;
  }

  async refresh(): Promise<boolean> {
    this.refreshCalls++;
    if (this.refreshResult) this.accessToken = 'token-2';
    return this.refreshResult;
  }
}

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let auth: FakeAuthService;

  beforeEach(() => {
    auth = new FakeAuthService();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: auth },
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('attaches the bearer token to an ordinary request', () => {
    http.get('/api/employees').subscribe();

    const request = httpMock.expectOne('/api/employees');
    expect(request.request.headers.get('Authorization')).toBe('Bearer token-1');
    request.flush({});
  });

  it('sends no Authorization header when there is no token', () => {
    auth.accessToken = null;

    http.get('/api/employees').subscribe();

    const request = httpMock.expectOne('/api/employees');
    expect(request.request.headers.has('Authorization')).toBeFalse();
    request.flush({});
  });

  it('leaves /api/auth/ requests untouched', () => {
    http.post('/api/auth/login', { userName: 'a', password: 'b' }).subscribe();

    const request = httpMock.expectOne('/api/auth/login');
    expect(request.request.headers.has('Authorization')).toBeFalse();
    request.flush({});
  });

  it('refreshes once on a 401 and retries with the new token', async () => {
    let body: unknown = null;
    http.get('/api/employees').subscribe(response => (body = response));

    httpMock.expectOne('/api/employees').flush('nope', { status: 401, statusText: 'Unauthorized' });

    // refresh() is a promise, so the retry is queued a microtask later.
    await Promise.resolve();
    await Promise.resolve();

    const retry = httpMock.expectOne('/api/employees');
    expect(auth.refreshCalls).toBe(1);
    expect(retry.request.headers.get('Authorization')).toBe('Bearer token-2');
    retry.flush({ ok: true });
    expect(body).toEqual({ ok: true });
  });

  it('propagates the 401 when the refresh fails', async () => {
    auth.refreshResult = false;
    let status = 0;
    http.get('/api/employees').subscribe({ error: (error: { status: number }) => (status = error.status) });

    httpMock.expectOne('/api/employees').flush('nope', { status: 401, statusText: 'Unauthorized' });
    await Promise.resolve();
    await Promise.resolve();

    expect(auth.refreshCalls).toBe(1);
    expect(status).toBe(401);
  });

  it('does not attempt a refresh for a non-401 failure', () => {
    let status = 0;
    http.get('/api/employees').subscribe({ error: (error: { status: number }) => (status = error.status) });

    httpMock.expectOne('/api/employees').flush('boom', { status: 500, statusText: 'Server Error' });

    expect(auth.refreshCalls).toBe(0);
    expect(status).toBe(500);
  });
});
