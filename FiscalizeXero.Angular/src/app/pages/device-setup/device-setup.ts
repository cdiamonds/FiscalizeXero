import { ManageNotice } from '../../components/manage-notice/manage-notice';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DevicesService } from '../../core/services/xero/devices.service';
import { FiscalizeDevice, DeviceConfig } from '../../core/models/xero.models';
import { PageHeader } from '../../components/page-header/page-header';
import { EmptyState } from '../../components/empty-state/empty-state';
import { ToastBanner } from '../../components/toast-banner/toast-banner';
import { ConfirmDialog } from '../../components/confirm-dialog/confirm-dialog';
import { DeviceConfigureSheet } from './device-configure-sheet';

const isProd = (platform: string | null | undefined): boolean => {
  if (!platform) return false;
  const p = platform.trim();
  return p === '1' || p.toLowerCase() === 'production' || p.toLowerCase() === 'prod';
};

const deviceStatusLabel = (s: string | null | undefined): string => {
  switch ((s ?? '').trim()) {
    case '0': return 'New';
    case '1': return 'Active';
    case '2': return 'Inactive';
    case '3': return 'Suspended';
    case '4': return 'Activation Error';
    default:  return s || 'Unknown';
  }
};

const statusToneCls = (s: string | null | undefined): string => {
  switch ((s ?? '').trim()) {
    case '1': return 'text-primary-700 dark:text-brand-400';
    case '3': return 'text-amber-600 dark:text-amber-400';
    case '4': return 'text-rose-500 dark:text-rose-400';
    case '2': return 'text-slate-500 dark:text-slate-400';
    case '0': return 'text-primary-600 dark:text-brand-400';
    default:  return 'text-slate-500 dark:text-slate-400';
  }
};

@Component({
  selector: 'app-device-setup',
  imports: [CommonModule, ManageNotice, PageHeader, EmptyState, ToastBanner, ConfirmDialog, DeviceConfigureSheet],
  template: `
    <app-page-header title="Device setup" subtitle="Pick the fiscal device that will issue receipts on behalf of this company.">
      <button type="button" (click)="refresh()" [disabled]="loading()"
              class="inline-flex items-center gap-1.5 rounded-xl border border-slate-200 bg-white/70 px-3 py-2 text-xs font-medium text-slate-700 transition-all duration-200 hover:bg-white hover:scale-[1.03] disabled:opacity-50 dark:border-white/10 dark:bg-white/5 dark:text-slate-200 dark:hover:bg-white/10">
        <svg class="h-3.5 w-3.5" [class.animate-spin]="loading()" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M23 4v6h-6M1 20v-6h6"/><path d="M3.51 9a9 9 0 0114.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0020.49 15"/></svg>
        Refresh
      </button>
    </app-page-header>
    <app-manage-notice />

    <app-toast-banner *ngIf="message()" kind="success" class="mb-4 block">{{ message() }}</app-toast-banner>
    <app-toast-banner *ngIf="error()" kind="error" class="mb-4 block">{{ error() }}</app-toast-banner>

    <div *ngIf="loading()" class="rounded-2xl border border-white/40 bg-white/70 p-12 text-center backdrop-blur-md dark:border-white/10 dark:bg-slate-900/60">
      <div class="mx-auto h-8 w-8 animate-spin rounded-full border-4 border-brand-200 border-t-primary-500"></div>
      <p class="mt-3 text-sm text-slate-500 dark:text-slate-400">Loading devices…</p>
    </div>

    <ng-container *ngIf="!loading()">
      <!-- Empty state -->
      <div *ngIf="devices().length === 0" class="rounded-2xl border border-white/40 bg-white/70 backdrop-blur-md dark:border-white/10 dark:bg-slate-900/60">
        <app-empty-state
          title="No fiscal devices linked"
          description="Add a fiscal device in the Fiscalize portal first, then return here to bind it to this Xero organisation.">
        </app-empty-state>
      </div>

      <!-- Default device hero banner -->
      <div *ngIf="defaultConfig() as dc"
           class="card-hover mb-6 overflow-hidden rounded-2xl border border-brand-200/70 bg-gradient-to-r from-brand-50/80 via-white to-brand-50/60 p-5 backdrop-blur-md fade-in-down dark:border-primary-500/30 dark:from-primary-500/10 dark:via-slate-900/50 dark:to-primary-600/10">
        <div class="flex items-center gap-4">
          <div class="flex h-14 w-14 flex-shrink-0 items-center justify-center rounded-2xl bg-gradient-to-br from-primary-500 to-primary-600 shadow-lg shadow-primary-500/30">
            <svg class="h-7 w-7 fill-current text-white" viewBox="0 0 24 24"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/></svg>
          </div>
          <div class="min-w-0 flex-1">
            <div class="flex flex-wrap items-center gap-2">
              <span class="text-xs font-semibold uppercase tracking-wider text-primary-700 dark:text-brand-300">Default fiscal device</span>
              <span [ngClass]="isProdConfig(dc) ? 'bg-brand-100 text-brand-700 dark:bg-brand-500/15 dark:text-brand-300 border-brand-200/70 dark:border-brand-500/30' : 'bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300 border-amber-200/70 dark:border-amber-500/30'"
                    class="inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider">
                <svg *ngIf="isProdConfig(dc)" class="h-3 w-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 21h18M5 21V7l8-4v18M19 21V11l-6-4"/></svg>
                <svg *ngIf="!isProdConfig(dc)" class="h-3 w-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 2v6m6-6v6M5 10h14M5 10v10a2 2 0 002 2h10a2 2 0 002-2V10M5 10l2-2h10l2 2"/></svg>
                {{ isProdConfig(dc) ? 'Production' : 'Test' }}
              </span>
            </div>
            <div class="mt-1 truncate text-base font-bold text-slate-900 dark:text-white">{{ deviceLabel(dc) }}</div>
            <div class="mt-0.5 flex flex-wrap gap-x-2 truncate text-xs text-slate-600 dark:text-slate-300">
              <span>Device #{{ dc.deviceNumber }}</span>
              <span *ngIf="dc.apiKeyDescription">·</span>
              <span *ngIf="dc.apiKeyDescription" class="truncate">Key: {{ dc.apiKeyDescription }}</span>
            </div>
          </div>
          <div class="hidden flex-shrink-0 sm:block">
            <div class="inline-flex items-center gap-1.5 rounded-xl bg-white/80 px-3 py-1.5 text-xs font-semibold text-emerald-700 shadow-sm dark:bg-slate-900/60 dark:text-emerald-300">
              <svg class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"/></svg>
              Active
            </div>
          </div>
        </div>
      </div>

      <!-- Production section -->
      <section *ngIf="productionDevices().length > 0" class="mb-8">
        <div class="mb-4 flex items-center gap-3">
          <div class="flex h-9 w-9 items-center justify-center rounded-xl bg-brand-100 text-brand-600 dark:bg-brand-500/15 dark:text-brand-400">
            <svg class="h-4.5 w-4.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 21h18M5 21V7l8-4v18M19 21V11l-6-4"/></svg>
          </div>
          <div>
            <h2 class="text-sm font-semibold text-slate-900 dark:text-white">Production</h2>
            <p class="text-xs text-slate-500 dark:text-slate-400">{{ productionDevices().length }} device{{ productionDevices().length !== 1 ? 's' : '' }}</p>
          </div>
          <div class="ml-2 h-px flex-1 bg-gradient-to-r from-brand-200 to-transparent dark:from-brand-500/30"></div>
        </div>
        <div class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          <ng-container *ngFor="let d of productionDevices(); let i = index">
            <ng-container *ngTemplateOutlet="deviceCard; context: { $implicit: d, i: i }"></ng-container>
          </ng-container>
        </div>
      </section>

      <!-- Test / Sandbox section -->
      <section *ngIf="testDevices().length > 0" class="mb-8">
        <div class="mb-4 flex items-center gap-3">
          <div class="flex h-9 w-9 items-center justify-center rounded-xl bg-amber-100 text-amber-600 dark:bg-amber-500/15 dark:text-amber-400">
            <svg class="h-4.5 w-4.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 2v6m6-6v6M5 10h14M5 10v10a2 2 0 002 2h10a2 2 0 002-2V10M5 10l2-2h10l2 2"/></svg>
          </div>
          <div>
            <h2 class="text-sm font-semibold text-slate-900 dark:text-white">Test / Sandbox</h2>
            <p class="text-xs text-slate-500 dark:text-slate-400">{{ testDevices().length }} device{{ testDevices().length !== 1 ? 's' : '' }}</p>
          </div>
          <div class="ml-2 h-px flex-1 bg-gradient-to-r from-amber-200 to-transparent dark:from-amber-500/30"></div>
        </div>
        <div class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          <ng-container *ngFor="let d of testDevices(); let i = index">
            <ng-container *ngTemplateOutlet="deviceCard; context: { $implicit: d, i: i }"></ng-container>
          </ng-container>
        </div>
      </section>
    </ng-container>

    <!-- Card template -->
    <ng-template #deviceCard let-d let-i="i">
      <div class="card-hover relative flex flex-col overflow-hidden rounded-2xl border bg-white/80 backdrop-blur-md fade-in-down transition-all duration-200 dark:bg-slate-900/60"
           [ngClass]="cardBorderClass(d)"
           [style.animation-delay]="(i * 0.06) + 's'">

        <!-- Accent bar -->
        <div class="h-1.5 w-full flex-shrink-0" [ngClass]="accentBarClass(d)"></div>

        <div class="flex flex-1 flex-col gap-4 p-5">
          <!-- Header -->
          <div class="flex items-start justify-between gap-2">
            <div class="flex min-w-0 items-center gap-3">
              <div class="flex h-11 w-11 flex-shrink-0 items-center justify-center rounded-2xl"
                   [ngClass]="iconBgClass(d)">
                <svg class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="6" y="3" width="12" height="18" rx="2"/><path d="M11 18h2"/></svg>
              </div>
              <div class="min-w-0">
                <h3 class="truncate text-sm font-semibold leading-snug text-slate-900 dark:text-white">{{ deviceTitle(d) }}</h3>
                <p class="mt-0.5 font-mono text-[11px] text-slate-500 dark:text-slate-400">#{{ d.deviceID }}</p>
              </div>
            </div>

            <!-- Status badges -->
            <div class="flex flex-shrink-0 flex-col items-end gap-1">
              <span *ngIf="isDefault(d)" class="inline-flex items-center gap-1 rounded-full border border-amber-200 bg-amber-100 px-1.5 py-0.5 text-[10px] font-semibold text-amber-700 dark:border-amber-500/30 dark:bg-amber-500/15 dark:text-amber-300">
                <svg class="h-2.5 w-2.5 fill-current" viewBox="0 0 24 24"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/></svg>
                Default
              </span>
              <span *ngIf="isConfigured(d) && !isDefault(d)" class="inline-flex items-center gap-1 rounded-full border border-brand-200/70 bg-brand-50 px-1.5 py-0.5 text-[10px] font-semibold text-primary-700 dark:border-primary-500/30 dark:bg-primary-500/15 dark:text-brand-300">
                <svg class="h-2.5 w-2.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M5 13l4 4L19 7"/></svg>
                Configured
              </span>
              <span [ngClass]="isProd(d.platform)
                  ? 'bg-brand-50 text-brand-600 border-brand-200 dark:bg-brand-500/10 dark:text-brand-300 dark:border-brand-500/30'
                  : 'bg-amber-50 text-amber-600 border-amber-200 dark:bg-amber-500/10 dark:text-amber-300 dark:border-amber-500/30'"
                    class="inline-flex items-center gap-1 rounded-full border px-1.5 py-0.5 text-[10px] font-medium">
                {{ isProd(d.platform) ? 'Production' : 'Test' }}
              </span>
            </div>
          </div>

          <!-- Info rows -->
          <div class="flex-1 space-y-1.5">
            <div *ngIf="d.modelName" class="flex items-center gap-2 text-xs text-slate-500 dark:text-slate-400">
              <svg class="h-3.5 w-3.5 flex-shrink-0 text-slate-400" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="2" y="3" width="20" height="14" rx="2" ry="2"/><line x1="8" y1="21" x2="16" y2="21"/><line x1="12" y1="17" x2="12" y2="21"/></svg>
              <span class="truncate">{{ d.modelName }}<span *ngIf="d.modelVersion"> · {{ d.modelVersion }}</span></span>
            </div>
            <div *ngIf="d.serialNumber" class="flex items-center gap-2 text-xs text-slate-500 dark:text-slate-400">
              <svg class="h-3.5 w-3.5 flex-shrink-0 text-slate-400" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>
              <span class="truncate font-mono text-[11px]">{{ d.serialNumber }}</span>
            </div>
            <div *ngIf="addressLine(d)" class="flex items-center gap-2 text-xs text-slate-500 dark:text-slate-400">
              <svg class="h-3.5 w-3.5 flex-shrink-0 text-slate-400" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0118 0z"/><circle cx="12" cy="10" r="3"/></svg>
              <span class="truncate">{{ addressLine(d) }}</span>
            </div>
            <div *ngIf="configFor(d.id)?.apiKeyDescription as keyDesc" class="flex items-center gap-2 text-xs text-slate-500 dark:text-slate-400">
              <svg class="h-3.5 w-3.5 flex-shrink-0 text-slate-400" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 2l-2 2m-7.61 7.61a5.5 5.5 0 11-7.778 7.778 5.5 5.5 0 017.777-7.777zm0 0L15.5 7.5m0 0l3 3L22 7l-3-3m-3.5 3.5L19 4"/></svg>
              <span class="truncate">{{ keyDesc }}</span>
            </div>
          </div>

          <!-- Status row + Configure button -->
          <div class="flex items-center justify-between border-t border-slate-200/60 pt-3 dark:border-white/5">
            <div class="flex items-center gap-2">
              <span class="inline-flex items-center gap-1 text-xs font-semibold" [ngClass]="statusToneCls(d.status)">
                <span class="h-1.5 w-1.5 rounded-full" [ngClass]="statusDotCls(d.status)"></span>
                {{ deviceStatusLabel(d.status) }}
              </span>
              <span *ngIf="d.isFiscalizationEnabled" class="inline-flex items-center gap-1 text-[10px] font-semibold text-primary-700 dark:text-brand-400">
                <svg class="h-3 w-3" viewBox="0 0 24 24" fill="currentColor"><polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"/></svg>
                Fiscal
              </span>
            </div>
            <button type="button" (click)="openConfigure(d)"
                    [ngClass]="configureButtonClass(d)"
                    class="inline-flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs font-semibold transition-all duration-200 hover:scale-[1.04] active:scale-[0.96]">
              <svg class="h-3 w-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 2l-2 2m-7.61 7.61a5.5 5.5 0 11-7.778 7.778 5.5 5.5 0 017.777-7.777zm0 0L15.5 7.5m0 0l3 3L22 7l-3-3m-3.5 3.5L19 4"/></svg>
              {{ isConfigured(d) ? 'Reconfigure' : 'Configure' }}
              <svg class="h-3 w-3 opacity-60" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M9 18l6-6-6-6"/></svg>
            </button>
          </div>

          <!-- Configured device extra actions -->
          <div *ngIf="configFor(d.id) as cfg" class="flex items-center gap-1.5">
            <button *ngIf="!cfg.isDefault" type="button" (click)="setDefault(cfg.id)" [disabled]="busy()"
                    class="inline-flex flex-1 items-center justify-center gap-1 rounded-lg border border-amber-200 bg-amber-50/50 px-2.5 py-1 text-xs font-semibold text-amber-700 transition-all duration-200 hover:bg-amber-100 hover:scale-[1.02] disabled:opacity-50 disabled:hover:scale-100 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-300 dark:hover:bg-amber-500/20">
              <svg *ngIf="settingDefaultId() === cfg.id" class="h-3 w-3 animate-spin" viewBox="0 0 24 24" fill="none"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
              <svg *ngIf="settingDefaultId() !== cfg.id" class="h-3 w-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/></svg>
              Set as default
            </button>
            <div *ngIf="cfg.isDefault" class="inline-flex h-7 flex-1 items-center justify-center gap-1.5 rounded-lg border border-amber-200 bg-amber-50/60 px-2.5 text-xs font-semibold text-amber-700 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-300">
              <svg class="h-3 w-3 fill-amber-500" viewBox="0 0 24 24"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/></svg>
              Default device
            </div>
            <button type="button" (click)="confirmRemoveId.set(cfg.id)" [disabled]="busy()"
                    class="inline-flex items-center gap-1 rounded-lg px-2 py-1 text-xs font-semibold text-rose-600 transition-all duration-200 hover:bg-rose-50 disabled:opacity-50 dark:text-rose-400 dark:hover:bg-rose-500/10">
              <svg *ngIf="removingId() === cfg.id" class="h-3 w-3 animate-spin" viewBox="0 0 24 24" fill="none"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
              <svg *ngIf="removingId() !== cfg.id" class="h-3 w-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 6L6 18M6 6l12 12"/></svg>
              Remove
            </button>
          </div>
        </div>
      </div>
    </ng-template>

    <app-device-configure-sheet
      [open]="sheetOpen()"
      [device]="selectedDevice()"
      [existingConfig]="selectedDevice() ? (configFor(selectedDevice()!.id) ?? null) : null"
      (close)="sheetOpen.set(false)"
      (saved)="onSaved($event)">
    </app-device-configure-sheet>

    <app-confirm-dialog [open]="confirmRemoveId() != null"
                        title="Remove device configuration?"
                        [description]="removeDescription()"
                        variant="danger"
                        confirmLabel="Remove"
                        [busy]="removingId() != null"
                        (confirm)="removeConfig(confirmRemoveId()!)"
                        (cancel)="confirmRemoveId.set(null)">
    </app-confirm-dialog>
  `
})
export class DeviceSetup implements OnInit {
  private service = inject(DevicesService);

  readonly devices = signal<FiscalizeDevice[]>([]);
  readonly configs = signal<DeviceConfig[]>([]);
  readonly loading = signal(false);
  readonly settingDefaultId = signal<number | null>(null);
  readonly removingId = signal<number | null>(null);
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);
  readonly sheetOpen = signal(false);
  readonly selectedDevice = signal<FiscalizeDevice | null>(null);
  readonly confirmRemoveId = signal<number | null>(null);

  readonly busy = computed(() => this.settingDefaultId() != null || this.removingId() != null);

  readonly defaultConfig = computed(() => this.configs().find((c) => c.isDefault) ?? null);
  readonly productionDevices = computed(() => this.devices().filter((d) => isProd(d.platform)));
  readonly testDevices = computed(() => this.devices().filter((d) => !isProd(d.platform)));

  // Re-export helpers to template
  readonly isProd = isProd;
  readonly deviceStatusLabel = deviceStatusLabel;
  readonly statusToneCls = statusToneCls;

  ngOnInit(): void { this.refresh(); }

  refresh(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service.listDevices().subscribe({
      next: (devices) => {
        this.devices.set(devices ?? []);
        this.service.listConfigs().subscribe({
          next: (configs) => { this.configs.set(configs ?? []); this.loading.set(false); },
          error: () => this.loading.set(false)
        });
      },
      error: (err) => { this.loading.set(false); this.error.set(err?.error?.message || err?.message || 'Failed to load devices'); }
    });
  }

  configFor(deviceId: number): DeviceConfig | undefined {
    return this.configs().find((c) => c.deviceId === deviceId);
  }

  isConfigured(d: FiscalizeDevice): boolean { return !!this.configFor(d.id); }
  isDefault(d: FiscalizeDevice): boolean { return this.configFor(d.id)?.isDefault ?? false; }
  isProdConfig(c: DeviceConfig): boolean { return isProd(c.platform ?? this.devices().find((x) => x.id === c.deviceId)?.platform ?? null); }

  deviceTitle(d: FiscalizeDevice): string {
    return d.tradeName || d.shortName || `Device ${d.deviceID}`;
  }

  deviceLabel(c: DeviceConfig): string {
    const d = this.devices().find((x) => x.id === c.deviceId);
    return d ? this.deviceTitle(d) : `Device #${c.deviceNumber}`;
  }

  addressLine(d: FiscalizeDevice): string {
    const a: any = d.address;
    if (!a) return '';
    if (typeof a === 'string') return a;
    return [a.city, a.province].filter(Boolean).join(', ');
  }

  cardBorderClass(d: FiscalizeDevice): string {
    if (this.isDefault(d)) {
      return isProd(d.platform)
        ? 'border-brand-300/70 ring-1 ring-brand-200 shadow-md shadow-brand-500/10 dark:border-brand-500/40 dark:ring-brand-500/20'
        : 'border-emerald-300/70 ring-1 ring-emerald-200 shadow-md shadow-primary-600/10 dark:border-primary-600/40 dark:ring-primary-600/20';
    }
    if (this.isConfigured(d)) return 'border-brand-300/40 dark:border-primary-500/20';
    return 'border-slate-200/70 hover:border-brand-300/40 dark:border-white/10 dark:hover:border-primary-500/30';
  }

  accentBarClass(d: FiscalizeDevice): string {
    if (this.isDefault(d)) {
      return isProd(d.platform)
        ? 'bg-gradient-to-r from-brand-500 via-sky-400 to-primary-500'
        : 'bg-gradient-to-r from-primary-600 to-brand-400';
    }
    if (this.isConfigured(d)) return 'bg-gradient-to-r from-primary-500/70 to-primary-600/40';
    return isProd(d.platform)
      ? 'bg-gradient-to-r from-brand-500/60 to-indigo-400/60'
      : 'bg-gradient-to-r from-amber-400/70 to-orange-400/70';
  }

  iconBgClass(d: FiscalizeDevice): string {
    return isProd(d.platform)
      ? 'bg-brand-100 text-brand-600 dark:bg-brand-500/15 dark:text-brand-400'
      : 'bg-amber-100 text-amber-600 dark:bg-amber-500/15 dark:text-amber-400';
  }

  configureButtonClass(d: FiscalizeDevice): string {
    if (this.isConfigured(d)) {
      return 'border border-slate-200 bg-white/70 text-slate-700 hover:bg-white dark:border-white/10 dark:bg-white/5 dark:text-slate-200 dark:hover:bg-white/10';
    }
    if (isProd(d.platform)) {
      return 'bg-gradient-to-r from-brand-500 to-indigo-500 text-white shadow-md shadow-brand-500/25 hover:from-brand-600 hover:to-indigo-600';
    }
    return 'bg-gradient-to-r from-amber-500 to-orange-500 text-white shadow-md shadow-amber-500/25 hover:from-amber-600 hover:to-orange-600';
  }

  statusDotCls(s: string | null | undefined): string {
    switch ((s ?? '').trim()) {
      case '1': return 'bg-primary-600';
      case '3': return 'bg-amber-500';
      case '4': return 'bg-rose-500';
      case '2': return 'bg-slate-400';
      case '0': return 'bg-primary-500';
      default:  return 'bg-slate-400';
    }
  }

  removeDescription(): string {
    const id = this.confirmRemoveId();
    const cfg = this.configs().find((c) => c.id === id);
    if (cfg?.isDefault) {
      return 'This is your default device. Removing it will promote the next configured device as default.';
    }
    return 'The device will no longer issue receipts for this company. You can re-configure it later.';
  }

  openConfigure(d: FiscalizeDevice): void {
    this.selectedDevice.set(d);
    this.sheetOpen.set(true);
  }

  onSaved(_cfg: DeviceConfig): void {
    this.message.set('Device configuration saved.');
    setTimeout(() => this.message.set(null), 3500);
    this.refresh();
  }

  setDefault(configId: number): void {
    this.settingDefaultId.set(configId);
    this.message.set(null); this.error.set(null);
    this.service.setDefaultConfig(configId).subscribe({
      next: () => {
        this.settingDefaultId.set(null);
        // Optimistic update
        this.configs.update((list) => list.map((c) => ({ ...c, isDefault: c.id === configId })));
        this.message.set('Default device updated.');
        setTimeout(() => this.message.set(null), 3500);
      },
      error: (err) => { this.settingDefaultId.set(null); this.error.set(err?.error?.message || err?.message || 'Failed'); }
    });
  }

  removeConfig(id: number): void {
    this.confirmRemoveId.set(null);
    this.removingId.set(id);
    this.service.deleteConfig(id).subscribe({
      next: () => {
        this.removingId.set(null);
        this.configs.update((list) => {
          const target = list.find((c) => c.id === id);
          let next = list.filter((c) => c.id !== id);
          if (target?.isDefault && next.length > 0) {
            next = next.map((c, i) => i === 0 ? { ...c, isDefault: true } : c);
          }
          return next;
        });
        this.message.set('Device configuration removed.');
        setTimeout(() => this.message.set(null), 3500);
      },
      error: (err) => { this.removingId.set(null); this.error.set(err?.error?.message || err?.message || 'Remove failed'); }
    });
  }
}
