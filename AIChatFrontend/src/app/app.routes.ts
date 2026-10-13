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
    // No guestGuard: a pending challenge already means the user is mid sign-in, so skip the refresh probe.
    canActivate: [otpChallengeGuard],
    loadComponent: () =>
      import('./features/otp-verification/otp-verification').then((m) => m.OtpVerification),
  },
  {
    path: 'chat',
    canActivate: [authGuard],
    loadComponent: () => import('./features/chat/chat-layout').then((m) => m.ChatLayout),
    children: [
      {
        path: '',
        title: 'New chat · AIChat',
        loadComponent: () => import('./features/chat/chat-thread').then((m) => m.ChatThread),
      },
      {
        path: ':conversationId',
        title: 'AIChat',
        loadComponent: () => import('./features/chat/chat-thread').then((m) => m.ChatThread),
      },
    ],
  },
  // profile (Phase 9) is added later.
  { path: '**', redirectTo: 'chat' },
];
