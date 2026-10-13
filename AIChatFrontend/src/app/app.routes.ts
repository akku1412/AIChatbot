import { Routes } from '@angular/router';

import { authGuard, guestGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'chat' },
  {
    path: 'login',
    title: 'Sign in · AIChat',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/login/login').then((m) => m.Login),
  },
  {
    path: 'chat',
    title: 'AIChat',
    canActivate: [authGuard],
    loadComponent: () => import('./features/chat/chat-page').then((m) => m.ChatPage),
  },
  // otp-verification (Phase 6), profile (Phase 9) are added later.
  { path: '**', redirectTo: 'chat' },
];
