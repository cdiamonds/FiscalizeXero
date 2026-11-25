import { Injectable, inject, signal } from '@angular/core';
import { SessionService } from '../session/session.service';

export type ToastKind = 'success' | 'error' | 'warning' | 'info';

export interface Toast {
  id: number;
  message: string;
  kind: ToastKind;
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  private counter = 0;
  private session = inject(SessionService);
  readonly toasts = signal<Toast[]>([]);

  show(message: string, kind: ToastKind = 'info', duration = 5000): void {
    const id = ++this.counter;
    this.toasts.update(ts => [...ts, { id, message, kind }]);
    setTimeout(() => this.dismiss(id), duration);
  }

  success(message: string): void { this.show(message, 'success', 4000); }
  // While the "sign in again" box is up, the errors behind it are all the same timed-out session: don't pile them up.
  error(message: string): void   { if (!this.session.sessionExpired()) this.show(message, 'error',   7000); }
  warning(message: string): void { if (!this.session.sessionExpired()) this.show(message, 'warning', 6000); }
  info(message: string): void    { this.show(message, 'info',    5000); }

  dismiss(id: number): void {
    this.toasts.update(ts => ts.filter(t => t.id !== id));
  }
}
