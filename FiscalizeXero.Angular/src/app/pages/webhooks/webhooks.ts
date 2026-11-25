import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { WebhooksService } from '../../core/services/xero/webhooks.service';
import { ToastService } from '../../core/services/toast/toast.service';
import { WebhookEvent, WebhookEventStatus, WebhookStatus } from '../../core/models/xero.models';
import { copyToClipboard } from '../../core/utils/download.util';
import { extractErrorMessage } from '../../core/utils/http-error';
import { asUtc } from '../../core/utils/xero-format';
import { PageHeader } from '../../components/page-header/page-header';

const XERO_PORTAL = 'https://developer.xero.com/app/manage';
const REFRESH_EVERY_MS = 30_000;

const STATUS_STYLE: Record<WebhookEventStatus, string> = {
  Processed: 'bg-emerald-50 text-emerald-700 ring-emerald-200 dark:bg-emerald-500/10 dark:text-emerald-300 dark:ring-emerald-500/30',
  Skipped:   'bg-slate-100 text-slate-600 ring-slate-200 dark:bg-white/5 dark:text-slate-300 dark:ring-white/10',
  Failed:    'bg-rose-50 text-rose-700 ring-rose-200 dark:bg-rose-500/10 dark:text-rose-300 dark:ring-rose-500/30',
  Pending:   'bg-amber-50 text-amber-700 ring-amber-200 dark:bg-amber-500/10 dark:text-amber-300 dark:ring-amber-500/30',
};

@Component({
  selector: 'app-webhooks',
  imports: [CommonModule, RouterLink, PageHeader],
  template: `
    <app-page-header title="Webhooks" subtitle="Let Xero tell us the moment an invoice or credit note changes, so approved documents are fiscalised within about a minute.">
      <button type="button" (click)="load(true)" [disabled]="loading()"
              class="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-4 py-2 text-sm font-semibold text-slate-700 shadow-sm transition hover:bg-slate-50 active:scale-95 disabled:cursor-not-allowed disabled:opacity-50 focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-2 dark:border-white/10 dark:bg-slate-900 dark:text-slate-200 dark:hover:bg-white/5 dark:focus:ring-offset-slate-900">
        <svg class="h-4 w-4" [class.animate-spin]="loading()" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"/>
        </svg>
        Refresh
      </button>
    </app-page-header>

    <!-- ── Initial load ───────────────────────────────────────────────────── -->
    @if (loading() && !data()) {
      <div class="flex items-center justify-center gap-3 rounded-2xl border border-slate-200/70 bg-white px-6 py-14 shadow-sm dark:border-white/10 dark:bg-slate-900">
        <div class="h-6 w-6 animate-spin rounded-full border-4 border-brand-200 border-t-primary-500"></div>
        <span class="text-sm text-slate-500 dark:text-slate-400">Checking webhook status…</span>
      </div>
    }

    @if (data(); as d) {

      <!-- ── Status tiles ─────────────────────────────────────────────────── -->
      <div class="grid grid-cols-2 gap-3 fade-in-down lg:grid-cols-4 sm:gap-4">

        <div class="rounded-2xl border border-slate-200/70 bg-white p-4 shadow-sm dark:border-white/10 dark:bg-slate-900">
          <p class="text-[11px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">Listener</p>
          <div class="mt-2 flex items-center gap-2">
            <span class="relative flex h-2.5 w-2.5">
              @if (d.listenerConfigured) {
                <span class="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-60"></span>
              }
              <span class="relative inline-flex h-2.5 w-2.5 rounded-full" [class]="d.listenerConfigured ? 'bg-emerald-500' : 'bg-amber-400'"></span>
            </span>
            <p class="text-lg font-bold leading-tight text-slate-900 dark:text-white">{{ d.listenerConfigured ? 'Ready' : 'Needs setup' }}</p>
          </div>
          <p class="mt-1 text-xs text-slate-500 dark:text-slate-400">{{ d.listenerConfigured ? 'The webhooks key is on the server.' : 'The server has no webhooks key yet.' }}</p>
        </div>

        <div class="rounded-2xl border border-slate-200/70 bg-white p-4 shadow-sm dark:border-white/10 dark:bg-slate-900">
          <p class="text-[11px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">Last delivery</p>
          <p class="mt-2 text-lg font-bold leading-tight text-slate-900 dark:text-white">{{ d.lastReceivedAt ? ago(d.lastReceivedAt) : 'Never' }}</p>
          <p class="mt-1 text-xs text-slate-500 dark:text-slate-400">{{ d.lastReceivedAt ? (utc(d.lastReceivedAt) | date:'d MMM, HH:mm') : 'Nothing has arrived from Xero.' }}</p>
        </div>

        <div class="rounded-2xl border border-slate-200/70 bg-white p-4 shadow-sm dark:border-white/10 dark:bg-slate-900">
          <p class="text-[11px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">Fiscalised · 24h</p>
          <p class="mt-2 text-lg font-bold leading-tight tabular-nums text-slate-900 dark:text-white">{{ d.last24Hours.processed }}</p>
          <p class="mt-1 text-xs text-slate-500 dark:text-slate-400">{{ d.last24Hours.received }} received · {{ d.last24Hours.skipped }} skipped</p>
        </div>

        <div class="rounded-2xl border p-4 shadow-sm"
             [class]="d.last24Hours.failed > 0
               ? 'border-rose-200 bg-rose-50/60 dark:border-rose-500/30 dark:bg-rose-500/10'
               : 'border-slate-200/70 bg-white dark:border-white/10 dark:bg-slate-900'">
          <p class="text-[11px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">Needs attention</p>
          <p class="mt-2 text-lg font-bold leading-tight tabular-nums" [class]="d.last24Hours.failed > 0 ? 'text-rose-700 dark:text-rose-300' : 'text-slate-900 dark:text-white'">{{ d.last24Hours.failed }}</p>
          <p class="mt-1 text-xs text-slate-500 dark:text-slate-400">{{ d.last24Hours.failed > 0 ? 'Failed in the last 24 hours. See below.' : 'No failures in the last 24 hours.' }}</p>
        </div>
      </div>

      <!-- ── Not connected ────────────────────────────────────────────────── -->
      @if (!d.connected) {
        <div class="mt-5 flex items-start gap-3 rounded-2xl border border-amber-200 bg-amber-50/80 p-4 text-sm text-amber-900 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-100" role="status">
          <svg class="mt-0.5 h-5 w-5 flex-shrink-0 text-amber-500" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 9v2m0 4h.01M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"/></svg>
          <div>
            <p class="font-semibold">Connect Xero first.</p>
            <p class="mt-0.5">Deliveries are matched to the Xero organisation you connect. <a routerLink="/xero-connection" class="font-semibold underline">Open Xero connection</a> and come back here.</p>
          </div>
        </div>
      }

      <!-- ── Auto flags off ───────────────────────────────────────────────── -->
      @if (d.connected && offFor(d)) {
        <div class="mt-5 flex items-start gap-3 rounded-2xl border border-amber-200 bg-amber-50/80 p-4 text-sm text-amber-900 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-100" role="status">
          <svg class="mt-0.5 h-5 w-5 flex-shrink-0 text-amber-500" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"/></svg>
          <div>
            <p class="font-semibold">Some documents will be skipped.</p>
            <p class="mt-0.5">
              Auto-fiscalise is off for
              {{ offFor(d) }}.
              Webhooks for those will arrive but be recorded as skipped.
              <a routerLink="/fiscalization-settings" class="font-semibold underline">Change in Fiscalization settings</a>.
            </p>
          </div>
        </div>
      }

      <div class="mt-5 grid gap-5 lg:grid-cols-5">

        <!-- ── Setup guide ──────────────────────────────────────────────── -->
        <section class="overflow-hidden rounded-2xl border border-slate-200/70 bg-white shadow-sm fade-in-down lg:col-span-3 dark:border-white/10 dark:bg-slate-900">
          <div class="flex items-center gap-3 border-b border-slate-100 px-5 py-4 sm:px-6 dark:border-white/10">
            <div class="flex h-8 w-8 items-center justify-center rounded-lg bg-brand-50 dark:bg-primary-500/15">
              <svg class="h-4 w-4 text-primary-600 dark:text-brand-400" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2m-6 9l2 2 4-4"/></svg>
            </div>
            <div>
              <p class="text-sm font-semibold text-slate-900 dark:text-white">Set up in the Xero developer portal</p>
              <p class="text-xs text-slate-500 dark:text-slate-400">About five minutes. Done once for the server; every connected organisation then uses it.</p>
            </div>
          </div>

          <ol class="divide-y divide-slate-100 dark:divide-white/10">
            <li class="flex gap-4 px-5 py-5 sm:px-6">
              <span class="flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-full bg-primary-500 text-xs font-bold text-white">1</span>
              <div class="min-w-0 flex-1">
                <p class="text-sm font-semibold text-slate-900 dark:text-white">Open the app in the Xero developer portal</p>
                <p class="mt-1 text-sm text-slate-600 dark:text-slate-300">Sign in at developer.xero.com, open <span class="font-medium text-slate-800 dark:text-slate-100">My Apps</span>, choose the app this server uses (Fiscalize ZIMRA), then <span class="font-medium text-slate-800 dark:text-slate-100">Webhooks</span> in the left menu. This is done once for the whole server, by whoever administers it.</p>
                <a [href]="xeroPortal" target="_blank" rel="noopener"
                   class="mt-3 inline-flex items-center gap-1.5 rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-xs font-semibold text-slate-700 shadow-sm transition hover:bg-slate-50 dark:border-white/10 dark:bg-slate-800 dark:text-slate-200 dark:hover:bg-slate-700">
                  Open developer.xero.com
                </a>
              </div>
            </li>
            <li class="flex gap-4 px-5 py-5 sm:px-6">
              <span class="flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-full bg-primary-500 text-xs font-bold text-white">2</span>
              <div class="min-w-0 flex-1">
                <p class="text-sm font-semibold text-slate-900 dark:text-white">Tick the events to send</p>
                <p class="mt-1 text-sm text-slate-600 dark:text-slate-300">Under the event list, tick these two. Other events are ignored if you tick them, but each one uses up calls.</p>
                <div class="mt-3 flex flex-wrap gap-2" data-testid="event-categories">
                  @for (e of d.eventCategories; track e) {
                    <span class="inline-flex items-center gap-1.5 rounded-full border border-slate-200 bg-white px-3 py-1 text-xs font-semibold text-slate-800 shadow-sm dark:border-white/10 dark:bg-slate-800 dark:text-slate-100">
                      {{ categoryLabel(e) }}
                    </span>
                  }
                </div>
              </div>
            </li>
            <li class="flex gap-4 px-5 py-5 sm:px-6">
              <span class="flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-full bg-primary-500 text-xs font-bold text-white">3</span>
              <div class="min-w-0 flex-1">
                <p class="text-sm font-semibold text-slate-900 dark:text-white">Paste the delivery URL, then save</p>
                <p class="mt-1 text-sm text-slate-600 dark:text-slate-300">Copy this address into <span class="font-medium text-slate-800 dark:text-slate-100">Send notifications to</span> exactly as shown, and click <span class="font-medium text-slate-800 dark:text-slate-100">Save</span>.</p>
                <div class="mt-3 flex flex-col gap-2 sm:flex-row sm:items-stretch">
                  <code class="min-w-0 flex-1 overflow-x-auto whitespace-nowrap rounded-xl border border-slate-200 bg-slate-50 px-3 py-2.5 font-mono text-xs text-slate-800 dark:border-white/10 dark:bg-slate-950 dark:text-slate-100" data-testid="callback-url">{{ d.callbackUrl }}</code>
                  <button type="button" (click)="copy(d.callbackUrl, 'Delivery URL')"
                          class="inline-flex flex-shrink-0 items-center justify-center gap-1.5 rounded-xl bg-primary-500 px-3.5 py-2.5 text-xs font-semibold text-white shadow-sm transition hover:bg-primary-600 active:scale-95 focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-2 dark:focus:ring-offset-slate-900">
                    {{ copied() === 'Delivery URL' ? 'Copied' : 'Copy' }}
                  </button>
                </div>
              </div>
            </li>
            <li class="flex gap-4 px-5 py-5 sm:px-6">
              <span class="flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-full bg-primary-500 text-xs font-bold text-white">4</span>
              <div class="min-w-0 flex-1">
                <div class="flex flex-wrap items-center gap-2">
                  <p class="text-sm font-semibold text-slate-900 dark:text-white">Copy the webhooks key to the server</p>
                  @if (d.listenerConfigured) {
                    <span class="inline-flex items-center gap-1 rounded-full bg-emerald-50 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-emerald-700 ring-1 ring-emerald-200 dark:bg-emerald-500/10 dark:text-emerald-300 dark:ring-emerald-500/30">Done</span>
                  } @else {
                    <span class="rounded-full bg-amber-50 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-amber-700 ring-1 ring-amber-200 dark:bg-amber-500/10 dark:text-amber-300 dark:ring-amber-500/30">Pending</span>
                  }
                </div>
                <p class="mt-1 text-sm text-slate-600 dark:text-slate-300">
                  After saving, Xero shows a <span class="font-medium text-slate-800 dark:text-slate-100">Webhooks key</span>. Every delivery is signed with it, and this server refuses anything it cannot verify. Whoever runs the Fiscalize server sets it as
                </p>
                <code class="mt-2 inline-block rounded-lg border border-slate-200 bg-slate-50 px-2.5 py-1.5 font-mono text-xs text-slate-800 dark:border-white/10 dark:bg-slate-950 dark:text-slate-100">Xero:WebhookKey</code>
                <p class="mt-2 text-sm text-slate-600 dark:text-slate-300">in the server's settings and restarts the API. Treat the key like a password. Do this before the next step, or Xero's check fails.</p>
              </div>
            </li>
            <li class="flex gap-4 px-5 py-5 sm:px-6">
              <span class="flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-full bg-primary-500 text-xs font-bold text-white">5</span>
              <div class="min-w-0 flex-1">
                <p class="text-sm font-semibold text-slate-900 dark:text-white">Send “Intent to receive”</p>
                <p class="mt-1 text-sm text-slate-600 dark:text-slate-300">Back in the portal the status reads “Intent to receive required”. Click <span class="font-medium text-slate-800 dark:text-slate-100">Send ‘Intent to receive’</span>. Xero sends a few test deliveries, some signed wrongly on purpose, and switches the webhook on once this server accepts the good ones and refuses the bad ones. The status then reads OK.</p>
              </div>
            </li>
            <li class="flex gap-4 px-5 py-5 sm:px-6">
              <span class="flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-full bg-primary-500 text-xs font-bold text-white">6</span>
              <div class="min-w-0 flex-1">
                <p class="text-sm font-semibold text-slate-900 dark:text-white">Try it</p>
                <p class="mt-1 text-sm text-slate-600 dark:text-slate-300">Approve an invoice in Xero. Within about a minute it should appear under <span class="font-medium text-slate-800 dark:text-slate-100">Recent deliveries</span> below, and if auto-fiscalise is on, the receipt number shows next to it. This page refreshes itself every 30 seconds.</p>
              </div>
            </li>
          </ol>
        </section>

        <!-- ── Side panel ───────────────────────────────────────────────── -->
        <div class="flex flex-col gap-5 lg:col-span-2">

          <section class="rounded-2xl border border-slate-200/70 bg-white p-5 shadow-sm fade-in-down dark:border-white/10 dark:bg-slate-900 sm:p-6">
            <p class="text-sm font-semibold text-slate-900 dark:text-white">How it works</p>
            <ul class="mt-3 space-y-3 text-sm text-slate-600 dark:text-slate-300">
              <li class="flex gap-3">
                <span class="mt-1.5 h-1.5 w-1.5 flex-shrink-0 rounded-full bg-primary-500"></span>
                <span>Xero sends a small notice (which document changed, not its contents) shortly after a change.</span>
              </li>
              <li class="flex gap-3">
                <span class="mt-1.5 h-1.5 w-1.5 flex-shrink-0 rounded-full bg-primary-500"></span>
                <span>We check the signature, fetch the document from Xero, and, if it is an approved sales invoice or credit note, fiscalise it as your ZIMRA device, exactly as the Fiscalise button does.</span>
              </li>
              <li class="flex gap-3">
                <span class="mt-1.5 h-1.5 w-1.5 flex-shrink-0 rounded-full bg-primary-500"></span>
                <span>A document is fiscalised once. Later edits arrive as skipped, so nothing is submitted twice.</span>
              </li>
              <li class="flex gap-3">
                <span class="mt-1.5 h-1.5 w-1.5 flex-shrink-0 rounded-full bg-primary-500"></span>
                <span>Drafts, bills from suppliers and voided or deleted documents are recorded as skipped and never sent to FDMS.</span>
              </li>
            </ul>
          </section>

          <section class="rounded-2xl border border-slate-200/70 bg-white p-5 shadow-sm fade-in-down dark:border-white/10 dark:bg-slate-900 sm:p-6">
            <div class="flex items-start justify-between gap-3">
              <div>
                <p class="text-sm font-semibold text-slate-900 dark:text-white">Safety net: periodic check</p>
                <p class="mt-1 text-sm text-slate-600 dark:text-slate-300">Xero does not guarantee every delivery. A few times a day the app asks Xero for the invoices and credit notes changed since the last check and fiscalizes anything a webhook missed. These show as <span class="font-medium text-slate-800 dark:text-slate-100">Sync</span> below. Documents that failed for a passing reason (Xero or the platform busy) are tried again automatically and show as <span class="font-medium text-slate-800 dark:text-slate-100">Retry</span>.</p>
              </div>
              <span class="flex-shrink-0 rounded-full px-2.5 py-1 text-[10px] font-semibold uppercase tracking-wide ring-1"
                    [class]="d.syncEnabled
                      ? 'bg-emerald-50 text-emerald-700 ring-emerald-200 dark:bg-emerald-500/10 dark:text-emerald-300 dark:ring-emerald-500/30'
                      : 'bg-slate-100 text-slate-600 ring-slate-200 dark:bg-white/5 dark:text-slate-300 dark:ring-white/10'">
                {{ d.syncEnabled ? 'On' : 'Off' }}
              </span>
            </div>
            <p class="mt-3 text-xs text-slate-500 dark:text-slate-400" data-testid="sync-status">
              @if (d.syncEnabled) {
                Runs about every {{ hoursLabel(d.syncIntervalHours) }} per company, and right after a connection.
                @if (d.connected) {
                  Last check: {{ d.lastSyncAt ? (utc(d.lastSyncAt) | date:'medium') : 'not yet run' }}.
                }
              } @else {
                The periodic check is switched off on this server. Ask your administrator to enable it so missed deliveries are picked up.
              }
            </p>
            @if (d.connected && d.lastSyncError) {
              <p class="mt-3 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-900 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-100" role="status" data-testid="sync-error">
                <span class="font-semibold">The last check didn't finish.</span> {{ d.lastSyncError }}
                @if (d.syncRetryAt) { It tries again at {{ utc(d.syncRetryAt) | date:'shortTime' }}. }
              </p>
            }
          </section>

          <section class="rounded-2xl border border-slate-200/70 bg-white p-5 shadow-sm fade-in-down dark:border-white/10 dark:bg-slate-900 sm:p-6">
            <p class="text-sm font-semibold text-slate-900 dark:text-white">If nothing arrives</p>
            <ul class="mt-3 space-y-2 text-sm text-slate-600 dark:text-slate-300">
              <li class="flex gap-2"><span class="text-slate-400">·</span><span>Check the webhook status in the Xero developer portal reads OK. “Intent to receive required” means step 5 has not passed yet.</span></li>
              <li class="flex gap-2"><span class="text-slate-400">·</span><span>Confirm the Listener tile says Ready. Without the webhooks key the server answers 401 to everything, and Xero will not switch the webhook on.</span></li>
              <li class="flex gap-2"><span class="text-slate-400">·</span><span>If the key was regenerated in the portal, the server needs the new one.</span></li>
              <li class="flex gap-2"><span class="text-slate-400">·</span><span>Xero pauses a webhook whose address keeps failing. After fixing the server, send “Intent to receive” again.</span></li>
            </ul>
          </section>
        </div>
      </div>

      <!-- ── Recent deliveries ────────────────────────────────────────────── -->
      <section class="mt-5 overflow-hidden rounded-2xl border border-slate-200/70 bg-white shadow-sm fade-in-down dark:border-white/10 dark:bg-slate-900">
        <div class="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 px-5 py-4 sm:px-6 dark:border-white/10">
          <div class="flex items-center gap-3">
            <div class="flex h-8 w-8 items-center justify-center rounded-lg bg-brand-50 dark:bg-primary-500/15">
              <svg class="h-4 w-4 text-primary-600 dark:text-brand-400" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 6h16M4 12h16M4 18h7"/></svg>
            </div>
            <div>
              <p class="text-sm font-semibold text-slate-900 dark:text-white">Recent deliveries</p>
              <p class="text-xs text-slate-500 dark:text-slate-400">The latest notices from Xero, and documents found by the periodic check, for this organisation.</p>
            </div>
          </div>
          @if (d.recent.length) {
            <p class="text-xs text-slate-500 dark:text-slate-400">Updated {{ ago(lastLoaded()) }}</p>
          }
        </div>

        @if (!d.recent.length) {
          <div class="flex flex-col items-center gap-2 px-6 py-12 text-center">
            <div class="flex h-12 w-12 items-center justify-center rounded-2xl bg-slate-100 dark:bg-white/5">
              <svg class="h-6 w-6 text-slate-400" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M13 10V3L4 14h7v7l9-11h-7z"/></svg>
            </div>
            <p class="text-sm font-semibold text-slate-800 dark:text-white">No deliveries yet</p>
            <p class="max-w-sm text-xs text-slate-500 dark:text-slate-400">
              {{ d.connected ? 'Finish the steps above, then approve an invoice in Xero. It will show up here.' : 'Connect Xero and complete the setup to start receiving events.' }}
            </p>
          </div>
        } @else {

          <!-- Desktop table -->
          <div class="hidden md:block">
            <table class="w-full text-left text-sm">
              <thead class="bg-slate-50/80 text-[11px] font-semibold uppercase tracking-wider text-slate-500 dark:bg-white/[0.03] dark:text-slate-400">
                <tr>
                  <th class="px-6 py-3">Received</th>
                  <th class="px-4 py-3">Document</th>
                  <th class="px-4 py-3">Change</th>
                  <th class="px-4 py-3">Status</th>
                  <th class="px-6 py-3">Outcome</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-slate-100 dark:divide-white/10">
                @for (e of d.recent; track e.id) {
                  <tr class="transition-colors hover:bg-slate-50/80 dark:hover:bg-white/[0.03]">
                    <td class="whitespace-nowrap px-6 py-3 text-slate-700 dark:text-slate-200">
                      <div>{{ utc(e.receivedAt) | date:'d MMM, HH:mm:ss' }}</div>
                      <div class="text-xs text-slate-400">{{ ago(e.receivedAt) }}</div>
                    </td>
                    <td class="px-4 py-3">
                      <div class="font-medium text-slate-900 dark:text-white">{{ entityLabel(e) }}</div>
                      <div class="text-xs text-slate-500 dark:text-slate-400">{{ e.documentNumber ? e.documentNumber : 'No number yet' }}</div>
                    </td>
                    <td class="px-4 py-3 text-slate-700 dark:text-slate-200">{{ e.operation }}
                      @if (e.source === 'Retry') { <span class="ml-1 rounded-full bg-violet-50 px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-violet-700 ring-1 ring-violet-200 dark:bg-violet-500/10 dark:text-violet-300 dark:ring-violet-500/30" title="An automatic retry of a document that failed earlier">Retry</span> }
                      @if (e.source === 'Sync') { <span class="ml-1 rounded-full bg-sky-50 px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-sky-700 ring-1 ring-sky-200 dark:bg-sky-500/10 dark:text-sky-300 dark:ring-sky-500/30" title="Found by the periodic check, not a webhook">Sync</span> }
                    </td>
                    <td class="px-4 py-3">
                      <span class="inline-flex rounded-full px-2.5 py-0.5 text-[11px] font-semibold ring-1" [class]="statusClass(e.status)">{{ e.status }}</span>
                    </td>
                    <td class="max-w-md px-6 py-3 text-xs text-slate-600 dark:text-slate-300">
                      <span class="line-clamp-2" [title]="e.detail || ''">{{ e.detail || (e.status === 'Pending' ? 'Waiting to be processed…' : '') }}</span>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>

          <!-- Mobile cards -->
          <ul class="divide-y divide-slate-100 md:hidden dark:divide-white/10">
            @for (e of d.recent; track e.id) {
              <li class="px-5 py-4">
                <div class="flex items-start justify-between gap-3">
                  <div class="min-w-0">
                    <p class="text-sm font-semibold text-slate-900 dark:text-white">{{ entityLabel(e) }} · {{ e.operation }}@if (e.source === 'Sync') { · Sync }@if (e.source === 'Retry') { · Retry }</p>
                    <p class="text-xs text-slate-500 dark:text-slate-400">{{ e.documentNumber ? e.documentNumber : 'No number yet' }} · {{ ago(e.receivedAt) }}</p>
                  </div>
                  <span class="inline-flex flex-shrink-0 rounded-full px-2.5 py-0.5 text-[11px] font-semibold ring-1" [class]="statusClass(e.status)">{{ e.status }}</span>
                </div>
                @if (e.detail) {
                  <p class="mt-2 text-xs text-slate-600 dark:text-slate-300">{{ e.detail }}</p>
                }
              </li>
            }
          </ul>
        }
      </section>
    }

    @if (error(); as err) {
      <div class="mt-5 flex items-start gap-3 rounded-2xl border border-rose-200 bg-rose-50/80 p-4 text-sm text-rose-800 dark:border-rose-500/30 dark:bg-rose-500/10 dark:text-rose-100" role="alert">
        <svg class="mt-0.5 h-5 w-5 flex-shrink-0 text-rose-500" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"/></svg>
        <div>
          <p class="font-semibold">Could not load webhook status.</p>
          <p class="mt-0.5">{{ err }}</p>
        </div>
      </div>
    }
  `
})
export class Webhooks implements OnInit {
  /** The document types whose auto-fiscalise switch is off, as a phrase ("invoices and credit notes"); empty when all are on. */
  offFor(d: { autoFiscalizeInvoices: boolean; autoFiscalizeCreditNotes: boolean }): string {
    const off: string[] = [];
    if (!d.autoFiscalizeInvoices) off.push('invoices');
    if (!d.autoFiscalizeCreditNotes) off.push('credit notes');
    return off.join(' and ');
  }

  private readonly api = inject(WebhooksService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  readonly xeroPortal = XERO_PORTAL;
  readonly utc = asUtc;

  readonly data = signal<WebhookStatus | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly copied = signal<string | null>(null);
  readonly lastLoaded = signal<string>(new Date().toISOString());

  ngOnInit(): void {
    this.load();
    const timer = setInterval(() => this.load(false, true), REFRESH_EVERY_MS);
    this.destroyRef.onDestroy(() => clearInterval(timer));
  }

  load(announce = false, silent = false): void {
    if (!silent) this.loading.set(true);
    this.api.status().subscribe({
      next: (s) => {
        this.data.set(s);
        this.error.set(null);
        this.lastLoaded.set(new Date().toISOString());
        this.loading.set(false);
        if (announce) this.toast.success('Webhook status refreshed.');
      },
      error: (err) => {
        this.error.set(extractErrorMessage(err));
        this.loading.set(false);
      }
    });
  }

  copy(text: string, label: string): void {
    copyToClipboard(text).then((ok) => {
      if (!ok) { this.toast.error('Could not copy. Select the text and copy it manually.'); return; }
      this.copied.set(label);
      this.toast.success(`${label} copied.`);
      setTimeout(() => { if (this.copied() === label) this.copied.set(null); }, 2500);
    });
  }

  statusClass(status: WebhookEventStatus): string {
    return STATUS_STYLE[status] ?? STATUS_STYLE.Skipped;
  }

  entityLabel(e: WebhookEvent): string {
    return e.entityName === 'CreditNote' ? 'Credit note' : e.entityName;
  }

  /** Xero's event categories as the developer portal words them. */
  categoryLabel(category: string): string {
    switch ((category ?? '').toUpperCase()) {
      case 'INVOICE': return 'Invoices';
      case 'CREDITNOTE': return 'Credit notes';
      default: return category;
    }
  }

  hoursLabel(hours: number): string {
    if (!hours || hours <= 0) return 'few hours';
    if (hours < 1) return `${Math.round(hours * 60)} minutes`;
    return hours === 1 ? 'hour' : `${hours} hours`;
  }

  ago(iso: string): string {
    const diff = Math.max(0, Date.now() - new Date(asUtc(iso) ?? iso).getTime());
    const s = Math.round(diff / 1000);
    if (s < 10) return 'just now';
    if (s < 60) return `${s}s ago`;
    const m = Math.round(s / 60);
    if (m < 60) return `${m} min ago`;
    const h = Math.round(m / 60);
    if (h < 24) return `${h} h ago`;
    const d = Math.round(h / 24);
    return d === 1 ? 'yesterday' : `${d} days ago`;
  }
}
