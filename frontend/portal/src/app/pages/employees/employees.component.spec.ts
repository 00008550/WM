import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../core/auth/auth.service';
import { EmployeeRow } from '../../core/api/workforce.api';
import { EmployeesComponent } from './employees.component';

/**
 * The SPA half of 011 P5's round-trip, and the reason it has to be a spec rather than a look at the
 * screen: if this component drops `version` between the row it read and the body it sends, the API
 * sees no token, and a backend suite full of green concurrency tests proves nothing at all about the
 * only client that exists. That failure is silent by construction — which is the exact class of
 * defect P5 is about.
 *
 * Only the drawer's read/echo path is exercised. Rendering is not the subject.
 */
describe('EmployeesComponent — the concurrency token round-trip', () => {
  const base = environment.apiUrl;

  /** Two distinct tokens, so "it echoed the right one" is a real assertion and not a coincidence. */
  const ADA_VERSION = '018f7c1e-0000-7000-8000-00000000000a';
  const GRACE_VERSION = '018f7c1e-0000-7000-8000-00000000000b';

  let fixture: ComponentFixture<EmployeesComponent>;
  let component: EmployeesComponent;
  let httpMock: HttpTestingController;

  const row = (id: string, version: string, lastName: string): EmployeeRow => ({
    id,
    code: 'E1001',
    firstName: 'Ada',
    lastName,
    email: null,
    jobTitle: null,
    siteId: '11111111-1111-1111-1111-111111111111',
    departmentId: null,
    employedFrom: '2024-01-15',
    employedUntil: null,
    isSuspended: false,
    leavingReasonId: null,
    leaverComments: null,
    status: 0,
    isEmployed: true,
    asAt: '2026-08-29',
    version,
  });

  const ada = row('aaaaaaaa-0000-0000-0000-000000000001', ADA_VERSION, 'Lovelace');
  const grace = row('aaaaaaaa-0000-0000-0000-000000000002', GRACE_VERSION, 'Hopper');

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [EmployeesComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        // The component asks for `employees.manage` before it loads sites and before it lets
        // anything be edited, so the fake has to answer that and nothing more.
        { provide: AuthService, useValue: { hasPermission: () => true } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(EmployeesComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);

    fixture.detectChanges(); // ngOnInit → list + sites
    httpMock
      .expectOne(req => req.url === `${base}/api/employees`)
      .flush({ items: [ada, grace], total: 2, page: 1, pageSize: 25 });
    httpMock.expectOne(`${base}/api/sites`).flush([]);
  });

  afterEach(() => httpMock.verify());

  it('echoes back the version of the record the drawer was opened with', () => {
    component.openEdit(ada);
    component.save();

    const request = httpMock.expectOne(`${base}/api/employees/${ada.id}`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body.version).toBe(ADA_VERSION);
    request.flush(ada);

    httpMock.expectOne(req => req.url === `${base}/api/employees`).flush({
      items: [ada, grace], total: 2, page: 1, pageSize: 25,
    });
  });

  it('echoes the second record\'s own version, not the first one it ever saw', () => {
    // The bug this guards is a token cached once on the component instead of carried per record —
    // which would send Ada's version when saving Grace, and 409 an edit that was never stale.
    component.openEdit(ada);
    component.close();
    component.openEdit(grace);
    component.save();

    const request = httpMock.expectOne(`${base}/api/employees/${grace.id}`);
    expect(request.request.body.version).toBe(GRACE_VERSION);
    request.flush(grace);

    httpMock.expectOne(req => req.url === `${base}/api/employees`).flush({
      items: [ada, grace], total: 2, page: 1, pageSize: 25,
    });
  });

  it('sends no version on a create, because there is nothing to be stale against', () => {
    component.openCreate();
    component.save();

    const request = httpMock.expectOne(`${base}/api/employees`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body.version).toBeNull();
    request.flush(ada);

    httpMock.expectOne(req => req.url === `${base}/api/employees`).flush({
      items: [ada, grace], total: 2, page: 1, pageSize: 25,
    });
  });

  it('shows the 409 message and keeps the drawer open with the user\'s edit in it', () => {
    // D4's whole user-facing contract: a message, and the form still there to re-enter from. If the
    // drawer closed on a conflict the manager would lose the typing as well as the save, which is
    // worse than the silent overwrite this replaced.
    component.openEdit(ada);
    component.form.jobTitle = 'Shift supervisor';
    component.save();

    httpMock.expectOne(`${base}/api/employees/${ada.id}`).flush(
      { detail: 'Someone else changed this record — reload and try again.' },
      { status: 409, statusText: 'Conflict' },
    );

    expect(component.error()).toBe('Someone else changed this record — reload and try again.');
    expect(component.editing()).toBeTrue();
    expect(component.busy()).toBeFalse();
    expect(component.form.jobTitle).toBe('Shift supervisor');
  });
});
