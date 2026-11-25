import { Component, DestroyRef, HostListener, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TaxDetailsService, CustomerTaxDetails, CustomerComplianceStatus } from '../../core/services/xero/tax-details.service';
import { ToastService } from '../../core/services/toast/toast.service';
import { extractErrorMessage } from '../../core/utils/http-error';
import { ConfirmDialog } from '../../components/confirm-dialog/confirm-dialog';
import {
  ALL_FIELDS, BUYER_KEYS, BUYER_SECTIONS, BuyerDraft, BuyerField, BuyerKey, EMPTY_DRAFT,
  customerInitials, draftFrom, fieldError, sameValue, toSavePayload, WRITTEN_TO_XERO
} from './buyer-fields';

type LiveStatus = CustomerComplianceStatus;
const QUEUES = ['Attention', 'MissingDetails', 'Invalid', 'Compliant', 'NoTin'];
const REQUIRED = ALL_FIELDS.filter((f) => f.required);

/**
 * One customer's ZIMRA buyer details. Every value can be typed here and wins over Xero; blank boxes keep the
 * Xero value. Opened from the Customers list (with its filter as a queue, so "Save & next" walks through every
 * customer that needs fixing) or from a failed document ("Fix buyer details", returning to that document).
 */
@Component({
  selector: 'app-customer-detail',
  imports: [CommonModule, FormsModule, ConfirmDialog],
  template: `
    @if (loading() && !customer()) {
      <div class="space-y-4" aria-busy="true" aria-label="Loading customer">
        <div class="h-20 animate-pulse rounded-2xl bg-white/60 dark:bg-white/5"></div>
        <div class="grid gap-6 lg:grid-cols-[minmax(0,1fr)_22rem]">
          <div class="h-[32rem] animate-pulse rounded-2xl bg-white/60 dark:bg-white/5"></div>
          <div class="hidden h-80 animate-pulse rounded-2xl bg-white/60 lg:block dark:bg-white/5"></div>
        </div>
      </div>
    } @else if (error()) {
      <div class="mx-auto max-w-lg rounded-2xl border border-rose-200 bg-white p-8 text-center shadow-sm dark:border-rose-500/30 dark:bg-slate-900">
        <p class="text-sm text-rose-700 dark:text-rose-300">{{ error() }}</p>
        <div class="mt-4 flex justify-center gap-2">
          <button type="button" (click)="back()" class="rounded-xl px-3 py-2 text-sm font-medium text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-white/10">Back</button>
          <button type="button" (click)="load(id)" class="btn-save">Try again</button>
        </div>
      </div>
    } @else if (customer(); as c) {
      <!-- Header band -->
      <div class="relative z-10 -mx-4 -mt-6 mb-6 border-b border-slate-200/40 bg-white/80 px-4 py-4 backdrop-blur-md fade-in-down dark:border-white/5 dark:bg-slate-950/60 sm:-mx-6 sm:px-6 lg:-mx-8 lg:-mt-8 lg:px-8 lg:py-5">
        <div class="mx-auto flex max-w-7xl flex-wrap items-center justify-between gap-3">
          <div class="flex min-w-0 items-center gap-3">
            <button type="button" (click)="back()" [attr.aria-label]="backLabel()"
                    class="flex-shrink-0 rounded-xl p-2 text-slate-500 transition-colors hover:bg-slate-100 hover:text-slate-900 dark:text-slate-400 dark:hover:bg-white/5 dark:hover:text-white">
              <svg class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M15 19l-7-7 7-7"/></svg>
            </button>
            <span class="avatar-chip hidden h-11 w-11 flex-shrink-0 text-sm sm:inline-flex">{{ initials(c.displayName) }}</span>
            <div class="min-w-0">
              <h1 class="truncate text-xl font-bold tracking-tight text-slate-900 sm:text-2xl dark:text-white">{{ c.displayName }}</h1>
              <div class="mt-1 flex flex-wrap items-center gap-2 text-sm">
                <ng-container *ngTemplateOutlet="badge; context: { $implicit: c.complianceStatus }"></ng-container>
              </div>
            </div>
          </div>

          @if (queue().length > 1 && position() >= 0) {
            <nav class="flex items-center gap-1 rounded-xl border border-slate-200 bg-white/70 p-1 text-xs dark:border-white/10 dark:bg-slate-900/60" aria-label="Customers in this list">
              <button type="button" (click)="go(-1)" [disabled]="position() <= 0" aria-label="Previous customer"
                      class="rounded-lg p-1.5 text-slate-500 transition hover:bg-slate-100 hover:text-slate-900 disabled:opacity-30 disabled:hover:bg-transparent dark:hover:bg-white/10 dark:hover:text-white">
                <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M15 18l-6-6 6-6"/></svg>
              </button>
              <span class="px-1.5 tabular-nums text-slate-600 dark:text-slate-300"><span class="font-semibold text-slate-900 dark:text-white">{{ position() + 1 }}</span> of {{ queue().length }} <span class="hidden sm:inline">{{ queueLabel() }}</span></span>
              <button type="button" (click)="go(1)" [disabled]="position() >= queue().length - 1" aria-label="Next customer"
                      class="rounded-lg p-1.5 text-slate-500 transition hover:bg-slate-100 hover:text-slate-900 disabled:opacity-30 disabled:hover:bg-transparent dark:hover:bg-white/10 dark:hover:text-white">
                <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M9 18l6-6-6-6"/></svg>
              </button>
            </nav>
          }
        </div>
      </div>

      <div class="mx-auto grid max-w-7xl gap-6 pb-4 lg:grid-cols-[minmax(0,1fr)_22rem]">
        <!-- Left: readiness + form -->
        <div class="min-w-0 space-y-5">
          <!-- Readiness, live as the user types -->
          @switch (liveStatus()) {
            @case ('Compliant') {
              <div class="flex items-start gap-3 rounded-2xl border border-emerald-200/70 bg-emerald-50/80 px-4 py-3.5 fade-in dark:border-emerald-500/25 dark:bg-emerald-500/10">
                <span class="flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-xl bg-emerald-500 text-white shadow-sm shadow-emerald-500/30">
                  <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M20 6 9 17l-5-5"/></svg>
                </span>
                <div class="text-sm">
                  <p class="font-semibold text-emerald-800 dark:text-emerald-200">{{ dirty() ? 'Ready for ZIMRA once saved' : 'Ready for ZIMRA' }}</p>
                  <p class="mt-0.5 text-xs text-emerald-700/90 dark:text-emerald-300/80">Every buyer detail ZIMRA asks for is in place. Documents for this customer fiscalise as B2B sales.</p>
                </div>
              </div>
            }
            @case ('MissingDetails') {
              <div class="rounded-2xl border border-amber-200/80 bg-amber-50/80 px-4 py-3.5 fade-in dark:border-amber-500/25 dark:bg-amber-500/10">
                <div class="flex items-start gap-3">
                  <span class="flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-xl bg-amber-500 text-white shadow-sm shadow-amber-500/30">
                    <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"/><path d="M12 8v4M12 16h.01"/></svg>
                  </span>
                  <div class="min-w-0 flex-1 text-sm">
                    <p class="font-semibold text-amber-900 dark:text-amber-100">{{ liveMissing().length }} {{ liveMissing().length === 1 ? 'detail' : 'details' }} still needed</p>
                    <p class="mt-0.5 text-xs text-amber-800/90 dark:text-amber-200/80">This customer has a TIN, so ZIMRA needs their full details. Fill in the highlighted boxes below.</p>
                    <div class="mt-2.5 flex items-center gap-3">
                      <div class="h-1.5 flex-1 overflow-hidden rounded-full bg-amber-200/70 dark:bg-amber-500/20" role="progressbar" [attr.aria-valuenow]="requiredDone()" aria-valuemin="0" [attr.aria-valuemax]="requiredTotal">
                        <div class="h-full rounded-full bg-amber-500 transition-all duration-300" [style.width.%]="requiredDone() / requiredTotal * 100"></div>
                      </div>
                      <span class="text-[11px] font-semibold tabular-nums text-amber-800 dark:text-amber-200">{{ requiredDone() }} / {{ requiredTotal }}</span>
                    </div>
                  </div>
                </div>
              </div>
            }
            @case ('Invalid') {
              <div class="flex items-start gap-3 rounded-2xl border border-rose-200/80 bg-rose-50/80 px-4 py-3.5 fade-in dark:border-rose-500/25 dark:bg-rose-500/10">
                <span class="flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-xl bg-rose-500 text-white shadow-sm shadow-rose-500/30">
                  <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M18 6 6 18M6 6l12 12"/></svg>
                </span>
                <div class="min-w-0 text-sm">
                  <p class="font-semibold text-rose-800 dark:text-rose-200">ZIMRA will reject this customer</p>
                  <ul class="mt-1 space-y-0.5 text-xs text-rose-700 dark:text-rose-300">
                    @for (p of liveProblems(); track p) { <li>{{ p }}</li> }
                  </ul>
                </div>
              </div>
            }
            @default {
              <div class="flex items-start gap-3 rounded-2xl border border-slate-200/80 bg-white/70 px-4 py-3.5 fade-in dark:border-white/10 dark:bg-slate-900/60">
                <span class="flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-xl bg-slate-200 text-slate-600 dark:bg-white/10 dark:text-slate-300">
                  <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="2" y="6" width="20" height="12" rx="2"/><circle cx="12" cy="12" r="2"/></svg>
                </span>
                <div class="text-sm">
                  <p class="font-semibold text-slate-800 dark:text-slate-100">Cash-sale customer</p>
                  <p class="mt-0.5 text-xs text-slate-500 dark:text-slate-400">No TIN, so documents are fiscalised as cash (B2C) sales and no buyer details are sent. Add a TIN to issue B2B tax invoices.</p>
                </div>
              </div>
            }
          }

          <!-- The form -->
          <form id="buyer-form" (ngSubmit)="save(false)" class="overflow-hidden rounded-2xl border border-white/40 bg-white/80 shadow-sm backdrop-blur-sm dark:border-white/10 dark:bg-slate-900/70" [attr.aria-label]="'Buyer details for ' + c.displayName">
            @for (s of sections; track s.key; let last = $last) {
              <fieldset class="px-5 py-5 sm:px-6" [ngClass]="last ? '' : 'border-b border-slate-200/60 dark:border-white/5'">
                <div class="mb-4 flex items-start gap-3">
                  <span class="flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-lg bg-brand-50 text-primary-600 dark:bg-primary-500/15 dark:text-brand-300">
                    <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path [attr.d]="s.icon"/></svg>
                  </span>
                  <div>
                    <legend class="text-sm font-semibold text-slate-900 dark:text-white">{{ s.title }}</legend>
                    <p class="mt-0.5 text-xs text-slate-500 dark:text-slate-400">{{ s.description }}</p>
                  </div>
                </div>
                <div class="grid gap-x-4 gap-y-4 sm:grid-cols-2">
                  @for (f of s.fields; track f.key) {
                    <div [ngClass]="f.wide ? 'sm:col-span-2' : ''">
                      <div class="relative mb-1.5 flex items-center gap-1.5">
                        <label [for]="'cf-' + f.key" class="text-xs font-medium text-slate-700 dark:text-slate-200">
                          {{ f.label }}@if (f.hint) {<span class="ml-1 font-normal text-slate-400">({{ f.hint }})</span>}
                        </label>
                        @if (f.required && hasTin()) { <span class="text-[10px] font-semibold uppercase tracking-wide text-slate-400" aria-hidden="true">Required</span> }
                        <button type="button" class="peer ml-auto inline-flex h-5 w-5 flex-shrink-0 items-center justify-center rounded-full text-slate-400 transition hover:bg-brand-50 hover:text-primary-600 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary-500 dark:hover:bg-white/10 dark:hover:text-brand-300"
                                [attr.aria-label]="'Where to find ' + f.label + ' in Xero'" [attr.aria-describedby]="'tip-' + f.key" [attr.aria-expanded]="openTip() === f.key"
                                (click)="toggleTip(f.key)" (blur)="openTip.set(null)">
                          <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"/><path d="M9.1 9a3 3 0 0 1 5.8 1c0 2-3 3-3 3M12 17h.01"/></svg>
                        </button>
                        <span role="tooltip" [id]="'tip-' + f.key"
                              class="pointer-events-none absolute bottom-full left-0 right-0 z-30 mb-2 rounded-xl bg-slate-900 px-3.5 py-2.5 text-[11px] font-normal leading-relaxed text-slate-100 shadow-xl ring-1 ring-black/10 transition-opacity duration-150 peer-hover:visible peer-hover:opacity-100 peer-focus-visible:visible peer-focus-visible:opacity-100 dark:bg-slate-700"
                              [ngClass]="openTip() === f.key ? 'visible opacity-100' : 'invisible opacity-0'">
                          <span class="mb-0.5 flex items-center gap-1.5 font-semibold text-brand-200">
                            <svg class="h-3 w-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 22s7-6.2 7-12a7 7 0 1 0-14 0c0 5.8 7 12 7 12z"/></svg>
                            Where is this in Xero?
                          </span>
                          {{ tip(f) }}
                        </span>
                      </div>
                      <input [id]="'cf-' + f.key" [name]="f.key" [(ngModel)]="draft[f.key]" (focus)="openTip.set(null)"
                             [type]="f.type || 'text'" [attr.inputmode]="f.inputmode || null" [attr.maxlength]="f.maxlength || null"
                             [attr.autocomplete]="f.autocomplete || 'off'" [placeholder]="f.empty"
                             class="field" [ngClass]="inputClass(f)"
                             [attr.aria-invalid]="!!fieldErr(f.key) || needed(f)"
                             [attr.aria-describedby]="'st-' + f.key" />
                      <p [id]="'st-' + f.key" class="mt-1.5 min-h-[1rem] text-[11px] leading-snug">
                        @if (fieldErr(f.key)) {
                          <span class="text-rose-600 dark:text-rose-400">{{ fieldErr(f.key) }}</span>
                        } @else if (draft[f.key].trim()) {
                          @if (draft[f.key].trim() !== savedValue(f.key)) {
                            <span class="inline-flex items-center gap-1 text-primary-600 dark:text-brand-400">
                              <span class="h-1.5 w-1.5 rounded-full bg-primary-500"></span>{{ savesTo(f.key) }}
                            </span>
                          } @else if (fromXero(f.key)) {
                            <span class="inline-flex items-center gap-1 text-slate-400"><span class="h-1.5 w-1.5 rounded-full bg-slate-300 dark:bg-slate-600"></span>From Xero</span>
                          } @else {
                            <span class="inline-flex items-center gap-1 text-emerald-600 dark:text-emerald-400" title="Xero has no place for this value, or did not accept it.">
                              <span class="h-1.5 w-1.5 rounded-full bg-emerald-500"></span>Saved in Fiscalize
                            </span>
                          }
                          @if (xeroValue(f.key) && !fromXero(f.key)) {
                            <span class="text-slate-300 dark:text-slate-600"> · </span>
                            <button type="button" (click)="draft[f.key] = xeroValue(f.key)" class="font-medium text-primary-600 underline-offset-2 hover:underline dark:text-brand-400" [title]="'Xero has: ' + xeroValue(f.key)">Use Xero value</button>
                          }
                        } @else if (needed(f)) {
                          <span class="font-medium text-amber-700 dark:text-amber-300">Needed by ZIMRA: type it here</span>
                        } @else if (savedValue(f.key) || isCleared(f.key)) {
                          <span class="inline-flex items-center gap-1 text-slate-500 dark:text-slate-400"><span class="h-1.5 w-1.5 rounded-full bg-slate-400"></span>{{ savedValue(f.key) ? 'Will be cleared, in Xero too' : 'Cleared' }}</span>
                          @if (xeroValue(f.key)) {
                            <span class="text-slate-300 dark:text-slate-600"> · </span>
                            <button type="button" (click)="draft[f.key] = xeroValue(f.key)" class="font-medium text-primary-600 underline-offset-2 hover:underline dark:text-brand-400" [title]="'Xero has: ' + xeroValue(f.key)">Use Xero value</button>
                          }
                        } @else if (!f.required || !hasTin()) {
                          <span class="text-slate-400">Optional</span>
                        }
                      </p>
                    </div>
                  }
                </div>
              </fieldset>
            }
          </form>

          <p class="flex items-start gap-2 px-1 text-[11px] leading-relaxed text-slate-500 dark:text-slate-400">
            <svg class="mt-px h-3.5 w-3.5 flex-shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4M12 8h.01"/></svg>
            <span>Saving writes the TIN to the contact's Tax ID number and the VAT number to its Company registration number in Xero, when those boxes are empty or already hold one. Everything else you type here is kept in Fiscalize and used instead of the Xero value. In Xero, customers are under Contacts › Customers (open the contact, then Edit).</span>
          </p>
        </div>

        <!-- Right: what ZIMRA will receive, live -->
        <aside class="min-w-0 lg:sticky lg:top-6 lg:self-start">
          <section class="overflow-hidden rounded-2xl border border-white/40 bg-white/80 shadow-sm backdrop-blur-sm dark:border-white/10 dark:bg-slate-900/70" aria-labelledby="preview-title">
            <header class="flex items-center justify-between gap-2 border-b border-slate-200/60 px-5 py-3.5 dark:border-white/5">
              <h2 id="preview-title" class="text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">ZIMRA will receive</h2>
              @if (dirty()) { <span class="rounded-full bg-brand-50 px-2 py-0.5 text-[10px] font-semibold text-primary-700 dark:bg-primary-500/15 dark:text-brand-300">Preview</span> }
            </header>
            @if (!hasTin()) {
              <p class="px-5 py-6 text-center text-xs text-slate-500 dark:text-slate-400">No buyer block: sold as a cash sale.</p>
            } @else {
              <dl class="divide-y divide-slate-100 px-5 text-xs dark:divide-white/5">
                @for (f of fields; track f.key) {
                  <div class="flex items-start justify-between gap-3 py-2">
                    <dt class="flex-shrink-0 text-slate-500 dark:text-slate-400">{{ f.label }}</dt>
                    <dd class="min-w-0 text-right">
                      @if (sent(f.key); as v) {
                        <span class="break-words text-slate-800 dark:text-slate-100" [class.font-mono]="f.mono">{{ v }}</span>
                        <span class="mt-0.5 block text-[10px]" [ngClass]="draft[f.key].trim() && !fromXero(f.key) ? 'text-emerald-600 dark:text-emerald-400' : 'text-slate-400'">{{ draft[f.key].trim() && !fromXero(f.key) ? 'Fiscalize' : (f.key === 'tradeName' && !xeroValue('tradeName') ? 'Registered name' : 'Xero') }}</span>
                      } @else if (f.required) {
                        <span class="rounded bg-amber-100 px-1.5 py-0.5 font-medium text-amber-800 dark:bg-amber-500/15 dark:text-amber-200">Missing</span>
                      } @else { <span class="text-slate-300 dark:text-slate-600">—</span> }
                    </dd>
                  </div>
                }
              </dl>
            }
          </section>
        </aside>
      </div>

      <!-- Action bar -->
      <div class="sticky bottom-0 z-20 -mx-4 mt-2 border-t border-slate-200/60 bg-white/90 px-4 py-3 backdrop-blur-md sm:-mx-6 sm:px-6 lg:-mx-8 lg:px-8 dark:border-white/5 dark:bg-slate-950/80">
        <div class="mx-auto flex max-w-7xl items-center justify-between gap-3">
          <p class="hidden items-center gap-2 text-xs sm:flex" aria-live="polite">
            @if (dirty()) {
              <span class="h-2 w-2 rounded-full bg-amber-500"></span><span class="font-medium text-slate-700 dark:text-slate-200">Unsaved changes</span>
            } @else {
              <svg class="h-3.5 w-3.5 text-emerald-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M20 6 9 17l-5-5"/></svg><span class="text-slate-500 dark:text-slate-400">All changes saved</span>
            }
          </p>
          <div class="flex flex-1 items-center justify-end gap-2 sm:flex-none">
            @if (dirty()) {
              <button type="button" (click)="discard()" [disabled]="saving()" class="rounded-xl px-3 py-2 text-sm font-medium text-slate-600 transition hover:bg-slate-100 disabled:opacity-50 dark:text-slate-300 dark:hover:bg-white/10">Discard</button>
            }
            <button type="submit" form="buyer-form" [disabled]="saving() || hasErrors() || !dirty()"
                    [ngClass]="nextId() ? 'border border-slate-200 bg-white text-slate-700 hover:bg-slate-50 dark:border-white/10 dark:bg-slate-800 dark:text-slate-200 dark:hover:bg-slate-700' : 'bg-gradient-to-r from-primary-600 to-primary-500 text-white shadow-md shadow-primary-500/25 hover:from-primary-700 hover:to-primary-600'"
                    class="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-sm font-semibold transition active:scale-[0.98] disabled:cursor-not-allowed disabled:opacity-50">
              @if (saving() === 'save') { <ng-container *ngTemplateOutlet="spinner"></ng-container> Saving… } @else { Save }
            </button>
            @if (nextId()) {
              <button type="button" (click)="save(true)" [disabled]="saving() || hasErrors()"
                      class="inline-flex items-center gap-1.5 rounded-xl bg-gradient-to-r from-primary-600 to-primary-500 px-4 py-2 text-sm font-semibold text-white shadow-md shadow-primary-500/25 transition hover:from-primary-700 hover:to-primary-600 active:scale-[0.98] disabled:cursor-not-allowed disabled:opacity-50">
                @if (saving() === 'next') { <ng-container *ngTemplateOutlet="spinner"></ng-container> Saving… } @else {
                  {{ dirty() ? 'Save & next' : 'Next' }}
                  <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M5 12h14M13 5l7 7-7 7"/></svg>
                }
              </button>
            }
          </div>
        </div>
      </div>
    }

    <app-confirm-dialog [open]="leaveOpen()" title="Leave without saving?" variant="warning"
                        description="You have changes to this customer's buyer details that haven't been saved."
                        confirmLabel="Leave without saving" cancelLabel="Keep editing"
                        (confirm)="resolveLeave(true)" (cancel)="resolveLeave(false)"></app-confirm-dialog>

    <ng-template #spinner>
      <svg class="h-3.5 w-3.5 animate-spin" viewBox="0 0 24 24" fill="none" aria-hidden="true"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
    </ng-template>

    <ng-template #badge let-s>
      @switch (s) {
        @case ('Compliant') { <span class="inline-flex items-center gap-1 rounded-full bg-emerald-50 px-2.5 py-0.5 text-xs font-medium text-emerald-700 ring-1 ring-inset ring-emerald-500/20 dark:bg-emerald-500/10 dark:text-emerald-300"><span class="h-1.5 w-1.5 rounded-full bg-emerald-500"></span>Compliant</span> }
        @case ('MissingDetails') { <span class="inline-flex items-center gap-1 rounded-full bg-amber-50 px-2.5 py-0.5 text-xs font-medium text-amber-700 ring-1 ring-inset ring-amber-500/20 dark:bg-amber-500/10 dark:text-amber-300"><span class="h-1.5 w-1.5 rounded-full bg-amber-500"></span>Missing details</span> }
        @case ('Invalid') { <span class="inline-flex items-center gap-1 rounded-full bg-rose-50 px-2.5 py-0.5 text-xs font-medium text-rose-700 ring-1 ring-inset ring-rose-500/20 dark:bg-rose-500/10 dark:text-rose-300"><span class="h-1.5 w-1.5 rounded-full bg-rose-500"></span>Invalid TIN / VAT</span> }
        @default { <span class="inline-flex items-center gap-1 rounded-full bg-slate-100 px-2.5 py-0.5 text-xs font-medium text-slate-600 ring-1 ring-inset ring-slate-500/10 dark:bg-white/10 dark:text-slate-300"><span class="h-1.5 w-1.5 rounded-full bg-slate-400"></span>No TIN · cash sale</span> }
      }
    </ng-template>
  `
})
export class CustomerDetail {
  private service = inject(TaxDetailsService);
  private toast = inject(ToastService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);

  readonly sections = BUYER_SECTIONS;
  readonly fields = ALL_FIELDS;
  readonly requiredTotal = REQUIRED.length;

  readonly customer = signal<CustomerTaxDetails | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly saving = signal<'save' | 'next' | null>(null);
  readonly openTip = signal<BuyerKey | null>(null);
  readonly queue = signal<string[]>([]);
  readonly leaveOpen = signal(false);

  id = '';
  draft: BuyerDraft = { ...EMPTY_DRAFT };
  private initial: BuyerDraft = { ...EMPTY_DRAFT };
  private queueKey = '';
  private leaveResolver: ((ok: boolean) => void) | null = null;

  constructor() {
    // The same component serves every customer (Save & next only changes the id), so follow the route.
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((p) => {
      this.id = p.get('id') ?? '';
      this.load(this.id);
    });
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((q) => {
      const status = q.get('queue') ?? '';
      const search = q.get('q') ?? '';
      const key = `${status}|${search}`;
      if (key === this.queueKey) return;
      this.queueKey = key;
      if (!QUEUES.includes(status) && !search) { this.queue.set([]); return; }
      this.service.customerOverview({ status: QUEUES.includes(status) ? status as CustomerComplianceStatus | 'Attention' : '', search: search || undefined, page: 1, pageSize: 100 })
        .subscribe({ next: (res) => this.queue.set((res.items ?? []).map((i) => i.customerId)), error: () => this.queue.set([]) });
    });
  }

  load(id: string): void {
    this.loading.set(true);
    this.error.set(null);
    this.openTip.set(null);
    this.service.getCustomer(id).subscribe({
      next: (c) => {
        this.apply(c);
        this.loading.set(false);
        // Put the cursor in the first box that still needs something, so fixing a customer is type-and-save.
        const first = ALL_FIELDS.find((f) => this.needed(f) || !!this.fieldErr(f.key) || (f.key === 'tin' && this.xeroTinInvalid()));
        // Desktop only: on a phone, focusing would pop the keyboard up and scroll past the header.
        const finePointer = typeof matchMedia === 'function' && matchMedia('(pointer: fine)').matches;
        if (first && finePointer) setTimeout(() => document.getElementById('cf-' + first.key)?.focus({ preventScroll: true }), 60);
      },
      error: (err) => {
        this.loading.set(false);
        this.customer.set(null);
        this.error.set(err?.status === 404 ? 'This customer no longer exists in Xero.' : extractErrorMessage(err, 'Could not load this customer.'));
      }
    });
  }

  private apply(c: CustomerTaxDetails): void {
    this.customer.set(c);
    this.draft = draftFrom(c.saved, c.xero);
    this.initial = { ...this.draft };
  }

  // ---- values ----

  xeroValue(key: BuyerKey): string { return (this.customer()?.xero?.[key] ?? '').toString().trim(); }

  /** What will be sent for a field: the typed value, else Xero (trade name falls back to the registered name). */
  /** What ZIMRA will get for a field: exactly what is in the box (an emptied box sends nothing). */
  sent(key: BuyerKey): string {
    const v = this.draft[key].trim();
    return v || (key === 'tradeName' ? this.sent('registeredName') : '');
  }

  hasTin(): boolean { return !!this.draft.tin.trim(); }

  needed(f: BuyerField): boolean { return !!f.required && this.hasTin() && !this.sent(f.key); }

  fieldErr(key: BuyerKey): string | null { return fieldError(key, this.draft[key]); }

  inputClass(f: BuyerField): string {
    const mono = f.mono ? 'font-mono ' : '';
    if (this.fieldErr(f.key)) return mono + '!border-rose-400 focus:!ring-rose-500/20';
    if (this.needed(f)) return mono + '!border-amber-400 !bg-amber-50/60 dark:!bg-amber-500/5';
    return mono;
  }

  hasErrors(): boolean { return BUYER_KEYS.some((k) => !!this.fieldErr(k)); }

  private xeroTinInvalid(): boolean { return !this.draft.tin.trim() && !!this.xeroValue('tin') && !!fieldError('tin', this.xeroValue('tin')); }

  liveProblems(): string[] {
    const out: string[] = [];
    const tinErr = this.fieldErr('tin'), vatErr = this.fieldErr('vatNumber');
    if (tinErr) out.push(tinErr);
    else if (this.xeroTinInvalid()) out.push(`The TIN from Xero ("${this.xeroValue('tin')}") is not 10 digits. Type the correct TIN here.`);
    if (vatErr) out.push(vatErr);
    else if (!this.draft.vatNumber.trim() && this.xeroValue('vatNumber') && fieldError('vatNumber', this.xeroValue('vatNumber')) && !/^0{9}$/.test(this.xeroValue('vatNumber').replace(/\D/g, '')))
      out.push(`The VAT number from Xero ("${this.xeroValue('vatNumber')}") is not 9 digits. Type the correct one here.`);
    const vat = this.sent('vatNumber').replace(/\D/g, '');
    if (!this.hasTin() && vat && !/^0+$/.test(vat)) out.push('Has a VAT number but no TIN. ZIMRA needs the TIN for every VAT-registered buyer.');
    return out;
  }

  liveMissing(): BuyerField[] { return REQUIRED.filter((f) => this.needed(f)); }

  requiredDone(): number { return this.requiredTotal - this.liveMissing().length; }

  liveStatus(): LiveStatus {
    if (this.liveProblems().length) return 'Invalid';
    if (!this.hasTin()) return 'NoTin';
    return this.liveMissing().length ? 'MissingDetails' : 'Compliant';
  }

  /** The "Where is this in Xero?" text. */
  tip(f: BuyerField): string { return f.where; }

  /** What is saved in Fiscalize for a field right now (after the last save). */
  savedValue(key: BuyerKey): string { return this.initial[key].trim(); }

  /** True when the value was cleared in Fiscalize (saved as ""), so Xero's value isn't used. */
  isCleared(key: BuyerKey): boolean { return this.customer()?.saved?.[key] === ''; }

  /** True when the box holds exactly what Xero has. */
  fromXero(key: BuyerKey): boolean { return sameValue(key, this.draft[key], this.xeroValue(key)); }

  /** Where a changed value will be saved: the TIN and VAT number also go to the Xero contact, the rest stays in Fiscalize. */
  savesTo(key: BuyerKey): string {
    return WRITTEN_TO_XERO.includes(key) ? 'Saves to Xero' : 'Saves in Fiscalize';
  }

  dirty(): boolean { return BUYER_KEYS.some((k) => this.draft[k].trim() !== this.initial[k].trim()); }

  // ---- queue ----

  position(): number { return this.queue().indexOf(this.id); }

  nextId(): string | null {
    const q = this.queue(), i = q.indexOf(this.id);
    return i >= 0 && i < q.length - 1 ? q[i + 1] : null;
  }

  queueLabel(): string {
    switch (this.route.snapshot.queryParamMap.get('queue')) {
      case 'Attention': return 'needing attention';
      case 'MissingDetails': return 'missing details';
      case 'Invalid': return 'with invalid TIN / VAT';
      case 'Compliant': return 'compliant';
      case 'NoTin': return 'without a TIN';
      default: return 'in this list';
    }
  }

  go(step: number): void {
    const target = this.queue()[this.position() + step];
    if (target) this.router.navigate(['/customers', target], { queryParamsHandling: 'preserve' });
  }

  // ---- actions ----

  toggleTip(key: BuyerKey): void { this.openTip.set(this.openTip() === key ? null : key); }

  discard(): void { this.draft = { ...this.initial }; }

  save(andNext: boolean): void {
    if (this.hasErrors() || this.saving()) return;
    const next = andNext ? this.nextId() : null;
    if (!this.dirty()) { if (next) this.router.navigate(['/customers', next], { queryParamsHandling: 'preserve' }); return; }

    this.saving.set(andNext ? 'next' : 'save');
    this.service.saveCustomer(this.id, toSavePayload(this.draft, this.customer()?.xero, this.customer()?.saved)).subscribe({
      next: (saved) => {
        this.saving.set(null);
        this.apply(saved);
        const where = saved.xeroUpdated ? ' and updated in Xero' : '';
        if (saved.xeroWarning) this.toast.warning(saved.xeroWarning);
        else this.toast.success(saved.complianceStatus === 'Compliant'
          ? `${saved.displayName} is ready for ZIMRA. Saved${where}.`
          : `Saved buyer details for ${saved.displayName}${where}.`);
        if (next) this.router.navigate(['/customers', next], { queryParamsHandling: 'preserve' });
      },
      error: (err) => {
        this.saving.set(null);
        this.toast.error(extractErrorMessage(err, 'Could not save buyer details.'));
      }
    });
  }

  private returnTo(): string | null {
    const r = this.route.snapshot.queryParamMap.get('returnTo');
    // Only paths inside this app.
    return r && r.startsWith('/') && !r.startsWith('//') ? r : null;
  }

  backLabel(): string { return this.returnTo() ? 'Back to the document' : 'Back to customers'; }

  back(): void {
    const r = this.returnTo();
    if (r) { this.router.navigateByUrl(r); return; }
    const status = this.route.snapshot.queryParamMap.get('queue');
    const q = this.route.snapshot.queryParamMap.get('q');
    this.router.navigate(['/customers'], { queryParams: { status: status || null, q: q || null } });
  }

  initials(name: string | null | undefined): string { return customerInitials(name); }

  // ---- unsaved changes ----

  /** Route guard: ask before leaving with unsaved edits. */
  canLeave(): boolean | Promise<boolean> {
    if (!this.dirty() || this.saving()) return true;
    this.leaveOpen.set(true);
    return new Promise<boolean>((resolve) => (this.leaveResolver = resolve));
  }

  resolveLeave(ok: boolean): void {
    this.leaveOpen.set(false);
    if (ok) this.initial = { ...this.draft }; // so a param-only navigation doesn't ask twice
    this.leaveResolver?.(ok);
    this.leaveResolver = null;
  }

  @HostListener('window:beforeunload', ['$event'])
  onBeforeUnload(e: BeforeUnloadEvent): void {
    if (this.dirty()) e.preventDefault();
  }
}
