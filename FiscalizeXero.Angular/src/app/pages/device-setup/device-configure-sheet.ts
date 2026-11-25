import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Dialog } from '../../components/dialog/dialog';
import { ConfirmDialog } from '../../components/confirm-dialog/confirm-dialog';
import { DevicesService } from '../../core/services/xero/devices.service';
import { AuthService } from '../../core/services/auth/auth.service';
import { ToastService } from '../../core/services/toast/toast.service';
import { FiscalizeDevice, FiscalizeDeviceApiKey, DeviceConfig } from '../../core/models/xero.models';
import { extractErrorMessage } from '../../core/utils/http-error';
import { copyToClipboard } from '../../core/utils/download.util';

@Component({
  selector: 'app-device-configure-sheet',
  imports: [CommonModule, ReactiveFormsModule, Dialog, ConfirmDialog],
  template: `
    <app-dialog [open]="open" [title]="device ? 'Configure ' + (device.shortName || device.tradeName || 'device') : ''" [subtitle]="device ? device.modelName + ' ' + (device.modelVersion || '') : ''" size="xl" [hasFooter]="true" (close)="close.emit()">

      <ng-container *ngIf="device">

        <!-- ── Create new API key ── -->
        <form [formGroup]="createForm" (ngSubmit)="createKey()" class="rounded-2xl border border-slate-200/70 bg-white/60 p-4 dark:border-white/10 dark:bg-slate-950/40">
          <h3 class="text-sm font-semibold text-slate-900 dark:text-white">Create new API key</h3>
          <div class="mt-3 grid gap-3 sm:grid-cols-2">
            <div>
              <label class="mb-1 block text-[11px] font-semibold uppercase tracking-wider text-slate-600 dark:text-slate-400">Description</label>
              <input type="text" formControlName="description" class="w-full rounded-lg border border-slate-200 bg-white/70 px-3 py-2 text-sm outline-none focus:border-primary-500 focus:ring-2 focus:ring-primary-500/30 dark:border-white/10 dark:bg-slate-950/70 dark:text-white" />
            </div>
            <div>
              <label class="mb-1 block text-[11px] font-semibold uppercase tracking-wider text-slate-600 dark:text-slate-400">Permissions</label>
              <select formControlName="permissions" class="w-full rounded-lg border border-slate-200 bg-white/70 px-3 py-2 text-sm outline-none focus:border-primary-500 focus:ring-2 focus:ring-primary-500/30 dark:border-white/10 dark:bg-slate-950/70 dark:text-white">
                <option value="ReadWrite">Read &amp; Write</option>
                <option value="Read">Read only</option>
                <option value="Write">Write only</option>
              </select>
            </div>
            <div class="sm:col-span-2">
              <label class="mb-1 block text-[11px] font-semibold uppercase tracking-wider text-slate-600 dark:text-slate-400">Assigned user</label>
              <input type="email" formControlName="assignedUserEmail" placeholder="user@example.com" class="w-full rounded-lg border border-slate-200 bg-white/70 px-3 py-2 text-sm outline-none focus:border-primary-500 focus:ring-2 focus:ring-primary-500/30 dark:border-white/10 dark:bg-slate-950/70 dark:text-white" />
            </div>
          </div>
          <div class="mt-3 flex justify-end">
            <button type="submit" [disabled]="createForm.invalid || busy() === 'create'"
                    class="inline-flex items-center gap-1.5 rounded-xl bg-gradient-to-r from-primary-500 to-primary-600 px-3 py-1.5 text-xs font-semibold text-white shadow-md shadow-primary-500/20 transition-all duration-200 hover:scale-[1.03] active:scale-[0.97] disabled:opacity-50 disabled:hover:scale-100">
              <svg *ngIf="busy() === 'create'" class="h-3 w-3 animate-spin" viewBox="0 0 24 24" fill="none"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
              {{ busy() === 'create' ? 'Creating…' : 'Create key' }}
            </button>
          </div>
        </form>

        <!-- ── Existing API keys ── -->
        <div class="mt-4 rounded-2xl border border-slate-200/70 bg-white/60 dark:border-white/10 dark:bg-slate-950/40">
          <div class="flex items-center justify-between px-4 py-3">
            <h3 class="text-sm font-semibold text-slate-900 dark:text-white">API keys</h3>
            <span class="text-xs text-slate-500 dark:text-slate-400">{{ keys().length }} key(s)</span>
          </div>
          <div *ngIf="loadingKeys()" class="px-4 py-8 text-center">
            <div class="mx-auto h-6 w-6 animate-spin rounded-full border-4 border-brand-200 border-t-primary-500"></div>
          </div>
          <div *ngIf="!loadingKeys() && keys().length === 0" class="px-4 py-8 text-center text-sm text-slate-500 dark:text-slate-400">No API keys yet — create one above.</div>
          <ul class="divide-y divide-slate-100 dark:divide-white/5">
            <li *ngFor="let k of keys()" class="px-4 py-3">
              <div class="flex flex-wrap items-start justify-between gap-3">
                <div class="min-w-0 flex-1">
                  <div class="flex flex-wrap items-center gap-2">
                    <span class="text-sm font-semibold text-slate-900 dark:text-white">{{ k.description || 'Unnamed key' }}</span>
                    <span [ngClass]="statusBadgeClass(k.status)" class="rounded-full px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider">{{ k.status || 'active' }}</span>
                    <span *ngIf="k.permissions" class="rounded-full bg-slate-100 px-2 py-0.5 text-[10px] font-semibold text-slate-700 dark:bg-white/10 dark:text-slate-200">{{ k.permissions }}</span>
                    <span *ngIf="selectedKeyId() === k.id && !manualKey()" class="rounded-full bg-brand-100 px-2 py-0.5 text-[10px] font-semibold text-primary-700 dark:bg-primary-500/15 dark:text-brand-300">Selected</span>
                    <span *ngIf="k.apiSecret" class="rounded-full bg-emerald-100 px-2 py-0.5 text-[10px] font-semibold text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300">Secret ready</span>
                    <span *ngIf="!k.apiSecret && !(existingConfig?.apiKeyId === k.id && existingConfig?.['hasApiKey'])" class="rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-semibold text-amber-700 dark:bg-amber-500/15 dark:text-amber-300" title="The platform only shows a key's secret when it is created or rotated. Rotate this key to get one.">Secret needed · rotate</span>
                  </div>
                  <div class="mt-1 flex items-center gap-2 text-xs text-slate-500 dark:text-slate-400">
                    <code class="rounded bg-slate-100 px-2 py-0.5 font-mono dark:bg-white/5">{{ maskKey(k.apiKey) }}</code>
                    <button type="button" (click)="copyKey(k)" class="rounded p-1 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 dark:hover:bg-white/5 dark:hover:text-slate-200" aria-label="Copy">
                      <svg class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1"/></svg>
                    </button>
                  </div>
                </div>
                <div class="flex flex-wrap items-center gap-2">
                  <button type="button" (click)="useKey(k)" [disabled]="(selectedKeyId() === k.id && !manualKey()) || !canUse(k)"
                          [title]="canUse(k) ? '' : 'Rotate this key first to get its secret'"
                          class="rounded-lg border border-slate-200 bg-white/70 px-2.5 py-1 text-[11px] font-semibold text-slate-700 transition-all duration-200 hover:bg-white hover:scale-[1.03] disabled:opacity-50 disabled:hover:scale-100 dark:border-white/10 dark:bg-white/5 dark:text-slate-200 dark:hover:bg-white/10">
                    {{ selectedKeyId() === k.id && !manualKey() ? 'In use' : 'Use' }}
                  </button>
                  <button type="button" (click)="testKey(k)" [disabled]="busyKeyId() === k.id"
                          class="rounded-lg border border-slate-200 bg-white/70 px-2.5 py-1 text-[11px] font-semibold text-slate-700 transition-all duration-200 hover:bg-white hover:scale-[1.03] disabled:opacity-50 disabled:hover:scale-100 dark:border-white/10 dark:bg-white/5 dark:text-slate-200 dark:hover:bg-white/10">
                    {{ busyKeyId() === k.id && busy() === 'test' ? 'Testing…' : 'Test' }}
                  </button>
                  <button type="button" (click)="rotateKey(k)" [disabled]="busyKeyId() === k.id"
                          class="rounded-lg border border-amber-200 bg-amber-50/70 px-2.5 py-1 text-[11px] font-semibold text-amber-700 transition-all duration-200 hover:bg-amber-100 hover:scale-[1.03] disabled:opacity-50 disabled:hover:scale-100 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-300 dark:hover:bg-amber-500/20">
                    {{ busyKeyId() === k.id && busy() === 'rotate' ? 'Rotating…' : 'Rotate' }}
                  </button>
                  <button type="button" (click)="confirmRevokeId.set(k.id)" [disabled]="busyKeyId() === k.id"
                          class="rounded-lg border border-rose-200 bg-rose-50/70 px-2.5 py-1 text-[11px] font-semibold text-rose-700 transition-all duration-200 hover:bg-rose-100 hover:scale-[1.03] disabled:opacity-50 disabled:hover:scale-100 dark:border-rose-500/30 dark:bg-rose-500/10 dark:text-rose-300 dark:hover:bg-rose-500/20">
                    Revoke
                  </button>
                </div>
              </div>
            </li>
          </ul>
        </div>

        <!-- ── Enter existing key manually ── -->
        <form [formGroup]="manualForm" (ngSubmit)="applyManualKey()" class="mt-4 rounded-2xl border border-slate-200/70 bg-white/60 p-4 dark:border-white/10 dark:bg-slate-950/40">
          <h3 class="text-sm font-semibold text-slate-900 dark:text-white">Or enter existing API key manually</h3>
          <p class="mt-0.5 text-xs text-slate-500 dark:text-slate-400">Paste a key that was generated outside this app.</p>
          <div class="mt-3 grid gap-3 sm:grid-cols-2">
            <div>
              <label class="mb-1 block text-[11px] font-semibold uppercase tracking-wider text-slate-600 dark:text-slate-400">API Key</label>
              <input type="text" formControlName="apiKey" placeholder="Paste API key…" class="w-full rounded-lg border border-slate-200 bg-white/70 px-3 py-2 text-sm font-mono outline-none focus:border-primary-500 focus:ring-2 focus:ring-primary-500/30 dark:border-white/10 dark:bg-slate-950/70 dark:text-white" />
            </div>
            <div>
              <label class="mb-1 block text-[11px] font-semibold uppercase tracking-wider text-slate-600 dark:text-slate-400">API Secret <span class="normal-case font-normal text-slate-400">(optional)</span></label>
              <input type="text" formControlName="apiSecret" placeholder="Paste secret…" class="w-full rounded-lg border border-slate-200 bg-white/70 px-3 py-2 text-sm font-mono outline-none focus:border-primary-500 focus:ring-2 focus:ring-primary-500/30 dark:border-white/10 dark:bg-slate-950/70 dark:text-white" />
            </div>
            <div class="sm:col-span-2">
              <label class="mb-1 block text-[11px] font-semibold uppercase tracking-wider text-slate-600 dark:text-slate-400">Label <span class="normal-case font-normal text-slate-400">(optional)</span></label>
              <input type="text" formControlName="description" placeholder="e.g. Main terminal" class="w-full rounded-lg border border-slate-200 bg-white/70 px-3 py-2 text-sm outline-none focus:border-primary-500 focus:ring-2 focus:ring-primary-500/30 dark:border-white/10 dark:bg-slate-950/70 dark:text-white" />
            </div>
          </div>
          <div *ngIf="manualKey()" class="mt-3 flex items-center justify-between rounded-xl bg-brand-50/70 px-3 py-2 dark:bg-primary-500/10">
            <span class="flex items-center gap-1.5 text-xs font-medium text-primary-700 dark:text-brand-300">
              <svg class="h-3.5 w-3.5 flex-shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6L9 17l-5-5"/></svg>
              Manual key applied — will be used on save
            </span>
            <button type="button" (click)="clearManualKey()" class="text-[11px] font-semibold text-rose-600 hover:text-rose-700 dark:text-rose-400 dark:hover:text-rose-300">Clear</button>
          </div>
          <div class="mt-3 flex justify-end">
            <button type="submit" [disabled]="manualForm.invalid"
                    class="inline-flex items-center gap-1.5 rounded-xl border border-brand-300 bg-brand-50/70 px-3 py-1.5 text-xs font-semibold text-primary-700 transition-all duration-200 hover:bg-brand-100 hover:scale-[1.03] active:scale-[0.97] disabled:opacity-50 disabled:hover:scale-100 dark:border-primary-500/30 dark:bg-primary-500/10 dark:text-brand-300 dark:hover:bg-primary-500/20">
              Apply key
            </button>
          </div>
        </form>

      </ng-container>

      <ng-container dialogFooter>
        <button type="button" (click)="close.emit()" class="inline-flex items-center rounded-xl border border-slate-200 bg-white/70 px-4 py-2 text-sm font-medium text-slate-700 transition-all duration-200 hover:bg-white hover:scale-[1.02] dark:border-white/10 dark:bg-white/5 dark:text-slate-200 dark:hover:bg-white/10">Close</button>
        <button type="button" (click)="saveConfig()" [disabled]="!canSave() || busy() === 'save'"
                class="inline-flex items-center gap-1.5 rounded-xl bg-gradient-to-r from-primary-500 to-primary-600 px-4 py-2 text-sm font-semibold text-white shadow-lg shadow-primary-500/25 transition-all duration-200 hover:scale-[1.02] active:scale-[0.98] disabled:opacity-50 disabled:hover:scale-100">
          <svg *ngIf="busy() === 'save'" class="h-3.5 w-3.5 animate-spin" viewBox="0 0 24 24" fill="none"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"/></svg>
          {{ busy() === 'save' ? 'Saving…' : 'Save configuration' }}
        </button>
      </ng-container>
    </app-dialog>

    <app-confirm-dialog [open]="confirmRevokeId() != null"
                        title="Revoke this API key?"
                        description="This permanently invalidates the key. Existing integrations using it will stop working."
                        variant="danger"
                        confirmLabel="Revoke"
                        [busy]="busy() === 'revoke'"
                        (confirm)="revokeKey(confirmRevokeId()!)"
                        (cancel)="confirmRevokeId.set(null)">
    </app-confirm-dialog>
  `
})
export class DeviceConfigureSheet implements OnChanges {
  @Input() open = false;
  @Input() device: FiscalizeDevice | null = null;
  @Input() existingConfig: DeviceConfig | null = null;
  @Output() close = new EventEmitter<void>();
  @Output() saved = new EventEmitter<DeviceConfig>();

  private devicesService = inject(DevicesService);
  private authService = inject(AuthService);
  private toast = inject(ToastService);
  private fb = inject(FormBuilder);

  readonly keys = signal<FiscalizeDeviceApiKey[]>([]);
  readonly loadingKeys = signal(false);
  readonly busy = signal<'create' | 'test' | 'rotate' | 'revoke' | 'save' | null>(null);
  readonly busyKeyId = signal<number | null>(null);
  readonly selectedKeyId = signal<number | null>(null);
  readonly manualKey = signal<{ apiKey: string; apiSecret: string; description: string } | null>(null);
  readonly confirmRevokeId = signal<number | null>(null);

  readonly createForm = this.fb.group({
    description: ['', Validators.required],
    permissions: ['ReadWrite', Validators.required],
    assignedUserEmail: ['']
  });

  readonly manualForm = this.fb.group({
    apiKey: ['', Validators.required],
    apiSecret: ['', Validators.required],
    description: ['']
  });

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['open']?.currentValue && this.device) {
      this.manualKey.set(null);
      this.manualForm.reset();
      this.createForm.patchValue({
        description: `${this.device.shortName || this.device.tradeName || 'Device'}`,
        permissions: 'ReadWrite',
        assignedUserEmail: this.authService.currentUser()?.email ?? ''
      });
      this.selectedKeyId.set(this.existingConfig?.apiKeyId ?? null);
      this.loadKeys();
    }
  }

  private loadKeys(): void {
    if (!this.device) return;
    this.loadingKeys.set(true);
    this.devicesService.listApiKeys(this.device.id).subscribe({
      next: (list) => { this.keys.set(list ?? []); this.loadingKeys.set(false); },
      error: () => this.loadingKeys.set(false)
    });
  }

  createKey(): void {
    if (!this.device || this.createForm.invalid) return;
    this.busy.set('create');
    const v = this.createForm.getRawValue();
    this.devicesService.createApiKey(this.device.id, {
      description: v.description!,
      permissions: v.permissions!,
      assignedUserEmail: v.assignedUserEmail || undefined
    }).subscribe({
      next: (k) => {
        this.busy.set(null);
        this.keys.update((cur) => [...cur, k]);
        this.selectedKeyId.set(k.id);
        this.manualKey.set(null);
        this.toast.success('API key created.');
        this.createForm.patchValue({ description: '' });
      },
      error: (err) => { this.busy.set(null); this.toast.error(extractErrorMessage(err, 'Failed to create key.')); }
    });
  }

  applyManualKey(): void {
    if (this.manualForm.invalid) return;
    const v = this.manualForm.getRawValue();
    this.manualKey.set({ apiKey: v.apiKey!, apiSecret: v.apiSecret ?? '', description: v.description ?? '' });
    this.selectedKeyId.set(null);
  }

  clearManualKey(): void {
    this.manualKey.set(null);
    this.manualForm.reset();
  }

  /** A key can only be used when its secret is known: fresh from create/rotate, or already stored for this device. */
  canUse(k: FiscalizeDeviceApiKey): boolean {
    if (k.apiSecret) return true;
    return !!this.existingConfig && this.existingConfig.apiKeyId === k.id && !!this.existingConfig['hasApiKey'];
  }

  useKey(k: FiscalizeDeviceApiKey): void {
    if (!this.canUse(k)) { this.toast.warning('Rotate this key first: the platform only shows a secret when a key is created or rotated.'); return; }
    this.selectedKeyId.set(k.id);
    this.manualKey.set(null);
  }

  testKey(k: FiscalizeDeviceApiKey): void {
    this.busy.set('test'); this.busyKeyId.set(k.id);
    this.devicesService.testApiKey(k.id).subscribe({
      next: (ok) => { this.busy.set(null); this.busyKeyId.set(null); ok ? this.toast.success('Key reachable.') : this.toast.error('Key test failed.'); },
      error: (err) => { this.busy.set(null); this.busyKeyId.set(null); this.toast.error(extractErrorMessage(err, 'Test failed.')); }
    });
  }

  rotateKey(k: FiscalizeDeviceApiKey): void {
    this.busy.set('rotate'); this.busyKeyId.set(k.id);
    this.devicesService.generateNewApiKey(k.id).subscribe({
      next: (rotated) => {
        this.busy.set(null); this.busyKeyId.set(null);
        this.keys.update((cur) => cur.map((x) => x.id === rotated.id ? rotated : x));
        this.selectedKeyId.set(rotated.id);
        this.manualKey.set(null);
        this.toast.success('Key rotated. Its new secret is ready; save to use it.');
      },
      error: (err) => { this.busy.set(null); this.busyKeyId.set(null); this.toast.error(extractErrorMessage(err, 'Rotate failed.')); }
    });
  }

  revokeKey(id: number): void {
    this.confirmRevokeId.set(null);
    this.busy.set('revoke'); this.busyKeyId.set(id);
    this.devicesService.revokeApiKey(id).subscribe({
      next: () => {
        this.busy.set(null); this.busyKeyId.set(null);
        this.keys.update((cur) => cur.filter((k) => k.id !== id));
        if (this.selectedKeyId() === id) this.selectedKeyId.set(null);
        this.toast.success('Key revoked.');
      },
      error: (err) => { this.busy.set(null); this.busyKeyId.set(null); this.toast.error(extractErrorMessage(err, 'Revoke failed.')); }
    });
  }

  copyKey(k: FiscalizeDeviceApiKey): void {
    if (!k.apiKey) return;
    copyToClipboard(k.apiKey).then((ok) => { if (ok) this.toast.success('Copied to clipboard.'); });
  }

  canSave(): boolean {
    const mk = this.manualKey();
    if (mk) return !!mk.apiKey && !!mk.apiSecret;
    const key = this.keys().find((k) => k.id === this.selectedKeyId());
    return !!key && this.canUse(key);
  }

  saveConfig(): void {
    if (!this.device || !this.canSave()) return;
    this.busy.set('save');

    const req = {
      deviceId: this.device.id,
      platform: this.device.platform ?? undefined,
      deviceNumber: this.device.deviceID,
      apiKeyId: undefined as number | undefined,
      apiKey: undefined as string | undefined,
      apiSecret: undefined as string | undefined,
      apiKeyDescription: undefined as string | undefined
    };

    if (this.manualKey()) {
      const mk = this.manualKey()!;
      req.apiKey = mk.apiKey;
      req.apiSecret = mk.apiSecret || undefined;
      req.apiKeyDescription = mk.description || undefined;
    } else {
      const key = this.keys().find((k) => k.id === this.selectedKeyId());
      if (!key) { this.busy.set(null); return; }
      req.apiKeyId = key.id;
      req.apiKey = key.apiKey ?? undefined;
      req.apiSecret = key.apiSecret ?? undefined;
      req.apiKeyDescription = key.description ?? undefined;
    }

    this.devicesService.saveConfig(req).subscribe({
      next: (cfg) => {
        this.busy.set(null);
        if (cfg.warning) this.toast.error(cfg.warning);
        else this.toast.success('Configuration saved. This device is now the default.');
        this.saved.emit(cfg);
        if (!cfg.warning) setTimeout(() => this.close.emit(), 1200);
      },
      error: (err) => { this.busy.set(null); this.toast.error(extractErrorMessage(err, 'Save failed.')); }
    });
  }

  maskKey(key: string | null | undefined): string {
    if (!key) return '—';
    if (key.length <= 8) return '••••' + key;
    return key.slice(0, 4) + '••••' + key.slice(-4);
  }

  statusBadgeClass(status: string | null | undefined): string {
    const s = (status || 'Active').toLowerCase();
    if (s === 'active') return 'bg-emerald-100 text-emerald-700 dark:bg-primary-600/15 dark:text-emerald-300';
    if (s === 'suspended' || s === 'inactive') return 'bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300';
    if (s === 'revoked' || s === 'error') return 'bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300';
    return 'bg-slate-100 text-slate-700 dark:bg-white/10 dark:text-slate-200';
  }
}
