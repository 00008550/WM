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
    phone: null,
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
    httpMock.expectOne(req => req.url === `${base}/api/departments`).flush([]);
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

/**
 * 003 P3: department and phone survive an edit. The editor used to send `departmentId: null` and an
 * empty phone on every save — data loss for phone, and since 003 P2b a 403 on every save for a
 * department-scoped manager. The backend half of the round-trip, run as such a manager, is
 * `OrganisationScopeEndpointTests.A_department_scoped_edit_made_from_the_list_row_keeps_department_and_phone`.
 */
describe('EmployeesComponent — department and phone round-trip', () => {
  const base = environment.apiUrl;
  const SITE_A = '11111111-1111-1111-1111-111111111111';
  const SITE_B = '22222222-2222-2222-2222-222222222222';
  const DEPT_A = '33333333-3333-3333-3333-333333333333';
  const DEPT_B = '44444444-4444-4444-4444-444444444444';

  let component: EmployeesComponent;
  let httpMock: HttpTestingController;

  const ada: EmployeeRow = {
    id: 'aaaaaaaa-0000-0000-0000-000000000001',
    code: 'E1001', firstName: 'Ada', lastName: 'Lovelace',
    email: null, phone: '+44 20 7946 0001', jobTitle: null,
    siteId: SITE_A, departmentId: DEPT_A,
    employedFrom: '2024-01-15', employedUntil: null, isSuspended: false,
    leavingReasonId: null, leaverComments: null,
    status: 0, isEmployed: true, asAt: '2026-09-23',
    version: '018f7c1e-0000-7000-8000-00000000000a',
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [EmployeesComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { hasPermission: () => true } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(EmployeesComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);

    fixture.detectChanges();
    httpMock.expectOne(req => req.url === `${base}/api/employees`)
      .flush({ items: [ada], total: 1, page: 1, pageSize: 25 });
    httpMock.expectOne(`${base}/api/sites`).flush([
      { id: SITE_A, name: 'North', parentId: null, timeZone: 'UTC' },
      { id: SITE_B, name: 'South', parentId: null, timeZone: 'UTC' },
    ]);
    httpMock.expectOne(req => req.url === `${base}/api/departments`).flush([
      { id: DEPT_A, name: 'Assembly', siteId: SITE_A },
      { id: DEPT_B, name: 'Packing', siteId: SITE_B },
    ]);
  });

  afterEach(() => httpMock.verify());

  const flushReload = () =>
    httpMock.expectOne(req => req.url === `${base}/api/employees`)
      .flush({ items: [ada], total: 1, page: 1, pageSize: 25 });

  it('carries the department and phone it was opened with through an unrelated edit', () => {
    component.openEdit(ada);
    component.form.jobTitle = 'Shift supervisor';
    component.save();

    const request = httpMock.expectOne(`${base}/api/employees/${ada.id}`);
    expect(request.request.body.departmentId).toBe(DEPT_A);
    expect(request.request.body.phone).toBe('+44 20 7946 0001');
    request.flush(ada);
    flushReload();
  });

  it('sends the department picked on a create', () => {
    component.openCreate();
    component.onSiteChange(SITE_B);
    expect(component.departmentsAtSite().map(d => d.id)).toEqual([DEPT_B]);
    component.form.departmentId = DEPT_B;
    component.form.phone = ' 07700 900123 ';
    component.save();

    const request = httpMock.expectOne(`${base}/api/employees`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body.departmentId).toBe(DEPT_B);
    expect(request.request.body.phone).toBe('07700 900123');
    request.flush(ada);
    flushReload();
  });

  it('offers only the selected site\'s departments and drops one left behind by a site move', () => {
    component.openEdit(ada);
    expect(component.departmentsAtSite().map(d => d.id)).toEqual([DEPT_A]);

    component.onSiteChange(SITE_B);
    expect(component.form.departmentId).toBe('');
    component.save();

    const request = httpMock.expectOne(`${base}/api/employees/${ada.id}`);
    expect(request.request.body.siteId).toBe(SITE_B);
    expect(request.request.body.departmentId).toBeNull();
    request.flush(ada);
    flushReload();
  });
});
