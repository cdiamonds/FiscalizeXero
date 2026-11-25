import { Injectable } from '@angular/core';

export interface AuthUser {
  id: number;
  name: string;
  email: string;
  company?: string;
  companyId?: number;
  /** The active company has a Xero connection record. */
  hasXeroConnection?: boolean;
  /** That connection is live, so the company pages can be used. */
  xeroSetupComplete?: boolean;
  tenantId?: string | null;
  tenantName?: string | null;
  [key: string]: any;
}

const KEYS = {
  authToken: 'authToken',
  userId: 'userId',
  authUser: 'authUser',
  sidebarCollapsed: 'sidebarCollapsed',
  theme: 'theme',
} as const;

@Injectable({ providedIn: 'root' })
export class LocalService {
  getToken(): string | null { return this.read(KEYS.authToken); }
  setToken(token: string): void { this.write(KEYS.authToken, token); }

  getUserId(): number | null {
    const raw = this.read(KEYS.userId);
    return raw ? Number(raw) : null;
  }
  setUserId(id: number): void { this.write(KEYS.userId, String(id)); }

  getAuthUser(): AuthUser | null {
    const raw = this.read(KEYS.authUser);
    if (!raw) return null;
    try { return JSON.parse(raw) as AuthUser; } catch { return null; }
  }
  setAuthUser(user: AuthUser): void { this.write(KEYS.authUser, JSON.stringify(user)); }

  getSidebarCollapsed(): boolean { return this.read(KEYS.sidebarCollapsed) === 'true'; }
  setSidebarCollapsed(collapsed: boolean): void { this.write(KEYS.sidebarCollapsed, String(collapsed)); }

  getTheme(): 'light' | 'dark' | null {
    const v = this.read(KEYS.theme);
    return v === 'dark' || v === 'light' ? v : null;
  }
  setTheme(theme: 'light' | 'dark'): void { this.write(KEYS.theme, theme); }

  clearAuth(): void {
    this.remove(KEYS.authToken);
    this.remove(KEYS.userId);
    this.remove(KEYS.authUser);
  }

  private read(key: string): string | null {
    if (typeof window === 'undefined') return null;
    return window.localStorage.getItem(key);
  }
  private write(key: string, value: string): void {
    if (typeof window === 'undefined') return;
    window.localStorage.setItem(key, value);
  }
  private remove(key: string): void {
    if (typeof window === 'undefined') return;
    window.localStorage.removeItem(key);
  }
}
