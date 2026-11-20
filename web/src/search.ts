import { byId, el } from './dom';
import type { Space, Tab, Tile } from './types';

export interface SearchHit {
  tab: Tab;
  /** `null` for an untitled section. */
  sectionTitle: string | null;
  tile: Tile;
}

const MAX_RESULTS = 8;

/** Lower case, accents removed, so "cafe" finds "Café". */
function fold(text: string): string {
  return text.normalize('NFKD').replace(/[̀-ͯ]/g, '').toLowerCase();
}

function hostOf(url: string): string {
  try {
    return new URL(url).hostname.replace(/^www\./, '');
  } catch {
    return url;
  }
}

/**
 * How well a tile matches: lower is better, `null` is no match. Every word of the query must be found. A title that
 * starts with the query beats one that has a word starting with it, which beats a match inside a word, which beats
 * a match on the host only.
 */
function score(tile: Tile, query: string, words: string[]): number | null {
  const title = fold(tile.title);
  const host = fold(hostOf(tile.url));
  if (!words.every((w) => title.includes(w) || host.includes(w))) return null;
  if (title.startsWith(query)) return 0;
  if (words.every((w) => title.startsWith(w) || title.includes(` ${w}`))) return 1;
  if (words.every((w) => title.includes(w))) return 2;
  return 3;
}

/** The tiles of the whole space that match, best first; ties keep their order in the space. */
export function searchSpace(space: Space, text: string): SearchHit[] {
  const query = fold(text.trim());
  if (!query) return [];
  const words = query.split(/\s+/);
  const hits: { hit: SearchHit; score: number }[] = [];
  for (const tab of space.tabs)
    for (const section of tab.sections)
      for (const tile of section.tiles) {
        const s = score(tile, query, words);
        if (s !== null) hits.push({ hit: { tab, sectionTitle: section.title, tile }, score: s });
      }
  return hits.sort((a, b) => a.score - b.score).map((h) => h.hit);
}

export interface SearchOptions {
  getSpace: () => Space;
  /** In edit mode a result opens the tile's dialog instead of the bookmark. */
  isEditing: () => boolean;
  editTile: (tile: Tile) => void;
}

/**
 * The header search box. Typing lists matches from every tab; Up and Down move through them, Enter opens the selected
 * one (Ctrl or Cmd+Enter in a new tab), Escape clears. "/" focuses the box from anywhere on the page.
 */
export function wireSearch(options: SearchOptions): void {
  const input = byId<HTMLInputElement>('search');
  const list = byId('search-results');
  let hits: SearchHit[] = [];
  let selected = 0;

  const close = (): void => {
    list.hidden = true;
    input.setAttribute('aria-expanded', 'false');
    input.removeAttribute('aria-activedescendant');
  };

  const open = (hit: SearchHit, e: MouseEvent | KeyboardEvent): void => {
    if (options.isEditing()) {
      options.editTile(hit.tile);
      input.value = '';
      close();
      return;
    }
    if (e instanceof KeyboardEvent) {
      if (e.ctrlKey || e.metaKey) window.open(hit.tile.url, '_blank', 'noopener');
      else location.assign(hit.tile.url);
    }
  };

  const mark = (): void => {
    [...list.children].forEach((node, i) => {
      node.classList.toggle('selected', i === selected);
      node.setAttribute('aria-selected', String(i === selected));
      if (i === selected) {
        input.setAttribute('aria-activedescendant', node.id);
        node.scrollIntoView({ block: 'nearest' });
      }
    });
  };

  const render = (): void => {
    hits = searchSpace(options.getSpace(), input.value);
    selected = 0;
    if (!input.value.trim()) return close();

    const shown = hits.slice(0, MAX_RESULTS);
    list.replaceChildren(
      ...(shown.length === 0
        ? [el('div', { className: 'search-empty', textContent: 'No bookmarks match.' })]
        : shown.map((hit, i) => resultRow(hit, i))),
    );
    if (hits.length > shown.length)
      list.append(el('div', { className: 'search-empty', textContent: `${hits.length - shown.length} more; keep typing to narrow it down.` }));
    list.hidden = false;
    input.setAttribute('aria-expanded', 'true');
    mark();
  };

  const resultRow = (hit: SearchHit, index: number): HTMLElement => {
    const thumb = el('span', { className: 'result-thumb' });
    if (hit.tile.image) thumb.append(el('img', { src: `/img/${hit.tile.image}`, alt: '', loading: 'lazy', decoding: 'async' }));
    else thumb.textContent = (/[\p{L}\p{N}]/u.exec(hit.tile.title)?.[0] ?? '?').toUpperCase();

    const where = [hit.tab.title, hit.sectionTitle].filter(Boolean).join(' › ');
    const row = el(
      'a',
      { className: 'result', id: `search-result-${index}`, href: hit.tile.url, role: 'option' },
      thumb,
      el('span', { className: 'result-title', textContent: hit.tile.title }),
      el('span', { className: 'result-where', textContent: where }),
    );
    row.addEventListener('mousemove', () => {
      if (selected === index) return;
      selected = index;
      mark();
    });
    row.addEventListener('click', (e) => {
      if (options.isEditing()) {
        e.preventDefault();
        open(hit, e);
      }
    });
    return row;
  };

  input.addEventListener('input', render);
  input.addEventListener('focus', () => {
    if (input.value.trim()) render();
  });

  input.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') {
      if (input.value) {
        input.value = '';
        close();
      } else {
        input.blur();
      }
      e.stopPropagation();
    } else if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      if (list.hidden || hits.length === 0) return;
      e.preventDefault();
      const count = Math.min(hits.length, MAX_RESULTS);
      selected = (selected + (e.key === 'ArrowDown' ? 1 : count - 1)) % count;
      mark();
    } else if (e.key === 'Enter') {
      const hit = hits[selected];
      if (!hit || list.hidden) return;
      e.preventDefault();
      open(hit, e);
    }
  });

  // Close when focus or a click lands outside the box and its results.
  const box = input.closest('.search')!;
  document.addEventListener('click', (e) => {
    if (!box.contains(e.target as Node)) close();
  });
  box.addEventListener('focusout', (e) => {
    if (!box.contains((e as FocusEvent).relatedTarget as Node | null)) close();
  });

  // "/" jumps to the search box, unless the user is typing somewhere or a dialog is open.
  document.addEventListener('keydown', (e) => {
    if (e.key !== '/' || e.ctrlKey || e.metaKey || e.altKey) return;
    const target = e.target as HTMLElement;
    if (target.closest('input, textarea, select, [contenteditable]') || document.querySelector('dialog[open]')) return;
    e.preventDefault();
    input.focus();
    input.select();
  });
}
