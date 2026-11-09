/** A failed API call, carrying the server's ProblemDetails. */
export class ApiError extends Error {
  readonly status: number;
  readonly errors: Record<string, string[]>;
  readonly conflictingReferences: string[];

  constructor(status: number, title: string, errors: Record<string, string[]> = {}, conflictingReferences: string[] = []) {
    super(title);
    this.name = 'ApiError';
    this.status = status;
    this.errors = errors;
    this.conflictingReferences = conflictingReferences;
  }

  /** The first message for a field, matched case-insensitively (ProblemDetails keys are camelCase). */
  fieldError(field: string): string | undefined {
    const key = Object.keys(this.errors).find((k) => k.toLowerCase() === field.toLowerCase());
    return key ? this.errors[key]?.[0] : undefined;
  }
}

interface Problem {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
  conflictingReferences?: string[];
}

function absolute(path: string): string {
  // Relative URLs are same-origin (the Pages Function proxies /api/*); tests run without a real origin.
  return typeof window !== 'undefined' && window.location?.origin && window.location.origin !== 'null'
    ? new URL(path, window.location.origin).toString()
    : path;
}

export async function request<T>(method: string, path: string, body?: unknown, signal?: AbortSignal): Promise<T> {
  let response: Response;
  try {
    response = await fetch(absolute(path), {
      method,
      headers: body === undefined ? { Accept: 'application/json' } : { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
      signal,
      credentials: 'same-origin',
    });
  } catch (e) {
    if (e instanceof DOMException && e.name === 'AbortError') throw e;
    throw new ApiError(0, "We couldn't reach the server. Check your connection and try again.");
  }
  if (response.status === 204) return undefined as T;
  const text = await response.text();
  const json: unknown = text ? safeParse(text) : undefined;
  if (!response.ok) {
    const p = (json ?? {}) as Problem;
    throw new ApiError(response.status, p.title ?? p.detail ?? `Something went wrong (${response.status}).`, p.errors ?? {}, p.conflictingReferences ?? []);
  }
  return json as T;
}

function safeParse(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return undefined;
  }
}

export const http = {
  get: <T>(path: string, signal?: AbortSignal) => request<T>('GET', path, undefined, signal),
  post: <T>(path: string, body?: unknown) => request<T>('POST', path, body ?? {}),
  put: <T>(path: string, body: unknown) => request<T>('PUT', path, body),
  delete: <T>(path: string) => request<T>('DELETE', path),
};

export function query(params: Record<string, string | number | boolean | null | undefined>): string {
  const q = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) if (v !== null && v !== undefined && v !== '') q.set(k, String(v));
  const s = q.toString();
  return s ? `?${s}` : '';
}

export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    return error.conflictingReferences.length > 0 ? `${error.message} (${error.conflictingReferences.join(', ')})` : error.message;
  }
  return error instanceof Error ? error.message : 'Something went wrong.';
}
