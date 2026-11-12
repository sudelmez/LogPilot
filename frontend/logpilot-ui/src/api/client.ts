export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}

export class ApiError extends Error {
  status: number;
  problem?: ProblemDetails;

  constructor(status: number, message: string, problem?: ProblemDetails) {
    super(message);
    this.status = status;
    this.problem = problem;
  }
}

/** Backend ProblemDetails (RFC 7807) yanıtlarını okunur bir hata mesajına çevirir. */
export async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, { ...init, headers: { 'Content-Type': 'application/json' } });

  if (!response.ok) {
    let problem: ProblemDetails | undefined;
    try {
      problem = (await response.json()) as ProblemDetails;
    } catch {
      // gövde yok veya JSON değil
    }
    const message = problem?.errors
      ? Object.values(problem.errors).flat().join(' ')
      : (problem?.detail ?? problem?.title ?? `Request failed (${response.status})`);
    throw new ApiError(response.status, message, problem);
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

type Primitive = string | number | boolean | null | undefined;

/** Boş değerleri atlayarak query string üretir ("?a=1&b=2" veya ""). */
export function qs(params: Record<string, Primitive>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') continue;
    search.set(key, String(value));
  }
  const text = search.toString();
  return text ? `?${text}` : '';
}
