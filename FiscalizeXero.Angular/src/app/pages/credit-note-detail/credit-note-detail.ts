import { Component, HostListener, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CreditNotesService } from '../../core/services/xero/credit-notes.service';
import { FiscalizationService } from '../../core/services/xero/fiscalization.service';
import { TaxDetailsService, ItemHsCode } from '../../core/services/xero/tax-details.service';
import { CreditNote, FiscalizeResult, XeroLineItem, TaxRateInfo, CreditNoteBaseDocument, BaseInvoiceCandidate } from '../../core/models/xero.models';
import { FormsModule } from '@angular/forms';
import { Dialog } from '../../components/dialog/dialog';
import { TaxRatesService } from '../../core/services/xero/tax-rates.service';
import { amountsAreLabel, calendarDate, isApproved, notFiscalisableReason, statusClasses, statusLabel } from '../../core/utils/xero-format';
import { FiscalAttachmentStatus } from '../../core/models/xero.models';
import { FiscalCacheService } from '../../core/services/fiscal-cache/fiscal-cache.service';
import { ToastService } from '../../core/services/toast/toast.service';
import { extractErrorMessage, extractErrorMessageAsync, isXeroAttention } from '../../core/utils/http-error';
import { downloadBlob, printBlob } from '../../core/utils/download.util';
import { FiscalPill } from '../../components/fiscal-pill/fiscal-pill';
import { BuyerSummary } from '../../components/buyer-summary/buyer-summary';
import { listReturnQuery } from '../../core/utils/document-list';
import { FiscalFailurePanel } from '../../components/fiscal-failure-panel/fiscal-failure-panel';
import { FiscalFailure } from '../../core/models/xero.models';
import { QrCode } from '../../components/qr-code/qr-code';
import { ConfirmDialog } from '../../components/confirm-dialog/confirm-dialog';

type Busy = 'fiscalize' | 'pdf' | 'pdf-fz' | 'pdf-layout' | 'print' | null;

/** Same layout as the Zoho Books credit note page: header band with an actions menu, info grid, items, totals. */
@Component({
  selector: 'app-credit-note-detail',
  imports: [FiscalFailurePanel, BuyerSummary, CommonModule, FormsModule, RouterLink, FiscalPill, QrCode, ConfirmDialog, Dialog],
  template: `
    <ng-container *ngIf="creditNote() as inv; else loadingTpl">
      <!-- Header band -->
      <div class="relative z-10 -mx-4 -mt-6 mb-6 border-b border-slate-200/40 bg-white/80 px-4 py-4 backdrop-blur-md fade-in-down dark:border-white/5 dark:bg-slate-950/60 sm:-mx-6 sm:px-6 lg:-mx-8 lg:-mt-8 lg:px-8 lg:py-5">
        <div class="mx-auto flex max-w-7xl items-center justify-between gap-3">
          <div class="flex items-center gap-3 min-w-0">
            <a routerLink="/credit-notes" [queryParams]="backQuery"
               class="flex-shrink-0 rounded-xl p-2 text-slate-500 transition-colors hover:bg-slate-100 hover:text-slate-900 dark:text-slate-400 dark:hover:bg-white/5 dark:hover:text-white"
               aria-label="Back to credit notes">
              <svg class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M15 19l-7-7 7-7"/></svg>
            </a>
            <div class="min-w-0">
              <h1 class="text-2xl font-bold tracking-tight text-slate-900 dark:text-white">{{ inv.creditNoteNumber || 'Credit note' }}</h1>
              <div class="mt-1 flex flex-wrap items-center gap-2 text-sm">
                <span class="text-slate-600 dark:text-slate-400">{{ inv.contact?.name || '—' }}</span>
                <span class="text-slate-400">·</span>
                <span class="text-slate-600 dark:text-slate-400">{{ date(inv.dateString) }}</span>
                <span class="inline-flex rounded-full px-2.5 py-0.5 text-[11px] font-semibold" [ngClass]="statusClasses(inv.status)" data-testid="xero-status">{{ statusLabel(inv.status) }}</span>
                <app-fiscal-pill [fiscalized]="isFiscalized()" [failureInfo]="failure()"></app-fiscal-pill>
              </div>
            </div>
          </div>

          <!-- Actions dropdown -->
          <div class="relative z-50" data-actions-menu>
            <button type="button" (click)="toggleActions()"
                    class="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-4 py-2 text-sm font-medium text-slate-700 transition-all duration-200 hover:bg-slate-50 focus:ring-2 focus:ring-primary-500 dark:border-white/10 dark:bg-slate-800 dark:text-slate-200 dark:hover:bg-slate-700">
              <svg *ngIf="busy()" class="h-4 w-4 animate-spin text-primary-500" viewBox="0 0 24 24" fill="none"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
              <svg *ngIf="!busy()" class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="5" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="12" cy="19" r="1"/></svg>
              <span class="hidden sm:inline">{{ busyLabel() }}</span>
            </button>
            <div *ngIf="showActions()"
                 class="absolute right-0 top-full z-50 mt-2 w-60 rounded-xl border border-slate-200 bg-white/95 py-1.5 shadow-xl backdrop-blur-md dark:border-white/10 dark:bg-slate-900/95"
                 style="animation: fadeIn 0.18s ease-out;">

              <!-- Fiscalize (always; the platform handles a document it has already seen) -->
              <button *ngIf="canFiscalize()" type="button" (click)="fiscalize()" [disabled]="!!busy()"
                      class="flex w-full items-center gap-2 px-4 py-2.5 text-sm transition-colors disabled:cursor-default"
                      [ngClass]="busy() === 'fiscalize'
                        ? 'bg-brand-50/60 text-primary-700 dark:bg-primary-500/10 dark:text-brand-400'
                        : busy() ? 'opacity-40 text-slate-700 dark:text-slate-200' : 'text-slate-700 hover:bg-slate-100 dark:text-slate-200 dark:hover:bg-white/5'">
                <svg *ngIf="busy() === 'fiscalize'" class="h-4 w-4 animate-spin text-primary-500" viewBox="0 0 24 24" fill="none"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
                <svg *ngIf="busy() !== 'fiscalize'" class="h-4 w-4 text-primary-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z"/></svg>
                <span>{{ busy() === 'fiscalize' ? 'Fiscalizing…' : 'Fiscalize' }}</span>
              </button>

              <!-- Refiscalize (already fiscalized) -->
              <button *ngIf="isFiscalized()" type="button" (click)="confirmRefiscalize.set(true); showActions.set(false)" [disabled]="!!busy()"
                      class="flex w-full items-center gap-2 px-4 py-2.5 text-sm transition-colors disabled:cursor-default"
                      [ngClass]="busy() ? 'opacity-40 text-slate-700 dark:text-slate-200' : 'text-slate-700 hover:bg-slate-100 dark:text-slate-200 dark:hover:bg-white/5'">
                <svg class="h-4 w-4 text-amber-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"/></svg>
                <span>Refiscalize</span>
              </button>

              <!-- Verify on FDMS -->
              <a *ngIf="qrlUrl()" [href]="qrlUrl()" target="_blank" rel="noopener" (click)="showActions.set(false)"
                 class="flex w-full items-center gap-2 px-4 py-2.5 text-sm transition-colors"
                 [ngClass]="busy() ? 'opacity-40 pointer-events-none text-slate-700 dark:text-slate-200' : 'text-slate-700 hover:bg-slate-100 dark:text-slate-200 dark:hover:bg-white/5'">
                <svg class="h-4 w-4 text-brand-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 3h7v7M10 14L21 3M21 14v7h-7M3 10V3h7M3 10l11 11"/></svg>
                <span>Verify on FDMS</span>
              </a>

              <ng-container *ngIf="isFiscalized()">
                <div class="my-1 h-px bg-slate-100 dark:bg-white/5"></div>

                <!-- Fiscalize PDF -->
                <button type="button" (click)="downloadFiscalizePdf()" [disabled]="!!busy()"
                        class="flex w-full items-center gap-2 px-4 py-2.5 text-sm transition-colors disabled:cursor-default"
                        [ngClass]="busy() === 'pdf-fz'
                          ? 'bg-brand-50/60 text-primary-700 dark:bg-primary-500/10 dark:text-brand-400'
                          : busy() ? 'opacity-40 text-slate-700 dark:text-slate-200' : 'text-slate-700 hover:bg-slate-100 dark:text-slate-200 dark:hover:bg-white/5'">
                  <svg *ngIf="busy() === 'pdf-fz'" class="h-4 w-4 animate-spin text-primary-600" viewBox="0 0 24 24" fill="none"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
                  <svg *ngIf="busy() !== 'pdf-fz'" class="h-4 w-4 text-primary-600" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z"/></svg>
                  <span>{{ busy() === 'pdf-fz' ? 'Downloading PDF…' : 'Fiscalize standard invoice' }}</span>
                </button>

                <!-- Fiscalize layout (the company's own, else Fiscalize's standard design) -->
                <button type="button" (click)="downloadLayoutPdf()" [disabled]="!!busy()"
                        class="flex w-full items-center gap-2 px-4 py-2.5 text-sm transition-colors disabled:cursor-default"
                        [ngClass]="busy() === 'pdf-layout'
                          ? 'bg-brand-50/60 text-primary-700 dark:bg-primary-500/10 dark:text-brand-400'
                          : busy() ? 'opacity-40 text-slate-700 dark:text-slate-200' : 'text-slate-700 hover:bg-slate-100 dark:text-slate-200 dark:hover:bg-white/5'">
                  <svg *ngIf="busy() === 'pdf-layout'" class="h-4 w-4 animate-spin text-primary-600" viewBox="0 0 24 24" fill="none"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
                  <svg *ngIf="busy() !== 'pdf-layout'" class="h-4 w-4 text-primary-600" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="18" height="18" rx="2"/><path d="M3 9h18M9 21V9"/></svg>
                  <span>{{ busy() === 'pdf-layout' ? 'Drawing layout…' : 'Fiscalize layout' }}</span>
                </button>

                <!-- Stamped PDF -->
                <button type="button" (click)="downloadStampedPdf()" [disabled]="!!busy()"
                        class="flex w-full items-center gap-2 px-4 py-2.5 text-sm transition-colors disabled:cursor-default"
                        [ngClass]="busy() === 'pdf'
                          ? 'bg-emerald-50/60 text-emerald-700 dark:bg-primary-600/10 dark:text-brand-400'
                          : busy() ? 'opacity-40 text-slate-700 dark:text-slate-200' : 'text-slate-700 hover:bg-slate-100 dark:text-slate-200 dark:hover:bg-white/5'">
                  <svg *ngIf="busy() === 'pdf'" class="h-4 w-4 animate-spin text-primary-600" viewBox="0 0 24 24" fill="none"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
                  <svg *ngIf="busy() !== 'pdf'" class="h-4 w-4 text-primary-600" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4M7 10l5 5 5-5M12 15V3"/></svg>
                  <span>{{ busy() === 'pdf' ? 'Downloading PDF…' : 'Xero PDF with stamp' }}</span>
                </button>

                <!-- Print -->
                <button type="button" (click)="printStampedPdf()" [disabled]="!!busy()"
                        class="flex w-full items-center gap-2 px-4 py-2.5 text-sm transition-colors disabled:cursor-default"
                        [ngClass]="busy() === 'print'
                          ? 'bg-slate-50/80 text-slate-900 dark:bg-white/5 dark:text-white'
                          : busy() ? 'opacity-40 text-slate-700 dark:text-slate-200' : 'text-slate-700 hover:bg-slate-100 dark:text-slate-200 dark:hover:bg-white/5'">
                  <svg *ngIf="busy() === 'print'" class="h-4 w-4 animate-spin text-slate-500" viewBox="0 0 24 24" fill="none"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
                  <svg *ngIf="busy() !== 'print'" class="h-4 w-4 text-slate-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 6 2 18 2 18 9"/><path d="M6 18H4a2 2 0 01-2-2v-5a2 2 0 012-2h16a2 2 0 012 2v5a2 2 0 01-2 2h-2"/><rect x="6" y="14" width="12" height="8"/></svg>
                  <span>{{ busy() === 'print' ? 'Printing…' : 'Print' }}</span>
                </button>
              </ng-container>
            </div>
          </div>
        </div>
      </div>

      <!-- Not approved in Xero: nothing to fiscalize yet -->
      <div *ngIf="notFiscalisable() as reason" role="status" data-testid="not-approved"
           class="mb-6 rounded-2xl border border-slate-200 bg-slate-50 px-5 py-3 text-sm text-slate-700 dark:border-white/10 dark:bg-white/5 dark:text-slate-200">
        {{ reason }}
      </div>

      <!-- Xero refused a call for this page -->
      <div *ngIf="xeroProblem() as problem" role="alert" data-testid="xero-attention"
           class="mb-6 flex flex-col gap-3 rounded-2xl border border-amber-200 bg-amber-50 p-4 text-sm text-amber-900 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-100 sm:flex-row sm:items-center sm:justify-between">
        <div><div class="font-semibold">Xero needs attention</div><div class="mt-0.5">{{ problem }}</div></div>
        <a routerLink="/xero-connection" class="inline-flex flex-shrink-0 items-center justify-center rounded-xl bg-amber-600 px-4 py-2 text-xs font-semibold text-white hover:bg-amber-700">Open Xero connection</a>
      </div>

      <!-- Why the last fiscalization attempt failed (hidden once fiscalized) -->
      <app-fiscal-failure-panel *ngIf="!isFiscalized() && failure()" class="mb-6 block"
                                [failureInfo]="failure()" [customerId]="inv.contact?.contactID" documentLabel="credit note"
                                [busy]="busy() === 'fiscalize'" (retry)="fiscalize()"></app-fiscal-failure-panel>

      <!-- Validation error banner -->
      <div *ngIf="hasFiscalError()"
           class="mb-6 flex items-center gap-4 rounded-2xl border border-red-200/60 bg-gradient-to-r from-red-50 to-rose-50 p-5 shadow-sm slide-down dark:border-red-800/40 dark:from-red-900/15 dark:to-rose-900/15">
        <div class="flex h-12 w-12 flex-shrink-0 items-center justify-center rounded-xl bg-gradient-to-br from-red-500 to-rose-600 shadow-lg shadow-red-500/20">
          <svg class="h-6 w-6 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4c-.77-.833-1.964-.833-2.732 0L3.732 16.5c-.77.833.192 2.5 1.732 2.5z"/></svg>
        </div>
        <div class="flex-1">
          <p class="text-sm font-bold text-red-700 dark:text-red-300">FDMS Validation Error</p>
          <p class="mt-0.5 text-xs text-red-600 dark:text-red-400">{{ result()?.validationError || 'This credit note was fiscalized with FDMS validation errors. Please verify on the FDMS portal.' }}</p>
        </div>
      </div>

      <!-- Writeback / attachment warnings -->
      <div *ngIf="result()?.writebackWarning || result()?.attachmentWarning"
           class="mb-6 rounded-2xl border border-amber-200/70 bg-amber-50/80 px-5 py-3 text-xs text-amber-800 slide-down dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-200">
        <p *ngIf="result()?.writebackWarning"><span class="font-semibold">Writing back to Xero:</span> {{ result()?.writebackWarning }}</p>
        <p *ngIf="result()?.attachmentWarning"><span class="font-semibold">Stamped PDF:</span> {{ result()?.attachmentWarning }}</p>
      </div>

      <!-- Top info grid: credit note details, bill-to, fiscal data, QR code -->
      <div class="mb-6 rounded-2xl border border-white/40 bg-white/70 p-6 shadow-sm backdrop-blur-md dark:border-white/10 dark:bg-slate-900/60">
        <div class="grid grid-cols-1 gap-6 lg:grid-cols-2 xl:grid-cols-4">
          <!-- Credit note Details -->
          <div class="space-y-4">
            <h2 class="text-lg font-semibold text-slate-900 dark:text-white">Credit note details</h2>
            <div class="space-y-3 text-sm">
              <div class="flex justify-between gap-3"><span class="text-slate-600 dark:text-slate-400">Number:</span><span class="text-right font-medium text-slate-900 dark:text-white">{{ inv.creditNoteNumber || '—' }}</span></div>
              <div *ngIf="inv.reference" class="flex justify-between gap-3"><span class="text-slate-600 dark:text-slate-400">Reference:</span><span class="break-all text-right font-medium text-slate-900 dark:text-white" data-testid="reference">{{ inv.reference }}</span></div>
              <div class="flex justify-between gap-3"><span class="text-slate-600 dark:text-slate-400">Date:</span><span class="text-right font-medium text-slate-900 dark:text-white">{{ date(inv.dateString) || '—' }}</span></div>
              <div class="flex flex-col gap-1">
                <span class="text-slate-600 dark:text-slate-400">Reverses invoice:</span>
                <span class="flex flex-wrap items-center gap-x-1 gap-y-1" data-testid="reverses">
                  <ng-container *ngIf="baseDocNo(); else noBaseTpl">
                    <span class="whitespace-nowrap font-medium text-slate-900 dark:text-white">{{ baseDocNo() }}</span>
                    <span class="ml-1.5 whitespace-nowrap rounded-full px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wider"
                          [ngClass]="baseIsFiscalized() ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300' : 'bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300'">
                      {{ baseIsFiscalized() ? (chosenBaseDocNo() ? 'chosen here' : 'from Xero') : 'not fiscalized' }}
                    </span>
                  </ng-container>
                  <ng-template #noBaseTpl>
                    <span *ngIf="baseLoading()" class="text-slate-400">…</span>
                    <span *ngIf="!baseLoading()" class="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 text-[11px] font-semibold text-amber-700 dark:bg-amber-500/15 dark:text-amber-300"><span class="h-1.5 w-1.5 rounded-full bg-amber-500"></span> Not allocated</span>
                  </ng-template>
                  <button *ngIf="!isFiscalized() && !baseLoading()" type="button" (click)="openBasePicker()" data-testid="choose-invoice" class="ml-2 text-xs font-semibold text-primary-600 hover:underline dark:text-brand-400">{{ baseDocNo() ? 'Change' : 'Choose' }}</button>
                </span>
              </div>
              <div class="flex justify-between gap-3"><span class="text-slate-600 dark:text-slate-400">Amounts are:</span><span class="text-right font-medium text-slate-900 dark:text-white">{{ amountsAre(inv.lineAmountTypes) || '—' }}</span></div>
              <div class="flex justify-between gap-3 border-t border-slate-200/60 pt-3 dark:border-white/5"><span class="text-slate-600 dark:text-slate-400">Total:</span><span class="text-right text-xl font-bold tabular-nums text-slate-900 dark:text-white">{{ currency(inv) }}{{ inv.total ?? 0 | number:'1.2-2' }}</span></div>
              <div class="flex justify-between gap-3"><span class="text-slate-600 dark:text-slate-400">Remaining credit:</span><span class="text-right font-semibold tabular-nums" [ngClass]="(inv.remainingCredit ?? 0) > 0 ? 'text-amber-600 dark:text-amber-400' : 'text-primary-700 dark:text-brand-400'">{{ currency(inv) }}{{ inv.remainingCredit ?? 0 | number:'1.2-2' }}</span></div>
            </div>
          </div>

          <!-- Customer -->
          <div class="space-y-4">
            <h2 class="text-lg font-semibold text-slate-900 dark:text-white">Customer</h2>
            <div class="space-y-1.5 text-sm">
              <p class="font-medium text-slate-900 dark:text-white">{{ inv.contact?.name || '—' }}</p>
              <p *ngIf="inv.contact?.emailAddress" class="break-all text-slate-600 dark:text-slate-400">{{ inv.contact?.emailAddress }}</p>
              <p *ngIf="inv.currencyCode" class="text-slate-600 dark:text-slate-400">Currency: {{ inv.currencyCode }}</p>
              <a *ngIf="inv.contact?.contactID" [routerLink]="['/customers', inv.contact!.contactID]" [queryParams]="{ returnTo: returnTo }"
                 class="inline-block text-xs font-semibold text-primary-600 hover:underline dark:text-brand-400">Buyer details for ZIMRA</a>
              <div *ngIf="allocations().length" class="mt-3 border-t border-slate-200/60 pt-3 dark:border-white/5" data-testid="allocations">
                <p class="mb-1.5 text-xs font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">Allocated in Xero</p>
                <div *ngFor="let a of allocations()" class="flex items-center justify-between gap-2 py-0.5 text-xs">
                  <span class="text-slate-700 dark:text-slate-200">Credit to <span class="font-mono font-semibold">{{ a.invoiceNumber || 'an invoice' }}</span>
                    <span *ngIf="!a.isFiscalized" class="ml-1 whitespace-nowrap rounded-full bg-amber-100 px-1.5 py-0.5 text-[10px] font-semibold text-amber-700 dark:bg-amber-500/15 dark:text-amber-300">not fiscalized</span>
                  </span>
                  <span class="whitespace-nowrap tabular-nums text-slate-600 dark:text-slate-400">{{ currency(inv) }}{{ a.amount | number:'1.2-2' }}</span>
                </div>
                <p *ngIf="allocations().length > 1" class="mt-1.5 text-[11px] text-slate-500 dark:text-slate-400">ZIMRA credits one receipt, so the invoice with the largest allocation is used unless you choose another.</p>
              </div>
            </div>
          </div>

          <!-- Fiscal Data -->
          <div class="space-y-4">
            <h2 class="text-lg font-semibold text-slate-900 dark:text-white">Fiscal Data</h2>
            <div *ngIf="result() as r; else notFiscalizedTpl" class="space-y-2 text-sm">
              <div *ngIf="r.receiptNumber" class="flex justify-between gap-3"><span class="text-slate-600 dark:text-slate-400">Receipt No:</span><span class="text-right font-medium text-slate-900 dark:text-white">{{ r.receiptNumber }}</span></div>
              <div *ngIf="r.globalNumber" class="flex justify-between gap-3"><span class="text-slate-600 dark:text-slate-400">Global No:</span><span class="text-right font-medium text-slate-900 dark:text-white">{{ r.globalNumber }}</span></div>
              <div *ngIf="r.fiscalDayNumber" class="flex justify-between gap-3"><span class="text-slate-600 dark:text-slate-400">Fiscal Day:</span><span class="text-right font-medium text-slate-900 dark:text-white">{{ r.fiscalDayNumber }}</span></div>
              <div *ngIf="r.deviceId" class="flex justify-between gap-3"><span class="text-slate-600 dark:text-slate-400">Device ID:</span><span class="text-right font-mono text-xs text-slate-900 dark:text-white">{{ r.deviceId }}</span></div>
              <div *ngIf="r.verificationCode" class="flex justify-between gap-3"><span class="text-slate-600 dark:text-slate-400">Verification:</span><span class="text-right font-mono text-xs break-all text-slate-900 dark:text-white">{{ r.verificationCode }}</span></div>
              <div class="flex justify-between gap-3"><span class="text-slate-600 dark:text-slate-400">Fiscalized:</span><span class="text-right text-xs text-slate-900 dark:text-white">{{ r.fiscalizedAt | date:'medium' }}</span></div>
              <div class="flex items-start justify-between gap-3 border-t border-slate-200/60 pt-3 dark:border-white/5">
                <span class="text-slate-600 dark:text-slate-400">PDF in Xero:</span>
                <span class="text-right">
                  <span *ngIf="attachment() === undefined && !attachWatching()" class="text-slate-400">…</span>
                  <span *ngIf="attachWatching()" class="inline-flex items-center gap-1.5 rounded-full bg-brand-50 px-2 py-0.5 text-[11px] font-semibold text-primary-700 dark:bg-primary-500/15 dark:text-brand-300"><span class="h-2.5 w-2.5 animate-spin rounded-full border-2 border-brand-200 border-t-primary-500"></span> Attaching…</span>
                  <ng-container *ngIf="!attachWatching() && attachment() as a">
                    <span *ngIf="a.attached" class="inline-flex items-center gap-1 rounded-full bg-emerald-100 px-2 py-0.5 text-[11px] font-semibold text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300" [title]="a.fileName || ''"><span class="h-1.5 w-1.5 rounded-full bg-emerald-500"></span> Attached</span>
                    <span *ngIf="!a.attached" class="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 text-[11px] font-semibold text-amber-700 dark:bg-amber-500/15 dark:text-amber-300"><span class="h-1.5 w-1.5 rounded-full bg-amber-500"></span> Not attached</span>
                    <button *ngIf="!a.attached" type="button" (click)="attachNow()" [disabled]="attaching()" class="ml-2 text-xs font-semibold text-primary-600 hover:underline disabled:opacity-50 dark:text-brand-400">{{ attaching() ? 'Attaching…' : 'Attach now' }}</button>
                  </ng-container>
                  <span *ngIf="attachment() === null" class="text-xs text-slate-400">unknown</span>
                </span>
              </div>
              <div class="pt-1">
                <a *ngIf="qrlUrl()" [href]="qrlUrl()" target="_blank" rel="noopener"
                   class="inline-flex items-center gap-1.5 text-sm font-semibold text-primary-600 hover:text-primary-700 hover:underline dark:text-brand-400">
                  Verify on FDMS
                  <svg class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 13v6a2 2 0 01-2 2H5a2 2 0 01-2-2V8a2 2 0 012-2h6M15 3h6v6M10 14L21 3"/></svg>
                </a>
              </div>
            </div>
            <ng-template #notFiscalizedTpl>
              <p class="text-sm text-slate-500 dark:text-slate-400">
                {{ notFiscalisable() || 'This credit note has not been fiscalized yet.' }}
              </p>
              <button *ngIf="canFiscalize()" type="button" (click)="fiscalize()" [disabled]="busy() === 'fiscalize'" data-testid="fiscalize-now"
                      class="inline-flex items-center gap-1.5 rounded-xl bg-gradient-to-r from-primary-500 to-primary-600 px-4 py-2 text-sm font-semibold text-white shadow-md shadow-primary-500/20 transition-all duration-200 hover:from-primary-600 hover:to-primary-700 hover:scale-[1.02] active:scale-[0.98] disabled:opacity-50 disabled:hover:scale-100">
                <svg *ngIf="busy() === 'fiscalize'" class="h-3.5 w-3.5 animate-spin" viewBox="0 0 24 24" fill="none"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
                {{ busy() === 'fiscalize' ? 'Submitting…' : 'Fiscalize now' }}
              </button>
            </ng-template>
          </div>

          <!-- QR Code -->
          <div class="space-y-4">
            <h2 class="text-lg font-semibold text-slate-900 dark:text-white">QR Code</h2>
            <div *ngIf="qrlUrl(); else noQrTpl" class="flex flex-col items-center gap-3 sm:items-end">
              <app-qr-code [value]="qrlUrl()" [size]="160" [hasError]="hasFiscalError()"></app-qr-code>
              <a [href]="qrlUrl()" target="_blank" rel="noopener" class="text-xs font-medium text-brand-600 hover:underline dark:text-brand-400">View on FDMS →</a>
            </div>
            <ng-template #noQrTpl>
              <div class="flex h-[160px] items-center justify-center rounded-xl border border-dashed border-slate-300 text-xs text-slate-500 dark:border-white/10 dark:text-slate-400">
                Available after fiscalization
              </div>
            </ng-template>
          </div>
        </div>
      </div>

      <!-- Buyer details that fiscalising sends to ZIMRA -->
      <app-buyer-summary class="mb-6 block" [customerId]="inv.contact?.contactID" documentLabel="credit note" [fiscalized]="isFiscalized()" [refreshOn]="result()"></app-buyer-summary>

      <!-- Line items -->
      <div class="mb-6 overflow-hidden rounded-2xl border border-white/40 bg-white/70 shadow-sm backdrop-blur-md table-slide-up dark:border-white/10 dark:bg-slate-900/60">
        <div class="flex items-center justify-between gap-3 border-b border-slate-200/30 p-6 dark:border-white/5">
          <h2 class="text-lg font-semibold text-slate-900 dark:text-white">Items</h2>
          <a *ngIf="missingHsCount() > 0" routerLink="/items"
             class="inline-flex items-center gap-1.5 rounded-full bg-amber-100 px-3 py-1 text-xs font-semibold text-amber-700 transition hover:bg-amber-200 dark:bg-amber-500/15 dark:text-amber-300 dark:hover:bg-amber-500/25">
            <span class="h-1.5 w-1.5 rounded-full bg-amber-500"></span>
            {{ missingHsCount() }} {{ missingHsCount() === 1 ? 'item' : 'items' }} without an HS code
          </a>
        </div>
        <div class="hidden overflow-x-auto md:block">
          <table class="w-full">
            <thead class="bg-slate-50/50 dark:bg-white/5">
              <tr>
                <th class="px-4 py-3 xl:px-6 text-left text-xs font-medium uppercase tracking-wider text-slate-500 dark:text-slate-400">Item</th>
                <th class="px-4 py-3 xl:px-6 text-left text-xs font-medium uppercase tracking-wider text-slate-500 dark:text-slate-400">HS code</th>
                <th class="px-4 py-3 xl:px-6 text-right text-xs font-medium uppercase tracking-wider text-slate-500 dark:text-slate-400">Qty</th>
                <th class="px-4 py-3 xl:px-6 text-right text-xs font-medium uppercase tracking-wider text-slate-500 dark:text-slate-400">Unit price</th>
                <th class="px-4 py-3 xl:px-6 text-right text-xs font-medium uppercase tracking-wider text-slate-500 dark:text-slate-400">Disc.</th>
                <th class="px-4 py-3 xl:px-6 text-right text-xs font-medium uppercase tracking-wider text-slate-500 dark:text-slate-400">Tax</th>
                <th class="px-4 py-3 xl:px-6 text-right text-xs font-medium uppercase tracking-wider text-slate-500 dark:text-slate-400">Amount</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-slate-200/30 dark:divide-white/5">
              <tr *ngFor="let l of itemLines(inv); let i = index"
                  class="table-row-fade-in transition-colors duration-200 hover:bg-slate-50/50 dark:hover:bg-white/5"
                  [style.animation-delay]="(i * 0.04) + 's'">
                <td class="px-4 py-4 xl:px-6">
                  <div class="text-sm font-medium text-slate-900 dark:text-white">{{ l.item?.name || firstLine(l.description) || l.itemCode || '—' }}</div>
                  <div *ngIf="l.itemCode" class="font-mono text-xs text-slate-500 dark:text-slate-400">{{ l.itemCode }}</div>
                  <div *ngIf="l.item?.name && restOfDescription(l) as more" class="whitespace-pre-line text-xs text-slate-500 dark:text-slate-400">{{ more }}</div>
                </td>
                <td class="px-4 py-4 xl:px-6">
                  <ng-container *ngIf="hsFor(l) as hs; else noHsTpl">
                    <span class="rounded-md bg-slate-100 px-2 py-0.5 font-mono text-xs text-slate-700 dark:bg-white/10 dark:text-slate-200" [title]="hsTitle(hs)">{{ hs.hsCode }}</span>
                    <span *ngIf="hs.hsCodeSource === 'Default'" class="ml-1.5 text-[10px] font-semibold uppercase tracking-wider text-slate-400 dark:text-slate-500">default</span>
                  </ng-container>
                  <ng-template #noHsTpl>
                    <span *ngIf="hsLoading()" class="text-xs text-slate-400 dark:text-slate-600">…</span>
                    <span *ngIf="!hsLoading()" class="text-xs text-slate-500 dark:text-slate-400" title="Lines without a Xero item take the company's default HS code">Default</span>
                  </ng-template>
                </td>
                <td class="px-4 py-4 xl:px-6 text-right text-sm tabular-nums text-slate-900 dark:text-white">{{ l.quantity ?? '—' }}</td>
                <td class="px-4 py-4 xl:px-6 text-right text-sm tabular-nums text-slate-900 dark:text-white">{{ l.unitAmount != null ? (l.unitAmount | number:'1.2-4') : '—' }}</td>
                <td class="px-4 py-4 xl:px-6 text-right text-sm tabular-nums text-slate-700 dark:text-slate-200">{{ discount(l) }}</td>
                <td class="px-4 py-4 xl:px-6 text-right text-sm tabular-nums text-slate-700 dark:text-slate-200" [title]="taxName(l)">
                  <div>{{ taxPercent(l, inv) }}</div>
                  <div *ngIf="l.taxAmount" class="text-xs text-slate-500 dark:text-slate-400">{{ l.taxAmount | number:'1.2-2' }}</div>
                </td>
                <td class="px-4 py-4 xl:px-6 text-right text-sm font-semibold tabular-nums text-slate-900 dark:text-white">{{ (l.lineAmount ?? 0) | number:'1.2-2' }}</td>
              </tr>
              <tr *ngIf="itemLines(inv).length === 0"><td colspan="7" class="px-6 py-8 text-center text-sm text-slate-500 dark:text-slate-400">This credit note has no lines with an amount.</td></tr>
            </tbody>
          </table>
        </div>
        <!-- Phones: one stacked row per line, nothing hidden off the edge -->
        <ul class="divide-y divide-slate-200/40 md:hidden dark:divide-white/5">
          <li *ngFor="let l of itemLines(inv)" class="px-5 py-4">
            <div class="flex items-start justify-between gap-3">
              <div class="min-w-0">
                <p class="text-sm font-medium text-slate-900 dark:text-white">{{ l.item?.name || firstLine(l.description) || l.itemCode || '—' }}</p>
                <p *ngIf="l.itemCode" class="font-mono text-xs text-slate-500 dark:text-slate-400">{{ l.itemCode }}</p>
              </div>
              <p class="shrink-0 text-sm font-semibold tabular-nums text-slate-900 dark:text-white">{{ (l.lineAmount ?? 0) | number:'1.2-2' }}</p>
            </div>
            <p class="mt-1.5 flex flex-wrap gap-x-3 gap-y-1 text-xs tabular-nums text-slate-500 dark:text-slate-400">
              <span>{{ l.quantity ?? '—' }} × {{ l.unitAmount != null ? (l.unitAmount | number:'1.2-4') : '—' }}</span>
              <span *ngIf="discount(l) !== '—'">Disc. {{ discount(l) }}</span>
              <span [title]="taxName(l)">Tax {{ taxPercent(l, inv) }}<ng-container *ngIf="l.taxAmount"> ({{ l.taxAmount | number:'1.2-2' }})</ng-container></span>
              <span>HS <span class="font-mono text-slate-700 dark:text-slate-200">{{ hsFor(l)?.hsCode || 'default' }}</span></span>
            </p>
          </li>
          <li *ngIf="itemLines(inv).length === 0" class="px-5 py-8 text-center text-sm text-slate-500 dark:text-slate-400">This credit note has no lines with an amount.</li>
        </ul>
      </div>

      <!-- Totals -->
      <div class="rounded-2xl border border-white/40 bg-white/70 p-6 shadow-sm backdrop-blur-md dark:border-white/10 dark:bg-slate-900/60">
        <div class="ml-auto max-w-md space-y-3 text-sm">
          <div class="flex justify-between"><span class="text-slate-600 dark:text-slate-400">Subtotal<ng-container *ngIf="amountsAre(inv.lineAmountTypes) === 'Tax inclusive'"> (includes tax)</ng-container>:</span><span class="font-medium tabular-nums text-slate-900 dark:text-white">{{ currency(inv) }}{{ inv.subTotal ?? 0 | number:'1.2-2' }}</span></div>
          <div class="flex justify-between"><span class="text-slate-600 dark:text-slate-400">Tax:</span><span class="font-medium tabular-nums text-slate-900 dark:text-white">{{ currency(inv) }}{{ inv.totalTax ?? 0 | number:'1.2-2' }}</span></div>
          <div class="border-t border-slate-200/30 pt-3 dark:border-white/5">
            <div class="flex justify-between">
              <span class="text-lg font-semibold text-slate-900 dark:text-white">Total:</span>
              <span class="text-xl font-bold tabular-nums text-slate-900 dark:text-white" data-testid="doc-total">{{ currency(inv) }}{{ inv.total ?? 0 | number:'1.2-2' }}</span>
            </div>
          </div>
          <p class="text-[11px] text-slate-500 dark:text-slate-400">ZIMRA receives each line at its amount including tax, so the fiscal receipt total equals this total.</p>
        </div>
      </div>

      <app-dialog [open]="basePickerOpen()" title="Which invoice does this credit note reverse?"
                  subtitle="ZIMRA credits a specific fiscal receipt, so the original invoice must already be fiscalized here. Xero's only link is the allocation; you can also choose the invoice."
                  [size]="'md'" variant="gradient" [hasFooter]="true"
                  iconPath="M9 17v-2m3 2v-4m3 4v-6m2 10H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"
                  (close)="basePickerOpen.set(false)">
        <p *ngIf="baseInfo()?.problem" class="mb-3 rounded-xl border border-amber-200/70 bg-amber-50 px-3 py-2 text-xs text-amber-700 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-300">{{ baseInfo()?.problem }}</p>
        <label class="mb-1.5 block text-xs font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">Invoice number</label>
        <input type="text" [ngModel]="pickerDocNo()" (ngModelChange)="pickerDocNo.set($event)" placeholder="e.g. INV-0005" list="base-invoice-options"
               class="mb-3 w-full rounded-xl border border-slate-200 bg-white px-3 py-2.5 font-mono text-sm text-slate-900 outline-none transition focus:border-primary-500 focus:ring-2 focus:ring-primary-500/30 dark:border-white/10 dark:bg-slate-950/60 dark:text-white" />
        <p class="mb-2 text-xs text-slate-500 dark:text-slate-400">Fiscalized invoices, newest first:</p>
        <div class="max-h-64 overflow-y-auto rounded-xl border border-slate-200/70 dark:border-white/10">
          <p *ngIf="!candidates().length" class="px-4 py-6 text-center text-sm text-slate-500 dark:text-slate-400">No invoices have been fiscalized here yet. Fiscalize the original invoice first.</p>
          <button *ngFor="let c of candidates()" type="button" (click)="pickerDocNo.set(c.documentNumber)"
                  class="flex w-full items-center justify-between gap-3 border-b border-slate-100 px-4 py-2.5 text-left text-sm transition-colors last:border-b-0 hover:bg-brand-50/60 dark:border-white/5 dark:hover:bg-primary-500/10"
                  [ngClass]="pickerDocNo() === c.documentNumber ? 'bg-brand-50 dark:bg-primary-500/10' : ''">
            <span class="flex items-center gap-2"><span class="font-mono font-semibold text-slate-900 dark:text-white">{{ c.documentNumber }}</span></span>
            <span class="text-xs text-slate-500 dark:text-slate-400">Receipt #{{ c.receiptNumber }} · {{ c.fiscalizedAt | date:'mediumDate' }}</span>
          </button>
        </div>
        <ng-container dialogFooter>
          <button type="button" (click)="basePickerOpen.set(false)" class="rounded-xl border border-slate-200 bg-white/70 px-4 py-2 text-sm font-medium text-slate-700 transition hover:bg-white dark:border-white/10 dark:bg-white/5 dark:text-slate-200">Cancel</button>
          <button type="button" (click)="confirmBasePick()" [disabled]="!pickerDocNo().trim()"
                  class="rounded-xl bg-gradient-to-r from-primary-500 to-primary-600 px-4 py-2 text-sm font-semibold text-white shadow-md shadow-primary-500/25 transition hover:from-primary-600 hover:to-primary-700 disabled:opacity-50">
            {{ pickerThenFiscalize() ? 'Use this invoice & fiscalize' : 'Use this invoice' }}
          </button>
        </ng-container>
      </app-dialog>

      <app-confirm-dialog
        [open]="confirmRefiscalize()"
        title="Refiscalize this credit note?"
        description="This is not a simple retry. It credits the original fiscal credit note at FDMS and submits the credit note again as a new fiscal record. Both are reported to the tax authority and cannot be undone. Only refiscalize to apply corrected data or to resolve FDMS errors."
        variant="warning"
        confirmLabel="Refiscalize"
        busyLabel="Fiscalizing…"
        [busy]="busy() === 'fiscalize'"
        (confirm)="refiscalize()"
        (cancel)="confirmRefiscalize.set(false)">
      </app-confirm-dialog>
    </ng-container>

    <ng-template #loadingTpl>
      <div *ngIf="!loadError()" class="rounded-2xl border border-white/40 bg-white/70 p-12 text-center backdrop-blur-md dark:border-white/10 dark:bg-slate-900/60">
        <div class="mx-auto h-8 w-8 animate-spin rounded-full border-4 border-brand-200 border-t-primary-500"></div>
        <p class="mt-3 text-sm text-slate-500 dark:text-slate-400">Loading credit note…</p>
      </div>
      <div *ngIf="loadError() as e" role="alert" data-testid="load-error"
           class="rounded-2xl border border-amber-200 bg-amber-50 p-8 text-center dark:border-amber-500/30 dark:bg-amber-500/10">
        <h2 class="text-lg font-semibold text-amber-900 dark:text-amber-100">{{ e.xero ? 'Xero needs attention' : 'This credit note could not be opened' }}</h2>
        <p class="mx-auto mt-2 max-w-xl text-sm text-amber-800 dark:text-amber-200">{{ e.message }}</p>
        <div class="mt-5 flex flex-wrap justify-center gap-2">
          <a routerLink="/credit-notes" [queryParams]="backQuery" class="rounded-xl border border-amber-300 bg-white px-4 py-2 text-sm font-semibold text-amber-900 hover:bg-amber-100 dark:border-amber-500/40 dark:bg-transparent dark:text-amber-100">Back to credit notes</a>
          <a *ngIf="e.xero" routerLink="/xero-connection" class="rounded-xl bg-amber-600 px-4 py-2 text-sm font-semibold text-white hover:bg-amber-700">Open Xero connection</a>
        </div>
      </div>
    </ng-template>
  `
})
export class CreditNoteDetail implements OnInit {
  /** Back to the list page, size and search the user came from. */
  readonly backQuery = listReturnQuery('/credit-notes');
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private documents = inject(CreditNotesService);
  private fiscalizationService = inject(FiscalizationService);
  private taxDetails = inject(TaxDetailsService);
  private taxCodes = inject(TaxRatesService);
  private fiscalCache = inject(FiscalCacheService);
  private toast = inject(ToastService);

  readonly creditNote = signal<CreditNote | null>(null);
  readonly result = signal<FiscalizeResult | null>(null);
  readonly busy = signal<Busy>(null);
  /** The latest failed fiscalization attempt, if any (cleared when it fiscalizes). */
  readonly failure = signal<FiscalFailure | null>(null);
  readonly showActions = signal(false);
  readonly confirmRefiscalize = signal(false);
  readonly taxRates = signal<Record<string, TaxRateInfo>>({});
  /** undefined = loading, null = could not be checked. */
  readonly attachment = signal<FiscalAttachmentStatus | null | undefined>(undefined);
  readonly attaching = signal(false);
  /** True while the background attachment is being watched after a fiscalisation. */
  readonly attachWatching = signal(false);
  private attachWatchTimer: ReturnType<typeof setTimeout> | null = null;
  /** The original invoice, from the credit note's allocations in Xero (or as chosen here). */
  readonly baseInfo = signal<CreditNoteBaseDocument | null>(null);
  readonly baseLoading = signal(false);
  readonly chosenBaseDocNo = signal<string | null>(null);
  readonly basePickerOpen = signal(false);
  readonly pickerDocNo = signal('');
  readonly pickerThenFiscalize = signal(false);
  readonly baseDocNo = computed(() => this.chosenBaseDocNo() || this.baseInfo()?.resolvedDocNo || null);
  readonly baseIsFiscalized = computed(() => {
    const chosen = this.chosenBaseDocNo();
    if (chosen) return this.candidates().some((c) => c.documentNumber === chosen);
    return !!this.baseInfo()?.resolvedIsFiscalized;
  });
  readonly candidates = computed<BaseInvoiceCandidate[]>(() => this.baseInfo()?.candidates ?? []);
  /** The invoices this credit was applied to in Xero, with whether each is fiscalized here. */
  readonly allocations = computed(() => this.baseInfo()?.allocations ?? []);
  /** HS code per Xero item code for the lines on this credit note. */
  readonly hsCodes = signal<Record<string, ItemHsCode>>({});
  readonly hsLoading = signal(false);

  readonly qrlUrl = computed(() => this.result()?.qrCodeUrl || null);
  readonly hasFiscalError = computed(() => !!this.result()?.validationError);
  readonly busyLabel = computed(() => {
    switch (this.busy()) {
      case 'fiscalize': return 'Fiscalizing…';
      case 'pdf':       return 'Downloading…';
      case 'pdf-fz':    return 'Downloading…';
      case 'print':     return 'Printing…';
      default:          return 'Actions';
    }
  });
  readonly missingHsCount = computed(() => 0);
  /** Why this document cannot be fiscalized in its Xero status, or null when it can. */
  readonly notFiscalisable = computed(() => {
    const d = this.creditNote();
    return d && !this.result() ? notFiscalisableReason(d.status, 'credit note') : null;
  });
  readonly canFiscalize = computed(() => isApproved(this.creditNote()?.status));
  /** The document could not be read at all. */
  readonly loadError = signal<{ message: string; xero: boolean } | null>(null);
  /** Xero refused a later call (fiscalising, attaching) with a reason worth keeping on screen. */
  readonly xeroProblem = signal<string | null>(null);
  readonly returnTo = '/credit-notes/' + (this.route.snapshot.paramMap.get('id') ?? '');

  readonly statusLabel = statusLabel;
  readonly statusClasses = statusClasses;
  readonly date = calendarDate;
  readonly amountsAre = amountsAreLabel;

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) { this.router.navigate(['/credit-notes']); return; }
    const cached = this.fiscalCache.get(id);
    if (cached) this.result.set(cached);
    this.load(id);
    this.taxCodes.rates().subscribe({ next: (r) => this.taxRates.set(r), error: () => {} });
    this.loadBaseInvoice(id);
    // The stored record is the source of truth for "fiscalized".
    this.loadFailure(id);
    this.fiscalizationService.creditNoteRecord(id).subscribe({
      next: (rec) => { this.result.set(rec); this.fiscalCache.cache(id, rec); this.loadAttachment(id); },
      error: () => { /* 404: not fiscalized yet */ }
    });
  }

  /** Why the last attempt failed. A failed lookup leaves the panel hidden rather than showing a false error. */
  private loadFailure(id: string): void {
    this.fiscalizationService.failure('creditnotes', id).subscribe({
      next: (f) => this.failure.set(f),
      error: () => {}
    });
  }

  toggleActions(): void {
    if (this.busy()) return;
    this.showActions.update((v) => !v);
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (this.busy()) return;
    const target = event.target as HTMLElement;
    if (!target.closest('[data-actions-menu]')) this.showActions.set(false);
  }

  isFiscalized(): boolean { return !!this.result(); }

  currency(doc: { currencyCode?: string | null }): string {
    return doc.currencyCode ? `${doc.currencyCode} ` : '';
  }

  /** Lines that are sales. Xero also allows lines that are only a description, which carry no amount. */
  itemLines(doc: { lineItems?: XeroLineItem[] | null }): XeroLineItem[] {
    return (doc.lineItems ?? []).filter((l) => l.quantity != null || l.unitAmount != null || (l.lineAmount ?? 0) !== 0);
  }

  firstLine(text: string | null | undefined): string {
    return (text ?? '').split('\n').map((t) => t.trim()).find((t) => !!t) ?? '';
  }

  /** The description without a first line that only repeats the item's name. */
  restOfDescription(line: XeroLineItem): string {
    const lines = (line.description ?? '').split('\n').map((t) => t.trim()).filter((t) => !!t);
    if (lines[0]?.toLowerCase() === (line.item?.name ?? '').trim().toLowerCase()) lines.shift();
    return lines.join('\n');
  }

  discount(line: XeroLineItem): string {
    if (line.discountRate) return `${this.trimPercent(line.discountRate)}%`;
    if (line.discountAmount) return line.discountAmount.toFixed(2);
    return '—';
  }

  /** The line's tax rate from the organisation's Xero tax rates; worked out from the line when the rate is unknown. */
  taxPercent(line: XeroLineItem, doc: { lineAmountTypes?: string | null }): string {
    if ((doc.lineAmountTypes ?? '').toLowerCase() === 'notax') return '0%';
    const known = line.taxType ? this.taxRates()[line.taxType]?.percent : undefined;
    if (known != null) return `${this.trimPercent(known)}%`;
    const tax = line.taxAmount ?? 0;
    const net = (line.lineAmount ?? 0) - ((doc.lineAmountTypes ?? '').toLowerCase() === 'inclusive' ? tax : 0);
    if (!tax || !net) return line.taxType ? '0%' : '—';
    return `${this.trimPercent(Math.round((tax / net) * 10000) / 100)}%`;
  }

  taxName(line: XeroLineItem): string {
    if (!line.taxType) return '';
    return this.taxRates()[line.taxType]?.name || `Xero tax rate ${line.taxType}`;
  }

  private trimPercent(v: number): string { return Number.isInteger(v) ? String(v) : v.toFixed(2).replace(/0+$/, '').replace(/\.$/, ''); }

  private loadAttachment(id: string): void {
    this.attachment.set(undefined);
    this.fiscalizationService.attachmentStatus('creditnotes', id).subscribe({
      next: (a) => this.attachment.set(a),
      error: () => this.attachment.set(null)
    });
  }

  /** After a fiscalisation the PDF is attached in the background: check every few seconds until it lands. */
  private watchAttachment(id: string, attempt = 0): void {
    if (this.attachWatchTimer) clearTimeout(this.attachWatchTimer);
    this.attachWatching.set(true);
    this.fiscalizationService.attachmentStatus('creditnotes', id).subscribe({
      next: (a) => {
        if (a.attached || attempt >= 12) { this.attachment.set(a); this.attachWatching.set(false); return; }
        this.attachWatchTimer = setTimeout(() => this.watchAttachment(id, attempt + 1), 3000);
      },
      error: () => { this.attachment.set(null); this.attachWatching.set(false); }
    });
  }

  attachNow(): void {
    const doc = this.creditNote();
    if (!doc || this.attaching()) return;
    this.attaching.set(true);
    this.fiscalizationService.attachNow('creditnotes', doc.creditNoteID).subscribe({
      next: (a) => {
        this.attaching.set(false); this.attachment.set(a);
        if (a.attached) this.toast.success('Fiscal PDF attached to the credit note in Xero.');
        else this.toast.error(a.warning || 'Could not attach the fiscal PDF.');
      },
      error: async (err) => { this.attaching.set(false); this.toast.error(await extractErrorMessageAsync(err, 'Could not attach the fiscal PDF.')); }
    });
  }

  hsFor(line: XeroLineItem): ItemHsCode | null {
    const code = line.itemCode?.trim();
    const hs = code ? this.lookupHs(code) : undefined;
    return hs?.hsCode ? hs : null;
  }

  /** Item codes are matched without regard to case, as the API does. */
  private lookupHs(code: string): ItemHsCode | undefined {
    const map = this.hsCodes();
    return map[code] ?? Object.entries(map).find(([k]) => k.toLowerCase() === code.toLowerCase())?.[1];
  }

  hsTitle(hs: ItemHsCode): string {
    return hs.hsCodeSource === 'Default' ? 'Company default HS code from Fiscalization settings' : 'Saved on the Items page';
  }

  private load(id: string): void {
    this.documents.get(id).subscribe({
      next: (doc) => { this.loadError.set(null); this.creditNote.set(doc); this.loadHsCodes(doc); },
      error: (err) => {
        const message = extractErrorMessage(err, 'Failed to load the credit note.');
        // Keep what is on screen when a refresh after an action fails; say why when nothing could be shown.
        if (this.creditNote()) { this.toast.error(message); return; }
        this.loadError.set({
          message: err?.status === 404 ? 'Xero has no credit note with this id in the connected organisation. It may have been deleted, or belong to another organisation.' : message,
          xero: isXeroAttention(err) || err?.status === 429
        });
      }
    });
  }

  private loadHsCodes(doc: { lineItems?: XeroLineItem[] | null }): void {
    const codes = [...new Set(this.itemLines(doc).map((l) => l.itemCode?.trim()).filter((v): v is string => !!v))];
    if (codes.length === 0) { this.hsCodes.set({}); return; }
    this.hsLoading.set(true);
    this.taxDetails.hsCodesFor(codes).subscribe({
      next: (map) => { this.hsCodes.set(map ?? {}); this.hsLoading.set(false); },
      error: () => this.hsLoading.set(false)
    });
  }

  private loadBaseInvoice(id: string): void {
    this.baseLoading.set(true);
    this.fiscalizationService.creditNoteBaseDocument(id).subscribe({
      next: (info) => { this.baseInfo.set(info); this.baseLoading.set(false); },
      error: () => this.baseLoading.set(false)
    });
  }

  openBasePicker(thenFiscalize = false): void {
    this.showActions.set(false);
    this.pickerThenFiscalize.set(thenFiscalize);
    this.pickerDocNo.set(this.baseDocNo() ?? '');
    this.basePickerOpen.set(true);
  }

  confirmBasePick(): void {
    const docNo = this.pickerDocNo().trim();
    if (!docNo) return;
    this.chosenBaseDocNo.set(docNo);
    this.basePickerOpen.set(false);
    if (this.pickerThenFiscalize()) this.submitFiscalize();
  }

  /** Fiscalize needs the original invoice; ask for it first when the credit is not allocated to a fiscalized invoice in Xero. */
  fiscalize(): void {
    if (!this.baseDocNo() || !this.baseIsFiscalized()) { this.openBasePicker(true); return; }
    this.submitFiscalize();
  }

  private submitFiscalize(): void {
    const inv = this.creditNote();
    if (!inv) return;
    this.busy.set('fiscalize');
    this.xeroProblem.set(null);
    this.showActions.set(true);
    this.fiscalizationService.fiscalizeCreditNote(inv.creditNoteID, this.chosenBaseDocNo()).subscribe({
      next: (res) => { this.failure.set(null); this.handleFiscalResult(inv, res); },
      error: (err) => {
        this.busy.set(null); this.showActions.set(false);
        const message = extractErrorMessage(err, 'Fiscalization failed.');
        if (isXeroAttention(err) || err?.status === 429) this.xeroProblem.set(message); else this.toast.error(message);
        this.loadFailure(inv.creditNoteID);
      }
    });
  }

  refiscalize(): void {
    this.confirmRefiscalize.set(false);
    const inv = this.creditNote();
    if (!inv) return;
    this.busy.set('fiscalize');
    this.xeroProblem.set(null);
    this.showActions.set(true);
    this.fiscalizationService.refiscalizeCreditNote(inv.creditNoteID, this.chosenBaseDocNo()).subscribe({
      next: (res) => this.handleFiscalResult(inv, res),
      error: (err) => { this.busy.set(null); this.showActions.set(false); this.toast.error(extractErrorMessage(err, 'Refiscalization failed.')); }
    });
  }

  downloadFiscalizePdf(): void {
    const inv = this.creditNote();
    if (!inv || this.busy()) return;
    this.busy.set('pdf-fz'); this.showActions.set(true);
    this.fiscalizationService.fiscalizeCreditNotePdf(inv.creditNoteID).subscribe({
      next: (blob) => { downloadBlob(blob, `${inv.creditNoteNumber || 'credit-note'}-fiscalize.pdf`); this.busy.set(null); this.showActions.set(false); },
      error: async (err) => { this.busy.set(null); this.showActions.set(false); this.toast.error(await extractErrorMessageAsync(err, 'Could not download the Fiscalize PDF.')); }
    });
  }

  private handleFiscalResult(inv: CreditNote, res: FiscalizeResult): void {
    this.busy.set(null); this.showActions.set(false);
    if (res.validationError && !res.receiptNumber) { this.toast.error(res.validationError); return; }
    this.result.set(res);
    this.fiscalCache.cache(inv.creditNoteID, res);
    if (res.attachmentWarning) this.toast.warning(res.attachmentWarning);
    const summary = res.receiptNumber ? `Receipt #${res.receiptNumber} · Verification ${res.verificationCode ?? '—'}` : 'Submitted to FDMS.';
    const headline = res.alreadyFiscalized
      ? `Credit note ${inv.creditNoteNumber || 'credit-note'} was already fiscalized. Fiscal data updated from the platform.`
      : `Credit note ${inv.creditNoteNumber || 'credit-note'} fiscalized.`;
    if (res.writebackWarning) {
      this.toast.warning(`${headline} Writing the result back to Xero failed: ${res.writebackWarning}`);
    } else {
      this.toast.success(`${headline} ${summary}`);
    }
    this.load(inv.creditNoteID);
    if (res.attachmentQueued) this.watchAttachment(inv.creditNoteID); else this.loadAttachment(inv.creditNoteID);
  }

  downloadStampedPdf(): void {
    const inv = this.creditNote();
    if (!inv || this.busy()) return;
    this.busy.set('pdf'); this.showActions.set(true);
    this.fiscalizationService.stampedCreditNotePdf(inv.creditNoteID).subscribe({
      next: (blob) => { downloadBlob(blob, `${inv.creditNoteNumber || 'credit-note'}-fiscal.pdf`); this.busy.set(null); this.showActions.set(false); },
      error: async (err) => { this.busy.set(null); this.showActions.set(false); this.toast.error(await extractErrorMessageAsync(err, 'Could not produce the stamped PDF.')); }
    });
  }

  /** The credit note drawn with the company's Fiscalize layout for Xero (or Fiscalize's standard design). */
  downloadLayoutPdf(): void {
    const inv = this.creditNote();
    if (!inv || this.busy()) return;
    this.busy.set('pdf-layout'); this.showActions.set(true);
    this.fiscalizationService.layoutPdf('creditnotes', inv.creditNoteID).subscribe({
      next: (blob) => { downloadBlob(blob, `${inv.creditNoteNumber || 'credit-note'}-fiscal.pdf`); this.busy.set(null); this.showActions.set(false); },
      error: async (err) => { this.busy.set(null); this.showActions.set(false); this.toast.error(await extractErrorMessageAsync(err, 'Could not draw the Fiscalize layout.')); }
    });
  }

  printStampedPdf(): void {
    const inv = this.creditNote();
    if (!inv || this.busy()) return;
    this.busy.set('print'); this.showActions.set(true);
    this.fiscalizationService.stampedCreditNotePdf(inv.creditNoteID).subscribe({
      next: (blob) => { printBlob(blob); this.busy.set(null); this.showActions.set(false); },
      error: async (err) => { this.busy.set(null); this.showActions.set(false); this.toast.error(await extractErrorMessageAsync(err, 'Could not produce the stamped PDF.')); }
    });
  }
}
