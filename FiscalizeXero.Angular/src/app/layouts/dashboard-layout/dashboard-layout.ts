import { Component, HostListener, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { NavBar } from '../../components/nav-bar/nav-bar';
import { FiscalizeLogo } from '../../components/branding/fiscalize-logo/fiscalize-logo';
import { LocalService } from '../../core/services/local/local.service';
import { CompaniesService } from '../../core/services/xero/companies.service';
import { SIDEBAR_GROUPS, SidebarGroup } from './sidebar-config';

@Component({
  selector: 'app-dashboard-layout',
  imports: [CommonModule, RouterLink, RouterLinkActive, RouterOutlet, NavBar, FiscalizeLogo],
  templateUrl: './dashboard-layout.html',
})
export class DashboardLayout implements OnInit {
  private localService = inject(LocalService);
  private router = inject(Router);
  private companiesService = inject(CompaniesService);

  /** Why the active company's Xero connection needs re-authorising, or null when it is healthy. */
  readonly connectionProblem = signal<string | null>(null);

  readonly groups: SidebarGroup[] = SIDEBAR_GROUPS.filter((g) => !g.hidden);
  readonly sidebarCollapsed = signal(false);
  readonly isMobile = signal(false);
  readonly mobileMenuOpen = signal(false);

  ngOnInit(): void {
    this.checkConnection();
    this.checkScreenSize();
    if (!this.isMobile()) {
      this.sidebarCollapsed.set(this.localService.getSidebarCollapsed());
    }
  }

  @HostListener('window:resize')
  onResize(): void {
    const wasMobile = this.isMobile();
    this.checkScreenSize();
    if (wasMobile && !this.isMobile()) this.mobileMenuOpen.set(false);
    if (!wasMobile && this.isMobile()) this.mobileMenuOpen.set(false);
  }

  private checkScreenSize(): void {
    if (typeof window === 'undefined') return;
    this.isMobile.set(window.innerWidth < 1024);
  }

  /** One call per page load; the API refreshes a lapsed token in passing and reports a connection it cannot refresh. */
  private checkConnection(): void {
    this.companiesService.myCompanies().subscribe({
      next: (rows) => {
        const active = (rows ?? []).find((c) => c.isActive);
        this.connectionProblem.set(active && active.needsReauth ? (active.connectionProblem || 'Xero needs to be reconnected.') : null);
      },
      error: () => this.connectionProblem.set(null)
    });
  }

  toggleSidebar(): void {
    if (this.isMobile()) {
      this.mobileMenuOpen.update((v) => !v);
    } else {
      const next = !this.sidebarCollapsed();
      this.sidebarCollapsed.set(next);
      this.localService.setSidebarCollapsed(next);
    }
  }

  closeMobileMenu(): void {
    this.mobileMenuOpen.set(false);
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.mobileMenuOpen()) this.closeMobileMenu();
  }
}
