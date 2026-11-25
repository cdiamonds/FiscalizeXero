import { DestroyRef, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Observable, Subject, EMPTY, catchError, switchMap, tap } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { DocumentPage, DocumentPageQuery } from '../models/xero.models';

export const PAGE_SIZES = [10, 25, 50, 100];
export const DEFAULT_PAGE_SIZE = 25;
const SIZE_KEY = 'fxero.documentPageSize';

/** The last page, size and search used on each document list, so a document's back link returns to it. */
const lastQuery = new Map<string, Record<string, string>>();

/** Query params that reopen a document list where the user left it (for back links on detail pages). */
export function listReturnQuery(listPath: string): Record<string, string> | null {
  return lastQuery.get(listPath) ?? null;
}

function storedPageSize(): number {
  try {
    const n = Number(localStorage.getItem(SIZE_KEY));
    return PAGE_SIZES.includes(n) ? n : DEFAULT_PAGE_SIZE;
  } catch { return DEFAULT_PAGE_SIZE; }
}

/**
 * Server-paged list (invoices and credit notes newest first; items by name).
 * Holds page, page size, search and status filter; keeps them in the URL (?page=&size=&q=&status=) so refresh, back and shared links land on
 * the same page; and drops answers to superseded requests so fast paging or typing never shows stale rows.
 * Create it in a component field initialiser (it uses inject()).
 */
export class DocumentList<T> {
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private destroyRef = inject(DestroyRef);

  readonly items = signal<T[]>([]);
  readonly page = signal(1);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly search = signal('');
  readonly total = signal(0);
  readonly totalPages = signal(1);
  /** '' = approved documents (the default), 'all', or one Xero status. */
  readonly status = signal('');
  /** A request is in flight. */
  readonly loading = signal(false);
  /** The first page has arrived (later loads keep the current rows on screen). */
  readonly loaded = signal(false);
  readonly failed = signal(false);

  readonly rangeStart = computed(() => this.total() === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1);
  readonly rangeEnd = computed(() => Math.min(this.total(), (this.page() - 1) * this.pageSize() + this.items().length));

  private readonly trigger$ = new Subject<boolean>();

  constructor(
    private readonly listPath: string,
    fetch: (q: DocumentPageQuery) => Observable<DocumentPage<T>>,
    onLoaded: (items: T[]) => void,
    onError: (err: HttpErrorResponse) => void
  ) {
    this.trigger$.pipe(
      tap(() => { this.loading.set(true); this.failed.set(false); }),
      switchMap((refresh) => fetch({ page: this.page(), pageSize: this.pageSize(), search: this.search() || undefined, status: this.status() || undefined, refresh }).pipe(
        catchError((err) => {
          this.loading.set(false);
          this.failed.set(true);
          onError(err);
          return EMPTY;
        })
      )),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe((res) => {
      const total = res.total ?? 0;
      const pages = Math.max(1, res.totalPages ?? 1);
      // The page asked for may be gone (documents deleted, a narrower search): show the last one instead.
      if (this.page() > pages && total > 0) {
        this.page.set(pages);
        this.syncUrl();
        this.trigger$.next(false);
        return;
      }
      this.items.set(res.items ?? []);
      this.total.set(total);
      this.totalPages.set(pages);
      this.loading.set(false);
      this.loaded.set(true);
      onLoaded(res.items ?? []);
    });
  }

  /** Read page, size and search from the URL and load. */
  init(): void {
    const q = this.route.snapshot.queryParamMap;
    const page = Number(q.get('page'));
    const size = Number(q.get('size'));
    this.page.set(Number.isInteger(page) && page > 0 ? page : 1);
    this.pageSize.set(PAGE_SIZES.includes(size) ? size : storedPageSize());
    this.search.set((q.get('q') ?? '').trim().slice(0, 100));
    this.status.set(/^[A-Za-z]{1,20}$/.test(q.get('status') ?? '') ? q.get('status')! : '');
    this.remember();
    this.trigger$.next(false);
  }

  goTo(page: number): void {
    const p = Math.min(Math.max(1, page), this.totalPages());
    if (p === this.page()) return;
    this.page.set(p);
    this.changed();
    this.scrollToTop();
  }

  setPageSize(size: number): void {
    if (!PAGE_SIZES.includes(size) || size === this.pageSize()) return;
    // Keep the first row on screen in view: jump to the page that contains it.
    const first = (this.page() - 1) * this.pageSize();
    this.pageSize.set(size);
    this.page.set(Math.floor(first / size) + 1);
    try { localStorage.setItem(SIZE_KEY, String(size)); } catch { /* private mode: keep it for this visit */ }
    this.changed();
  }

  setSearch(text: string): void {
    const t = text.trim().slice(0, 100);
    if (t === this.search()) return;
    this.search.set(t);
    this.page.set(1);
    this.changed();
  }

  setStatus(status: string): void {
    if (status === this.status()) return;
    this.status.set(status);
    this.page.set(1);
    this.changed();
  }

  /** Reload this page. With refresh, the server reads Xero again instead of its short cache. */
  reload(refresh = false): void { this.trigger$.next(refresh); }

  private changed(): void {
    this.syncUrl();
    this.trigger$.next(false);
  }

  private queryParams(): Record<string, string> {
    const q: Record<string, string> = {};
    if (this.page() > 1) q['page'] = String(this.page());
    if (this.pageSize() !== DEFAULT_PAGE_SIZE) q['size'] = String(this.pageSize());
    if (this.search()) q['q'] = this.search();
    if (this.status()) q['status'] = this.status();
    return q;
  }

  private remember(): void { lastQuery.set(this.listPath, this.queryParams()); }

  private syncUrl(): void {
    this.remember();
    const q = this.queryParams();
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { page: q['page'] ?? null, size: q['size'] ?? null, q: q['q'] ?? null, status: q['status'] ?? null },
      queryParamsHandling: 'merge',
      replaceUrl: true
    });
  }

  private scrollToTop(): void {
    try { window.scrollTo({ top: 0, behavior: 'smooth' }); } catch { /* old browsers */ }
  }
}
