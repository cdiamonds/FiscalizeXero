import { Injectable } from '@angular/core';
import { FiscalizeResult } from '../../models/xero.models';

// Thin session-only cache: bridges the gap between fiscalization and the next
// list/detail reload so the fiscal pill appears immediately after fiscalizing
// without waiting for a round-trip. Source of truth is the backend DB.
@Injectable({ providedIn: 'root' })
export class FiscalCacheService {
  private session = new Map<string, FiscalizeResult>();

  cache(id: string, result: FiscalizeResult): void {
    this.session.set(id, result);
  }

  get(id: string): FiscalizeResult | undefined {
    return this.session.get(id);
  }

  has(id: string): boolean {
    return this.session.has(id);
  }

  /** Drop a cached record, e.g. after a fiscal cancellation. */
  clear(id: string): void {
    this.session.delete(id);
  }
}
