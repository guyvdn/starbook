import type { Space, SpaceSummary } from './types';

export type SaveOutcome =
  | { kind: 'saved'; space: Space }
  | { kind: 'conflict'; space: Space }
  | { kind: 'error'; message: string };

export interface IconResult {
  image: string | null;
  title: string | null;
}

async function errorOf(response: Response): Promise<string> {
  try {
    const body = (await response.json()) as { error?: string; detail?: string };
    return body.error ?? body.detail ?? `${response.status} ${response.statusText}`;
  } catch {
    return `${response.status} ${response.statusText}`;
  }
}

async function json<T>(response: Response): Promise<T> {
  if (!response.ok) throw new Error(await errorOf(response));
  return (await response.json()) as T;
}

const jsonHeaders = { 'Content-Type': 'application/json' };

export const api = {
  listSpaces: async () => json<SpaceSummary[]>(await fetch('/api/spaces')),

  getSpace: async (slug: string) => json<Space>(await fetch(`/api/spaces/${encodeURIComponent(slug)}`)),

  async saveSpace(space: Space): Promise<SaveOutcome> {
    try {
      const response = await fetch(`/api/spaces/${encodeURIComponent(space.slug)}`, {
        method: 'PUT',
        headers: jsonHeaders,
        body: JSON.stringify(space),
      });
      if (response.ok) return { kind: 'saved', space: (await response.json()) as Space };
      if (response.status === 409) return { kind: 'conflict', space: (await response.json()) as Space };
      return { kind: 'error', message: await errorOf(response) };
    } catch (e) {
      return { kind: 'error', message: e instanceof Error ? e.message : String(e) };
    }
  },

  createSpace: async (slug: string, name: string) =>
    json<Space>(await fetch('/api/spaces', { method: 'POST', headers: jsonHeaders, body: JSON.stringify({ slug, name }) })),

  async deleteSpace(slug: string): Promise<void> {
    const response = await fetch(`/api/spaces/${encodeURIComponent(slug)}`, { method: 'DELETE' });
    if (!response.ok) throw new Error(await errorOf(response));
  },

  fillMissingIcons: async (slug: string) =>
    json<Space>(await fetch(`/api/spaces/${encodeURIComponent(slug)}/missing-icons`, { method: 'POST' })),

  lookupIcon: async (url: string) =>
    json<IconResult>(await fetch('/api/icons', { method: 'POST', headers: jsonHeaders, body: JSON.stringify({ url }) })),

  searchLogos: async (query: string) =>
    (await json<{ id: string }[]>(await fetch(`/api/logos?q=${encodeURIComponent(query)}`))).map((l) => l.id),

  uploadImage: async (image: Blob) =>
    (await json<{ image: string }>(await fetch('/api/images', { method: 'POST', body: image }))).image,
};
