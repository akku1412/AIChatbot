import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import { AuthService } from '../../core/auth/auth.service';
import { errorMessage } from '../../core/http/api-error';
import { ChatStore } from '../../core/services/chat.store';
import { UserApiService } from '../../core/services/user-api.service';
import { ConfirmDialogService } from '../../shared/components/confirm-dialog/confirm-dialog.service';
import { LoadingSpinner } from '../../shared/components/loading-spinner/loading-spinner';
import { NotificationService } from '../../shared/components/toast/notification.service';
import { CustomInstructions } from './custom-instructions';

const NAME_MAX = 200;

@Component({
  selector: 'app-profile',
  imports: [ReactiveFormsModule, DatePipe, LoadingSpinner, CustomInstructions],
  templateUrl: './profile.html',
  styleUrls: ['./profile-card.css', './profile.css'],
})
export class Profile {
  protected readonly auth = inject(AuthService);
  private readonly users = inject(UserApiService);
  private readonly store = inject(ChatStore);
  private readonly notifications = inject(NotificationService);
  private readonly confirmDialog = inject(ConfirmDialogService);

  protected readonly user = this.auth.user;
  protected readonly nameMax = NAME_MAX;
  protected readonly name = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(NAME_MAX), Validators.pattern(/\S/)],
  });
  /** [formGroup] on the <form> is what makes (ngSubmit) work and stops a native page-reloading submit. */
  protected readonly form = new FormGroup({ name: this.name });
  protected readonly editing = signal(false);
  protected readonly saving = signal(false);
  protected readonly imageFailed = signal(false);

  protected readonly initials = computed(() =>
    (this.user()?.fullName ?? '?')
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]!.toUpperCase())
      .join(''),
  );

  constructor() {
    // Nothing is "open" in the sidebar while on the profile page.
    this.store.startNewChat();

    // Refresh from the server, so the page never shows stale data.
    this.users.getMe().subscribe({ next: (user) => this.auth.updateUser(user), error: () => undefined });

    effect(() => {
      const user = this.user();
      if (user && !this.editing()) {
        this.name.setValue(user.fullName);
      }
    });
  }

  protected startEdit(): void {
    this.editing.set(true);
    this.name.setValue(this.user()?.fullName ?? '');
  }

  protected cancelEdit(): void {
    this.editing.set(false);
    this.name.setValue(this.user()?.fullName ?? '');
  }

  protected save(): void {
    const value = this.name.value.trim();
    if (this.name.invalid || this.saving()) {
      this.name.markAsTouched();
      return;
    }
    if (value === this.user()?.fullName) {
      this.editing.set(false);
      return;
    }

    this.saving.set(true);
    this.users.updateMe(value).subscribe({
      next: (user) => {
        this.auth.updateUser(user);
        this.saving.set(false);
        this.editing.set(false);
        this.notifications.success('Your name has been updated.');
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.notifications.error(errorMessage(err));
      },
    });
  }

  protected async logout(): Promise<void> {
    const confirmed = await this.confirmDialog.confirm({
      title: 'Log out?',
      message: 'You will need to sign in again (with Google or an email code) next time.',
      confirmText: 'Log out',
    });
    if (confirmed) {
      this.store.reset();
      this.auth.logout();
    }
  }
}
