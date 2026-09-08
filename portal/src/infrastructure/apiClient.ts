export const apiEnabled = import.meta.env.VITE_DATA_MODE !== 'local';
export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message);
  }
}
export async function apiRequest<T>(path: string, init: RequestInit = {}, cached?: T): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set('X-Kanbada-Request', '1');
  if (init.body && !(init.body instanceof FormData))
    headers.set('Content-Type', 'application/json');
  const response = await fetch('/api' + path, { ...init, headers, credentials: 'include' });
  if (response.status === 304 && cached !== undefined) return structuredClone(cached);
  if (!response.ok) {
    if (response.status === 401) window.dispatchEvent(new Event('kanbada-session-expired'));
    let body: { detail?: string; error?: string; title?: string } = {};
    try {
      body = await response.json();
    } catch {
      /* Keep a useful fallback. */
    }
    throw new ApiError(
      response.status,
      body.detail || body.error || body.title || 'The request could not be completed.',
    );
  }
  if (response.status === 204) return undefined as T;
  return response.json();
}
export async function downloadApi(path: string, name: string) {
  const response = await fetch('/api' + path, { credentials: 'include' });
  if (!response.ok) throw new Error('This file is unavailable.');
  const url = URL.createObjectURL(await response.blob());
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  a.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
