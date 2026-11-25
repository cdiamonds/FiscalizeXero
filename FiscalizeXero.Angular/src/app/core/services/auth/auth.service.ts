import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { LocalService, AuthUser } from '../local/local.service';
import { LoginRequest, LoginResponse, RegisterRequest } from '../../models/auth.models';
import { SessionService } from '../session/session.service';

export interface OAuthCallbackResponse {
  success: boolean;
  message: string;
  /** The Xero organisation that was connected. */
  tenantId?: string;
  tenantName?: string | null;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private localService = inject(LocalService);
  private sessionService = inject(SessionService);
  private router = inject(Router);

  readonly currentUser = signal<AuthUser | null>(this.localService.getAuthUser());
  readonly isAuthenticated = computed(() => !!this.currentUser() && !!this.localService.getToken());

  login(payload: LoginRequest): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${environment.apiURL}Auths/Login`, payload).pipe(
      tap((res) => { this.persistSession(res); this.sessionService.markSignedIn(); })
    );
  }

  register(payload: RegisterRequest): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${environment.apiURL}Auths/Register`, payload).pipe(
      tap((res) => this.persistSession(res))
    );
  }

  /**
   * Exchange a one-time launch token issued by the Fiscalyze platform for a
   * full QB-app session. Backend also persists the Fiscalyze JWT it gets back,
   * so subsequent server-to-server calls into the platform are authenticated
   * as the launching user.
   */
  /** Asks the API for a one-time launch URL back into the Fiscalize platform for the active company. */
  openFiscalize(): Observable<{ launchUrl: string }> {
    return this.http.post<{ launchUrl: string }>(`${environment.apiURL}Auths/OpenFiscalize`, {});
  }

  exchangeLaunchToken(token: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${environment.apiURL}Auths/ExchangeLaunchToken`, { token }).pipe(
      tap((res) => this.persistSession(res))
    );
  }

  /** Decide where to send the user after a successful login. */
  defaultRouteAfterLogin(res: LoginResponse): string {
    const stored = this.sessionService.takeReturnTo();
    if (stored && !stored.startsWith('/login') && !stored.startsWith('/redirect-url')) {
      return stored;
    }
    const org = res?.xeroOrganisation;
    return org && org.isSetupComplete !== false ? '/dashboard' : '/register-client';
  }

  /** Where the user was before being sent to sign in, without using it up. */
  peekReturnTo(): string | null {
    const v = this.sessionService.takeReturnTo();
    if (v) this.sessionService.storeReturnTo(v);
    return v && !v.startsWith('/login') && !v.startsWith('/redirect-url') && !v.startsWith('/select-company') ? v : null;
  }

  /**
   * Ends this person's sign-in on every device and browser, then here. If the server can't be reached, this browser
   * is still signed out, and the user is told the other devices may still be signed in.
   */
  signOutEverywhere(): Observable<boolean> {
    return new Observable<boolean>((sub) => {
      this.http.post<void>(`${environment.apiURL}Auths/SignOutEverywhere`, null).subscribe({
        next: () => { this.logout(); sub.next(true); sub.complete(); },
        error: () => { this.logout(); sub.next(false); sub.complete(); },
      });
    });
  }

  logout(redirect = true): void {
    this.localService.clearAuth();
    this.currentUser.set(null);
    this.sessionService.dismiss();
    if (redirect) {
      this.router.navigate(['/login']);
    }
  }

  /** Hands Xero's answer (the state and code from the redirect) to the API, which completes the connection. */
  completeOAuth(state: string, code: string): Observable<OAuthCallbackResponse> {
    const params = new HttpParams().set('state', state).set('code', code);
    return this.http.get<OAuthCallbackResponse>(`${environment.apiURL}ClientConfigs/oauth/callback`, { params }).pipe(
      tap((res) => this.markActiveCompanySetupComplete(res?.tenantId, res?.tenantName))
    );
  }

  /** Reports Xero's own refusal (the user cancelled, or Xero returned an error) so the API can explain it. */
  reportOAuthError(state: string | null, error: string): Observable<OAuthCallbackResponse> {
    let params = new HttpParams().set('error', error);
    if (state) params = params.set('state', state);
    return this.http.get<OAuthCallbackResponse>(`${environment.apiURL}ClientConfigs/oauth/callback`, { params });
  }

  markActiveCompanySetupComplete(tenantId?: string | null, tenantName?: string | null): void {
    const u = this.localService.getAuthUser();
    if (!u) return;
    const next: AuthUser = {
      ...u, hasXeroConnection: true, xeroSetupComplete: true,
      tenantId: tenantId ?? u.tenantId ?? null, tenantName: tenantName ?? u.tenantName ?? null
    };
    this.localService.setAuthUser(next);
    this.currentUser.set(next);
  }

  refreshFromStorage(): void {
    this.currentUser.set(this.localService.getAuthUser());
  }

  markActiveCompanyDisconnected(): void {
    const u = this.localService.getAuthUser();
    if (!u) return;
    const next: AuthUser = { ...u, hasXeroConnection: false, xeroSetupComplete: false, tenantId: null, tenantName: null };
    this.localService.setAuthUser(next);
    this.currentUser.set(next);
  }

  persistSession(res: LoginResponse): void {
    if (res.token) this.localService.setToken(res.token);
    if (res.userId != null) this.localService.setUserId(res.userId);

    const org = res.xeroOrganisation;
    const user: AuthUser = {
      id: res.userId,
      name: res.fullName,
      email: res.email ?? '',
      company: res.activeCompany?.name,
      companyId: res.activeCompany?.id,
      hasXeroConnection: !!org,
      xeroSetupComplete: org?.isSetupComplete ?? false,
      tenantId: org?.id ?? null,
      tenantName: org?.name ?? null,
    };
    this.localService.setAuthUser(user);
    this.currentUser.set(user);
  }
}
