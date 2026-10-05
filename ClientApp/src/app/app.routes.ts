import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () =>
      import('./features/login/login.component').then((m) => m.LoginComponent),
  },
  {
    path: 'setup',
    loadComponent: () =>
      import('./features/setup/setup.component').then((m) => m.SetupComponent),
  },
  { path: '', pathMatch: 'full', redirectTo: 'login' },
];
