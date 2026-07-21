import { Routes } from '@angular/router';
import { authGuard, permissionGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./pages/login/login.component').then(m => m.LoginComponent),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./shell/shell.component').then(m => m.ShellComponent),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        canActivate: [permissionGuard({ permission: 'attendance.view' })],
        loadComponent: () => import('./pages/dashboard/dashboard.component').then(m => m.DashboardComponent),
      },
      {
        path: 'employees',
        canActivate: [permissionGuard({ permission: 'employees.view' })],
        loadComponent: () => import('./pages/employees/employees.component').then(m => m.EmployeesComponent),
      },
      {
        path: 'me',
        canActivate: [permissionGuard({ requireEmployee: true })],
        loadComponent: () => import('./pages/self-service/self-service.component').then(m => m.SelfServiceComponent),
      },
      {
        path: 'users',
        canActivate: [permissionGuard({ permission: 'users.manage' })],
        loadComponent: () => import('./pages/users/users.component').then(m => m.UsersComponent),
      },
      {
        path: 'security-groups',
        canActivate: [permissionGuard({ permission: 'roles.manage' })],
        loadComponent: () =>
          import('./pages/security-groups/security-groups.component').then(m => m.SecurityGroupsComponent),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
