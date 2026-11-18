import type { Section, Space, Tab, Tile } from './types';

export function newId(prefix: 'b' | 's' | 't'): string {
  return `${prefix}_${crypto.randomUUID().replaceAll('-', '').slice(0, 10)}`;
}

export function newTab(title: string): Tab {
  return { id: newId('b'), title, sections: [newSection(null)] };
}

export function newSection(title: string | null): Section {
  return { id: newId('s'), title, tiles: [] };
}

export function findSection(space: Space, sectionId: string): { tab: Tab; section: Section } | undefined {
  for (const tab of space.tabs) {
    const section = tab.sections.find((s) => s.id === sectionId);
    if (section) return { tab, section };
  }
  return undefined;
}

export function findTile(space: Space, tileId: string): { tab: Tab; section: Section; tile: Tile } | undefined {
  for (const tab of space.tabs)
    for (const section of tab.sections) {
      const tile = section.tiles.find((t) => t.id === tileId);
      if (tile) return { tab, section, tile };
    }
  return undefined;
}

/**
 * Move an item to `index` in `to`, where `index` counts positions in `to` as it was before the move.
 * Handles moves within the same array.
 */
function move<T>(from: T[], item: T, to: T[], index: number): void {
  const fromIndex = from.indexOf(item);
  if (fromIndex < 0) return;
  from.splice(fromIndex, 1);
  if (from === to && fromIndex < index) index--;
  to.splice(Math.max(0, Math.min(index, to.length)), 0, item);
}

export function moveTile(space: Space, tileId: string, toSectionId: string, index: number): void {
  const source = findTile(space, tileId);
  const target = findSection(space, toSectionId);
  if (source && target) move(source.section.tiles, source.tile, target.section.tiles, index);
}

export function moveSection(space: Space, sectionId: string, index: number): void {
  const found = findSection(space, sectionId);
  if (found) move(found.tab.sections, found.section, found.tab.sections, index);
}

export function moveTab(space: Space, tabId: string, index: number): void {
  const tab = space.tabs.find((t) => t.id === tabId);
  if (tab) move(space.tabs, tab, space.tabs, index);
}

export function removeTile(space: Space, tileId: string): Tile | undefined {
  const found = findTile(space, tileId);
  if (!found) return undefined;
  found.section.tiles.splice(found.section.tiles.indexOf(found.tile), 1);
  return found.tile;
}

export function tileCount(sections: Section[]): number {
  return sections.reduce((n, s) => n + s.tiles.length, 0);
}

/** "Customer A" → "customer-a", matching the server's slug rule. */
export function slugify(name: string): string {
  return name
    .normalize('NFKD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 40)
    .replace(/-+$/, '');
}

/**
 * The readable URL segment of each tab, derived from its title: "/spaces/personal/tools". Tabs with the same
 * title get "-2", "-3" in order, so every tab keeps a unique address. Renaming a tab changes its address.
 */
export function tabSlugs(tabs: readonly Tab[]): Map<string, string> {
  const result = new Map<string, string>();
  const taken = new Set<string>();
  for (const tab of tabs) {
    const base = slugify(tab.title) || 'tab';
    let slug = base;
    for (let n = 2; taken.has(slug); n++) slug = `${base}-${n}`;
    taken.add(slug);
    result.set(tab.id, slug);
  }
  return result;
}

/** Accept "example.com" as well as a full URL. */
export function normalizeUrl(input: string): string {
  const trimmed = input.trim();
  return /^[a-z][a-z0-9+.-]*:/i.test(trimmed) ? trimmed : `https://${trimmed}`;
}
