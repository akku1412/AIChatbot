import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { ConfirmDialog } from './shared/components/confirm-dialog/confirm-dialog';
import { ToastContainer } from './shared/components/toast/toast-container';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, ToastContainer, ConfirmDialog],
  template: '<router-outlet /><app-toast-container /><app-confirm-dialog />',
})
export class App {}
