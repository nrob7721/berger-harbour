/** A failed API call, carrying the ProblemDetails the API returned. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly title: string,
    readonly fieldErrors: Record<string, string[]> = {},
    readonly conflictingReferences: string[] = [],
    readonly type?: string,
  ) {
    super(title);
  }

  get isConflict(): boolean {
    return this.status === 409;
  }

  get isVersionConflict(): boolean {
    return this.status === 409 && this.type === 'concurrency-conflict';
  }

  /** All messages, field errors first. */
  get messages(): string[] {
    const fields = Object.values(this.fieldErrors).flat();
    return fields.length > 0 ? fields : [this.title];
  }
}

interface ProblemDetails {
  title?: string;
  type?: string;
  errors?: Record<string, string[]>;
  conflictingReferences?: string[];
}

/** Calls the same-origin API (proxied to Cloud Run by the Pages Function) and parses ProblemDetails on failure. */
export async function api<T>(path: string, init: { method?: string; body?: unknown; signal?: AbortSignal } = {}): Promise<T> {
  let response: Response;
  try {
    response = await fetch(path, {
      method: init.method ?? (init.body === undefined ? 'GET' : 'POST'),
      headers: init.body === undefined ? { Accept: 'application/json' } : { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: init.body === undefined ? undefined : JSON.stringify(init.body),
      signal: init.signal,
    });
  } catch (e) {
    if ((e as Error).name === 'AbortError') throw e;
    throw new ApiError(0, 'We could not reach the server. Please check your connection and try again.');
  }

  if (response.status === 204) return undefined as T;
  const text = await response.text();
  const json: unknown = text ? safeParse(text) : undefined;
  if (!response.ok) {
    const problem = (json ?? {}) as ProblemDetails;
    throw new ApiError(
      response.status,
      problem.title ?? (response.status >= 500 ? 'Something went wrong. Please try again.' : `Request failed (${response.status}).`),
      problem.errors ?? {},
      problem.conflictingReferences ?? [],
      problem.type,
    );
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

export function query(params: Record<string, string | number | boolean | null | undefined>): string {
  const search = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v !== null && v !== undefined && v !== '') search.set(k, String(v));
  }
  const s = search.toString();
  return s ? `?${s}` : '';
}
