import { Component, HostListener, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ClientConfigService } from '../../core/services/xero/client-config.service';
import { AuthService } from '../../core/services/auth/auth.service';
import { XeroAppConfig, UserCompany } from '../../core/models/xero.models';
import { CompaniesService, FiscalyzeCompany } from '../../core/services/xero/companies.service';
import { extractErrorMessage } from '../../core/utils/http-error';
import { FiscalizeLogo } from '../../components/branding/fiscalize-logo/fiscalize-logo';
import { ToastService } from '../../core/services/toast/toast.service';

@Component({
  selector: 'app-register-client',
  imports: [CommonModule, FormsModule, FiscalizeLogo],
  template: `
    <div class="relative min-h-screen overflow-hidden bg-gradient-to-br from-slate-50 via-white to-primary-50/40 dark:from-slate-950 dark:via-slate-950 dark:to-black">
      <div class="bg-blob teal"    style="top: -160px; right: -120px; width: 480px; height: 480px;"></div>
      <div class="bg-blob emerald" style="bottom: -180px; left: -100px; width: 460px; height: 460px;"></div>

      <div class="relative z-10 mx-auto min-h-screen w-full max-w-3xl px-4 py-10 sm:px-6 lg:py-14">

        <!-- Top utility bar -->
        <div class="mb-6 flex items-center justify-between gap-2 fade-in-down">
          <div *ngIf="user()" class="hidden min-w-0 items-center gap-2 sm:flex">
            <span class="flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-primary-500 to-primary-600 text-[11px] font-bold text-white shadow-sm">
              {{ initialsOf(user()?.name || user()?.email || 'U') }}
            </span>
            <div class="min-w-0">
              <div class="truncate text-xs font-semibold text-slate-900 dark:text-white">{{ user()?.name || user()?.email }}</div>
              <div class="truncate text-[10px] text-slate-500 dark:text-slate-400">{{ user()?.email }}</div>
            </div>
          </div>
          <div class="flex items-center gap-2 sm:ml-auto">
            <button type="button" (click)="goHome()"
                    class="inline-flex items-center gap-1.5 rounded-xl border border-slate-200 bg-white/80 px-3 py-2 text-xs font-medium text-slate-700 transition-all duration-200 hover:bg-white hover:scale-[1.03] dark:border-white/10 dark:bg-slate-900/60 dark:text-slate-200 dark:hover:bg-slate-900">
              <svg class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 9l9-7 9 7v11a2 2 0 01-2 2h-4a2 2 0 01-2-2v-4a2 2 0 00-2-2h-2a2 2 0 00-2 2v4a2 2 0 01-2 2H5a2 2 0 01-2-2z"/></svg>
              Home
            </button>
            <button type="button" (click)="signOut()"
                    class="inline-flex items-center gap-1.5 rounded-xl border border-rose-200 bg-white/80 px-3 py-2 text-xs font-medium text-rose-600 transition-all duration-200 hover:bg-rose-50 hover:scale-[1.03] dark:border-rose-500/30 dark:bg-rose-500/10 dark:text-rose-300 dark:hover:bg-rose-500/15">
              <svg class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 21H5a2 2 0 01-2-2V5a2 2 0 012-2h4M16 17l5-5-5-5M21 12H9"/></svg>
              Sign out
            </button>
          </div>
        </div>

        <!-- Brand -->
        <div class="mb-8 flex flex-col items-center gap-2 fade-in-down">
          <app-fiscalize-logo></app-fiscalize-logo>
          <p class="text-center text-sm text-slate-500 dark:text-slate-400">Connect Xero in two steps.</p>
        </div>

        <div *ngIf="disconnectedName() as name" role="status"
             class="mb-6 flex items-start gap-3 rounded-2xl border border-emerald-200 bg-emerald-50/80 p-4 text-sm text-emerald-800 fade-in-down dark:border-emerald-500/30 dark:bg-emerald-500/10 dark:text-emerald-200">
          <svg class="mt-0.5 h-5 w-5 flex-shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 11.08V12a10 10 0 11-5.93-9.14"/><path d="M22 4L12 14.01l-3-3"/></svg>
          <div class="flex-1"><strong>{{ name }}</strong> was disconnected from Xero. Connect again below, or switch to another company.</div>
          <button type="button" (click)="disconnectedName.set(null)" aria-label="Dismiss" class="rounded-md p-1 text-emerald-700/70 hover:bg-emerald-100 hover:text-emerald-900 dark:text-emerald-300/70 dark:hover:bg-emerald-500/15">
            <svg class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M18 6L6 18M6 6l12 12"/></svg>
          </button>
        </div>


        <!-- Already-connected branch -->
        <div *ngIf="activeCompanyAlreadySetup()"
             class="rounded-3xl border border-emerald-200 bg-white p-8 shadow-xl shadow-slate-900/[0.04] scale-in dark:border-primary-600/30 dark:bg-slate-900 dark:shadow-black/30">
          <div class="flex items-start gap-4">
            <div class="flex h-12 w-12 flex-shrink-0 items-center justify-center rounded-2xl bg-gradient-to-br from-primary-600 to-primary-500 shadow-lg shadow-primary-600/30">
              <svg class="h-6 w-6 text-white" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M5 13l4 4L19 7"/></svg>
            </div>
            <div class="min-w-0 flex-1">
              <h2 class="text-lg font-bold text-slate-900 dark:text-white">{{ activeCompanyName() }} is connected</h2>
              <p class="mt-1 text-sm text-slate-600 dark:text-slate-300">
                Xero is connected<ng-container *ngIf="activeCompany()?.isUsingSharedConnection"> by a colleague</ng-container>.
                Everyone in this company fiscalises through the same connection.
              </p>
              <div class="mt-4 flex flex-wrap gap-2">
                <button type="button" (click)="goToDashboard()"
                        class="inline-flex items-center gap-1.5 rounded-xl bg-gradient-to-r from-primary-500 to-primary-600 px-4 py-2 text-sm font-semibold text-white shadow-md shadow-primary-500/25 transition-all duration-200 hover:from-primary-600 hover:to-primary-700 hover:scale-[1.02] active:scale-[0.98]">
                  Open dashboard
                  <svg class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14M13 5l7 7-7 7"/></svg>
                </button>
                <button type="button" (click)="goToConnection()"
                        class="inline-flex items-center rounded-xl border border-slate-200 bg-white px-4 py-2 text-sm font-medium text-slate-700 transition-all duration-200 hover:bg-slate-50 dark:border-white/10 dark:bg-slate-800 dark:text-slate-200 dark:hover:bg-slate-700">
                  Manage connection
                </button>
                <a *ngIf="activeCompany()?.xeroUrl as url" [href]="url" target="_blank" rel="noopener"
                   class="inline-flex items-center gap-1.5 rounded-xl border border-emerald-300 bg-white px-4 py-2 text-sm font-medium text-emerald-700 transition-all duration-200 hover:bg-emerald-50 dark:border-primary-600/40 dark:bg-primary-600/10 dark:text-emerald-200 dark:hover:bg-primary-600/15">
                  Open Xero
                  <svg class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 13v6a2 2 0 01-2 2H5a2 2 0 01-2-2V8a2 2 0 012-2h6M15 3h6v6M10 14L21 3"/></svg>
                </a>
              </div>
              <button *ngIf="hasOtherCompanies()" type="button" (click)="forceWizard.set(true)"
                      class="mt-4 inline-flex items-center gap-1 text-xs font-semibold text-primary-600 hover:underline dark:text-primary-400">
                <svg class="h-3 w-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M16 17l5-5-5-5M21 12H9M9 7H7a2 2 0 00-2 2v6a2 2 0 002 2h2"/></svg>
                Switch to another company
              </button>
            </div>
          </div>
        </div>

        <!-- Wizard card -->
        <div *ngIf="!activeCompanyAlreadySetup()"
             class="rounded-3xl border border-white/40 bg-white/95 p-6 shadow-xl shadow-slate-900/[0.04] backdrop-blur-md scale-in dark:border-white/10 dark:bg-slate-900/90 dark:shadow-black/30 sm:p-8">


          <!-- Card header -->
          <div class="mb-5 flex items-start gap-4">
            <div class="flex h-11 w-11 flex-shrink-0 items-center justify-center rounded-2xl bg-gradient-to-br from-primary-500 to-primary-600 shadow-md shadow-primary-500/25">
              <svg class="h-5 w-5 text-white" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10 13a5 5 0 007.54.54l3-3a5 5 0 00-7.07-7.07l-1.72 1.71"/><path d="M14 11a5 5 0 00-7.54-.54l-3 3a5 5 0 007.07 7.07l1.71-1.71"/></svg>
            </div>
            <div class="min-w-0 flex-1">
              <div class="text-[11px] font-semibold uppercase tracking-wider text-primary-600 dark:text-primary-400">{{ needsReconnect() ? 'Reconnect' : 'Two steps' }}</div>
              <h2 class="mt-0.5 text-xl font-bold text-slate-900 dark:text-white sm:text-2xl">{{ needsReconnect() ? 'Reconnect to Xero' : 'Connect to Xero' }}</h2>
              <p class="mt-1 text-sm text-slate-600 dark:text-slate-300">Confirm the Fiscalize company, then sign in to Xero and choose the organisation to fiscalise.</p>
            </div>
          </div>

          <div class="mb-2 flex items-center gap-2 text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">
            <span class="flex h-5 w-5 items-center justify-center rounded-full bg-primary-500 text-[10px] font-bold text-white">1</span>
            Fiscalize company
          </div>

          <!-- ──────────── Company ──────────── -->
          <div class="relative z-20 space-y-5 fade-in-down">
            <div class="relative" data-rc-dropdown style="z-index: 50; isolation: isolate;">
              <button type="button" (click)="toggleDropdown()" [disabled]="switchingId() != null || loadingCompanies() || !hasOtherCompanies()"
                      [attr.aria-label]="hasOtherCompanies() ? 'Switch Fiscalize company' : 'Active Fiscalize company'"
                      [attr.aria-expanded]="dropdownOpen()"
                      class="group flex w-full items-center gap-4 rounded-2xl border border-primary-200/70 bg-gradient-to-br from-primary-50 to-primary-50/40 p-5 text-left transition-all duration-200 enabled:hover:scale-[1.005] enabled:hover:border-primary-300 enabled:hover:shadow-md disabled:cursor-default dark:border-primary-500/30 dark:from-primary-500/10 dark:to-primary-600/5 dark:enabled:hover:border-primary-400/60">
                <div class="flex h-12 w-12 flex-shrink-0 items-center justify-center rounded-xl bg-gradient-to-br from-primary-500 to-primary-600 text-base font-bold text-white shadow-md">
                  {{ initialsOf(activeCompanyName()) }}
                </div>
                <div class="min-w-0 flex-1">
                  <div class="text-[10px] font-semibold uppercase tracking-wider text-primary-700 dark:text-primary-300">Working as</div>
                  <div class="mt-0.5 truncate text-base font-bold text-slate-900 dark:text-white">{{ activeCompanyName() }}</div>
                  <div *ngIf="hasOtherCompanies()" class="mt-1 inline-flex items-center gap-1 text-[11px] font-semibold text-primary-600 dark:text-primary-400">
                    <svg class="h-3 w-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M16 17l5-5-5-5M21 12H9M9 7H7a2 2 0 00-2 2v6a2 2 0 002 2h2"/></svg>
                    Switch company
                  </div>
                  <div *ngIf="!hasOtherCompanies() && !loadingCompanies()" class="mt-1 text-[11px] text-slate-500 dark:text-slate-400">Only company in your account</div>
                </div>
                <svg *ngIf="!loadingCompanies() && hasOtherCompanies()" class="h-5 w-5 flex-shrink-0 text-slate-400 transition-transform" [class.rotate-180]="dropdownOpen()" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M6 9l6 6 6-6"/></svg>
                <svg *ngIf="loadingCompanies()" class="h-5 w-5 flex-shrink-0 animate-spin text-primary-500" viewBox="0 0 24 24" fill="none"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
              </button>

              <div *ngIf="dropdownOpen()"
                   class="fade-in-down absolute left-0 right-0 z-[100] mt-2 overflow-hidden rounded-xl border border-slate-200 bg-white shadow-2xl ring-1 ring-slate-900/10 dark:border-white/10 dark:bg-slate-900 dark:ring-white/10">
                <div class="border-b border-slate-100 p-2 dark:border-white/5">
                  <div class="relative">
                    <svg class="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-slate-400" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg>
                    <input id="rc-company-search" type="text" [ngModel]="companySearch()" (ngModelChange)="companySearch.set($event)"
                           placeholder="Search companies…" autocomplete="off" spellcheck="false"
                           class="w-full rounded-lg border border-slate-200 bg-white py-2 pl-8 pr-7 text-sm outline-none focus:border-primary-500 focus:ring-2 focus:ring-primary-500/20 dark:border-white/10 dark:bg-slate-950 dark:text-white" />
                    <button *ngIf="companySearch()" type="button" (click)="companySearch.set('')" aria-label="Clear search"
                            class="absolute right-1.5 top-1/2 -translate-y-1/2 rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700 dark:hover:bg-white/5 dark:hover:text-slate-200">
                      <svg class="h-3 w-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M18 6L6 18M6 6l12 12"/></svg>
                    </button>
                  </div>
                </div>

                <div class="max-h-72 overflow-y-auto py-1">
                  <div *ngIf="filteredCompanies().length === 0" class="px-3 py-5 text-center text-xs text-slate-500 dark:text-slate-400">
                    {{ companySearch() ? 'No companies match "' + companySearch() + '"' : 'No other companies in your account' }}
                  </div>
                  <button *ngFor="let c of filteredCompanies()" type="button" (click)="switchTo(c)"
                          [disabled]="c.isActive || switchingId() != null"
                          [class]="'group flex w-full items-center gap-3 px-3 py-2.5 text-left transition-colors duration-150 disabled:cursor-default ' +
                            (c.isActive ? 'bg-primary-50/70 dark:bg-primary-500/10' : 'hover:bg-slate-50 disabled:opacity-60 dark:hover:bg-white/5')">
                    <div [class]="'flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-lg text-[11px] font-bold text-white ' +
                            (c.isActive ? 'bg-gradient-to-br from-primary-500 to-primary-600' : 'bg-gradient-to-br from-slate-400 to-slate-500')">
                      {{ initialsOf(c.companyName) }}
                    </div>
                    <div class="min-w-0 flex-1">
                      <div class="flex items-center gap-2">
                        <div class="truncate text-sm font-semibold text-slate-900 dark:text-white">{{ c.companyName }}</div>
                        <span *ngIf="c.isActive" class="rounded-full bg-primary-100 px-1.5 py-0.5 text-[9px] font-bold uppercase text-primary-700 dark:bg-primary-500/20 dark:text-primary-300">Current</span>
                      </div>
                      <div class="mt-0.5">
                        <span class="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider"
                              [ngClass]="statusOf(c) === 'connected' ? 'bg-emerald-100 text-emerald-700 dark:bg-primary-600/15 dark:text-emerald-300' : statusOf(c) === 'reauth' ? 'bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300' : 'bg-slate-100 text-slate-600 dark:bg-white/10 dark:text-slate-300'">
                          <span class="h-1 w-1 rounded-full" [ngClass]="statusOf(c) === 'connected' ? 'bg-primary-600' : statusOf(c) === 'reauth' ? 'bg-amber-500' : 'bg-slate-400'"></span>
                          {{ statusOf(c) === 'connected' ? 'Connected' : statusOf(c) === 'reauth' ? 'Re-auth needed' : 'Not connected' }}
                        </span>
                      </div>
                    </div>
                    <svg *ngIf="c.isActive" class="h-4 w-4 flex-shrink-0 text-primary-600 dark:text-primary-400" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M5 13l4 4L19 7"/></svg>
                    <svg *ngIf="!c.isActive && switchingId() === c.companyId" class="h-4 w-4 flex-shrink-0 animate-spin text-primary-500" viewBox="0 0 24 24" fill="none"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
                    <svg *ngIf="!c.isActive && switchingId() == null" class="h-4 w-4 flex-shrink-0 text-slate-300 transition-colors group-hover:text-primary-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 5l7 7-7 7"/></svg>
                  </button>
                </div>
              </div>
            </div>

            <div *ngIf="companiesNote() as note" role="status" data-testid="companies-note"
                 class="flex flex-wrap items-center gap-x-3 gap-y-1 rounded-xl border border-amber-200 bg-amber-50 px-4 py-2.5 text-xs text-amber-800 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-200">
              <span class="flex-1">{{ note }} Only companies already used here can be chosen.</span>
              <button type="button" (click)="loadCompanies()" class="font-semibold underline-offset-2 hover:underline">Try again</button>
            </div>

            <div class="rounded-xl border border-slate-200 bg-slate-50/60 p-4 text-sm dark:border-white/10 dark:bg-slate-950/40">
              <div class="flex items-start gap-2">
                <svg class="mt-0.5 h-4 w-4 flex-shrink-0 text-primary-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4M12 8h.01"/></svg>
                <div class="text-slate-700 dark:text-slate-300">
                  <strong class="text-slate-900 dark:text-white">What is the Fiscalize company?</strong>
                  This is the entity inside Fiscalize you're connecting to Xero. Approved sales invoices and credit notes from the Xero organisation you choose are fiscalised under this company's tax registration and ZIMRA device.
                </div>
              </div>
            </div>
          </div>

          <!-- ──────────── Xero ──────────── -->
          <div class="mt-6 space-y-4 fade-in-down">
            <div class="flex items-center gap-2 text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">
              <span class="flex h-5 w-5 items-center justify-center rounded-full bg-primary-500 text-[10px] font-bold text-white">2</span>
              Xero organisation
            </div>

            <div *ngIf="needsReconnect() as problem" role="status" data-testid="reconnect-reason"
                 class="rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-200">
              {{ problem }}
            </div>

            <div class="rounded-xl border border-primary-200/70 bg-primary-50/70 p-4 text-sm dark:border-primary-500/30 dark:bg-primary-500/10">
              <div class="flex items-start gap-2 text-primary-800 dark:text-primary-200">
                <svg class="mt-0.5 h-4 w-4 flex-shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z"/></svg>
                <div>
                  <strong>Connect to Xero</strong> takes you to Xero's own sign-in. Choose <strong>one organisation</strong> on the
                  consent screen and click <strong>Allow access</strong>. Xero then brings you straight back here.
                </div>
              </div>
            </div>

            <ul class="grid gap-2 text-xs text-slate-600 dark:text-slate-300 sm:grid-cols-2">
              <li class="flex items-start gap-2 rounded-lg border border-slate-200 bg-white px-3 py-2 dark:border-white/10 dark:bg-slate-950/40">
                <svg class="mt-0.5 h-3.5 w-3.5 flex-shrink-0 text-primary-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M5 13l4 4L19 7"/></svg>
                Reads approved sales invoices and credit notes, contacts, items and tax rates.
              </li>
              <li class="flex items-start gap-2 rounded-lg border border-slate-200 bg-white px-3 py-2 dark:border-white/10 dark:bg-slate-950/40">
                <svg class="mt-0.5 h-3.5 w-3.5 flex-shrink-0 text-primary-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M5 13l4 4L19 7"/></svg>
                Writes the fiscal result back as a history note and an attached PDF. Amounts are never changed.
              </li>
            </ul>

            <div *ngIf="appConfig() && !appConfig()!.configured" role="alert" data-testid="not-configured"
                 class="rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-200">
              This server has not been given its Xero app credentials yet, so connecting is not possible. Ask your Fiscalize administrator to finish the server setup.
            </div>

            <div *ngIf="error()" role="alert" class="rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700 dark:border-rose-500/30 dark:bg-rose-500/10 dark:text-rose-300">
              {{ error() }}
            </div>
          </div>

          <!-- ─────── Footer ─────── -->
          <div class="mt-7 flex items-center justify-between gap-3 border-t border-slate-100 pt-5 dark:border-white/5">
            <span class="hidden items-center gap-1.5 text-[11px] text-slate-500 dark:text-slate-400 sm:inline-flex">
              <svg class="h-3.5 w-3.5 text-primary-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>
              One connection, shared with your colleagues
            </span>
            <button type="button" (click)="connect()" [disabled]="busy() || !canConnect()" data-testid="connect-xero"
                    class="group inline-flex w-full items-center justify-center gap-2 rounded-xl bg-gradient-to-r from-primary-500 to-primary-600 px-6 py-2.5 text-sm font-semibold text-white shadow-md shadow-primary-500/25 transition-all duration-200 hover:from-primary-600 hover:to-primary-700 hover:scale-[1.02] active:scale-[0.98] disabled:cursor-not-allowed disabled:opacity-50 disabled:hover:scale-100 sm:w-auto">
              <svg *ngIf="busy()" class="h-4 w-4 animate-spin" viewBox="0 0 24 24" fill="none"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
              {{ busy() ? 'Opening Xero…' : (needsReconnect() ? 'Reconnect to Xero' : 'Connect to Xero') }}
              <svg *ngIf="!busy()" class="h-4 w-4 transition-transform group-hover:translate-x-0.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14M13 5l7 7-7 7"/></svg>
            </button>
          </div>
        </div>


        <div class="mt-5 flex flex-col items-center gap-2">
          <p class="text-center text-[11px] text-slate-500 dark:text-slate-400">
            You sign in on Xero's own site. Fiscalize never sees your Xero password.
          </p>
          <button type="button" (click)="openFiscalize()" [disabled]="openingFiscalize()"
                  class="inline-flex items-center gap-1.5 text-[11px] font-medium text-primary-600 transition-colors hover:text-primary-700 disabled:opacity-60 dark:text-primary-400 dark:hover:text-primary-300">
            <svg *ngIf="!openingFiscalize()" class="h-3 w-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M15 19l-7-7 7-7"/></svg>
            <svg *ngIf="openingFiscalize()" class="h-3 w-3 animate-spin" viewBox="0 0 24 24" fill="none"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
            {{ openingFiscalize() ? 'Opening Fiscalize…' : 'Back to Fiscalize' }}
          </button>
          <p *ngIf="fiscalizeError()" class="text-center text-[11px] text-rose-600 dark:text-rose-400">{{ fiscalizeError() }}</p>
        </div>
      </div>
    </div>
  `
})
export class RegisterClient implements OnInit {
  private clientConfigService = inject(ClientConfigService);
  private authService = inject(AuthService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private toast = inject(ToastService);
  private companiesService = inject(CompaniesService);

  readonly user = this.authService.currentUser;
  readonly appConfig = signal<XeroAppConfig | null>(null);
  readonly companies = signal<UserCompany[]>([]);
  readonly loadingCompanies = signal(true);
  /** Why the full company list from the Fiscalize platform is missing, or null. */
  readonly companiesNote = signal<string | null>(null);
  readonly switchingId = signal<number | null>(null);
  readonly disconnectedName = signal<string | null>(null);
  readonly dropdownOpen = signal(false);
  readonly companySearch = signal('');
  readonly openingFiscalize = signal(false);
  readonly fiscalizeError = signal<string | null>(null);
  /** Show the connect card even though the active company is connected (to switch to another company). */
  readonly forceWizard = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  readonly activeCompany = computed(() => {
    const u = this.user();
    const list = this.companies();
    return list.find((c) => c.isActive) ?? list.find((c) => c.companyId === u?.companyId) ?? null;
  });
  readonly activeCompanyName = computed(() => this.activeCompany()?.companyName || this.user()?.company || 'Your company');
  readonly hasOtherCompanies = computed(() => this.companies().some((c) => !c.isActive));

  readonly filteredCompanies = computed(() => {
    const term = this.companySearch().toLowerCase().trim();
    const list = this.companies();
    return term ? list.filter((c) => c.companyName.toLowerCase().includes(term)) : list;
  });

  /** Why the active company's existing connection must be authorised again, or null. */
  readonly needsReconnect = computed(() => {
    const active = this.activeCompany();
    return active?.needsReauth ? (active.connectionProblem || 'The Xero connection for this company has stopped working. Connect again to carry on.') : null;
  });

  /** A connected, healthy active company gets the "already connected" card instead of the connect card. */
  activeCompanyAlreadySetup(): boolean {
    if (this.forceWizard()) return false;
    const active = this.activeCompany();
    if (active) return active.isSetupComplete && !active.needsReauth;
    return this.loadingCompanies() ? false : !!this.user()?.xeroSetupComplete;
  }

  statusOf(c: UserCompany): 'connected' | 'reauth' | 'none' {
    if (c.needsReauth) return 'reauth';
    return c.isSetupComplete ? 'connected' : 'none';
  }

  ngOnInit(): void {
    this.disconnectedName.set(this.route.snapshot.queryParamMap.get('disconnected'));
    this.loadCompanies();
    this.clientConfigService.getXeroAppConfig().subscribe({
      next: (cfg) => this.appConfig.set(cfg),
      error: () => {} // non-fatal: the server says what is missing when the connection is started
    });
  }

  canConnect(): boolean {
    return this.appConfig()?.configured !== false && this.switchingId() == null;
  }

  connect(): void {
    if (this.busy() || !this.canConnect()) return;
    this.busy.set(true);
    this.error.set(null);
    this.clientConfigService.register().subscribe({
      next: (res) => {
        if (res.authUrl) window.location.href = res.authUrl;
        else { this.busy.set(false); this.error.set('Xero did not return a sign-in address. Try again.'); }
      },
      error: (err) => {
        this.busy.set(false);
        this.error.set(extractErrorMessage(err, 'Could not start the Xero sign-in.'));
      }
    });
  }


  // ─── Company list ──────────────────────────────────────────────────────────
  /**
   * Every company the user belongs to on the Fiscalize platform, with this app's connection status laid over it.
   * This app's own rows only cover companies already used here, so on their own a user could never pick a company
   * they haven't opened in Fiscalize for Xero yet (switching creates that row once the platform confirms access).
   * If the platform list can't be had, the local rows are still shown so connecting keeps working.
   */
  loadCompanies(): void {
    this.loadingCompanies.set(true);
    this.companiesNote.set(null);
    this.companiesService.myCompanies().subscribe({
      next: (local) => { this.companies.set(local ?? []); this.loadPlatformCompanies(local ?? []); },
      error: () => this.loadPlatformCompanies([])
    });
  }

  private loadPlatformCompanies(local: UserCompany[]): void {
    this.companiesService.myFiscalyzeCompanies().subscribe({
      next: (platform) => {
        if (platform?.length) this.companies.set(this.mergeCompanies(platform, local));
        this.loadingCompanies.set(false);
      },
      error: (err) => {
        this.loadingCompanies.set(false);
        this.companiesNote.set(err?.status === 424
          ? 'Your Fiscalize sign-in has expired, so your other companies can\'t be listed. Sign out and in again to see them.'
          : extractErrorMessage(err, 'Could not load your company list from Fiscalize.'));
      }
    });
  }

  private mergeCompanies(platform: FiscalyzeCompany[], local: UserCompany[]): UserCompany[] {
    const activeId = this.user()?.companyId;
    const byId = new Map(local.map((c) => [c.companyId, c] as const));
    const merged = [...platform]
      .sort((a, b) => (b.accessCount ?? 0) - (a.accessCount ?? 0))
      .map((f): UserCompany => {
        const known = byId.get(f.id);
        return {
          ...(known ?? { id: 0, isSetupComplete: false, isUsingSharedConnection: false, needsReauth: false }),
          companyId: f.id,
          companyName: known?.companyName || f.name || f.friendlyName || f.tradeName || 'Untitled company',
          isActive: activeId != null ? f.id === activeId : !!known?.isActive
        };
      });
    // A company this app knows that the platform didn't list (access removed, or beyond the page): keep it visible.
    const extra = local.filter((c) => !platform.some((f) => f.id === c.companyId));
    return [...merged, ...extra];
  }

  // ─── Company switcher ──────────────────────────────────────────────────────
  toggleDropdown(): void {
    if (this.switchingId() != null || !this.hasOtherCompanies()) return;
    this.dropdownOpen.update((v) => !v);
    if (this.dropdownOpen()) {
      this.companySearch.set('');
      setTimeout(() => (document.getElementById('rc-company-search') as HTMLInputElement | null)?.focus(), 0);
    }
  }

  /** Switch to another company; a connected one lands on its dashboard, an unconnected one reloads this page. */
  switchTo(c: UserCompany): void {
    if (this.switchingId() !== null || c.isActive) return;
    this.switchingId.set(c.companyId);
    this.companiesService.switch(c.companyId).subscribe({
      next: (res) => {
        this.authService.persistSession(res);
        window.location.href = c.isSetupComplete && !c.needsReauth ? '/dashboard' : '/register-client';
      },
      error: (err) => {
        this.switchingId.set(null);
        this.toast.error(extractErrorMessage(err, `Could not switch to ${c.companyName}.`));
      }
    });
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(ev: MouseEvent): void {
    if (!(ev.target as HTMLElement).closest('[data-rc-dropdown]')) this.dropdownOpen.set(false);
  }

  @HostListener('document:keydown.escape')
  onEscape(): void { this.dropdownOpen.set(false); }
  // ─── Navigation ────────────────────────────────────────────────────────────
  goToDashboard(): void { this.router.navigate(['/dashboard']); }
  goToConnection(): void { this.router.navigate(['/xero-connection']); }

  goHome(): void {
    if (this.activeCompanyAlreadySetup()) this.router.navigate(['/dashboard']);
    else this.router.navigate(['/']);
  }

  /** Return to the Fiscalize platform through a one-time launch URL minted by the API. */
  openFiscalize(): void {
    if (this.openingFiscalize()) return;
    this.openingFiscalize.set(true);
    this.fiscalizeError.set(null);
    this.authService.openFiscalize().subscribe({
      next: (result) => {
        this.openingFiscalize.set(false);
        if (result?.launchUrl) window.location.href = result.launchUrl;
      },
      error: (err) => {
        this.openingFiscalize.set(false);
        this.fiscalizeError.set(extractErrorMessage(err, 'Could not open Fiscalize.'));
        setTimeout(() => this.fiscalizeError.set(null), 4000);
      }
    });
  }

  signOut(): void { this.authService.logout(); }

  initialsOf(s: string): string {
    return (s || '').split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]?.toUpperCase()).join('') || '?';
  }
}
