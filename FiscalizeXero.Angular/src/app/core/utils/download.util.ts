/** Trigger a browser download for a Blob. */
export function downloadBlob(blob: Blob, filename: string): void {
  if (typeof window === 'undefined') return;
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  setTimeout(() => URL.revokeObjectURL(url), 250);
}

/** Open a Blob (typically PDF) in a new window and try to print it. */
export function printBlob(blob: Blob): void {
  if (typeof window === 'undefined') return;
  const url = URL.createObjectURL(blob);
  const w = window.open(url, '_blank');
  if (w) {
    w.addEventListener('load', () => {
      try { w.focus(); w.print(); } catch { /* swallow */ }
    });
  }
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}

/** Copy text to the clipboard with a fallback. */
export async function copyToClipboard(text: string): Promise<boolean> {
  if (typeof navigator !== 'undefined' && navigator.clipboard) {
    try { await navigator.clipboard.writeText(text); return true; } catch { /* fallthrough */ }
  }
  if (typeof document !== 'undefined') {
    const ta = document.createElement('textarea');
    ta.value = text;
    ta.style.position = 'fixed';
    ta.style.opacity = '0';
    document.body.appendChild(ta);
    ta.select();
    try { document.execCommand('copy'); document.body.removeChild(ta); return true; } catch { document.body.removeChild(ta); }
  }
  return false;
}
