import { Routes } from '@angular/router';

import { adminGuard, authGuard, guestGuard, otpChallengeGuard } from './core/guards/auth.guard';

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
    // Public, read-only shared chat: no sign-in (and no guard, so signed-in users can open links too).
    path: 'share/:token',
    title: 'Shared chat · AIChat',
    loadComponent: () => import('./features/share/shared-chat').then((m) => m.SharedChat),
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
        path: 'projects/:projectId',
        title: 'Project · AIChat',
        loadComponent: () => import('./features/projects/project-page').then((m) => m.ProjectPage),
      },
      {
        path: 'profile',
        title: 'Profile · AIChat',
        loadComponent: () => import('./features/profile/profile').then((m) => m.Profile),
      },
      {
        path: 'admin',
        canActivate: [adminGuard],
        loadComponent: () => import('./features/admin/admin-page').then((m) => m.AdminPage),
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'users' },
          {
            path: 'users',
            title: 'Users · Admin · AIChat',
            loadComponent: () => import('./features/admin/admin-users').then((m) => m.AdminUsers),
          },
          {
            path: 'usage',
            title: 'Usage · Admin · AIChat',
            loadComponent: () => import('./features/admin/admin-usage').then((m) => m.AdminUsage),
          },
          {
            path: 'feedback',
            title: 'Feedback · Admin · AIChat',
            loadComponent: () => import('./features/admin/admin-feedback').then((m) => m.AdminFeedbackPage),
          },
        ],
      },
    ],
  },
  { path: '**', redirectTo: 'chat' },
];
