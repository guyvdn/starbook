import { api } from './api';
import { el } from './dom';
import { newId, newTab } from './model';
import type { SpaceSummary, Tab, Tile } from './types';

/** The parts of a Speed Dial 2 export that are imported. Thumbnails, visits and preferences are ignored. */
interface SpeedDialExport {
  groups: { id: number; title: string; position: number }[];
  dials: { id: number; idgroup: number; position: number; title: string; url: string; ts_created?: number }[];
}

export function parseExport(text: string): SpeedDialExport {
  const data = JSON.parse(text) as Partial<SpeedDialExport>;
  if (!Array.isArray(data.groups) || !Array.isArray(data.dials)) throw new Error('Not a Speed Dial 2 export: no groups or dials.');
  return data as SpeedDialExport;
}

/** The server accepts only absolute http(s) URLs with a plain host name or IP address. */
function parseHttpUrl(raw: string): URL | null {
  try {
    const url = new URL(raw.trim());
    const plainHost = /^[a-z0-9.-]+$/i.test(url.hostname) || /^\[[0-9a-f:.]+\]$/i.test(url.hostname);
    return (url.protocol === 'http:' || url.protocol === 'https:') && plainHost ? url : null;
  } catch {
    return null;
  }
}

/**
 * Old Speed Dial entries can hold a pasted URL behind a stray prefix, e.g. "http://%20https://example.com/".
 * Browsers accept that; the server does not. Use the part from the last "http(s)://" when the whole is invalid.
 */
export function cleanUrl(raw: string): string | null {
  const url = parseHttpUrl(raw);
  if (url) return url.href;
  const last = Math.max(raw.lastIndexOf('http://'), raw.lastIndexOf('https://'));
  return last > 0 ? (parseHttpUrl(raw.slice(last))?.href ?? null) : null;
}

function toTile(dial: SpeedDialExport['dials'][number]): Tile | null {
  const href = cleanUrl(dial.url ?? '');
  if (!href) return null;
  const title = dial.title?.trim() || new URL(href).hostname.replace(/^www\./, '');
  return { id: newId('t'), title, url: href, image: null };
}

/** Each Speed Dial group becomes a tab with one untitled section, in the export's order. */
export function toTab(group: SpeedDialExport['groups'][number], data: SpeedDialExport): { tab: Tab; skipped: number } {
  const dials = data.dials
    .filter((d) => d.idgroup === group.id)
    .sort((a, b) => a.position - b.position || (a.ts_created ?? 0) - (b.ts_created ?? 0) || a.id - b.id);
  const tiles = dials.map(toTile).filter((t): t is Tile => t !== null);
  const tab = newTab(group.title.trim() || 'Untitled');
  tab.sections[0]!.tiles = tiles;
  return { tab, skipped: dials.length - tiles.length };
}

/**
 * Pick a Speed Dial 2 export, map each group to a space (or skip it), import, then fetch icons. Works against
 * the server directly and reloads the page when done, so the caller must have no unsaved changes.
 */
export function openImportDialog(spaces: SpaceSummary[], currentSlug: string): void {
  const file = el('input', { type: 'file', accept: '.json,application/json' });
  const mapping = el('div', { className: 'import-map' });
  const status = el('p', { className: 'muted' });
  const importButton = el('button', { type: 'submit', className: 'primary', textContent: 'Import', disabled: true });
  let data: SpeedDialExport | null = null;
  const selects = new Map<number, HTMLSelectElement>();

  file.addEventListener('change', async () => {
    const chosen = file.files?.[0];
    if (!chosen) return;
    try {
      data = parseExport(await chosen.text());
    } catch (e) {
      status.textContent = e instanceof Error ? e.message : String(e);
      return;
    }
    selects.clear();
    const groups = [...data.groups].sort((a, b) => a.position - b.position);
    mapping.replaceChildren(
      ...groups.map((group) => {
        const count = data!.dials.filter((d) => d.idgroup === group.id).length;
        const select = el(
          'select',
          {},
          el('option', { value: '', textContent: 'Skip' }),
          ...spaces.map((s) => el('option', { value: s.slug, textContent: s.name, selected: s.slug === currentSlug })),
        );
        selects.set(group.id, select);
        return el('label', {}, el('span', { textContent: `${group.title} (${count})` }), select);
      }),
    );
    status.textContent = `${data.dials.length} bookmarks in ${data.groups.length} groups. Images are not imported; icons are fetched afterwards.`;
    importButton.disabled = false;
  });

  const form = el(
    'form',
    { method: 'dialog' },
    el('h2', { textContent: 'Import from Speed Dial 2' }),
    el('p', {
      className: 'muted',
      textContent:
        'Each group becomes a tab in the space you pick. Create the spaces first. Importing twice adds the tabs twice.',
    }),
    file,
    mapping,
    status,
    el(
      'div',
      { className: 'actions' },
      el('span', { className: 'spacer' }),
      el('button', { type: 'button', textContent: 'Cancel', onclick: () => dialog.close() }),
      importButton,
    ),
  );

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    if (!data) return;
    importButton.disabled = true;
    file.disabled = true;
    for (const select of selects.values()) select.disabled = true;

    try {
      // Tabs per target space, in Speed Dial's group order.
      const bySpace = new Map<string, Tab[]>();
      let skipped = 0;
      for (const group of [...data.groups].sort((a, b) => a.position - b.position)) {
        const slug = selects.get(group.id)?.value;
        if (!slug) continue;
        const { tab, skipped: n } = toTab(group, data);
        skipped += n;
        bySpace.set(slug, [...(bySpace.get(slug) ?? []), tab]);
      }

      for (const [slug, tabs] of bySpace) {
        status.textContent = `Adding ${tabs.length} tab(s) to ${nameOf(slug)}…`;
        const space = await api.getSpace(slug);
        // A new space starts with one empty tab; replace it rather than keep an empty "Home" next to the import.
        const onlyEmptyTab = space.tabs.length === 1 && space.tabs[0]!.sections.every((x) => x.tiles.length === 0);
        if (onlyEmptyTab) space.tabs = [];
        space.tabs.push(...tabs);
        const outcome = await api.saveSpace(space);
        if (outcome.kind !== 'saved') throw new Error(outcome.kind === 'error' ? outcome.message : `${nameOf(slug)} changed meanwhile; try again.`);
      }

      let n = 0;
      for (const slug of bySpace.keys()) {
        status.textContent = `Fetching icons for ${nameOf(slug)} (${++n}/${bySpace.size}). This can take a few minutes…`;
        await api.fillMissingIcons(slug);
      }

      status.textContent = `Done.${skipped ? ` Skipped ${skipped} bookmark(s) without an http(s) URL.` : ''} Reloading…`;
      setTimeout(() => location.reload(), skipped ? 3000 : 800);
    } catch (err) {
      status.textContent = `Import stopped: ${err instanceof Error ? err.message : String(err)}`;
      importButton.disabled = false;
    }
  });

  function nameOf(slug: string): string {
    return spaces.find((s) => s.slug === slug)?.name ?? slug;
  }

  const dialog: HTMLDialogElement = el('dialog', { className: 'tile-dialog import-dialog' }, form);
  dialog.addEventListener('close', () => dialog.remove());
  document.body.append(dialog);
  dialog.showModal();
}
