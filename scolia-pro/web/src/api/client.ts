// Client HTTP : cookies de session, en-tête anti-CSRF, erreurs normalisées (RFC 9457).
export class ApiError extends Error {
  status: number;
  code?: string;
  errors?: Record<string, string[]>;
  data?: Record<string, unknown>;

  constructor(status: number, message: string, payload?: Record<string, unknown>) {
    super(message);
    this.status = status;
    this.code = payload?.code as string | undefined;
    this.errors = payload?.errors as Record<string, string[]> | undefined;
    this.data = payload;
  }

  fieldError(field: string): string | undefined {
    if (!this.errors) return undefined;
    const key = Object.keys(this.errors).find((k) => k.toLowerCase() === field.toLowerCase());
    return key ? this.errors[key]?.[0] : undefined;
  }
}

type Query = Record<string, string | number | boolean | null | undefined>;

export function qs(query?: Query): string {
  if (!query) return '';
  const params = new URLSearchParams();
  for (const [k, v] of Object.entries(query)) if (v !== undefined && v !== null && v !== '') params.set(k, String(v));
  const s = params.toString();
  return s ? `?${s}` : '';
}

let onUnauthorized: (() => void) | null = null;
export function setUnauthorizedHandler(handler: () => void) {
  onUnauthorized = handler;
}

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const init: RequestInit = { method, credentials: 'same-origin', headers: { Accept: 'application/json' } };
  if (method !== 'GET') (init.headers as Record<string, string>)['X-Scolia'] = '1';
  if (body instanceof FormData) init.body = body;
  else if (body !== undefined) {
    (init.headers as Record<string, string>)['Content-Type'] = 'application/json';
    init.body = JSON.stringify(body);
  }
  let response: Response;
  try {
    response = await fetch(url, init);
  } catch {
    throw new ApiError(0, 'Le serveur SCOLIA ne répond pas. Vérifiez qu’il est démarré et que le réseau est disponible.');
  }
  if (response.status === 204) return undefined as T;
  const type = response.headers.get('content-type') ?? '';
  const payload = type.includes('json') ? await response.json() : undefined;
  if (!response.ok) {
    if (response.status === 401 && onUnauthorized && !url.endsWith('/api/session')) onUnauthorized();
    const message =
      (payload?.title as string) ??
      (response.status === 403 ? 'Vous n’avez pas les droits nécessaires pour cette action.' :
        response.status === 429 ? 'Trop de tentatives. Patientez une minute.' : `Erreur ${response.status}`);
    throw new ApiError(response.status, message, payload);
  }
  return payload as T;
}

export const api = {
  get: <T>(url: string, query?: Query) => request<T>('GET', url + qs(query)),
  post: <T>(url: string, body?: unknown) => request<T>('POST', url, body ?? {}),
  put: <T>(url: string, body?: unknown) => request<T>('PUT', url, body ?? {}),
  del: <T>(url: string) => request<T>('DELETE', url),
};

/** Ouvre un document (PDF, Excel) servi par l'API dans un nouvel onglet ou le télécharge. */
export function openDocument(url: string, query?: Query) {
  window.open(url + qs(query), '_blank', 'noopener');
}
