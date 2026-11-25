import { Injectable, inject, signal } from '@angular/core';
import { HttpBackend, HttpClient } from '@angular/common/http';
import { environment } from '../../../../environments/environment';

/** One running part: its version, the git commit it was built from, and when. */
export interface RunningVersion {
  version: string;
  commit?: string | null;
  builtAt?: string | null;
  startedAt?: string | null;
  environment?: string | null;
}

/**
 * Which builds are running: this web app (public /version.json, written at build time) and the API (GET api/version).
 * Read once, on demand, without the auth interceptor or the service worker cache, so it always reflects what is deployed.
 */
@Injectable({ providedIn: 'root' })
export class VersionService {
  // HttpBackend skips the interceptors: no token, no session handling, works signed out.
  private readonly http = new HttpClient(inject(HttpBackend));
  readonly web = signal<RunningVersion | null>(null);
  readonly api = signal<RunningVersion | null>(null);
  readonly apiUnavailable = signal(false);
  private loaded = false;

  load(force = false): void {
    if (this.loaded && !force) return;
    this.loaded = true;
    // Query parameters, not headers: no CORS preflight, and ngsw-bypass keeps the service worker out of the way.
    const fresh = `ngsw-bypass=true&t=${Date.now()}`;
    this.http.get<RunningVersion>(`version.json?${fresh}`).subscribe({
      next: v => this.web.set(v?.version ? v : { version: 'development' }),
      error: () => this.web.set({ version: 'development' })
    });
    this.http.get<RunningVersion>(`${environment.apiURL}version?${fresh}`).subscribe({
      next: v => { this.api.set(v); this.apiUnavailable.set(false); },
      error: () => this.apiUnavailable.set(true)
    });
  }
}
