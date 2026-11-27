import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, catchError, debounceTime, distinctUntilChanged, of, switchMap, tap } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from '../../core/services/auth/auth.service';
import { CompaniesService, FiscalyzeCompany } from '../../core/services/xero/companies.service';
import { UserCompany } from '../../core/models/xero.models';
import { FiscalizeLogo } from '../../components/branding/fiscalize-logo/fiscalize-logo';
import { extractErrorMessage } from '../../core/utils/http-error';
import { OpenFiscalize } from '../../components/open-fiscalize/open-fiscalize';

const PAGE_SIZE = 20;

/** One company to choose: from the Fiscalize platform, joined with what this app knows about its Xero connection. */
interface Choice {
  id: number;
  name: string;
  subtitle: string | null;
  logo: string | null;
  role: string | null;
  /** Xero is connected and ready. */
  connected: boolean;
  /** Connected, but the user must allow access in Xero again. */
  needsReauth: boolean;
  /** The company this app last had open for the user. */
  lastUsed: boolean;
}

/**
 * Choose which Fiscalize company to work in. Shown after a direct sign-in that didn't bring a company (and from the
 * company switcher's "All companies"). Searches the platform's list on the server and pages it, so it stays quick for
 * people with many companies; falls back to the companies this app already knows if the platform list can't be read.
 */
@Component({
  selector: 'app-select-company',
  imports: [CommonModule, FiscalizeLogo, OpenFiscalize],
  template: `
    <div class="relative flex min-h-screen justify-center overflow-hidden bg-gradient-to-br from-slate-50 via-white to-brand-50/50 px-4 py-8 sm:items-center sm:py-12 dark:from-slate-950 dark:via-slate-950 dark:to-black">
      <div class="bg-blob teal"    style="top: -120px; left: -100px; width: 460px; height: 460px;"></div>
      <div class="bg-blob emerald" style="bottom: -160px; right: -120px; width: 480px; height: 480px;"></div>

      <main class="relative z-10 w-full max-w-xl scale-in" aria-labelledby="choose-title">
        <div class="mb-6 flex items-center justify-between gap-3">
          <app-fiscalize-logo></app-fiscalize-logo>
          <div class="flex items-center gap-2">
          <app-open-fiscalize variant="page"></app-open-fiscalize>
          <button type="button" (click)="signOut()"
                  class="inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-sm font-medium text-slate-500 transition-colors hover:bg-slate-100 hover:text-slate-700 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary-500 dark:text-slate-400 dark:hover:bg-white/5 dark:hover:text-slate-200">
            <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9"/></svg>
            Sign out
          </button>
          </div>
        </div>

        <div class="overflow-hidden rounded-2xl border border-white/40 bg-white/80 shadow-xl shadow-slate-900/[0.04] backdrop-blur-md dark:border-white/10 dark:bg-slate-900/70 dark:shadow-black/30">
          <header class="border-b border-slate-200/60 px-5 pb-5 pt-6 sm:px-7 dark:border-white/5">
            <h1 id="choose-title" class="text-xl font-bold tracking-tight text-slate-900 sm:text-2xl dark:text-white">Choose a company</h1>
            <p class="mt-1 text-sm text-slate-600 dark:text-slate-300">
              @if (userName()) { Signed in as <span class="font-medium text-slate-900 dark:text-white">{{ userName() }}</span>. }
              Pick the company whose Xero organisation you want to fiscalise.
            </p>

            <div class="relative mt-5">
              <svg class="pointer-events-none absolute left-3.5 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg>
              <input #box type="search" [value]="search()" (input)="onSearch(box.value)" (keydown.arrowdown)="focusFirst($event)"
                     placeholder="Search by company or trade name…" aria-label="Search companies" autocomplete="off" autofocus
                     class="w-full rounded-xl border border-slate-200 bg-white py-2.5 pl-10 pr-10 text-sm text-slate-900 placeholder-slate-400 shadow-sm transition focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-500/30 dark:border-white/10 dark:bg-slate-950/60 dark:text-white dark:placeholder-slate-500">
              @if (searching()) {
                <span class="absolute right-3.5 top-1/2 h-4 w-4 -translate-y-1/2 animate-spin rounded-full border-2 border-brand-200 border-t-primary-500" aria-hidden="true"></span>
              } @else if (search()) {
                <button type="button" (click)="box.value = ''; onSearch('')" aria-label="Clear search"
                        class="absolute right-2.5 top-1/2 -translate-y-1/2 rounded-md p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-600 dark:hover:bg-white/10">
                  <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 6 6 18M6 6l12 12"/></svg>
                </button>
              }
            </div>
          </header>

          @if (notice()) {
            <div class="mx-5 mt-4 flex gap-2.5 rounded-xl border border-amber-200/70 bg-amber-50/80 px-3.5 py-2.5 text-xs text-amber-900 sm:mx-7 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-200" role="status">
              <svg class="mt-px h-4 w-4 flex-shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"/><path d="M12 8v4M12 16h.01"/></svg>
              <span>{{ notice() }}</span>
            </div>
          }

          <div class="px-3 py-3 sm:px-4">
            @if (loading() && !items().length) {
              <ul class="space-y-1" aria-label="Loading companies" aria-busy="true">
                @for (i of [1, 2, 3, 4, 5]; track i) {
                  <li class="flex items-center gap-3 rounded-xl px-3 py-3">
                    <div class="h-10 w-10 animate-pulse rounded-xl bg-slate-200 dark:bg-white/10"></div>
                    <div class="flex-1 space-y-2"><div class="h-3.5 w-2/5 animate-pulse rounded bg-slate-200 dark:bg-white/10"></div><div class="h-3 w-1/4 animate-pulse rounded bg-slate-200/70 dark:bg-white/5"></div></div>
                  </li>
                }
              </ul>
            } @else if (error()) {
              <div class="px-3 py-10 text-center">
                <p class="text-sm font-medium text-slate-900 dark:text-white">We couldn't load your companies</p>
                <p class="mx-auto mt-1 max-w-sm text-sm text-slate-500 dark:text-slate-400">{{ error() }}</p>
                <div class="mt-4 flex justify-center gap-2">
                  <button type="button" (click)="reload()" class="rounded-xl border border-slate-200 bg-white px-3.5 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50 dark:border-white/10 dark:bg-slate-800 dark:text-slate-200">Try again</button>
                  <button type="button" (click)="signOut()" class="rounded-xl bg-gradient-to-r from-primary-500 to-primary-600 px-3.5 py-2 text-sm font-semibold text-white shadow-md shadow-primary-500/20">Sign in again</button>
                </div>
              </div>
            } @else if (!items().length) {
              <div class="px-3 py-10 text-center">
                <div class="mx-auto flex h-11 w-11 items-center justify-center rounded-xl bg-slate-100 text-slate-400 dark:bg-white/5">
                  <svg class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 21h18M5 21V7l8-4v18M19 21V11l-6-4"/></svg>
                </div>
                @if (search()) {
                  <p class="mt-3 text-sm font-medium text-slate-900 dark:text-white">No companies match “{{ search() }}”</p>
                  <p class="mt-1 text-sm text-slate-500 dark:text-slate-400">Try part of the name, or the trade name.</p>
                } @else {
                  <p class="mt-3 text-sm font-medium text-slate-900 dark:text-white">No companies on your Fiscalize account yet</p>
                  <p class="mx-auto mt-1 max-w-sm text-sm text-slate-500 dark:text-slate-400">Create your company on the Fiscalize platform, or ask its administrator to add you, then sign in again.</p>
                  <a href="https://fiscalize.co.zw" target="_blank" rel="noopener" class="mt-3 inline-block text-sm font-semibold text-primary-600 hover:underline dark:text-brand-400">Open Fiscalize →</a>
                }
              </div>
            } @else {
              <ul class="space-y-1" role="list" [attr.aria-busy]="loading()">
                @for (c of items(); track c.id) {
                  <li>
                    <button type="button" (click)="choose(c)" [disabled]="choosingId() !== null" data-company
                            class="group flex w-full items-center gap-3 rounded-xl px-3 py-3 text-left transition-colors hover:bg-slate-100/80 focus:outline-none focus-visible:bg-slate-100 focus-visible:ring-2 focus-visible:ring-primary-500 disabled:cursor-wait dark:hover:bg-white/5 dark:focus-visible:bg-white/5"
                            [class.opacity-50]="choosingId() !== null && choosingId() !== c.id"
                            [attr.aria-label]="'Open ' + c.name + (c.connected ? ', Xero connected' : ', Xero not connected yet')">
                      @if (c.logo) {
                        <img [src]="c.logo" alt="" class="h-10 w-10 flex-shrink-0 rounded-xl border border-slate-200 bg-white object-contain p-1 dark:border-white/10" (error)="c.logo = null">
                      } @else {
                        <span class="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-xl text-sm font-bold"
                              [ngClass]="c.connected ? 'bg-brand-50 text-primary-700 dark:bg-primary-500/15 dark:text-brand-300' : 'bg-slate-100 text-slate-500 dark:bg-white/10 dark:text-slate-300'">{{ initials(c.name) }}</span>
                      }
                      <span class="min-w-0 flex-1">
                        <span class="flex items-center gap-2">
                          <span class="truncate text-sm font-semibold text-slate-900 dark:text-white">{{ c.name }}</span>
                          @if (c.lastUsed) { <span class="flex-shrink-0 rounded-full bg-slate-100 px-2 py-px text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:bg-white/10 dark:text-slate-400">Last used</span> }
                        </span>
                        <span class="mt-0.5 flex flex-wrap items-center gap-x-2 gap-y-0.5 text-xs text-slate-500 dark:text-slate-400">
                          @if (c.subtitle) { <span class="truncate">{{ c.subtitle }}</span><span aria-hidden="true">·</span> }
                          @if (c.needsReauth) {
                            <span class="inline-flex items-center gap-1 text-amber-600 dark:text-amber-400"><span class="h-1.5 w-1.5 rounded-full bg-amber-500"></span>Reconnect Xero</span>
                          } @else if (c.connected) {
                            <span class="inline-flex items-center gap-1 text-emerald-600 dark:text-emerald-400"><span class="h-1.5 w-1.5 rounded-full bg-emerald-500"></span>Xero connected</span>
                          } @else {
                            <span class="inline-flex items-center gap-1"><span class="h-1.5 w-1.5 rounded-full bg-slate-300 dark:bg-slate-600"></span>Not connected yet</span>
                          }
                          @if (c.role) { <span aria-hidden="true">·</span><span>{{ c.role }}</span> }
                        </span>
                      </span>
                      @if (choosingId() === c.id) {
                        <span class="h-5 w-5 flex-shrink-0 animate-spin rounded-full border-2 border-brand-200 border-t-primary-500" aria-label="Opening"></span>
                      } @else {
                        <svg class="h-5 w-5 flex-shrink-0 text-slate-300 transition-transform group-hover:translate-x-0.5 group-hover:text-slate-500 dark:text-slate-600" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m9 18 6-6-6-6"/></svg>
                      }
                    </button>
                  </li>
                }
              </ul>
              @if (hasMore()) {
                <div class="px-3 pb-1 pt-2">
                  <button type="button" (click)="loadMore()" [disabled]="loading()"
                          class="w-full rounded-xl border border-slate-200 bg-white/70 px-4 py-2.5 text-sm font-medium text-slate-700 transition-colors hover:bg-white disabled:opacity-60 dark:border-white/10 dark:bg-slate-950/60 dark:text-slate-300 dark:hover:bg-white/5">
                    {{ loading() ? 'Loading…' : 'Show more companies' }}
                  </button>
                </div>
              }
            }
          </div>

          @if (chooseError()) {
            <div class="mx-5 mb-4 rounded-xl border border-rose-200/70 bg-rose-50/80 px-3.5 py-2.5 text-sm text-rose-800 sm:mx-7 dark:border-rose-500/30 dark:bg-rose-500/10 dark:text-rose-200" role="alert">{{ chooseError() }}</div>
          }
        </div>

        <p class="mt-5 text-center text-xs text-slate-500 dark:text-slate-500">You can switch company at any time from the top bar.</p>
      </main>
    </div>
  `
})
export class SelectCompany implements OnInit {
  private companies = inject(CompaniesService);
  private auth = inject(AuthService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private destroyRef = inject(DestroyRef);

  readonly search = signal('');
  readonly items = signal<Choice[]>([]);
  readonly loading = signal(true);
  readonly searching = signal(false);
  readonly hasMore = signal(false);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly choosingId = signal<number | null>(null);
  readonly chooseError = signal<string | null>(null);
  readonly userName = computed(() => this.auth.currentUser()?.email || this.auth.currentUser()?.name || '');

  /** What this app knows per company: connection state and which one was open last. */
  private known = new Map<number, UserCompany>();
  private page = 1;
  private readonly search$ = new Subject<string>();

  ngOnInit(): void {
    this.companies.myCompanies().pipe(catchError(() => of([] as UserCompany[]))).subscribe((list) => {
      this.known = new Map((list ?? []).map((c) => [c.companyId, c]));
      this.items.update((rows) => rows.map((r) => this.withKnown(r)));
      this.fetch(true);
    });

    this.search$.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      tap(() => this.searching.set(true)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(() => this.fetch(true));
  }

  onSearch(text: string): void {
    this.search.set(text.trim().slice(0, 100));
    this.search$.next(this.search());
  }

  reload(): void { this.fetch(true); }
  loadMore(): void { this.page += 1; this.fetch(false); }

  /** Arrow-down from the search box moves to the first company. */
  focusFirst(ev: Event): void {
    const first = document.querySelector<HTMLButtonElement>('button[data-company]');
    if (first) { ev.preventDefault(); first.focus(); }
  }

  private fetchSub?: { unsubscribe(): void };

  private fetch(reset: boolean): void {
    if (reset) this.page = 1;
    this.loading.set(true);
    this.error.set(null);
    this.fetchSub?.unsubscribe();
    const term = this.search();
    this.fetchSub = this.companies.myFiscalyzeCompanies(term, PAGE_SIZE, this.page).subscribe({
      next: (rows) => {
        const mapped = (rows ?? []).map((r) => this.fromPlatform(r));
        const merged = reset ? mapped : [...this.items(), ...mapped.filter((m) => !this.items().some((i) => i.id === m.id))];
        // The company used last first, then the rest in the platform's order.
        this.items.set(reset && !term ? [...merged.filter((m) => m.lastUsed), ...merged.filter((m) => !m.lastUsed)] : merged);
        this.hasMore.set((rows ?? []).length >= PAGE_SIZE);
        this.notice.set(null);
        this.done();
      },
      error: (err) => {
        // The platform list can't be read: offer the companies this app already knows, filtered locally.
        const local = [...this.known.values()]
          .filter((c) => !term || c.companyName.toLowerCase().includes(term.toLowerCase()))
          .map((c) => this.fromLocal(c));
        if (local.length) {
          this.items.set(local);
          this.hasMore.set(false);
          this.notice.set('Showing the companies you have used here before. ' + extractErrorMessage(err, 'The full list from Fiscalize is unavailable right now.'));
        } else {
          this.items.set([]);
          this.error.set(extractErrorMessage(err, 'The Fiscalize platform did not return your companies.'));
        }
        this.done();
      }
    });
  }

  private done(): void { this.loading.set(false); this.searching.set(false); }

  private fromPlatform(r: FiscalyzeCompany): Choice {
    const name = r.friendlyName || r.name;
    const subtitle = [r.tradeName, r.friendlyName ? r.name : null].find((s) => !!s && s !== name) ?? null;
    return this.withKnown({ id: r.id, name, subtitle, logo: r.logo || null, role: r.role || null, connected: false, needsReauth: false, lastUsed: false });
  }

  private fromLocal(c: UserCompany): Choice {
    return this.withKnown({ id: c.companyId, name: c.companyName, subtitle: null, logo: null, role: null, connected: false, needsReauth: false, lastUsed: false });
  }

  private withKnown(c: Choice): Choice {
    const k = this.known.get(c.id);
    return { ...c, connected: !!k?.isSetupComplete, needsReauth: !!k?.needsReauth, lastUsed: !!k?.isActive };
  }

  choose(c: Choice): void {
    if (this.choosingId() !== null) return;
    this.choosingId.set(c.id);
    this.chooseError.set(null);
    this.companies.switch(c.id).subscribe({
      next: (res) => {
        this.auth.persistSession(res);
        const org = res.xeroOrganisation;
        const ready = !!org && org.isSetupComplete !== false;
        const back = this.route.snapshot.queryParamMap.get('returnUrl');
        const safeBack = back && back.startsWith('/') && !back.startsWith('//') && !/^\/(login|select-company|redirect-url)/.test(back) ? back : null;
        const target = ready ? (safeBack ?? '/dashboard') : '/register-client';
        // A full load, so every part of the app starts on the chosen company's session.
        window.location.assign(target);
      },
      error: (err) => {
        this.choosingId.set(null);
        this.chooseError.set(extractErrorMessage(err, `Could not open ${c.name}. Please try again.`));
      }
    });
  }

  signOut(): void { this.auth.logout(); }

  initials(name: string): string {
    const words = (name || '').replace(/\([^)]*\)/g, ' ').split(/[\s.,]+/)
      .filter((w) => w && !/^(pvt|private|ltd|limited|inc|llc|plc|co|company|t\/a|and|&|the|of)$/i.test(w));
    if (!words.length) return (name || '?').trim().charAt(0).toUpperCase() || '?';
    return (words[0][0] + (words.length > 1 ? words[1][0] : '')).toUpperCase();
  }
}
