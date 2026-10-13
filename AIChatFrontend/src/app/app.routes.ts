import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  {
    path: 'login',
    title: 'Sign in · AIChat',
    loadComponent: () => import('./features/login/login').then((m) => m.Login),
  },
  // otp-verification, chat, profile routes are added in Phases 6–9.
  { path: '**', redirectTo: 'login' },
];
