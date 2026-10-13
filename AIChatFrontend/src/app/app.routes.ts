import { Routes } from '@angular/router';

import { authGuard, guestGuard, otpChallengeGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'chat' },
  {
    path: 'login',
    title: 'Sign in · AIChat',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/login/login').then((m) => m.Login),
  },
  {
    path: 'verify-otp',
    title: 'Verify your email · AIChat',
    canActivate: [guestGuard, otpChallengeGuard],
    loadComponent: () =>
      import('./features/otp-verification/otp-verification').then((m) => m.OtpVerification),
  },
  {
    path: 'chat',
    title: 'AIChat',
    canActivate: [authGuard],
    loadComponent: () => import('./features/chat/chat-page').then((m) => m.ChatPage),
  },
  // profile (Phase 9) is added later.
  { path: '**', redirectTo: 'chat' },
];
