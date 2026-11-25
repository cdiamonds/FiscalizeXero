import { HttpErrorResponse } from '@angular/common/http';

const STATUS_MESSAGES: Record<number, string> = {
  0:   'Unable to reach the server. Check your connection.',
  400: 'The request was invalid.',
  401: 'Authentication required. Please sign in.',
  403: 'You do not have permission to do that.',
  424: 'Xero or the Fiscalize platform refused the request. Check the Xero connection and the device, then try again.',
  404: 'The requested resource was not found.',
  422: 'The data provided is invalid.',
  423: 'Your account is locked. Contact support.',
  429: 'Too many requests. Please wait and try again.',
  500: 'A server error occurred. Please try again.',
  502: 'The server is temporarily unavailable.',
  503: 'The service is temporarily unavailable. Please try again shortly.',
  504: 'The server did not respond in time. Please try again.',
};

function isHtml(value: unknown): boolean {
  return typeof value === 'string' && /<[a-z][\s\S]*>/i.test(value);
}

function collectMessages(body: unknown): string[] {
  if (!body || typeof body !== 'object') return [];
  const b = body as Record<string, unknown>;
  const msgs: string[] = [];

  // Flat string arrays: { errors: [...] } or { error: [...] } or { validationErrors: [...] }
  for (const key of ['validationErrors', 'errors', 'error']) {
    if (Array.isArray(b[key])) {
      for (const e of b[key] as unknown[]) {
        if (typeof e === 'string' && (e as string).trim()) msgs.push((e as string).trim());
      }
    }
  }

  // ASP.NET ModelState: { errors: { field: ["msg"] } }
  if (b['errors'] && typeof b['errors'] === 'object' && !Array.isArray(b['errors'])) {
    for (const val of Object.values(b['errors'] as Record<string, unknown>)) {
      if (Array.isArray(val)) {
        for (const v of val) { if (typeof v === 'string' && v.trim()) msgs.push(v.trim()); }
      } else if (typeof val === 'string' && val.trim()) {
        msgs.push(val.trim());
      }
    }
  }

  // Array body: [{ message: "..." }, ...]
  if (Array.isArray(body)) {
    for (const e of body) {
      const m = (e as Record<string, unknown>)?.['message'] ?? (e as Record<string, unknown>)?.['description'];
      if (typeof m === 'string' && m.trim() && !isHtml(m)) msgs.push(m.trim());
      else if (typeof e === 'string' && e.trim()) msgs.push(e.trim());
    }
  }

  return msgs;
}

export function extractErrorMessage(err: HttpErrorResponse, fallback = 'An unexpected error occurred.'): string {
  const body = err.error;

  if (body && !isHtml(body)) {
    if (typeof body === 'string' && body.trim()) return body.trim();

    if (typeof body === 'object') {
      const b = body as Record<string, unknown>;

      // Prefer detailed error arrays over generic message fields
      const msgs = collectMessages(body);
      if (msgs.length === 1) return msgs[0];
      if (msgs.length > 1) return msgs.join(' · ');

      // Fall back to primary single-message fields
      const primary = b['message'] ?? b['title'] ?? b['detail'] ?? b['error'];
      if (typeof primary === 'string' && primary.trim() && !isHtml(primary)) return primary.trim();
    }
  }

  return STATUS_MESSAGES[err.status] ?? fallback;
}

/**
 * Like extractErrorMessage, for requests made with responseType 'blob': a failed download arrives as a
 * Blob holding the API's JSON error, which has to be read before its message can be shown.
 */
export async function extractErrorMessageAsync(err: unknown, fallback: string): Promise<string> {
  const e = err as HttpErrorResponse;
  if (e?.error instanceof Blob) {
    try {
      const body = JSON.parse(await e.error.text());
      return extractErrorMessage(new HttpErrorResponse({ error: body, status: e.status, statusText: e.statusText, url: e.url ?? undefined }), fallback);
    } catch { /* not JSON: fall through to the status-based message */ }
  }
  return extractErrorMessage(err as HttpErrorResponse, fallback);
}

/**
 * True when a failed call means the Xero connection needs attention (it must be reconnected, or Xero refused the
 * call), not that the user must sign in again. The API answers these with 424; the same status is used when the
 * ZIMRA device or the Fiscalize platform refuses, so the address and message decide which it is.
 */
export function isXeroAttention(err: unknown): boolean {
  const e = err as HttpErrorResponse;
  if (e?.status !== 424) return false;
  const message = typeof e.error === 'object' && e.error ? String((e.error as Record<string, unknown>)['message'] ?? '') : '';
  return /\/api\/xero\//i.test(e.url ?? '') || /xero/i.test(message);
}
