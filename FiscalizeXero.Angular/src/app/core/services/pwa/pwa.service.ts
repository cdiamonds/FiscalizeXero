import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SwUpdate } from '@angular/service-worker';
import { filter } from 'rxjs/operators';

interface BeforeInstallPromptEvent extends Event {
  readonly platforms: string[];
  prompt(): Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed'; platform: string }>;
}

const DISMISS_KEY = 'pwa.installDismissedAt';
const DISMISS_TTL_MS = 14 * 24 * 60 * 60 * 1000; // 14 days

@Injectable({ providedIn: 'root' })
export class PwaService {
  private destroyRef = inject(DestroyRef);
  private swUpdate = inject(SwUpdate, { optional: true });

  /** Whether the browser fired beforeinstallprompt and the app isn't already installed. */
  readonly canInstall = signal(false);
  /** True after a successful install. */
  readonly installed = signal(false);
  /** iOS Safari has no install prompt — we show manual instructions. */
  readonly isIosSafari = signal(false);
  /** True when running as an installed PWA (display-mode: standalone). */
  readonly isStandalone = signal(false);
  /** A new service-worker version is ready — caller can prompt the user to reload. */
  readonly updateReady = signal(false);

  private deferredPrompt: BeforeInstallPromptEvent | null = null;

  init(): void {
    if (typeof window === 'undefined') return;

    this.isStandalone.set(this.detectStandalone());
    this.isIosSafari.set(this.detectIosSafari());

    window.addEventListener('beforeinstallprompt', this.onBeforeInstall);
    window.addEventListener('appinstalled', this.onAppInstalled);

    // If already installed, never prompt
    if (this.isStandalone()) {
      this.canInstall.set(false);
    }

    // Watch for service-worker updates
    if (this.swUpdate?.isEnabled) {
      this.swUpdate.versionUpdates
        .pipe(
          filter((evt) => evt.type === 'VERSION_READY'),
          takeUntilDestroyed(this.destroyRef)
        )
        .subscribe(() => this.updateReady.set(true));
    }
  }

  /** Trigger the native install prompt. Returns true if the user accepted. */
  async promptInstall(): Promise<boolean> {
    const evt = this.deferredPrompt;
    if (!evt) return false;
    await evt.prompt();
    const choice = await evt.userChoice;
    this.deferredPrompt = null;
    this.canInstall.set(false);
    if (choice.outcome === 'accepted') {
      this.installed.set(true);
      return true;
    }
    this.dismissForNow();
    return false;
  }

  dismissForNow(): void {
    try {
      localStorage.setItem(DISMISS_KEY, String(Date.now()));
    } catch { /* ignore quota / disabled storage */ }
    this.canInstall.set(false);
  }

  private wasRecentlyDismissed(): boolean {
    try {
      const v = localStorage.getItem(DISMISS_KEY);
      if (!v) return false;
      const ts = parseInt(v, 10);
      return Number.isFinite(ts) && Date.now() - ts < DISMISS_TTL_MS;
    } catch {
      return false;
    }
  }

  /** Apply a downloaded update by reloading. */
  applyUpdate(): void {
    if (!this.swUpdate?.isEnabled) { window.location.reload(); return; }
    this.swUpdate.activateUpdate().finally(() => window.location.reload());
  }

  private onBeforeInstall = (event: Event): void => {
    event.preventDefault();
    this.deferredPrompt = event as BeforeInstallPromptEvent;
    if (!this.isStandalone() && !this.wasRecentlyDismissed()) {
      this.canInstall.set(true);
    }
  };

  private onAppInstalled = (): void => {
    this.deferredPrompt = null;
    this.canInstall.set(false);
    this.installed.set(true);
    this.isStandalone.set(true);
  };

  private detectStandalone(): boolean {
    const mql = window.matchMedia?.('(display-mode: standalone)');
    if (mql?.matches) return true;
    return (navigator as any).standalone === true;
  }

  private detectIosSafari(): boolean {
    const ua = navigator.userAgent || '';
    const isIos = /iPad|iPhone|iPod/.test(ua) && !(window as any).MSStream;
    const isSafari = /Safari/i.test(ua) && !/CriOS|FxiOS|EdgiOS|OPiOS/i.test(ua);
    return isIos && isSafari && !this.detectStandalone();
  }
}
