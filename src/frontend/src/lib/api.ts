const API_BASE = "/api";

/**
 * GETs a JSON endpoint under the relative /api base (nginx proxies it to :8080).
 */
export async function apiGet<T>(path: string): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`);
  if (!res.ok) {
    throw new Error(`GET ${path} failed: ${res.status}`);
  }
  return res.json() as Promise<T>;
}
