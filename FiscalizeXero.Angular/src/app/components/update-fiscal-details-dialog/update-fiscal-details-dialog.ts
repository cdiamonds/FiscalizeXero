import { Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Dialog } from '../dialog/dialog';
import { SearchBar } from '../search-bar/search-bar';
import { HsCodeDialog } from '../hs-code-dialog/hs-code-dialog';
import { TaxDetailsService, CustomerTaxDetails, ItemHsCode } from '../../core/services/xero/tax-details.service';
import { Invoice, HsCodeLookupItem, UpdateInvoiceFiscalInfoRequest } from '../../core/models/xero.models';

interface BuyerForm {
  registeredName: string; tradeName: string; tin: string; vatNumber: string;
  phone: string; email: string; province: string; street: string; houseNo: string; city: string;
}

/** itemId is the Xero item code. */
interface LineRow { itemId: string; name: string; originalCode: string; code: string }

/** An HS code change to save for a Xero item code before re-fiscalizing. */
export interface HsCodeChange { itemId: string; hsCode: string }

/** Payload emitted when the user confirms a fiscal details update. */
export interface FiscalDetailsSubmit { request: UpdateInvoiceFiscalInfoRequest; hsCodeChanges: HsCodeChange[] }

const EMPTY_FORM: BuyerForm = { registeredName: '', tradeName: '', tin: '', vatNumber: '', phone: '', email: '', province: '', street: '', houseNo: '', city: '' };

/**
 * Update an invoice's buyer fiscal details and line HS codes, then re-fiscalize. The buyer form is prefilled from
 * the invoice's Xero contact (plus anything saved in Fiscalize); another customer can be searched and used.
 * HS code edits are saved against the item in Fiscalize and picked up by the re-fiscalization.
 */
@Component({
  selector: 'app-update-fiscal-details-dialog',
  imports: [CommonModule, FormsModule, Dialog, SearchBar, HsCodeDialog],
  template: `
    <app-dialog [open]="open" [title]="'Update Fiscal Details'" [subtitle]="'Correct buyer information & HS codes, then re-fiscalize'"
                [size]="'lg'" [variant]="'gradient'" [hasFooter]="true" [closeOnBackdrop]="!busy"
                [iconPath]="'M11 4H4a2 2 0 00-2 2v14a2 2 0 002 2h14a2 2 0 002-2v-7M18.5 2.5a2.121 2.121 0 013 3L12 15l-4 1 1-4 9.5-9.5z'"
                (close)="onClose()">

      <div class="mb-5 flex items-start gap-3 rounded-xl border border-amber-200/70 bg-amber-50 p-4 dark:border-amber-500/30 dark:bg-amber-500/10">
        <svg class="mt-0.5 h-5 w-5 flex-shrink-0 text-amber-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
        <div class="text-xs leading-relaxed text-amber-700 dark:text-amber-300">
          <span class="font-semibold">This {{ documentLabel }} is already fiscalized.</span>
          Saving will issue a credit note against the original FDMS record to cancel it, then submit the {{ documentLabel }}
          to FDMS again with the corrected details. Both are reported to the tax authority and <span class="font-semibold">cannot be undone</span>.
        </div>
      </div>

      <div class="mb-5">
        <label class="mb-1.5 block text-xs font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">Use another Xero contact</label>
        <app-search-bar [placeholder]="'Search customers by name…'" (valueChange)="onSearch($event)"></app-search-bar>
        <div *ngIf="searching()" class="mt-2 px-1 text-xs text-slate-500 dark:text-slate-400">Searching…</div>
        <div *ngIf="results().length" class="mt-2 max-h-52 overflow-y-auto rounded-xl border border-slate-200 bg-white/70 p-1 shadow-sm dark:border-white/10 dark:bg-slate-950/50">
          <button *ngFor="let c of results()" type="button" (click)="selectCustomer(c)"
                  class="flex w-full items-center justify-between gap-3 rounded-lg px-3 py-2 text-left text-sm transition-colors hover:bg-slate-100 dark:hover:bg-white/5">
            <span class="min-w-0">
              <span class="block truncate font-medium text-slate-900 dark:text-white">{{ c.displayName || c.registeredName }}</span>
              <span class="block truncate text-xs text-slate-500 dark:text-slate-400">{{ c.tin ? 'TIN ' + c.tin : (c.email || c.phone || '—') }}</span>
            </span>
            <svg class="h-4 w-4 flex-shrink-0 text-primary-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14M12 5l7 7-7 7"/></svg>
          </button>
        </div>
        <p *ngIf="selectedName()" class="mt-2 inline-flex items-center gap-1.5 rounded-lg bg-brand-50 px-2.5 py-1 text-xs font-medium text-primary-700 dark:bg-primary-500/10 dark:text-brand-300">
          <svg class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6L9 17l-5-5"/></svg>
          Filled from {{ selectedName() }}
        </p>
      </div>

      <div class="space-y-5">
        <div>
          <h3 class="mb-1 text-sm font-semibold text-slate-900 dark:text-white">Buyer Information</h3>
          <p class="mb-3 text-xs text-slate-500 dark:text-slate-400">What you correct here is remembered for this customer, so their next document uses it too. The TIN and VAT number are also written to the Xero contact.</p>
          <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div><label [class]="labelClass">Registered Name <span class="text-rose-500">*</span></label><input type="text" [(ngModel)]="form.registeredName" maxlength="100" placeholder="Legal registered name" [class]="inputClass" /></div>
            <div><label [class]="labelClass">Trade Name</label><input type="text" [(ngModel)]="form.tradeName" maxlength="100" placeholder="Trading name (if different)" [class]="inputClass" /></div>
            <div><label [class]="labelClass">TIN <span class="text-rose-500">*</span> <span class="font-normal text-slate-400">(10 digits)</span></label><input type="text" inputmode="numeric" [(ngModel)]="form.tin" maxlength="10" placeholder="e.g. 1234567890" [class]="inputClass" /></div>
            <div><label [class]="labelClass">VAT Number <span class="font-normal text-slate-400">(9 digits, optional)</span></label><input type="text" inputmode="numeric" [(ngModel)]="form.vatNumber" maxlength="9" placeholder="e.g. 123456789" [class]="inputClass" /></div>
            <div><label [class]="labelClass">Phone <span class="text-rose-500">*</span></label><input type="tel" [(ngModel)]="form.phone" maxlength="20" placeholder="+263771234567" [class]="inputClass" /></div>
            <div><label [class]="labelClass">Email <span class="text-rose-500">*</span></label><input type="email" [(ngModel)]="form.email" placeholder="buyer@example.com" [class]="inputClass" /></div>
          </div>
        </div>

        <div>
          <h3 class="mb-3 text-sm font-semibold text-slate-900 dark:text-white">Buyer Address</h3>
          <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div><label [class]="labelClass">Province <span class="text-rose-500">*</span></label><input type="text" [(ngModel)]="form.province" maxlength="50" placeholder="e.g. Harare" [class]="inputClass" /></div>
            <div><label [class]="labelClass">City <span class="text-rose-500">*</span></label><input type="text" [(ngModel)]="form.city" maxlength="50" placeholder="e.g. Harare" [class]="inputClass" /></div>
            <div><label [class]="labelClass">House / Building No <span class="text-rose-500">*</span></label><input type="text" [(ngModel)]="form.houseNo" maxlength="50" placeholder="e.g. 42" [class]="inputClass" /></div>
            <div><label [class]="labelClass">Street <span class="text-rose-500">*</span></label><input type="text" [(ngModel)]="form.street" maxlength="100" placeholder="Street name" [class]="inputClass" /></div>
          </div>
        </div>

        <div *ngIf="lines().length">
          <h3 class="mb-1 text-sm font-semibold text-slate-900 dark:text-white">Line Item HS Codes</h3>
          <p class="mb-3 text-xs text-slate-500 dark:text-slate-400">Changing an HS code saves it on the item in Fiscalize and is applied when the {{ documentLabel }} is re-fiscalized.</p>
          <div class="divide-y divide-slate-100 overflow-hidden rounded-xl border border-slate-200/70 dark:divide-white/5 dark:border-white/10">
            <div *ngFor="let li of lines()" class="flex items-center gap-3 px-3 py-2.5">
              <div class="min-w-0 flex-1">
                <p class="truncate text-sm font-medium text-slate-900 dark:text-white">{{ li.name }}</p>
                <p class="text-xs" [ngClass]="li.code !== li.originalCode ? 'font-semibold text-primary-600 dark:text-brand-400' : 'text-slate-500 dark:text-slate-400'">
                  <span class="font-mono">{{ li.code || '—' }}</span>
                  <span *ngIf="li.code !== li.originalCode" class="ml-1 text-slate-400">(was {{ li.originalCode || '—' }})</span>
                </p>
              </div>
              <button type="button" (click)="editHs(li)"
                      class="inline-flex flex-shrink-0 items-center gap-1.5 rounded-lg border border-slate-200 bg-white px-2.5 py-1.5 text-xs font-medium text-slate-700 transition-all duration-200 hover:bg-slate-50 hover:scale-[1.03] dark:border-white/10 dark:bg-white/5 dark:text-slate-200 dark:hover:bg-white/10">
                <svg class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg>
                {{ li.code ? 'Change' : 'Set' }}
              </button>
            </div>
          </div>
        </div>

        <div *ngIf="errors().length" class="rounded-xl border border-rose-200/70 bg-rose-50 p-3 dark:border-rose-500/30 dark:bg-rose-500/10">
          <ul class="space-y-1 text-xs text-rose-700 dark:text-rose-300">
            <li *ngFor="let e of errors()" class="flex items-start gap-1.5"><span class="mt-1 h-1 w-1 flex-shrink-0 rounded-full bg-rose-500"></span>{{ e }}</li>
          </ul>
        </div>

        <button type="button" (click)="removeBuyer.emit()" [disabled]="busy"
                class="text-xs font-medium text-slate-500 underline-offset-2 transition-colors hover:text-rose-600 hover:underline disabled:opacity-50 dark:text-slate-400 dark:hover:text-rose-400">
          Remove buyer & re-fiscalize as a cash sale (B2C)
        </button>
      </div>

      <ng-container dialogFooter>
        <button type="button" (click)="onClose()" [disabled]="busy"
                class="inline-flex items-center rounded-xl border border-slate-200 bg-white/70 px-4 py-2 text-sm font-medium text-slate-700 transition-all duration-200 hover:bg-white hover:scale-[1.02] active:scale-[0.98] disabled:opacity-50 dark:border-white/10 dark:bg-white/5 dark:text-slate-200 dark:hover:bg-white/10">
          Cancel
        </button>
        <button type="button" (click)="onSubmit()" [disabled]="busy"
                class="inline-flex items-center gap-1.5 rounded-xl bg-gradient-to-r from-amber-500 to-orange-500 px-4 py-2 text-sm font-semibold text-white shadow-lg shadow-amber-500/25 transition-all duration-200 hover:scale-[1.02] active:scale-[0.98] disabled:opacity-50 disabled:hover:scale-100">
          <svg *ngIf="busy" class="h-3.5 w-3.5 animate-spin" viewBox="0 0 24 24" fill="none"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
          {{ busy ? 'Updating…' : 'Update & Re-fiscalize' }}
        </button>
      </ng-container>
    </app-dialog>

    <app-hs-code-dialog [open]="hsDialogOpen()" [initialSearch]="hsSearch()" (picked)="onHsPicked($event)" (close)="hsDialogOpen.set(false)"></app-hs-code-dialog>
  `
})
export class UpdateFiscalDetailsDialog {
  private taxDetails = inject(TaxDetailsService);

  readonly labelClass = 'mb-1 block text-xs font-medium text-slate-600 dark:text-slate-300';
  readonly inputClass = 'w-full rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-sm text-slate-900 outline-none transition focus:border-primary-500 focus:bg-white focus:ring-2 focus:ring-primary-500/20 dark:border-white/10 dark:bg-slate-800 dark:text-white';

  @Input() busy = false;
  @Input() set open(value: boolean) { this._open = value; if (value) this.onOpen(); }
  get open(): boolean { return this._open; }
  private _open = false;

  /** The invoice being edited: prefills the buyer and lists the lines. */
  @Input() invoice: Invoice | null = null;
  /** How the document is called in the dialog text. */
  @Input() documentLabel = 'invoice';
  /** HS code in use per item id (from the document page), so the current codes show. */
  @Input() hsCodes: Record<string, ItemHsCode> = {};

  @Output() submitDetails = new EventEmitter<FiscalDetailsSubmit>();
  @Output() removeBuyer = new EventEmitter<void>();
  @Output() close = new EventEmitter<void>();

  form: BuyerForm = { ...EMPTY_FORM };
  readonly results = signal<CustomerTaxDetails[]>([]);
  readonly searching = signal(false);
  readonly selectedName = signal<string | null>(null);
  readonly errors = signal<string[]>([]);
  readonly lines = signal<LineRow[]>([]);
  readonly hsDialogOpen = signal(false);
  readonly hsSearch = signal<string | null>(null);
  private editingItemId: string | null = null;

  private onOpen(): void {
    this.form = { ...EMPTY_FORM };
    this.results.set([]); this.selectedName.set(null); this.errors.set([]);
    this.hsDialogOpen.set(false); this.editingItemId = null;

    const inv = this.invoice;
    const seen = new Set<string>();
    const rows: LineRow[] = [];
    // HS codes are saved per Xero item code; lines without an item take the company default and are not listed.
    for (const l of inv?.lineItems ?? []) {
      const id = l.itemCode?.trim();
      if (!id || seen.has(id.toLowerCase())) continue;
      seen.add(id.toLowerCase());
      const code = (this.hsCodes[id]?.hsCode ?? '').trim();
      rows.push({ itemId: id, name: l.item?.name || (l.description ?? '').split('\n')[0] || id, originalCode: code, code });
    }
    this.lines.set(rows);

    const customerId = inv?.contact?.contactID;
    if (customerId) {
      this.taxDetails.getCustomer(customerId).subscribe({
        next: (c) => this.applyCustomer(c, inv?.contact?.name || c.displayName || ''),
        error: () => {}
      });
    }
  }

  onSearch(text: string): void {
    const q = text.trim();
    if (q.length < 2) { this.results.set([]); return; }
    this.searching.set(true);
    this.taxDetails.listCustomers(q, 1, 8).subscribe({
      next: (res) => { this.results.set(res ?? []); this.searching.set(false); },
      error: () => { this.results.set([]); this.searching.set(false); }
    });
  }

  selectCustomer(c: CustomerTaxDetails): void {
    this.results.set([]);
    this.taxDetails.getCustomer(c.customerId).subscribe({
      next: (full) => this.applyCustomer(full, full.displayName || c.displayName || ''),
      error: () => this.applyCustomer(c, c.displayName || '')
    });
  }

  private applyCustomer(c: CustomerTaxDetails, displayName: string): void {
    const digits = (v?: string | null) => (v ?? '').replace(/\D/g, '');
    this.form = {
      registeredName: c.registeredName || c.displayName || '',
      tradeName: c.tradeName || '',
      tin: /^\d{10}$/.test(digits(c.tin)) ? digits(c.tin) : (c.tin ?? ''),
      vatNumber: c.vatNumber ?? '',
      // Resolved by the API the same way fiscalizing does: Phone, else Mobile, else Direct dial; the address
      // split into house number, street, city and province.
      phone: c.phone ?? c.mobile ?? c.defaultPhone ?? '',
      email: c.email ?? '',
      province: c.province ?? '',
      street: c.street ?? '',
      houseNo: c.houseNo ?? '',
      city: c.city ?? ''
    };
    this.selectedName.set(displayName);
    this.errors.set([]);
  }

  editHs(line: LineRow): void {
    this.editingItemId = line.itemId;
    this.hsSearch.set(line.code || null);
    this.hsDialogOpen.set(true);
  }

  onHsPicked(item: HsCodeLookupItem): void {
    const id = this.editingItemId;
    this.hsDialogOpen.set(false);
    if (!id) return;
    this.lines.update((rows) => rows.map((r) => r.itemId === id ? { ...r, code: (item.code ?? '').trim() } : r));
    this.editingItemId = null;
  }

  private hsCodeChanges(): HsCodeChange[] {
    return this.lines()
      .filter((l) => l.code.trim() && l.code.trim() !== l.originalCode.trim())
      .map((l) => ({ itemId: l.itemId, hsCode: l.code.trim() }));
  }

  private validate(): string[] {
    const f = this.form; const errs: string[] = [];
    const tin = f.tin.trim(); const vat = f.vatNumber.trim();
    if (!tin) errs.push('TIN is required. Use the cash-sale option below to fiscalize without a buyer.');
    else if (!/^\d{10}$/.test(tin)) errs.push('TIN must be exactly 10 numeric digits.');
    else if (tin === '0000000000') errs.push('TIN cannot be 0000000000.');
    if (vat && !/^\d{9}$/.test(vat)) errs.push('VAT number must be exactly 9 numeric digits.');
    if (!f.registeredName.trim()) errs.push('Registered name is required.');
    if (!f.phone.trim()) errs.push('Phone number is required.');
    else if (!/^\+?\d{7,15}$/.test(f.phone.replace(/[\s().-]/g, ''))) errs.push('Phone number must have 7 to 15 digits, e.g. 0771234567 or +263771234567.');
    if (!f.email.trim()) errs.push('Email is required.');
    else if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(f.email.trim())) errs.push('Enter a valid email address.');
    if (!f.province.trim()) errs.push('Province is required.');
    if (!f.city.trim()) errs.push('City is required.');
    if (!f.houseNo.trim()) errs.push('House / building number is required.');
    if (!f.street.trim()) errs.push('Street is required.');
    return errs;
  }

  onSubmit(): void {
    const errs = this.validate();
    this.errors.set(errs);
    if (errs.length) return;
    const f = this.form;
    const request: UpdateInvoiceFiscalInfoRequest = {
      buyerData: {
        registeredName: f.registeredName.trim(),
        tradeName: f.tradeName.trim() || undefined,
        vatNumber: f.vatNumber.trim() || undefined,
        tin: f.tin.trim(),
        contacts: { phoneNumber: f.phone.trim(), email: f.email.trim() },
        address: { province: f.province.trim(), street: f.street.trim(), houseNo: f.houseNo.trim(), city: f.city.trim() }
      }
    };
    this.submitDetails.emit({ request, hsCodeChanges: this.hsCodeChanges() });
  }

  onClose(): void { if (!this.busy) this.close.emit(); }
}
