import { Routes } from '@angular/router';

import { authGuard, guestGuard, otpChallengeGuard } from './core/guards/auth.guard';

const chatThread = () => import('./features/chat/chat-thread').then((m) => m.ChatThread);

export const routes: Routes = [
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
    // Signed-in area: sidebar + header layout around chat and profile pages.
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./features/chat/chat-layout').then((m) => m.ChatLayout),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'chat' },
      { path: 'chat', title: 'New chat · AIChat', loadComponent: chatThread },
      { path: 'chat/:conversationId', title: 'AIChat', loadComponent: chatThread },
      {
        path: 'profile',
        title: 'Profile · AIChat',
        loadComponent: () => import('./features/profile/profile').then((m) => m.Profile),
      },
    ],
  },
  { path: '**', redirectTo: 'chat' },
];
