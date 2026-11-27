import { Component, ElementRef, EventEmitter, HostListener, Input, OnInit, Output, ViewChild, computed, effect, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth/auth.service';
import { ThemeService } from '../../core/services/theme/theme.service';
import { LocalService } from '../../core/services/local/local.service';
import { CompaniesService, FiscalyzeCompany } from '../../core/services/xero/companies.service';
import { SessionService } from '../../core/services/session/session.service';
import { ClientConfigService } from '../../core/services/xero/client-config.service';
import { RunningVersion, VersionService } from '../../core/services/version/version.service';
import { OpenFiscalize } from '../open-fiscalize/open-fiscalize';
import { OpenXero } from '../open-xero/open-xero';

interface CompanyRow {
  id: number;
  name: string;
  friendlyName?: string | null;
  logo?: string | null;
  role?: string;
  isActive: boolean;
  accessCount: number;
}

@Component({
  selector: 'app-nav-bar',
  imports: [CommonModule, FormsModule, RouterLink, OpenFiscalize, OpenXero],
  templateUrl: './nav-bar.html',
})
export class NavBar implements OnInit {
  private authService = inject(AuthService);
  private themeService = inject(ThemeService);
  private localService = inject(LocalService);
  private companiesService = inject(CompaniesService);
  private clientConfigService = inject(ClientConfigService);
  private router = inject(Router);
  private sessionService = inject(SessionService);
  readonly versions = inject(VersionService);

  @Input() onToggleSidebar?: () => void;
  @Input() collapsed = false;
  @Output() toggleSidebar = new EventEmitter<void>();

  readonly showProfileDropdown = signal(false);
  readonly showCompanyDropdown = signal(false);
  readonly companies = signal<CompanyRow[]>([]);
  readonly loadingCompanies = signal(false);
  readonly companiesError = signal<string | null>(null);
  /** The platform session is gone (424 from the company proxy): offer a fresh sign-in rather than a retry. */
  readonly platformSignInNeeded = signal(false);
  readonly switchingId = signal<number | null>(null);
  readonly switchError = signal<string | null>(null);
  readonly orgSearch = signal('');
  readonly reauthDismissed = signal(false);

  @ViewChild('orgSearchInput') orgSearchInput?: ElementRef<HTMLInputElement>;

  user = this.authService.currentUser;
  theme = this.themeService.theme;

  readonly activeCompany = computed(() => {
    const u = this.user();
    if (!u?.companyId) return null;
    const known = this.companies().find((c) => c.id === u.companyId);
    if (known) return known;
    // The session always knows which company it was issued for; never show "Select company" for a signed-in user.
    return u.company ? { id: u.companyId, name: u.company, isActive: true, accessCount: 0 } as CompanyRow : null;
  });

  readonly otherCompanies = computed(() => {
    const u = this.user();
    const term = this.orgSearch().toLowerCase().trim();
    return this.companies()
      .filter((c) => c.id !== u?.companyId)
      .filter((c) => !term || c.name.toLowerCase().includes(term) || (c.friendlyName ?? '').toLowerCase().includes(term) || (c.role ?? '').toLowerCase().includes(term));
  });

  readonly hasNoOtherCompanies = computed(() => {
    const u = this.user();
    return this.companies().filter((c) => c.id !== u?.companyId).length === 0;
  });

  readonly needsReauth = computed(() => false);

  readonly initials = computed(() => {
    const name = this.user()?.name || this.user()?.email || 'U';
    return name.split(/\s+/).map((p) => p[0]).filter(Boolean).slice(0, 2).join('').toUpperCase();
  });

  constructor() {
    effect(() => {
      if (this.showCompanyDropdown()) {
        this.switchError.set(null);
        this.orgSearch.set('');
        setTimeout(() => this.orgSearchInput?.nativeElement?.focus(), 0);
      }
    });
  }

  ngOnInit(): void { this.loadCompanies(); }

  /**
   * Two sources, so the switcher always has something to show: the companies this app already holds (fast, never
   * depends on the Fiscalize platform), then the full list from the platform, which adds logos, roles and companies
   * not yet opened here. A platform failure leaves the local list in place and explains itself in the dropdown.
   */
  private loadCompanies(): void {
    if (!this.localService.getToken()) return;
    this.loadingCompanies.set(true);
    this.companiesError.set(null);
    this.companiesService.myCompanies().subscribe({
      next: (local) => {
        const rows = (local ?? []).map((c) => ({ id: c.companyId, name: c.companyName, friendlyName: null, logo: null, role: c.isSetupComplete ? 'Connected' : 'Not connected', isActive: true, accessCount: c.isActive ? 1 : 0 } as CompanyRow));
        if (rows.length) this.companies.set(rows);
        this.loadPlatformCompanies();
      },
      error: () => this.loadPlatformCompanies()
    });
  }

  private loadPlatformCompanies(): void {
    this.companiesService.myFiscalyzeCompanies().subscribe({
      next: (rows) => {
        const platform = (rows ?? []).map((f) => this.toRow(f)).sort((a, b) => b.accessCount - a.accessCount);
        if (platform.length) {
          const extra = this.companies().filter((c) => !platform.some((p) => p.id === c.id));
          this.companies.set([...platform, ...extra]);
        }
        this.loadingCompanies.set(false);
      },
      error: (err) => {
        this.loadingCompanies.set(false);
        // Keep the local list usable; only say why the full platform list is missing.
        this.platformSignInNeeded.set(err?.status === 424);
        this.companiesError.set(err?.error?.message || err?.message || 'Could not load the full company list from Fiscalize.');
      }
    });
  }

  retryLoadCompanies(): void { this.loadCompanies(); }

  /** Hands over to the session-expired flow: back to login, returning to this page afterwards. */
  signInAgain(): void { this.showCompanyDropdown.set(false); this.sessionService.dispatchExpired(); }

  private toRow(f: FiscalyzeCompany): CompanyRow {
    return {
      id: f.id,
      name: f.name,
      friendlyName: f.friendlyName ?? f.tradeName ?? null,
      logo: f.logo,
      role: f.role,
      isActive: f.isActive,
      accessCount: f.accessCount ?? 0
    };
  }

  statusFor(c: CompanyRow): { label: string; cls: string; dot: string } {
    return c.isActive
      ? { label: 'Active', cls: 'bg-emerald-100 text-emerald-700 dark:bg-primary-600/15 dark:text-emerald-300', dot: 'bg-primary-600' }
      : { label: 'Inactive', cls: 'bg-slate-100 text-slate-600 dark:bg-white/10 dark:text-slate-300', dot: 'bg-slate-400' };
  }

  private readonly brokenLogos = signal<Set<number>>(new Set());

  logoUrl(c: CompanyRow): string | null {
    if (!c.logo) return null;
    if (this.brokenLogos().has(c.id)) return null;
    const trimmed = c.logo.trim();
    if (!trimmed) return null;
    if (trimmed.startsWith('http://') || trimmed.startsWith('https://') || trimmed.startsWith('data:')) return trimmed;
    return null;
  }

  onLogoError(c: CompanyRow): void {
    this.brokenLogos.update((set) => {
      if (set.has(c.id)) return set;
      const next = new Set(set); next.add(c.id); return next;
    });
  }

  initialsFor(c: CompanyRow): string {
    const n = c.friendlyName || c.name || '?';
    return n.split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]?.toUpperCase()).join('') || '?';
  }

  onSidebarToggle(): void { this.toggleSidebar.emit(); this.onToggleSidebar?.(); }
  toggleTheme(): void { this.themeService.toggle(); }

  toggleProfile(): void {
    this.showProfileDropdown.update((v) => !v);
    this.showCompanyDropdown.set(false);
    if (this.showProfileDropdown()) this.versions.load();
  }

  /** "Commit 76e4408027c7, built 25 Nov 2026 09:18" for the version line's tooltip. */
  versionTitle(label: string, v: RunningVersion | null): string {
    if (!v) return '';
    const when = v.builtAt ? new Date(v.builtAt).toLocaleString() : null;
    return [`${label} ${v.version}`, v.commit ? `commit ${v.commit}` : null, when ? `built ${when}` : null,
            v.startedAt ? `running since ${new Date(v.startedAt).toLocaleString()}` : null].filter(Boolean).join(' · ');
  }
  toggleCompany(): void { this.showCompanyDropdown.update((v) => !v); this.showProfileDropdown.set(false); }

  onSearchInput(value: string): void { this.orgSearch.set(value); }

  private static readonly DETAIL_SEGMENTS = new Set(['invoices', 'credit-notes']);

  selectCompany(c: CompanyRow): void {
    if (c.id === this.user()?.companyId) { this.showCompanyDropdown.set(false); return; }
    this.switchingId.set(c.id);
    this.switchError.set(null);
    this.companiesService.switch(c.id).subscribe({
      next: (res) => {
        this.authService.persistSession(res);
        this.switchingId.set(null);
        this.showCompanyDropdown.set(false);
        const parts = this.router.url.replace(/^\//, '').split('/');
        const onDetailPage = parts.length >= 2 && NavBar.DETAIL_SEGMENTS.has(parts[0]);
        if (onDetailPage) window.location.href = `/${parts[0]}`;
        else window.location.reload();
      },
      error: (err) => {
        this.switchingId.set(null);
        this.switchError.set(err?.error?.message || err?.message || `Could not switch to ${c.friendlyName || c.name}.`);
      }
    });
  }

  reauthActive(): void {
    this.reauthDismissed.set(true);
    this.clientConfigService.reauth().subscribe({
      next: (res) => { if (res?.authUrl) window.location.href = res.authUrl; }
    });
  }

  dismissReauth(): void { this.reauthDismissed.set(true); }

  logout(): void { this.authService.logout(); }

  signOutEverywhere(): void {
    this.showProfileDropdown.set(false);
    this.authService.signOutEverywhere().subscribe();
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    const target = event.target as HTMLElement;
    if (!target.closest('[data-nav-dropdown]')) {
      this.showProfileDropdown.set(false);
      this.showCompanyDropdown.set(false);
    }
  }
}
