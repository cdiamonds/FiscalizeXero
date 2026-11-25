import { Component, DestroyRef, ElementRef, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { interval } from 'rxjs';
import { AuthService } from '../../core/services/auth/auth.service';
import { DashboardService, DashboardSummary } from '../../core/services/xero/dashboard.service';
import { failureGuide, relativeTime, absoluteTime } from '../../core/utils/fiscal-failure';

interface Bar {
  date: string;
  label: string;
  invoices: number;
  creditNotes: number;
  x: number;
  invH: number;
  cnH: number;
}

const GUTTER = 28;
const GAP = 2;

@Component({
  selector: 'app-dashboard',
  imports: [CommonModule, RouterLink, DatePipe],
  template: `
    <div class="space-y-5">
      <!-- ── Header ─────────────────────────────────────────────────────── -->
      <header class="flex flex-col gap-4 fade-in-down sm:flex-row sm:items-end sm:justify-between">
        <div class="min-w-0">
          <p class="text-sm font-medium text-slate-500 dark:text-slate-400">{{ greeting() }}</p>
          <h1 class="mt-0.5 text-2xl leading-tight [overflow-wrap:anywhere] sm:truncate font-bold tracking-tight text-slate-900 dark:text-white sm:text-3xl">
            {{ data()?.connection?.tenantName || user()?.tenantName || user()?.company || 'Dashboard' }}
          </h1>
          <p class="mt-1 text-sm text-slate-600 dark:text-slate-300">ZIMRA fiscalisation for your Xero invoices and credit notes.</p>
        </div>
        <div class="flex shrink-0 items-center gap-2">
          <button type="button" (click)="load()" [disabled]="loading()" aria-label="Refresh"
                  class="inline-flex h-10 items-center gap-2 rounded-xl border border-slate-200 bg-white px-3.5 text-sm font-semibold text-slate-700 shadow-sm transition hover:bg-slate-50 disabled:opacity-50 focus:outline-none focus:ring-2 focus:ring-primary-500 dark:border-white/10 dark:bg-slate-900 dark:text-slate-200 dark:hover:bg-white/5">
            <svg class="h-4 w-4" [class.animate-spin]="loading()" fill="none" stroke="currentColor" stroke-width="2" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"/></svg>
            <span class="hidden sm:inline">Refresh</span>
          </button>
          <a routerLink="/invoices"
             class="inline-flex h-10 items-center gap-2 rounded-xl bg-[var(--brand-solid)] px-4 text-sm font-semibold text-white shadow-sm transition hover:bg-[var(--brand-solid-hover)] focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-2 dark:focus:ring-offset-slate-900">
            Invoices
            <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14M13 5l7 7-7 7"/></svg>
          </a>
        </div>
      </header>

      @if (error() && !data()) {
        <div class="rounded-2xl border border-rose-200 bg-rose-50 p-5 text-sm text-rose-800 dark:border-rose-500/30 dark:bg-rose-500/10 dark:text-rose-200" role="alert">
          <p class="font-semibold">The dashboard could not be loaded.</p>
          <p class="mt-1">{{ error() }}</p>
          <button type="button" (click)="load()" class="mt-3 font-semibold underline underline-offset-2">Try again</button>
        </div>
      }

      @if (!data() && !error()) {
        <!-- Skeleton keeps the layout still while the first answer arrives. -->
        <div class="grid grid-cols-2 gap-3 sm:gap-4 lg:grid-cols-4" aria-hidden="true">
          @for (i of [1, 2, 3, 4]; track i) { <div class="h-[104px] animate-pulse rounded-2xl bg-slate-200/60 dark:bg-white/5"></div> }
        </div>
        <div class="grid gap-4 lg:grid-cols-3" aria-hidden="true">
          <div class="h-72 animate-pulse rounded-2xl bg-slate-200/60 dark:bg-white/5 lg:col-span-2"></div>
          <div class="h-72 animate-pulse rounded-2xl bg-slate-200/60 dark:bg-white/5"></div>
        </div>
      }

      @if (data(); as d) {
        <!-- ── What needs doing, only when something does ─────────────────── -->
        @if (setupSteps(); as steps) {
          @if (steps.remaining > 0) {
            <section class="rounded-2xl border border-slate-200/70 bg-white p-5 shadow-sm fade-in-down dark:border-white/10 dark:bg-slate-900 sm:p-6">
              <div class="flex flex-col gap-1 sm:flex-row sm:items-baseline sm:justify-between">
                <h2 class="text-base font-semibold text-slate-900 dark:text-white">Finish setting up</h2>
                <p class="text-xs font-medium text-slate-500 dark:text-slate-400">{{ 3 - steps.remaining }} of 3 done</p>
              </div>
              <ol class="mt-4 grid gap-3 md:grid-cols-3">
                @for (s of steps.items; track s.title; let i = $index) {
                  <li>
                    <a [routerLink]="s.url"
                       class="group flex h-full gap-3 rounded-xl border p-4 transition hover:border-primary-300 hover:bg-slate-50 dark:hover:bg-white/5"
                       [class]="s.done ? 'border-slate-200/70 dark:border-white/5' : 'border-primary-200 bg-primary-50/40 dark:border-primary-500/30 dark:bg-primary-500/5'">
                      <span class="flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-xs font-bold"
                            [class]="s.done ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300' : 'bg-[var(--brand-solid)] text-white'">
                        @if (s.done) { <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3"><path stroke-linecap="round" stroke-linejoin="round" d="M5 13l4 4L19 7"/></svg> } @else { {{ i + 1 }} }
                      </span>
                      <span class="min-w-0">
                        <span class="block text-sm font-semibold text-slate-900 dark:text-white" [class.line-through]="s.done" [class.opacity-60]="s.done">{{ s.title }}</span>
                        <span class="mt-0.5 block text-xs text-slate-500 dark:text-slate-400">{{ s.description }}</span>
                      </span>
                    </a>
                  </li>
                }
              </ol>
            </section>
          }
        }

        @if (d.failureCount > 0) {
          <button type="button" (click)="scrollToAttention()"
             class="flex w-full items-center gap-3 rounded-2xl border border-amber-200 text-left bg-amber-50 px-4 py-3 text-sm text-amber-900 transition hover:bg-amber-100/70 fade-in-down dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-100 dark:hover:bg-amber-500/15">
            <svg class="h-5 w-5 shrink-0 text-amber-600 dark:text-amber-300" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path stroke-linecap="round" stroke-linejoin="round" d="M12 9v4m0 4h.01M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"/></svg>
            <span class="min-w-0 flex-1"><span class="font-semibold">{{ d.failureCount }} {{ d.failureCount === 1 ? 'document needs' : 'documents need' }} attention.</span>
              <span class="hidden sm:inline"> Automatic retries continue; the list below says what to fix.</span></span>
          </button>
        }

        <!-- ── Headline figures ────────────────────────────────────────── -->
        <div class="grid grid-cols-2 gap-3 fade-in-down sm:gap-4 lg:grid-cols-4">
          <div class="dash-tile">
            <p class="dash-tile-label">Fiscalised today</p>
            <p class="dash-tile-value">{{ d.counts.today }}</p>
            <p class="dash-tile-note">{{ d.recent.length ? 'Last ' + ago(d.recent[0].fiscalizedAt) : 'No receipts yet' }}</p>
          </div>
          <div class="dash-tile">
            <p class="dash-tile-label">Last 7 days</p>
            <p class="dash-tile-value">{{ d.counts.last7Days }}</p>
            <p class="dash-tile-note">{{ d.counts.total }} since connecting</p>
          </div>
          <div class="dash-tile">
            <p class="dash-tile-label">This month</p>
            <p class="dash-tile-value">{{ d.counts.thisMonth }}</p>
            <p class="dash-tile-note">{{ d.counts.invoicesThisMonth }} invoice{{ d.counts.invoicesThisMonth === 1 ? '' : 's' }} · {{ d.counts.creditNotesThisMonth }} credit note{{ d.counts.creditNotesThisMonth === 1 ? '' : 's' }}</p>
          </div>
          <div class="dash-tile" [class.dash-tile-warn]="d.failureCount > 0">
            <p class="dash-tile-label">Needs attention</p>
            <p class="dash-tile-value">{{ d.failureCount }}</p>
            <p class="dash-tile-note">{{ d.failureCount ? 'Waiting for a fix or a retry' : 'Nothing has failed' }}</p>
          </div>
        </div>

        <!-- ── Activity + automation health ────────────────────────────── -->
        <div class="grid items-stretch gap-4 lg:grid-cols-3">
          <section class="dash-card flex flex-col lg:col-span-2" aria-labelledby="activity-h">
            <div class="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h2 id="activity-h" class="dash-card-title">Receipts issued</h2>
                <p class="dash-card-sub">Last 14 days, Zimbabwe time</p>
              </div>
              <ul class="flex items-center gap-4 text-xs text-slate-600 dark:text-slate-300" aria-label="Legend">
                <li class="flex items-center gap-1.5"><span class="h-2.5 w-2.5 rounded-sm bg-[#0d9488]"></span>Invoices</li>
                <li class="flex items-center gap-1.5"><span class="h-2.5 w-2.5 rounded-sm bg-[#7c3aed] dark:bg-[#8b5cf6]"></span>Credit notes</li>
              </ul>
            </div>

            <div #chartBox class="relative mt-4 flex-1" (mouseleave)="hover.set(null)">
              <svg [attr.viewBox]="'0 0 ' + chartW + ' ' + (chartH + 22)" [attr.width]="chartW" [attr.height]="chartH + 22" class="block max-w-full overflow-visible" role="img"
                   [attr.aria-label]="'Receipts issued per day over the last 14 days, ' + chartTotal() + ' in total'">
                <!-- recessive grid: baseline, midline, top -->
                @for (g of grid(); track g.y) {
                  <line [attr.x1]="gutter" [attr.x2]="chartW" [attr.y1]="g.y" [attr.y2]="g.y"
                        class="stroke-slate-200 dark:stroke-white/10" [attr.stroke-dasharray]="g.base ? null : '3 4'" stroke-width="1" />
                  <text [attr.x]="gutter - 8" [attr.y]="g.y + 3.5" text-anchor="end" class="fill-slate-400 text-[10px] tabular-nums">{{ g.v }}</text>
                }
                @for (b of bars(); track b.date; let i = $index) {
                  <g [class.opacity-40]="hover() !== null && hover() !== i" class="transition-opacity">
                    @if (b.invH > 0) {
                      <path [attr.d]="barPath(b.x, chartH - b.invH, b.invH, b.cnH === 0)" fill="#0d9488" />
                    }
                    @if (b.cnH > 0) {
                      <path [attr.d]="barPath(b.x, chartH - b.invH - (b.invH > 0 ? gap : 0) - b.cnH, b.cnH, true)" class="fill-[#7c3aed] dark:fill-[#8b5cf6]" />
                    }
                  </g>
                  @if ((13 - i) % 2 === 0) {
                    <text [attr.x]="b.x + barW / 2" [attr.y]="chartH + 16" text-anchor="middle"
                          class="fill-slate-400 text-[10px]" [class.max-sm:hidden]="(13 - i) % 4 !== 0">{{ b.label }}</text>
                  }
                  <!-- hit target wider and taller than the bar -->
                  <rect [attr.x]="b.x - (slot - barW) / 2" y="0" [attr.width]="slot" [attr.height]="chartH" fill="transparent"
                        (mouseenter)="hover.set(i)" (focus)="hover.set(i)" (blur)="hover.set(null)" tabindex="0"
                        [attr.aria-label]="b.label + ': ' + b.invoices + ' invoices, ' + b.creditNotes + ' credit notes'" />
                }
              </svg>

              @if (hover() !== null) {
                @if (bars()[hover()!]; as b) {
                  <div class="pointer-events-none absolute top-0 z-10 -translate-x-1/2 rounded-lg border border-slate-200 bg-white px-3 py-2 text-xs shadow-lg dark:border-white/10 dark:bg-slate-800"
                       [style.left.%]="((b.x + barW / 2) / chartW) * 100">
                    <p class="font-semibold text-slate-900 dark:text-white">{{ b.date | date:'EEE d MMM' }}</p>
                    <p class="mt-1 flex items-center gap-1.5 text-slate-600 dark:text-slate-300"><span class="h-2 w-2 rounded-sm bg-[#0d9488]"></span>{{ b.invoices }} invoice{{ b.invoices === 1 ? '' : 's' }}</p>
                    <p class="flex items-center gap-1.5 text-slate-600 dark:text-slate-300"><span class="h-2 w-2 rounded-sm bg-[#7c3aed] dark:bg-[#8b5cf6]"></span>{{ b.creditNotes }} credit note{{ b.creditNotes === 1 ? '' : 's' }}</p>
                  </div>
                }
              }

              @if (chartTotal() === 0) {
                <div class="absolute inset-0 flex items-center justify-center pb-6">
                  <p class="rounded-lg bg-white/90 px-3 py-1.5 text-sm text-slate-500 dark:bg-slate-900/90 dark:text-slate-400">No receipts in the last 14 days</p>
                </div>
              }

              <table class="sr-only">
                <caption>Receipts issued per day</caption>
                <thead><tr><th>Date</th><th>Invoices</th><th>Credit notes</th></tr></thead>
                <tbody>@for (b of bars(); track b.date) { <tr><td>{{ b.date }}</td><td>{{ b.invoices }}</td><td>{{ b.creditNotes }}</td></tr> }</tbody>
              </table>
            </div>
          </section>

          <section class="dash-card flex flex-col" aria-labelledby="auto-h">
            <div class="flex items-start justify-between gap-3">
              <div>
                <h2 id="auto-h" class="dash-card-title">Automatic fiscalisation</h2>
                <p class="dash-card-sub">{{ autoSummary() }}</p>
              </div>
              <span class="dash-pill" [class]="autoOn() ? 'dash-pill-good' : 'dash-pill-off'">
                <span class="h-1.5 w-1.5 rounded-full" [class]="autoOn() ? 'bg-emerald-500' : 'bg-slate-400'"></span>
                {{ autoOn() ? 'On' : 'Off' }}
              </span>
            </div>
            <ul class="mt-4 flex-1 divide-y divide-slate-100 dark:divide-white/5">
              <li>
                <a routerLink="/xero-connection" class="dash-health-row">
                  <span class="dash-dot" [class]="d.connection.connected ? 'bg-emerald-500' : 'bg-rose-500'"></span>
                  <span class="dash-health-label">Xero</span>
                  <span class="dash-health-value">{{ d.connection.connected ? 'Connected' : 'Not connected' }}</span>
                </a>
              </li>
              <li>
                <a routerLink="/device-setup" class="dash-health-row">
                  <span class="dash-dot" [class]="d.device ? 'bg-emerald-500' : 'bg-amber-400'"></span>
                  <span class="dash-health-label">Fiscal device</span>
                  <span class="dash-health-value">
                    @if (d.device) {
                      #{{ d.device.deviceNumber }}
                      @if (d.device.environment === 'test') { <span class="ml-1 rounded bg-amber-100 px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-amber-800 dark:bg-amber-500/15 dark:text-amber-200">Test</span> }
                    } @else { Not set up }
                  </span>
                </a>
              </li>
              <li>
                <a routerLink="/fiscalization-settings" class="dash-health-row">
                  <span class="dash-dot" [class]="d.connection.autoFiscalizeInvoices ? 'bg-emerald-500' : 'bg-slate-300 dark:bg-slate-600'"></span>
                  <span class="dash-health-label">Invoices</span>
                  <span class="dash-health-value">{{ d.connection.autoFiscalizeInvoices ? 'Automatic' : 'Manual' }}</span>
                </a>
              </li>
              <li>
                <a routerLink="/fiscalization-settings" class="dash-health-row">
                  <span class="dash-dot" [class]="d.connection.autoFiscalizeCreditNotes ? 'bg-emerald-500' : 'bg-slate-300 dark:bg-slate-600'"></span>
                  <span class="dash-health-label">Credit notes</span>
                  <span class="dash-health-value">{{ d.connection.autoFiscalizeCreditNotes ? 'Automatic' : 'Manual' }}</span>
                </a>
              </li>
              <li>
                <a routerLink="/webhooks" class="dash-health-row">
                  <span class="dash-dot" [class]="d.connection.webhooksConfigured ? 'bg-emerald-500' : 'bg-amber-400'"></span>
                  <span class="dash-health-label">Instant (webhooks)</span>
                  <span class="dash-health-value" [title]="abs(d.connection.lastWebhookAt)">{{ d.connection.webhooksConfigured ? (d.connection.lastWebhookAt ? ago(d.connection.lastWebhookAt) : 'Ready') : 'Needs setup' }}</span>
                </a>
              </li>
              <li>
                <a routerLink="/webhooks" class="dash-health-row">
                  <span class="dash-dot" [class]="d.connection.syncEnabled && d.connection.lastSyncAt && !d.connection.lastSyncError ? 'bg-emerald-500' : 'bg-amber-400'"></span>
                  <span class="dash-health-label">Safety-net check</span>
                  <span class="dash-health-value" [title]="abs(d.connection.lastSyncAt)">{{ !d.connection.syncEnabled ? 'Off' : d.connection.lastSyncError ? 'Retrying' : d.connection.lastSyncAt ? ago(d.connection.lastSyncAt) : 'Pending' }}</span>
                </a>
              </li>
            </ul>
          </section>
        </div>

        <!-- ── Needs attention: only when something does, full width so nothing is left half-empty ── -->
        @if (d.failures.length) {
          <section id="attention" class="dash-card scroll-mt-24 !p-0" aria-labelledby="attn-h">
            <div class="flex items-center justify-between gap-3 px-5 pt-5 sm:px-6">
              <div>
                <h2 id="attn-h" class="dash-card-title">Needs attention</h2>
                <p class="dash-card-sub">Documents that could not be fiscalised. Open one to see how to fix it.</p>
              </div>
              @if (d.failureCount > d.failures.length) {
                <span class="hidden text-xs text-slate-500 dark:text-slate-400 sm:inline">Latest {{ d.failures.length }} of {{ d.failureCount }}</span>
              }
            </div>
            <ul class="mt-3 divide-y divide-slate-100 border-t border-slate-100 dark:divide-white/5 dark:border-white/5">
              @for (f of d.failures; track f.documentId) {
                <li>
                  <a [routerLink]="docLink(f.documentType, f.documentId)" class="flex flex-col gap-1.5 px-5 py-3 transition hover:bg-amber-50/50 dark:hover:bg-white/5 sm:flex-row sm:items-center sm:gap-3 sm:px-6">
                    <span class="w-fit shrink-0 rounded-md bg-amber-100 px-2 py-0.5 text-center sm:w-32 text-[11px] font-semibold text-amber-800 dark:bg-amber-500/15 dark:text-amber-200 sm:mt-0">{{ guide(f.category, f.message) }}</span>
                    <span class="min-w-0 flex-1">
                      <span class="line-clamp-2 block text-sm text-slate-800 dark:text-slate-100 sm:line-clamp-1">{{ f.message }}</span>
                      <span class="mt-0.5 block text-xs text-slate-500 dark:text-slate-400">{{ f.documentType === 'creditnote' ? 'Credit note' : 'Invoice' }} · {{ f.attempts }} attempt{{ f.attempts === 1 ? '' : 's' }} · {{ ago(f.lastFailedAt) }}</span>
                    </span>
                    <svg class="hidden h-4 w-4 shrink-0 text-slate-400 sm:block" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path stroke-linecap="round" stroke-linejoin="round" d="M9 5l7 7-7 7"/></svg>
                  </a>
                </li>
              }
            </ul>
          </section>
        }

        <!-- ── Latest receipts ─────────────────────────────────────────── -->
        <section class="dash-card !p-0" aria-labelledby="recent-h">
          <div class="flex items-center justify-between gap-3 px-5 pt-5 sm:px-6">
            <div>
              <h2 id="recent-h" class="dash-card-title">Latest receipts</h2>
              <p class="dash-card-sub">Documents fiscalised with ZIMRA, newest first</p>
            </div>
            <a routerLink="/invoices" class="shrink-0 text-sm font-semibold text-[var(--brand-ink)] hover:underline">All invoices</a>
          </div>
          @if (d.recent.length) {
            <table class="mt-3 w-full text-sm">
              <thead class="border-y border-slate-100 bg-slate-50/60 text-left text-[11px] font-semibold uppercase tracking-wider text-slate-500 dark:border-white/5 dark:bg-white/[0.02] dark:text-slate-400">
                <tr>
                  <th scope="col" class="px-5 py-2.5 font-semibold sm:px-6">Document</th>
                  <th scope="col" class="hidden px-3 py-2.5 font-semibold sm:table-cell">Type</th>
                  <th scope="col" class="hidden px-3 py-2.5 font-semibold md:table-cell">Receipt</th>
                  <th scope="col" class="hidden px-3 py-2.5 font-semibold lg:table-cell">Verification code</th>
                  <th scope="col" class="px-5 py-2.5 text-right font-semibold sm:px-6">Fiscalised</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-slate-100 dark:divide-white/5">
                @for (r of d.recent; track r.documentId) {
                  <tr class="group cursor-pointer transition hover:bg-slate-50 dark:hover:bg-white/5" (click)="open(r.documentType, r.documentId)">
                    <td class="px-5 py-3 sm:px-6">
                      <a [routerLink]="docLink(r.documentType, r.documentId)" class="flex items-center gap-3 focus:outline-none" (click)="$event.stopPropagation()">
                        <span class="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg"
                              [class]="r.documentType === 'creditnote' ? 'bg-violet-50 text-violet-700 dark:bg-violet-500/10 dark:text-violet-300' : 'bg-teal-50 text-teal-700 dark:bg-teal-500/10 dark:text-teal-300'">
                          <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path [attr.d]="r.documentType === 'creditnote' ? creditIcon : invoiceIcon"/></svg>
                        </span>
                        <span class="min-w-0">
                          <span class="block truncate font-semibold text-slate-900 group-hover:text-[var(--brand-ink)] dark:text-white">{{ r.documentNumber }}</span>
                          <span class="block text-xs text-slate-500 dark:text-slate-400 sm:hidden">{{ r.documentType === 'creditnote' ? 'Credit note' : 'Invoice' }}</span>
                        </span>
                      </a>
                    </td>
                    <td class="hidden px-3 py-3 text-slate-600 dark:text-slate-300 sm:table-cell">{{ r.documentType === 'creditnote' ? 'Credit note' : 'Invoice' }}</td>
                    <td class="hidden px-3 py-3 tabular-nums text-slate-700 dark:text-slate-200 md:table-cell">{{ r.receiptNumber ?? 'Pending' }}</td>
                    <td class="hidden px-3 py-3 font-mono text-xs text-slate-500 dark:text-slate-400 lg:table-cell">{{ r.verificationCode || '—' }}</td>
                    <td class="px-5 py-3 text-right sm:px-6">
                      <span class="block text-xs font-medium text-slate-700 dark:text-slate-200" [title]="abs(r.fiscalizedAt)">{{ ago(r.fiscalizedAt) }}</span>
                      <span class="block text-[11px] text-slate-400">{{ r.fiscalizedAt | date:'d MMM, HH:mm' }}</span>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          } @else {
            <div class="flex flex-col items-center justify-center px-6 py-12 text-center">
              <p class="text-sm font-semibold text-slate-900 dark:text-white">No receipts yet</p>
              <p class="mt-1 max-w-sm text-sm text-slate-500 dark:text-slate-400">Approve an invoice in Xero, or fiscalise one from the Invoices page, and it will show here.</p>
            </div>
          }
        </section>
      }
    </div>
  `,
})
export class Dashboard implements OnInit {
  private auth = inject(AuthService);
  private api = inject(DashboardService);
  private destroyRef = inject(DestroyRef);
  private router = inject(Router);

  readonly user = this.auth.currentUser;
  readonly data = signal<DashboardSummary | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly hover = signal<number | null>(null);
  private readonly now = signal(Date.now());

  /** The chart is drawn at the container's real pixel width so its labels stay legible on any screen. */
  private readonly width = signal(640);
  get chartW(): number { return this.width(); }
  get chartH(): number { return this.width() < 480 ? 170 : 236; }
  get slot(): number { return (this.width() - GUTTER) / 14; }
  get barW(): number { return Math.round(Math.min(26, Math.max(8, this.slot * 0.58))); }
  readonly gap = GAP;
  @ViewChild('chartBox') set chartBox(el: ElementRef<HTMLElement> | undefined) {
    this.resize?.disconnect();
    if (!el || typeof ResizeObserver === 'undefined') return;
    this.resize = new ResizeObserver(([e]) => { const w = Math.floor(e.contentRect.width); if (w > 0 && w !== this.width()) this.width.set(w); });
    this.resize.observe(el.nativeElement);
  }
  private resize?: ResizeObserver;
  readonly gutter = GUTTER;
  readonly invoiceIcon = 'M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z';
  readonly creditIcon = 'M9 14l-4-4 4-4M5 10h11a4 4 0 010 8h-1';

  readonly greeting = computed(() => {
    const h = new Date(this.now()).getHours();
    const first = (this.user()?.name ?? '').trim().split(/\s+/)[0];
    const part = h < 12 ? 'Good morning' : h < 17 ? 'Good afternoon' : 'Good evening';
    return first ? `${part}, ${first}` : part;
  });

  readonly autoOn = computed(() => {
    const c = this.data()?.connection;
    return !!c && c.connected && !!this.data()?.device && (c.autoFiscalizeInvoices || c.autoFiscalizeCreditNotes);
  });

  readonly autoSummary = computed(() => {
    const d = this.data();
    if (!d) return '';
    if (!d.connection.connected) return 'Connect Xero to start.';
    if (!d.device) return 'Set up a fiscal device to start.';
    const c = d.connection;
    if (!c.autoFiscalizeInvoices && !c.autoFiscalizeCreditNotes) return 'Documents are fiscalised only when you click.';
    const what = c.autoFiscalizeInvoices && c.autoFiscalizeCreditNotes ? 'invoices and credit notes'
      : c.autoFiscalizeInvoices ? 'invoices' : 'credit notes';
    return `Approved ${what} are fiscalised without a click.`;
  });

  readonly setupSteps = computed(() => {
    const d = this.data();
    if (!d) return null;
    const items = [
      { title: 'Connect Xero', description: 'Link the organisation whose invoices you fiscalise.', url: '/xero-connection', done: d.connection.connected },
      { title: 'Set up the fiscal device', description: 'Add the ZIMRA device that issues the receipts.', url: '/device-setup', done: !!d.device },
      { title: 'Fiscalise a first document', description: 'Approve an invoice in Xero, or fiscalise one here.', url: '/invoices', done: d.counts.total > 0 },
    ];
    return { items, remaining: items.filter((i) => !i.done).length };
  });

  readonly bars = computed<Bar[]>(() => {
    const days = this.data()?.daily ?? [];
    const max = this.scaleMax();
    return days.map((d, i) => {
      const x = GUTTER + i * this.slot + (this.slot - this.barW) / 2;
      const scale = (n: number) => (n === 0 ? 0 : Math.max(3, (n / max) * (this.chartH - 8)));
      const dt = new Date(d.date + 'T00:00:00');
      return {
        date: d.date, invoices: d.invoices, creditNotes: d.creditNotes, x,
        label: dt.toLocaleDateString(undefined, { day: 'numeric', month: 'short' }),
        invH: scale(d.invoices), cnH: scale(d.creditNotes),
      };
    });
  });

  readonly chartTotal = computed(() => (this.data()?.daily ?? []).reduce((s, d) => s + d.invoices + d.creditNotes, 0));

  /** A round top value so the gridline labels read as whole numbers. */
  private readonly scaleMax = computed(() => {
    const peak = Math.max(0, ...(this.data()?.daily ?? []).map((d) => d.invoices + d.creditNotes));
    if (peak <= 4) return 4;
    const step = Math.pow(10, Math.floor(Math.log10(peak)));
    const nice = [1, 2, 2.5, 5, 10].map((m) => m * step).find((v) => v >= peak) ?? peak;
    return nice % 2 === 0 ? nice : Math.ceil(nice / 2) * 2;
  });

  readonly grid = computed(() => {
    const max = this.scaleMax();
    const h = this.chartH;
    const usable = h - 8;
    return [
      { y: h, v: 0, base: true },
      { y: h - usable / 2, v: max / 2, base: false },
      { y: h - usable, v: max, base: false },
    ];
  });

  constructor() { this.destroyRef.onDestroy(() => this.resize?.disconnect()); }

  ngOnInit(): void {
    this.load();
    // Keep the figures current while the page is open and visible; the endpoint never calls Xero.
    interval(60_000).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.now.set(Date.now());
      if (document.visibilityState === 'visible') this.load(true);
    });
  }

  load(quiet = false): void {
    if (!quiet) this.loading.set(true);
    this.api.summary().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (d) => { this.data.set(d); this.error.set(null); this.loading.set(false); this.now.set(Date.now()); },
      error: (e) => {
        this.loading.set(false);
        if (!this.data()) this.error.set(e?.error?.message || 'Check your connection and try again.');
      },
    });
  }

  /** A bar with a 4px rounded top when it is the topmost segment, square otherwise. */
  barPath(x: number, y: number, h: number, roundTop: boolean): string {
    const w = this.barW;
    const r = roundTop ? Math.min(4, h, w / 2) : 0;
    return `M${x},${y + h} V${y + r} Q${x},${y} ${x + r},${y} H${x + w - r} Q${x + w},${y} ${x + w},${y + r} V${y + h} Z`;
  }

  open(type: string, id: string): void { this.router.navigate(this.docLink(type, id)); }

  docLink(type: string, id: string): string[] {
    return [type === 'creditnote' ? '/credit-notes' : '/invoices', id];
  }

  scrollToAttention(): void { document.getElementById('attention')?.scrollIntoView({ behavior: 'smooth', block: 'start' }); }

  guide(category: string, message: string): string { return failureGuide(category, message).label; }
  ago(iso: string | null | undefined): string { this.now(); return relativeTime(iso); }
  abs(iso: string | null | undefined): string { return absoluteTime(iso); }
}
