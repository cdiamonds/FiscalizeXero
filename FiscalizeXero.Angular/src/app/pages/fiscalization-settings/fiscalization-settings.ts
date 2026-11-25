import { ManageNotice } from '../../components/manage-notice/manage-notice';
import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { FormsModule } from '@angular/forms';
import { ClientConfigService } from '../../core/services/xero/client-config.service';
import { StampPrefs, FiscalStampPosition } from '../../core/services/preferences/preferences.service';
import { ToastService } from '../../core/services/toast/toast.service';
import { extractErrorMessage } from '../../core/utils/http-error';
import { PageHeader } from '../../components/page-header/page-header';
import { RouterLink } from '@angular/router';
import { HsCodeDialog } from '../../components/hs-code-dialog/hs-code-dialog';
import { HsCodesService } from '../../core/services/xero/hs-codes.service';
import { HsCodeLookupItem, FiscalizationValidationMode, FiscalLayoutStatus, FiscalPdfLayout, LayoutPaper } from '../../core/models/xero.models';
import { Subscription } from 'rxjs';

const STAMP_POSITIONS: { value: FiscalStampPosition; label: string }[] = [
  { value: 'top-left',      label: 'Top Left' },
  { value: 'top-center',    label: 'Top Center' },
  { value: 'top-right',     label: 'Top Right' },
  { value: 'bottom-left',   label: 'Bottom Left' },
  { value: 'bottom-center', label: 'Bottom Center' },
  { value: 'bottom-right',  label: 'Bottom Right' },
];

const MARGIN_FIELDS: { field: keyof StampPrefs & string; label: string }[] = [
  { field: 'fiscalStampMarginTop',    label: 'Top' },
  { field: 'fiscalStampMarginBottom', label: 'Bottom' },
  { field: 'fiscalStampMarginLeft',   label: 'Left' },
  { field: 'fiscalStampMarginRight',  label: 'Right' },
];

@Component({
  selector: 'app-fiscalization-settings',
  imports: [CommonModule, ManageNotice, ReactiveFormsModule, FormsModule, PageHeader, RouterLink, HsCodeDialog],
  template: `
    <app-page-header title="Fiscalization" subtitle="Configure automatic fiscalization, the default HS code, and how fiscal stamps appear on your PDFs.">
      <button (click)="saveAll()" [disabled]="saving() || loading() || stampLoading() || defaultCheck() === 'invalid' || defaultCheck() === 'validating'"
              class="inline-flex items-center gap-2 rounded-xl bg-primary-500 px-4 py-2 text-sm font-semibold text-white shadow-sm transition hover:bg-primary-600 active:scale-95 disabled:opacity-50 disabled:cursor-not-allowed focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-2 dark:focus:ring-offset-slate-900">
        @if (saving()) {
          <svg class="h-4 w-4 animate-spin" viewBox="0 0 24 24" fill="none">
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/>
            <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/>
          </svg>
          Saving…
        } @else {
          <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7"/>
          </svg>
          Save settings
        }
      </button>
    </app-page-header>
    <app-manage-notice />

    <!-- ── Auto-fiscalize ─────────────────────────────────────────────── -->
    <section class="rounded-2xl border border-slate-200/70 bg-white shadow-sm fade-in-down dark:border-white/10 dark:bg-slate-900 overflow-hidden">

      <!-- Section header -->
      <div class="flex items-center justify-between gap-4 border-b border-slate-100 px-6 py-4 dark:border-white/10">
        <div class="flex items-center gap-3">
          <div class="flex h-8 w-8 items-center justify-center rounded-lg bg-brand-50 dark:bg-primary-500/15">
            <svg class="h-4 w-4 text-primary-600 dark:text-brand-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 10V3L4 14h7v7l9-11h-7z"/>
            </svg>
          </div>
          <div>
            <p class="text-sm font-semibold text-slate-900 dark:text-white">Auto-fiscalization</p>
            <p class="text-xs text-slate-500 dark:text-slate-400">Automatically submit documents to FDMS when they are approved in Xero</p>
          </div>
        </div>
        <div></div>
      </div>

      <!-- Form body -->
      <form [formGroup]="form">
        @if (loading()) {
          <div class="flex items-center justify-center gap-3 px-6 py-10">
            <div class="h-6 w-6 animate-spin rounded-full border-4 border-brand-200 border-t-primary-500"></div>
            <span class="text-sm text-slate-500 dark:text-slate-400">Loading settings…</span>
          </div>
        }

        @if (!loading()) {
          <div class="divide-y divide-slate-100 dark:divide-white/10">

            <label class="flex cursor-pointer items-start gap-4 px-6 py-4 transition-colors hover:bg-slate-50/80 dark:hover:bg-white/[0.03]">
              <input type="checkbox" formControlName="autoFiscalizeInvoices" class="mt-0.5 h-4 w-4 flex-shrink-0 rounded border-slate-300 text-primary-500 accent-primary-500 focus:ring-primary-500 focus:ring-offset-0" />
              <div class="flex-1 min-w-0">
                <div class="flex items-center gap-2">
                  <span class="text-sm font-medium text-slate-900 dark:text-white">Auto-fiscalize invoices</span>
                  <span class="rounded-full bg-brand-50 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-primary-600 ring-1 ring-brand-200 dark:bg-primary-500/10 dark:text-brand-400 dark:ring-primary-500/30">Recommended</span>
                </div>
                <p class="mt-0.5 text-xs text-slate-500 dark:text-slate-400">Submit sales invoices to FDMS as soon as they are approved in Xero (status Awaiting payment). Drafts and invoices awaiting approval are left alone.</p>
              </div>
            </label>

            <label class="flex cursor-pointer items-start gap-4 px-6 py-4 transition-colors hover:bg-slate-50/80 dark:hover:bg-white/[0.03]">
              <input type="checkbox" formControlName="autoFiscalizeCreditNotes" class="mt-0.5 h-4 w-4 flex-shrink-0 rounded border-slate-300 text-primary-500 accent-primary-500 focus:ring-primary-500 focus:ring-offset-0" />
              <div class="flex-1 min-w-0">
                <span class="text-sm font-medium text-slate-900 dark:text-white">Auto-fiscalize credit notes</span>
                <p class="mt-0.5 text-xs text-slate-500 dark:text-slate-400">Submit customer credit notes to FDMS once they are approved. ZIMRA credits a specific receipt, so the credit note must be allocated to a fiscalized invoice in Xero; until then it waits, and is fiscalized as soon as it is allocated or you choose the invoice on the credit note page.</p>
              </div>
            </label>

          </div>

          <div class="flex flex-col gap-3 border-t border-slate-100 bg-slate-50/60 px-6 py-4 text-xs text-slate-600 sm:flex-row sm:items-center sm:justify-between dark:border-white/10 dark:bg-white/[0.03] dark:text-slate-300">
            <p>
              <span class="font-semibold text-slate-800 dark:text-white">How fast is automatic?</span>
              With Xero webhooks, documents are fiscalised within about a minute of being approved. Without them, a check every few hours picks them up.
            </p>
            <a routerLink="/webhooks" class="inline-flex flex-shrink-0 items-center gap-1.5 font-semibold text-primary-600 hover:underline dark:text-brand-400">
              Set up webhooks
              <svg class="h-3.5 w-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5l7 7-7 7"/></svg>
            </a>
          </div>
        }
      </form>
    </section>

    <!-- ── Buyer validation ───────────────────────────────────────────────── -->
    <section class="mt-5 rounded-2xl border border-slate-200/70 bg-white shadow-sm fade-in-down dark:border-white/10 dark:bg-slate-900 overflow-hidden">
      <div class="flex flex-col gap-3 border-b border-slate-100 px-6 py-4 sm:flex-row sm:items-center sm:justify-between dark:border-white/10">
        <div class="flex items-center gap-3">
          <div class="flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-lg bg-brand-50 dark:bg-primary-500/15">
            <svg class="h-4 w-4 text-primary-600 dark:text-brand-400" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z"/></svg>
          </div>
          <div>
            <p class="text-sm font-semibold text-slate-900 dark:text-white">Buyer validation</p>
            <p class="text-xs text-slate-500 dark:text-slate-400">What happens when a customer with a TIN is missing details ZIMRA asks for</p>
          </div>
        </div>
        <div role="radiogroup" aria-label="Buyer validation mode" class="inline-flex flex-shrink-0 self-start rounded-xl border border-slate-200 bg-slate-50 p-1 sm:self-auto dark:border-white/10 dark:bg-white/5">
          @for (m of validationModes; track m) {
            <button type="button" role="radio" [attr.aria-checked]="validationMode() === m" (click)="validationMode.set(m)" [disabled]="loading()"
                    class="rounded-lg px-4 py-1.5 text-sm font-semibold transition focus:outline-none focus-visible:ring-2 focus-visible:ring-primary-500"
                    [ngClass]="validationMode() === m
                      ? 'bg-white text-primary-700 shadow-sm ring-1 ring-slate-200 dark:bg-slate-800 dark:text-brand-300 dark:ring-white/10'
                      : 'text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-slate-200'">{{ m }}</button>
          }
        </div>
      </div>

      <div class="grid gap-4 px-6 py-5 md:grid-cols-2">
        <div class="rounded-xl border p-4 transition" [ngClass]="validationMode() === 'Strict' ? 'border-primary-300 bg-brand-50/50 ring-1 ring-primary-200 dark:border-primary-500/40 dark:bg-primary-500/10 dark:ring-primary-500/20' : 'border-slate-200 dark:border-white/10'">
          <div class="mb-1.5 flex items-center gap-2">
            <span class="text-sm font-semibold text-slate-900 dark:text-white">Strict</span>
            <span class="rounded-full bg-brand-50 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-primary-600 ring-1 ring-brand-200 dark:bg-primary-500/10 dark:text-brand-400 dark:ring-primary-500/30">Recommended</span>
          </div>
          <p class="text-xs leading-relaxed text-slate-600 dark:text-slate-300">
            Every buyer detail must be on the Xero contact or typed on the Customers page: phone (Phone, Mobile or Direct dial), email, house/building number,
            street, city and province. If any is missing the document isn't sent, and it shows as <span class="font-semibold text-rose-600 dark:text-rose-400">Failed</span> with exactly what to add.
          </p>
        </div>
        <div class="rounded-xl border p-4 transition" [ngClass]="validationMode() === 'Lenient' ? 'border-amber-300 bg-amber-50/60 ring-1 ring-amber-200 dark:border-amber-500/40 dark:bg-amber-500/10 dark:ring-amber-500/20' : 'border-slate-200 dark:border-white/10'">
          <p class="mb-1.5 text-sm font-semibold text-slate-900 dark:text-white">Lenient</p>
          <p class="text-xs leading-relaxed text-slate-600 dark:text-slate-300">Only the missing details are filled with placeholders, so the document still goes through:</p>
          <ul class="mt-1.5 grid grid-cols-1 gap-x-4 gap-y-0.5 pl-4 text-xs text-slate-600 list-disc sm:grid-cols-2 dark:text-slate-300">
            <li>Phone → <span class="font-mono">0770000000</span></li>
            <li>Email → <span class="font-mono">customer&#64;email.com</span></li>
            <li>House no. → <span class="font-mono">01</span></li>
            <li>Street / city / province → <span class="font-mono">Harare</span></li>
          </ul>
        </div>
      </div>
      <div class="flex flex-col gap-2 border-t border-slate-100 bg-slate-50/60 px-6 py-3 text-xs text-slate-600 sm:flex-row sm:items-center sm:justify-between dark:border-white/10 dark:bg-white/[0.03] dark:text-slate-300">
        <p>In both modes, customers without a TIN are fiscalized as cash sales, and an invalid TIN or VAT number always stops fiscalization.</p>
        <a routerLink="/customers" class="inline-flex flex-shrink-0 items-center gap-1.5 font-semibold text-primary-600 hover:underline dark:text-brand-400">
          Check customer compliance
          <svg class="h-3.5 w-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5l7 7-7 7"/></svg>
        </a>
      </div>
    </section>

    <!-- ── Writing back to Xero ───────────────────────────────────────────── -->
    <section class="mt-5 rounded-2xl border border-slate-200/70 bg-white shadow-sm fade-in-down dark:border-white/10 dark:bg-slate-900 overflow-hidden" data-testid="writeback-section">
      <div class="border-b border-slate-100 bg-slate-50/80 px-6 py-4 dark:border-white/10 dark:bg-white/[0.03]">
        <div class="flex items-center gap-3">
          <div class="flex h-8 w-8 items-center justify-center rounded-lg bg-brand-100 dark:bg-primary-500/20">
            <svg class="h-4 w-4 text-primary-600 dark:text-brand-400" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"/></svg>
          </div>
          <div>
            <p class="text-sm font-semibold text-slate-900 dark:text-white">Writing back to Xero</p>
            <p class="text-xs text-slate-500 dark:text-slate-400">Where the fiscal result is recorded on the invoice or credit note in Xero. Amounts, dates and lines are never changed.</p>
          </div>
        </div>
      </div>
      <form [formGroup]="form" class="divide-y divide-slate-100 dark:divide-white/10">
        <label class="flex cursor-pointer items-start gap-4 px-6 py-4 transition-colors hover:bg-slate-50/80 dark:hover:bg-white/[0.03]">
          <input type="checkbox" formControlName="writebackHistoryNote" class="mt-0.5 h-4 w-4 flex-shrink-0 rounded border-slate-300 text-primary-500 accent-primary-500 focus:ring-primary-500 focus:ring-offset-0" />
          <div class="min-w-0 flex-1">
            <div class="flex items-center gap-2">
              <span class="text-sm font-medium text-slate-900 dark:text-white">Add a history note</span>
              <span class="rounded-full bg-brand-50 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-primary-600 ring-1 ring-brand-200 dark:bg-primary-500/10 dark:text-brand-400 dark:ring-primary-500/30">Recommended</span>
            </div>
            <p class="mt-0.5 text-xs text-slate-500 dark:text-slate-400">The receipt number, fiscal day, device, verification code and verification link are added under “History and notes” on the document. Notes work on paid invoices too, and cannot be edited afterwards.</p>
          </div>
        </label>
        <label class="flex items-start gap-4 px-6 py-4 transition-colors" [ngClass]="stamp()?.fiscalStampEnabled ? 'cursor-pointer hover:bg-slate-50/80 dark:hover:bg-white/[0.03]' : 'opacity-60'">
          <input type="checkbox" formControlName="attachmentIncludeOnline" class="mt-0.5 h-4 w-4 flex-shrink-0 rounded border-slate-300 text-primary-500 accent-primary-500 focus:ring-primary-500 focus:ring-offset-0" />
          <div class="min-w-0 flex-1">
            <span class="text-sm font-medium text-slate-900 dark:text-white">Show the attachment on the online invoice</span>
            <p class="mt-0.5 text-xs text-slate-500 dark:text-slate-400">Your customer sees the stamped fiscal PDF on the invoice link Xero sends them. Applies to the PDF attached by “Attach the fiscal PDF in Xero” below<ng-container *ngIf="stamp() && !stamp()!.fiscalStampEnabled">, which is switched off</ng-container>.</p>
          </div>
        </label>
        <label class="flex cursor-pointer items-start gap-4 px-6 py-4 transition-colors hover:bg-slate-50/80 dark:hover:bg-white/[0.03]">
          <input type="checkbox" formControlName="writebackReference" class="mt-0.5 h-4 w-4 flex-shrink-0 rounded border-slate-300 text-primary-500 accent-primary-500 focus:ring-primary-500 focus:ring-offset-0" />
          <div class="min-w-0 flex-1">
            <span class="text-sm font-medium text-slate-900 dark:text-white">Write the fiscal reference into Reference</span>
            <p class="mt-0.5 text-xs text-slate-500 dark:text-slate-400">Adds a short fiscal marker after whatever is already in the document's Reference. Off by default, because Reference often holds your customer's purchase order number. Xero refuses the change on credit notes that are already allocated; that shows as a warning and nothing else is affected.</p>
          </div>
        </label>
      </form>
    </section>

    <!-- ── Fiscal Stamp on PDF ─────────────────────────────────────────── -->
    <section class="mt-5 rounded-2xl border border-slate-200/70 bg-white shadow-sm fade-in-down dark:border-white/10 dark:bg-slate-900 overflow-hidden">

      <!-- Section header with toggle -->
      <div class="flex items-center justify-between gap-4 border-b border-slate-100 px-6 py-4 dark:border-white/10">
        <div class="flex items-center gap-3">
          <div class="flex h-8 w-8 items-center justify-center rounded-lg bg-brand-50 dark:bg-primary-500/15">
            <svg class="h-4 w-4 text-primary-600 dark:text-brand-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
                d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"/>
            </svg>
          </div>
          <div>
            <p class="text-sm font-semibold text-slate-900 dark:text-white">Attach the fiscal PDF in Xero</p>
            <p class="text-xs text-slate-500 dark:text-slate-400">On by default. After each fiscalization a fiscal PDF is attached to the invoice or credit note in Xero: Xero's own PDF with the ZIMRA stamp, or a Fiscalize layout. Off: every version can still be downloaded or attached from the document page.</p>
          </div>
        </div>

        <!-- Master toggle -->
        <div class="flex items-center gap-3 flex-shrink-0">
          @if (stamp()) {
            <button (click)="toggleStampEnabled()"
                    [attr.aria-checked]="stamp()!.fiscalStampEnabled"
                    role="switch"
                    [class]="'relative inline-flex h-6 w-11 flex-shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 ease-in-out focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-2 dark:focus:ring-offset-slate-900 ' +
                             (stamp()!.fiscalStampEnabled ? 'bg-primary-500' : 'bg-slate-200 dark:bg-slate-700')">
              <span [class]="'pointer-events-none inline-block h-5 w-5 transform rounded-full bg-white shadow ring-0 transition-transform duration-200 ease-in-out ' +
                            (stamp()!.fiscalStampEnabled ? 'translate-x-5' : 'translate-x-0')"></span>
            </button>
          }
        </div>
      </div>

      <!-- Loading -->
      @if (stampLoading()) {
        <div class="flex items-center justify-center gap-3 px-6 py-10">
          <div class="h-6 w-6 animate-spin rounded-full border-4 border-brand-200 border-t-primary-500"></div>
          <span class="text-sm text-slate-500 dark:text-slate-400">Loading stamp settings…</span>
        </div>
      }

      <!-- Body -->
      @if (!stampLoading() && stamp()) {
      <div class="p-6">

        <!-- ── Which PDF is attached ─────────────────────────────────────── -->
        <fieldset class="mb-8 transition-opacity duration-200" [class.opacity-50]="!stamp()!.fiscalStampEnabled">
          <legend class="text-sm font-semibold text-slate-900 dark:text-white">Which PDF goes to Xero</legend>
          <p class="mt-0.5 text-xs text-slate-500 dark:text-slate-400">
            Whichever you choose, the document is fiscalised the same way. If a Fiscalize layout cannot be made at that moment, Xero's PDF with the stamp is attached instead, so your customer always gets a fiscal document.
          </p>
          <div class="mt-3 grid gap-3 md:grid-cols-3" role="radiogroup" aria-label="PDF attached in Xero">
            @for (option of pdfOptions; track option.value) {
              <button type="button" role="radio" [attr.aria-checked]="pdfLayout() === option.value"
                      (click)="choosePdf(option.value)"
                      class="group relative flex h-full flex-col items-start gap-1.5 rounded-xl border p-4 text-left transition focus:outline-none focus-visible:ring-2 focus-visible:ring-primary-500"
                      [ngClass]="pdfLayout() === option.value
                        ? 'border-primary-500 bg-brand-50/70 ring-1 ring-primary-500 dark:border-primary-400 dark:bg-primary-500/10'
                        : 'border-slate-200 bg-white hover:border-slate-300 hover:bg-slate-50 dark:border-white/10 dark:bg-slate-900 dark:hover:bg-white/[0.03]'">
                <span class="flex w-full items-center justify-between gap-2">
                  <span class="text-sm font-semibold text-slate-900 dark:text-white">{{ option.label }}</span>
                  <span class="flex h-4 w-4 flex-shrink-0 items-center justify-center rounded-full border"
                        [ngClass]="pdfLayout() === option.value ? 'border-primary-500 bg-primary-500' : 'border-slate-300 dark:border-slate-600'" aria-hidden="true">
                    @if (pdfLayout() === option.value) { <span class="h-1.5 w-1.5 rounded-full bg-white"></span> }
                  </span>
                </span>
                <span class="text-xs leading-relaxed text-slate-500 dark:text-slate-400">{{ option.help }}</span>
                @if (option.value === 'XeroStamp') {
                  <span class="mt-1 inline-flex rounded-full bg-slate-100 px-2 py-0.5 text-[11px] font-medium text-slate-600 dark:bg-white/10 dark:text-slate-300">Default</span>
                }
              </button>
            }
          </div>

          @if (pdfLayout() === 'FiscalizeLayout') {
            <div class="mt-4 rounded-xl border border-slate-200/80 bg-slate-50/70 p-4 dark:border-white/10 dark:bg-white/[0.03]">
              <div class="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
                <div class="min-w-0">
                  <p class="text-sm font-medium text-slate-900 dark:text-white">Your layouts</p>
                  <p class="mt-0.5 text-xs text-slate-500 dark:text-slate-400">
                    Designed on the Fiscalize platform under Plugin layouts, for <span class="font-medium">Xero</span>. Until you make one live, Fiscalize's standard design is used with your company letterhead and logo.
                  </p>
                </div>
                <label class="flex flex-shrink-0 items-center gap-2 text-sm">
                  <span class="text-xs font-medium text-slate-600 dark:text-slate-300">Paper</span>
                  <select class="rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-sm text-slate-900 focus:border-primary-500 focus:ring-primary-500 dark:border-white/15 dark:bg-slate-800 dark:text-white"
                          [ngModel]="layoutPaper()" (ngModelChange)="layoutPaper.set($event)" aria-label="Layout paper">
                    @for (p of papers; track p.value) { <option [value]="p.value">{{ p.label }}</option> }
                  </select>
                </label>
              </div>

              <ul class="mt-4 grid gap-2 sm:grid-cols-2" aria-live="polite">
                @for (row of layoutRows(); track row.documentType) {
                  <li class="flex items-center justify-between gap-3 rounded-lg border border-slate-200 bg-white px-3 py-2.5 dark:border-white/10 dark:bg-slate-900">
                    <span class="text-sm text-slate-700 dark:text-slate-200">{{ row.documentType === 'CreditNote' ? 'Credit notes' : 'Invoices' }}</span>
                    @if (layoutsLoading()) {
                      <span class="h-4 w-24 animate-pulse rounded bg-slate-200 dark:bg-slate-700"></span>
                    } @else if (!row.available) {
                      <span class="rounded-full bg-amber-50 px-2 py-0.5 text-xs font-medium text-amber-700 dark:bg-amber-500/10 dark:text-amber-300" title="Attaches Xero's PDF with the stamp until a layout is available">Not available yet</span>
                    } @else if (row.isStandardDesign) {
                      <span class="rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-700 dark:bg-white/10 dark:text-slate-200">Fiscalize standard design</span>
                    } @else {
                      <span class="rounded-full bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-300">Your layout, version {{ row.version }}</span>
                    }
                  </li>
                }
              </ul>
              @if (layoutPaper() !== savedPaper()) {
                <p class="mt-2 text-xs text-slate-500 dark:text-slate-400">Save to see the layouts for {{ layoutPaper() }}.</p>
              }

              <div class="mt-4 flex flex-wrap items-center gap-2">
                @if (designerUrl()) {
                  <a [href]="designerUrl()" target="_blank" rel="noopener"
                     class="inline-flex items-center gap-1.5 rounded-lg bg-primary-500 px-3 py-2 text-xs font-semibold text-white shadow-sm transition hover:bg-primary-600">
                    Design layouts on Fiscalize
                    <svg class="h-3.5 w-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14"/></svg>
                  </a>
                }
                <button type="button" (click)="loadLayouts(true)" [disabled]="layoutsLoading()"
                        class="inline-flex items-center gap-1.5 rounded-lg border border-slate-300 bg-white px-3 py-2 text-xs font-medium text-slate-700 transition hover:bg-slate-50 disabled:opacity-50 dark:border-white/15 dark:bg-slate-800 dark:text-slate-200 dark:hover:bg-slate-700">
                  <svg class="h-3.5 w-3.5" [class.animate-spin]="layoutsLoading()" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"/></svg>
                  Check for new layouts
                </button>
              </div>
            </div>
          }

          @if (pdfLayout() === 'FiscalizeStandard') {
            <p class="mt-3 rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-600 dark:bg-white/[0.03] dark:text-slate-300">
              The same fiscal invoice the Fiscalize platform keeps for the receipt. Your Xero branding theme is not used.
            </p>
          }
        </fieldset>

        @if (pdfLayout() !== 'XeroStamp') {
          <p class="mb-3 text-xs font-medium text-slate-500 dark:text-slate-400">Stamp position (used for Xero's PDF, including when a layout falls back to it)</p>
        }

        <!-- Two-column layout: visual left, info right -->
        <div class="flex flex-col gap-8 lg:flex-row lg:items-start lg:gap-10">

        <!-- ── LEFT: A4 page + margin annotation controls ──────────────── -->
        <!-- Dim the visual editor when stamping is disabled; info panel stays readable -->
        <div class="flex flex-col items-center gap-2 flex-shrink-0 transition-opacity duration-200"
             [class.opacity-40]="!stamp()!.fiscalStampEnabled"
             [class.pointer-events-none]="!stamp()!.fiscalStampEnabled">

          <p class="mb-2 self-start text-xs text-slate-500 dark:text-slate-400">
            Click a zone on the page to set stamp position · Adjust margins with the controls around it
          </p>

          <!-- Outer grid: margin controls orbit the A4 page -->
          <!-- cols: [left-ctrl 80px] [page 190px] [right-ctrl 80px] -->
          <!-- rows: [top-ctrl 56px] [page body] [bottom-ctrl 56px] -->
          <div style="display:grid;grid-template-columns:80px 190px 80px;grid-template-rows:56px auto 56px;align-items:stretch;">

            <!-- ┌─────┐ top-left empty corner -->
            <div></div>

            <!-- ↑ TOP margin control -->
            <div class="flex flex-col items-center justify-end gap-1 pb-1.5">
              <span class="text-[9px] font-bold uppercase tracking-widest text-primary-500 dark:text-brand-400">Top</span>
              <div class="flex items-center gap-1.5">
                <button type="button" (click)="adjustMargin('fiscalStampMarginTop', -1)"
                        class="flex h-6 w-6 items-center justify-center rounded-lg border border-brand-200 bg-white text-primary-600 shadow-sm transition hover:bg-brand-50 active:scale-95 dark:border-primary-500/40 dark:bg-primary-500/10 dark:text-brand-400 dark:hover:bg-primary-500/15">
                  <svg class="h-3 w-3" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2.5" d="M20 12H4"/></svg>
                </button>
                <span class="w-8 text-center text-sm font-bold tabular-nums text-slate-800 dark:text-white">{{ getMargin('fiscalStampMarginTop') }}</span>
                <button type="button" (click)="adjustMargin('fiscalStampMarginTop', 1)"
                        class="flex h-6 w-6 items-center justify-center rounded-lg border border-brand-200 bg-white text-primary-600 shadow-sm transition hover:bg-brand-50 active:scale-95 dark:border-primary-500/40 dark:bg-primary-500/10 dark:text-brand-400 dark:hover:bg-primary-500/15">
                  <svg class="h-3 w-3" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2.5" d="M12 4v16M4 12h16"/></svg>
                </button>
              </div>
              <!-- tick line connecting to page top edge -->
              <div class="h-2 w-px bg-brand-300 dark:bg-primary-600/60"></div>
            </div>

            <!-- ┐ top-right empty corner -->
            <div></div>

            <!-- ← LEFT margin control -->
            <div class="flex flex-row items-center justify-center gap-1.5">
              <!-- "Left" label — vertical, reads bottom-to-top -->
              <span style="writing-mode:vertical-rl;transform:rotate(180deg)"
                    class="text-[9px] font-bold uppercase tracking-widest text-primary-500 dark:text-brand-400 select-none">Left</span>
              <!-- Stepper -->
              <div class="flex flex-col items-center gap-1.5">
                <button type="button" (click)="adjustMargin('fiscalStampMarginLeft', -1)"
                        class="flex h-6 w-6 items-center justify-center rounded-lg border border-brand-200 bg-white text-primary-600 shadow-sm transition hover:bg-brand-50 active:scale-95 dark:border-primary-500/40 dark:bg-primary-500/10 dark:text-brand-400 dark:hover:bg-primary-500/15">
                  <svg class="h-3 w-3" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2.5" d="M20 12H4"/></svg>
                </button>
                <span class="text-sm font-bold tabular-nums text-slate-800 dark:text-white">{{ getMargin('fiscalStampMarginLeft') }}</span>
                <button type="button" (click)="adjustMargin('fiscalStampMarginLeft', 1)"
                        class="flex h-6 w-6 items-center justify-center rounded-lg border border-brand-200 bg-white text-primary-600 shadow-sm transition hover:bg-brand-50 active:scale-95 dark:border-primary-500/40 dark:bg-primary-500/10 dark:text-brand-400 dark:hover:bg-primary-500/15">
                  <svg class="h-3 w-3" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2.5" d="M12 4v16M4 12h16"/></svg>
                </button>
              </div>
              <!-- tick line to page edge -->
              <div class="h-px w-2 bg-brand-300 dark:bg-primary-600/60"></div>
            </div>

            <!-- The A4 page itself -->
            <div class="relative border-2 border-slate-300 bg-white shadow-xl dark:border-slate-600 dark:bg-slate-950"
                 style="width:190px;height:268px;">

              <!-- Simulated page content (non-interactive) -->
              <div class="pointer-events-none absolute left-14 right-8 top-14 space-y-1.5">
                <div class="h-2 w-20 rounded-sm bg-slate-300 dark:bg-slate-600"></div>
                <div class="h-px w-full bg-slate-200 dark:bg-slate-700"></div>
                <div class="h-1.5 w-full rounded-sm bg-slate-100 dark:bg-slate-800"></div>
                <div class="h-1.5 w-4/5 rounded-sm bg-slate-100 dark:bg-slate-800"></div>
                <div class="h-1.5 w-full rounded-sm bg-slate-100 dark:bg-slate-800"></div>
                <div class="h-1.5 w-3/4 rounded-sm bg-slate-100 dark:bg-slate-800"></div>
                <div class="h-px w-full bg-slate-100 dark:bg-slate-700"></div>
                <div class="h-1.5 w-2/5 rounded-sm bg-slate-200 dark:bg-slate-700"></div>
                <div class="h-1.5 w-full rounded-sm bg-slate-100 dark:bg-slate-800"></div>
                <div class="h-1.5 w-4/5 rounded-sm bg-slate-100 dark:bg-slate-800"></div>
                <div class="h-1.5 w-full rounded-sm bg-slate-100 dark:bg-slate-800"></div>
                <div class="h-px w-full bg-slate-100 dark:bg-slate-700"></div>
                <div class="h-1.5 w-16 rounded-sm bg-slate-200 dark:bg-slate-700"></div>
                <div class="h-1.5 w-full rounded-sm bg-slate-100 dark:bg-slate-800"></div>
                <div class="h-1.5 w-3/5 rounded-sm bg-slate-100 dark:bg-slate-800"></div>
              </div>

              <!-- 6 clickable stamp zones -->
              @for (pos of stampPositions; track pos.value) {
                <button type="button"
                        (click)="setPosition(pos.value)"
                        [title]="pos.label"
                        [ngStyle]="stampZoneStyle(pos.value)"
                        class="group absolute focus:outline-none">
                  <div [class]="'flex items-center justify-center rounded border transition-all duration-150 ' +
                                (stamp()!.fiscalStampPosition === pos.value
                                  ? 'border-primary-500 bg-brand-50 shadow-[0_0_0_2px_rgba(6,182,212,0.25)] dark:bg-primary-500/10'
                                  : 'border-slate-300 bg-white/90 opacity-30 hover:opacity-90 hover:border-brand-400 hover:bg-brand-50/60 dark:border-slate-600 dark:bg-slate-800/80 dark:hover:border-primary-500/60 dark:hover:bg-primary-500/10')"
                       style="width:46px;height:46px;">
                    <svg viewBox="0 0 24 24" class="h-7 w-7"
                         [class]="stamp()!.fiscalStampPosition === pos.value ? 'text-primary-500 dark:text-brand-400' : 'text-slate-400 dark:text-slate-500'"
                         fill="currentColor">
                      <rect x="3" y="3" width="7" height="7" rx="1" fill="none" stroke="currentColor" stroke-width="1.5"/>
                      <rect x="5" y="5" width="3" height="3"/>
                      <rect x="14" y="3" width="7" height="7" rx="1" fill="none" stroke="currentColor" stroke-width="1.5"/>
                      <rect x="16" y="5" width="3" height="3"/>
                      <rect x="3" y="14" width="7" height="7" rx="1" fill="none" stroke="currentColor" stroke-width="1.5"/>
                      <rect x="5" y="16" width="3" height="3"/>
                      <rect x="14" y="14" width="2" height="2"/>
                      <rect x="17" y="14" width="2" height="2"/>
                      <rect x="20" y="14" width="2" height="2"/>
                      <rect x="14" y="17" width="2" height="2"/>
                      <rect x="17" y="17" width="2" height="2"/>
                      <rect x="14" y="20" width="2" height="2"/>
                      <rect x="17" y="20" width="2" height="2"/>
                      <rect x="20" y="20" width="2" height="2"/>
                    </svg>
                  </div>
                  @if (stamp()!.fiscalStampPosition === pos.value) {
                    <div class="absolute -top-1.5 -right-1.5 flex h-4 w-4 items-center justify-center rounded-full bg-primary-500 shadow ring-2 ring-white dark:ring-slate-900">
                      <svg class="h-2.5 w-2.5 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="3" d="M5 13l4 4L19 7"/>
                      </svg>
                    </div>
                  }
                </button>
              }
            </div>

            <!-- → RIGHT margin control -->
            <div class="flex flex-row items-center justify-center gap-1.5">
              <!-- tick line from page edge -->
              <div class="h-px w-2 bg-brand-300 dark:bg-primary-600/60"></div>
              <!-- Stepper -->
              <div class="flex flex-col items-center gap-1.5">
                <button type="button" (click)="adjustMargin('fiscalStampMarginRight', -1)"
                        class="flex h-6 w-6 items-center justify-center rounded-lg border border-brand-200 bg-white text-primary-600 shadow-sm transition hover:bg-brand-50 active:scale-95 dark:border-primary-500/40 dark:bg-primary-500/10 dark:text-brand-400 dark:hover:bg-primary-500/15">
                  <svg class="h-3 w-3" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2.5" d="M20 12H4"/></svg>
                </button>
                <span class="text-sm font-bold tabular-nums text-slate-800 dark:text-white">{{ getMargin('fiscalStampMarginRight') }}</span>
                <button type="button" (click)="adjustMargin('fiscalStampMarginRight', 1)"
                        class="flex h-6 w-6 items-center justify-center rounded-lg border border-brand-200 bg-white text-primary-600 shadow-sm transition hover:bg-brand-50 active:scale-95 dark:border-primary-500/40 dark:bg-primary-500/10 dark:text-brand-400 dark:hover:bg-primary-500/15">
                  <svg class="h-3 w-3" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2.5" d="M12 4v16M4 12h16"/></svg>
                </button>
              </div>
              <!-- "Right" label — vertical, reads top-to-bottom -->
              <span style="writing-mode:vertical-rl"
                    class="text-[9px] font-bold uppercase tracking-widest text-primary-500 dark:text-brand-400 select-none">Right</span>
            </div>

            <!-- └─────┘ bottom-left empty corner -->
            <div></div>

            <!-- ↓ BOTTOM margin control -->
            <div class="flex flex-col items-center justify-start gap-1 pt-1.5">
              <!-- tick line connecting to page bottom edge -->
              <div class="h-2 w-px bg-brand-300 dark:bg-primary-600/60"></div>
              <div class="flex items-center gap-1.5">
                <button type="button" (click)="adjustMargin('fiscalStampMarginBottom', -1)"
                        class="flex h-6 w-6 items-center justify-center rounded-lg border border-brand-200 bg-white text-primary-600 shadow-sm transition hover:bg-brand-50 active:scale-95 dark:border-primary-500/40 dark:bg-primary-500/10 dark:text-brand-400 dark:hover:bg-primary-500/15">
                  <svg class="h-3 w-3" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2.5" d="M20 12H4"/></svg>
                </button>
                <span class="w-8 text-center text-sm font-bold tabular-nums text-slate-800 dark:text-white">{{ getMargin('fiscalStampMarginBottom') }}</span>
                <button type="button" (click)="adjustMargin('fiscalStampMarginBottom', 1)"
                        class="flex h-6 w-6 items-center justify-center rounded-lg border border-brand-200 bg-white text-primary-600 shadow-sm transition hover:bg-brand-50 active:scale-95 dark:border-primary-500/40 dark:bg-primary-500/10 dark:text-brand-400 dark:hover:bg-primary-500/15">
                  <svg class="h-3 w-3" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2.5" d="M12 4v16M4 12h16"/></svg>
                </button>
              </div>
              <span class="text-[9px] font-bold uppercase tracking-widest text-primary-500 dark:text-brand-400">Bottom</span>
            </div>

            <!-- ┘ bottom-right empty corner -->
            <div></div>

          </div>

          <!-- Selected label + mm hint -->
          <div class="flex items-center gap-3 text-xs text-slate-400 dark:text-slate-500">
            <span>Selected: <span class="font-semibold text-slate-700 dark:text-slate-300">{{ selectedPositionLabel() }}</span></span>
            <span class="h-3 w-px bg-slate-200 dark:bg-white/10"></span>
            <span>Margins in mm</span>
          </div>

        </div>

        <!-- ── RIGHT: contextual guidance ──────────────────────────────── -->
        <div class="flex flex-col gap-3 flex-1 min-w-0">

          <p class="text-xs font-semibold uppercase tracking-wider text-slate-400 dark:text-slate-500">How PDF stamping works</p>

          <!-- Card 1: Scope -->
          <div class="flex flex-col gap-2 rounded-xl border border-amber-200/80 bg-amber-50/70 p-4 dark:border-amber-500/20 dark:bg-amber-500/[0.07]">
            <div class="flex items-center gap-2">
              <div class="flex h-6 w-6 flex-shrink-0 items-center justify-center rounded-md bg-amber-100 dark:bg-amber-500/20">
                <svg class="h-3.5 w-3.5 text-amber-600 dark:text-amber-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"/>
                </svg>
              </div>
              <span class="text-[11px] font-semibold uppercase tracking-wider text-amber-700 dark:text-amber-400">How the stamp is applied</span>
            </div>
            <p class="text-xs leading-relaxed text-amber-800/80 dark:text-amber-300/80">
              The stamp is drawn on the PDF Xero generates for the document (your branding theme), at the moment it is attached after fiscalization or downloaded from the document page. Your Xero invoice template is not changed, and unfiscalized documents are never affected.
            </p>
          </div>

          <!-- Card 2: Fiscalyze layouts -->
          <div class="flex flex-col gap-2 rounded-xl border border-brand-200/80 bg-brand-50/60 p-4 dark:border-primary-500/20 dark:bg-primary-500/[0.07]">
            <div class="flex items-center gap-2">
              <div class="flex h-6 w-6 flex-shrink-0 items-center justify-center rounded-md bg-brand-100 dark:bg-primary-500/20">
                <svg class="h-3.5 w-3.5 text-primary-600 dark:text-brand-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7"/>
                </svg>
              </div>
              <span class="text-[11px] font-semibold uppercase tracking-wider text-primary-700 dark:text-brand-400">Fiscalyze layouts</span>
            </div>
            <p class="text-xs leading-relaxed text-primary-700/80 dark:text-brand-300/80">
              Invoices printed using Fiscalyze's own print layouts already include fiscal details — the ZIMRA receipt number, fiscal day, device ID, QR code and verification code — embedded automatically. No configuration needed here.
            </p>
          </div>

          <!-- Card 3: what the customer sees -->
          <div class="flex flex-col gap-2 rounded-xl border border-indigo-200/80 bg-indigo-50/60 p-4 dark:border-indigo-500/20 dark:bg-indigo-500/[0.07]">
            <div class="flex items-center gap-2">
              <div class="flex h-6 w-6 flex-shrink-0 items-center justify-center rounded-md bg-indigo-100 dark:bg-indigo-500/20">
                <svg class="h-3.5 w-3.5 text-indigo-600 dark:text-indigo-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 10V3L4 14h7v7l9-11h-7z"/>
                </svg>
              </div>
              <span class="text-[11px] font-semibold uppercase tracking-wider text-indigo-700 dark:text-indigo-400">What your customer sees</span>
            </div>
            <p class="text-xs leading-relaxed text-indigo-800/80 dark:text-indigo-300/80">
              Xero cannot print fiscal details on its own invoice template, so the stamped PDF is how your customer gets them. With “Show the attachment on the online invoice” on (under Writing back to Xero), it appears as a file on the invoice link Xero emails.
            </p>
          </div>

        </div>

        </div><!-- end two-column flex -->

      </div>
      }
    </section>

    <!-- ── Default HS code ───────────────────────────────────────────────── -->
    <section class="mt-5 rounded-2xl border border-slate-200/70 bg-white shadow-sm fade-in-down dark:border-white/10 dark:bg-slate-900 overflow-hidden">
      <div class="flex items-center gap-3 border-b border-slate-100 px-6 py-4 dark:border-white/10">
        <div class="flex h-8 w-8 items-center justify-center rounded-lg bg-brand-50 dark:bg-primary-500/15">
          <svg class="h-4 w-4 text-primary-600 dark:text-brand-400" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M20 7l-8-4-8 4m16 0l-8 4m8-4v10l-8 4m0-10L4 7m8 4v10M4 7v10l8 4"/></svg>
        </div>
        <div>
          <p class="text-sm font-semibold text-slate-900 dark:text-white">Default HS code</p>
          <p class="text-xs text-slate-500 dark:text-slate-400">Used for any invoice line whose item has no HS code of its own, so fiscalization is never blocked by a missing code. Every company starts with the system default <span class="font-mono">04021099</span>; pick a code that better matches what you mostly sell.</p>
        </div>
      </div>

      @if (loading()) {
        <div class="flex items-center justify-center gap-3 px-6 py-10">
          <div class="h-6 w-6 animate-spin rounded-full border-4 border-brand-200 border-t-primary-500"></div>
          <span class="text-sm text-slate-500 dark:text-slate-400">Loading…</span>
        </div>
      } @else {
        <div class="px-6 py-5">
          <label class="mb-1.5 block text-xs font-semibold uppercase tracking-wider text-slate-600 dark:text-slate-400">HS code</label>
          <div class="flex flex-col gap-2 sm:flex-row sm:items-center">
            <input type="text" [ngModel]="defaultHsCode()" (ngModelChange)="onDefaultTyped($event)" placeholder="e.g. 04021099" inputmode="numeric"
                   class="w-full rounded-xl border bg-white px-3 py-2.5 font-mono text-sm text-slate-900 outline-none transition focus:ring-2 focus:ring-primary-500/30 sm:max-w-xs dark:bg-slate-950/60 dark:text-white"
                   [ngClass]="defaultCheck() === 'valid' ? 'border-emerald-400 focus:border-emerald-500' : defaultCheck() === 'invalid' ? 'border-rose-400 focus:border-rose-500' : 'border-slate-200 focus:border-primary-500 dark:border-white/10'" />
            <div class="flex gap-2">
              <button type="button" (click)="pickerOpen.set(true)"
                      class="inline-flex items-center gap-1.5 rounded-xl border border-slate-200 bg-white/70 px-3 py-2.5 text-sm font-medium text-slate-700 transition hover:bg-white dark:border-white/10 dark:bg-white/5 dark:text-slate-200 dark:hover:bg-white/10">
                <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg>
                Browse
              </button>
              <button *ngIf="defaultHsCode() !== systemDefaultHsCode" type="button" (click)="useSystemDefault()"
                      class="inline-flex items-center gap-1.5 rounded-xl border border-slate-200 bg-white/70 px-3 py-2.5 text-sm font-medium text-slate-500 transition hover:bg-white dark:border-white/10 dark:bg-white/5 dark:text-slate-400 dark:hover:bg-white/10">
                Use system default
              </button>
            </div>
          </div>
          <p *ngIf="defaultCheck() === 'validating'" class="mt-2 text-xs text-slate-500 dark:text-slate-400">Checking the catalogue…</p>
          <p *ngIf="defaultCheck() === 'valid'" class="mt-2 inline-flex items-center gap-1.5 rounded-full bg-emerald-100 px-3 py-1 text-xs font-semibold text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300">
            <svg class="h-3 w-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M5 13l4 4L19 7"/></svg>
            Valid · {{ defaultDescription() || 'In the HS code catalogue' }}
          </p>
          <p *ngIf="defaultCheck() === 'invalid'" class="mt-2 inline-flex items-center gap-1.5 rounded-full bg-rose-100 px-3 py-1 text-xs font-semibold text-rose-700 dark:bg-rose-500/15 dark:text-rose-300">Not in the HS code catalogue. Pick one with Browse.</p>
          <p *ngIf="defaultCheck() === 'idle' && !defaultHsCode()" class="mt-2 text-xs text-slate-500 dark:text-slate-400">Leave empty to use the system default <span class="font-mono">04021099</span>.</p>
        </div>
      }
    </section>

    <app-hs-code-dialog [open]="pickerOpen()" [initialSearch]="defaultHsCode()" (close)="pickerOpen.set(false)" (picked)="onDefaultPicked($event)"></app-hs-code-dialog>

    <!-- ── Sticky save bar ───────────────────────────────────────────────── -->
    <div class="sticky bottom-0 z-10 mt-6 -mx-4 sm:-mx-6 lg:-mx-8 px-4 sm:px-6 lg:px-8 py-3 border-t border-slate-200/80 bg-white/80 backdrop-blur-md dark:border-white/10 dark:bg-slate-900/80">
      <div class="flex items-center justify-between gap-4 max-w-screen-xl mx-auto">
        <p class="text-xs text-slate-500 dark:text-slate-400">
          Changes are not saved until you click <span class="font-semibold text-slate-700 dark:text-slate-300">Save settings</span>.
        </p>
        <button (click)="saveAll()" [disabled]="saving() || loading() || stampLoading() || defaultCheck() === 'invalid' || defaultCheck() === 'validating'"
                class="inline-flex items-center gap-2 rounded-xl bg-primary-500 px-5 py-2.5 text-sm font-semibold text-white shadow-md transition hover:bg-primary-600 active:scale-95 disabled:opacity-50 disabled:cursor-not-allowed focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-2 dark:focus:ring-offset-slate-900">
          @if (saving()) {
            <svg class="h-4 w-4 animate-spin" viewBox="0 0 24 24" fill="none">
              <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/>
              <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/>
            </svg>
            Saving…
          } @else {
            <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7"/>
            </svg>
            Save settings
          }
        </button>
      </div>
    </div>
  `
})
export class FiscalizationSettings implements OnInit {
  private fb = inject(FormBuilder);
  private service = inject(ClientConfigService);
  private hsCodes = inject(HsCodesService);
  private toast = inject(ToastService);

  // ── default HS code ───────────────────────────────────────────────────
  readonly systemDefaultHsCode = '04021099';
  readonly defaultHsCode = signal<string>('');
  readonly defaultCheck = signal<'idle' | 'validating' | 'valid' | 'invalid'>('idle');
  readonly defaultDescription = signal('');
  readonly pickerOpen = signal(false);
  private defaultTimer: ReturnType<typeof setTimeout> | null = null;
  private defaultSub: Subscription | null = null;

  onDefaultTyped(value: string): void {
    this.defaultHsCode.set(value);
    this.defaultDescription.set('');
    this.defaultSub?.unsubscribe();
    if (this.defaultTimer) clearTimeout(this.defaultTimer);
    const code = value.trim();
    if (!code) { this.defaultCheck.set('idle'); return; }
    this.defaultCheck.set('validating');
    this.defaultTimer = setTimeout(() => this.validateDefault(code), 400);
  }

  private validateDefault(code: string): void {
    this.defaultSub?.unsubscribe();
    this.defaultCheck.set('validating');
    this.defaultSub = this.hsCodes.validate(code).subscribe({
      next: (res) => {
        if (res?.code) { this.defaultCheck.set('valid'); this.defaultDescription.set(res.description ?? ''); }
        else this.defaultCheck.set('invalid');
      },
      error: () => this.defaultCheck.set('invalid')
    });
  }

  onDefaultPicked(picked: HsCodeLookupItem): void {
    this.defaultSub?.unsubscribe();
    if (this.defaultTimer) clearTimeout(this.defaultTimer);
    this.pickerOpen.set(false);
    this.defaultHsCode.set(picked.code);
    this.defaultDescription.set(picked.description ?? '');
    this.defaultCheck.set('valid');
  }

  useSystemDefault(): void { this.onDefaultTyped(this.systemDefaultHsCode); }

  // ── buyer validation ──────────────────────────────────────────────────
  readonly validationModes: FiscalizationValidationMode[] = ['Strict', 'Lenient'];
  readonly validationMode = signal<FiscalizationValidationMode>('Strict');

  // ── auto-fiscalize ────────────────────────────────────────────────────
  readonly loading = signal(false);
  readonly saving = signal(false);

  readonly form = this.fb.group({
    autoFiscalizeInvoices: [false],
    autoFiscalizeCreditNotes: [false],
    writebackHistoryNote: [true],
    attachmentIncludeOnline: [true],
    writebackReference: [false]
  });

  // ── which PDF goes to Xero ────────────────────────────────────────────
  readonly pdfOptions: { value: FiscalPdfLayout; label: string; help: string }[] = [
    { value: 'XeroStamp', label: "Xero's PDF with the stamp", help: 'Your Xero invoice as Xero draws it (your branding theme), with the ZIMRA QR code and receipt details stamped on it.' },
    { value: 'FiscalizeLayout', label: 'Fiscalize layout', help: 'Your own layout designed on Fiscalize, filled from Xero: your logo, colours and the Xero fields you choose.' },
    { value: 'FiscalizeStandard', label: 'Fiscalize standard invoice', help: "Fiscalize's own fiscal invoice for the receipt, the same one the platform keeps." }
  ];
  readonly papers: { value: LayoutPaper; label: string }[] = [
    { value: 'A4', label: 'A4 page' }, { value: '80mm', label: '80mm till slip' }, { value: '58mm', label: '58mm till slip' }
  ];
  readonly pdfLayout = signal<FiscalPdfLayout>('XeroStamp');
  readonly layoutPaper = signal<LayoutPaper>('A4');
  readonly savedPaper = signal<LayoutPaper>('A4');
  readonly designerUrl = signal<string | null>(null);
  readonly layoutsLoading = signal(false);
  readonly layouts = signal<FiscalLayoutStatus[]>([]);
  readonly layoutRows = computed<FiscalLayoutStatus[]>(() => {
    const held = this.layouts();
    return ['Invoice', 'CreditNote'].map(t => held.find(l => l.documentType === t)
      ?? { documentType: t, paperSize: this.savedPaper(), available: false, isStandardDesign: false, version: 0 });
  });

  choosePdf(value: FiscalPdfLayout): void {
    this.pdfLayout.set(value);
    if (value === 'FiscalizeLayout' && this.layouts().length === 0 && !this.layoutsLoading()) this.loadLayouts(false);
  }

  loadLayouts(refresh: boolean): void {
    this.layoutsLoading.set(true);
    this.service.getFiscalLayouts(refresh).subscribe({
      next: rows => { this.layouts.set(rows ?? []); this.layoutsLoading.set(false); },
      error: err => {
        this.layoutsLoading.set(false);
        if (refresh) this.toast.error(extractErrorMessage(err, 'Could not check for layouts. Try again in a moment.'));
      }
    });
  }

  // ── fiscal stamp ──────────────────────────────────────────────────────
  readonly stampLoading = signal(false);
  readonly stamp = signal<StampPrefs | null>(null);

  readonly stampPositions = STAMP_POSITIONS;
  readonly marginFields = MARGIN_FIELDS;

  readonly selectedPositionLabel = computed(() =>
    STAMP_POSITIONS.find(p => p.value === this.stamp()?.fiscalStampPosition)?.label ?? ''
  );

  readonly previewStyle = computed((): { [key: string]: string } => {
    const pos = this.stamp()?.fiscalStampPosition ?? 'bottom-right';
    const s: { [key: string]: string } = { position: 'absolute' };
    if (pos.startsWith('top')) { s['top'] = '4px'; } else { s['bottom'] = '4px'; }
    if (pos.endsWith('left')) { s['left'] = '4px'; }
    else if (pos.endsWith('right')) { s['right'] = '4px'; }
    else { s['left'] = '50%'; s['transform'] = 'translateX(-50%)'; }
    return s;
  });

  stampZoneStyle(pos: FiscalStampPosition): { [key: string]: string } {
    const s: { [key: string]: string } = { position: 'absolute', width: '46px' };
    if (pos.startsWith('top')) { s['top'] = '8px'; } else { s['bottom'] = '8px'; }
    if (pos.endsWith('left')) { s['left'] = '8px'; }
    else if (pos.endsWith('right')) { s['right'] = '8px'; }
    else { s['left'] = '50%'; s['transform'] = 'translateX(-50%)'; }
    return s;
  }

  ngOnInit(): void {
    this.loadSettings();
  }

  // ── load ──────────────────────────────────────────────────────────────

  private loadSettings(): void {
    this.loading.set(true);
    this.stampLoading.set(true);
    this.service.getFiscalizationSettings().subscribe({
      next: (res) => {
        this.form.patchValue({
          autoFiscalizeInvoices: !!res?.autoFiscalizeInvoices,
          autoFiscalizeCreditNotes: !!res?.autoFiscalizeCreditNotes,
          writebackHistoryNote: res?.writebackHistoryNote !== false,
          attachmentIncludeOnline: res?.attachmentIncludeOnline !== false,
          writebackReference: !!res?.writebackReference
        }, { emitEvent: false });

        this.validationMode.set(res?.fiscalizationValidationMode === 'Lenient' ? 'Lenient' : 'Strict');
        const choice = res?.fiscalPdfLayout;
        this.pdfLayout.set(choice === 'FiscalizeLayout' || choice === 'FiscalizeStandard' ? choice : 'XeroStamp');
        const paper = (['A4', '80mm', '58mm'] as LayoutPaper[]).find(p => p === res?.layoutPaperSize) ?? 'A4';
        this.layoutPaper.set(paper);
        this.savedPaper.set(paper);
        this.designerUrl.set(res?.layoutDesignerUrl ?? null);
        if (this.pdfLayout() === 'FiscalizeLayout') this.loadLayouts(false);
        this.defaultHsCode.set(res?.defaultHsCode ?? '');
        this.defaultCheck.set('idle');
        this.defaultDescription.set('');
        if (res?.defaultHsCode) this.validateDefault(res.defaultHsCode);

        this.stamp.set({
          fiscalStampEnabled: typeof res?.fiscalStampEnabled === 'boolean' ? res.fiscalStampEnabled : false,
          fiscalStampPosition: (res?.fiscalStampPosition as FiscalStampPosition) ?? 'bottom-right',
          fiscalStampMarginTop: res?.fiscalStampMarginTop ?? 15,
          fiscalStampMarginBottom: res?.fiscalStampMarginBottom ?? 15,
          fiscalStampMarginLeft: res?.fiscalStampMarginLeft ?? 15,
          fiscalStampMarginRight: res?.fiscalStampMarginRight ?? 15,
        });

        this.loading.set(false);
        this.stampLoading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.stampLoading.set(false);
        this.toast.error(extractErrorMessage(err, 'Failed to load settings.'));
      }
    });
  }

  // ── save all ──────────────────────────────────────────────────────────

  saveAll(): void {
    if (this.defaultCheck() === 'invalid' || this.defaultCheck() === 'validating') return;
    this.saving.set(true);
    const v = this.form.getRawValue();
    const stamp = this.stamp();
    this.service.updateFiscalizationSettings({
      autoFiscalizeInvoices: !!v.autoFiscalizeInvoices,
      autoFiscalizeCreditNotes: !!v.autoFiscalizeCreditNotes,
      writebackHistoryNote: !!v.writebackHistoryNote,
      attachmentIncludeOnline: !!v.attachmentIncludeOnline,
      writebackReference: !!v.writebackReference,
      defaultHsCode: this.defaultHsCode().trim() || null,
      fiscalizationValidationMode: this.validationMode(),
      fiscalStampEnabled: stamp?.fiscalStampEnabled,
      fiscalPdfLayout: this.pdfLayout(),
      layoutPaperSize: this.layoutPaper(),
      fiscalStampPosition: stamp?.fiscalStampPosition,
      fiscalStampMarginTop: stamp?.fiscalStampMarginTop,
      fiscalStampMarginBottom: stamp?.fiscalStampMarginBottom,
      fiscalStampMarginLeft: stamp?.fiscalStampMarginLeft,
      fiscalStampMarginRight: stamp?.fiscalStampMarginRight,
    }).subscribe({
      next: () => {
        this.saving.set(false);
        this.toast.success('Settings saved successfully.');
        this.savedPaper.set(this.layoutPaper());
        // Ask the platform now, so the layouts shown are the ones that will be attached.
        if (this.pdfLayout() === 'FiscalizeLayout') this.loadLayouts(true);
      },
      error: (err) => {
        this.saving.set(false);
        this.toast.error(extractErrorMessage(err, 'Failed to save settings. Please try again.'));
      }
    });
  }

  // ── stamp local state ─────────────────────────────────────────────────

  private saveStamp(patch: Partial<StampPrefs>): void {
    const current = this.stamp();
    if (!current) return;
    this.stamp.set({ ...current, ...patch });
  }

  toggleStampEnabled(): void {
    const current = this.stamp();
    if (!current) return;
    const enabling = !current.fiscalStampEnabled;
    this.saveStamp(enabling
      ? {
          fiscalStampEnabled: true,
          fiscalStampPosition: 'bottom-right',
          fiscalStampMarginTop: 15,
          fiscalStampMarginBottom: 15,
          fiscalStampMarginLeft: 15,
          fiscalStampMarginRight: 15,
        }
      : { fiscalStampEnabled: false }
    );
  }

  setPosition(pos: FiscalStampPosition): void {
    this.saveStamp({ fiscalStampPosition: pos });
  }

  getMargin(field: string): number {
    return (this.stamp() as any)?.[field] ?? 10;
  }

  setMargin(field: string, value: number): void {
    const clamped = Math.max(0, Math.min(50, +value || 0));
    this.saveStamp({ [field]: clamped } as Partial<StampPrefs>);
  }

  adjustMargin(field: string, delta: number): void {
    this.setMargin(field, this.getMargin(field) + delta);
  }
}
