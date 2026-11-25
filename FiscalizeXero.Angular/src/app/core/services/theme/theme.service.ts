import { Injectable, inject, signal } from '@angular/core';
import { LocalService } from '../local/local.service';
import { PreferencesService } from '../preferences/preferences.service';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private localService = inject(LocalService);
  private preferencesService = inject(PreferencesService);
  readonly theme = signal<'light' | 'dark'>('light');

  init(): void {
    const stored = this.localService.getTheme();
    const prefersDark = typeof window !== 'undefined'
      && window.matchMedia
      && window.matchMedia('(prefers-color-scheme: dark)').matches;

    const initial: 'light' | 'dark' = stored ?? (prefersDark ? 'dark' : 'light');
    this.apply(initial, false);

    if (this.localService.getToken()) {
      this.preferencesService.get().subscribe({
        next: (res) => {
          if (res?.theme === 'dark' || res?.theme === 'light') {
            this.apply(res.theme, false);
          }
        },
        error: () => { /* ignore */ }
      });
    }
  }

  toggle(): void {
    const next = this.theme() === 'dark' ? 'light' : 'dark';
    this.apply(next, true);
  }

  set(theme: 'light' | 'dark'): void {
    this.apply(theme, true);
  }

  private apply(theme: 'light' | 'dark', persistRemote: boolean): void {
    this.theme.set(theme);
    this.localService.setTheme(theme);
    if (typeof document !== 'undefined') {
      const root = document.documentElement;
      if (theme === 'dark') root.classList.add('dark');
      else root.classList.remove('dark');
    }
    if (persistRemote && this.localService.getToken()) {
      this.preferencesService.save(theme).subscribe({ error: () => { /* ignore */ } });
    }
  }
}
