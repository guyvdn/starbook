import './style.css';
import type { Space, SpaceSummary, StartPageData, Tab, Tile } from './types';

/**
 * In production the API embeds the space in the page. Under the Vite dev server there is no embedded data,
 * so fetch it instead.
 */
async function loadData(): Promise<StartPageData | null> {
  const embedded = document.getElementById('space-data');
  if (embedded?.textContent) return JSON.parse(embedded.textContent) as StartPageData;

  const spaces = (await getJson<SpaceSummary[]>('/api/spaces')) ?? [];
  const slug = /^\/s\/([^/]+)/.exec(location.pathname)?.[1];
  if (!slug) {
    if (spaces[0]) location.replace(`/s/${spaces[0].slug}`);
    return null;
  }
  const space = await getJson<Space>(`/api/spaces/${encodeURIComponent(slug)}`);
  return space ? { space, spaces } : null;
}

async function getJson<T>(url: string): Promise<T | null> {
  const response = await fetch(url);
  return response.ok ? ((await response.json()) as T) : null;
}

function el<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  props: Partial<HTMLElementTagNameMap[K]> = {},
  ...children: (Node | string)[]
): HTMLElementTagNameMap[K] {
  const node = Object.assign(document.createElement(tag), props);
  node.append(...children);
  return node;
}

function byId(id: string): HTMLElement {
  const node = document.getElementById(id);
  if (!node) throw new Error(`Missing #${id}`);
  return node;
}

/** The active tab lives in the URL hash, so it survives a reload and can be bookmarked. */
function activeTab(space: Space): Tab | undefined {
  const id = decodeURIComponent(location.hash.slice(1));
  return space.tabs.find((t) => t.id === id) ?? space.tabs[0];
}

function render({ space, spaces }: StartPageData): void {
  document.title = space.name;
  byId('space-name').textContent = space.name;

  const current = activeTab(space);

  byId('tabs').replaceChildren(
    ...space.tabs.map((tab) =>
      el('a', {
        href: `#${encodeURIComponent(tab.id)}`,
        className: tab === current ? 'tab active' : 'tab',
        textContent: tab.title,
      }),
    ),
  );

  const others = spaces.filter((s) => s.slug !== space.slug);
  byId('switcher').classList.toggle('single', others.length === 0);
  byId('spaces').replaceChildren(
    ...others.map((s) => el('a', { href: `/s/${encodeURIComponent(s.slug)}`, className: 'space', textContent: s.name })),
  );

  const content = byId('content');
  if (!current) {
    content.replaceChildren(el('p', { className: 'empty', textContent: 'No tabs yet.' }));
    return;
  }

  content.replaceChildren(
    ...current.sections.map((section) =>
      el(
        'section',
        {},
        ...(section.title ? [el('h2', { textContent: section.title })] : []),
        section.tiles.length
          ? el('div', { className: 'grid' }, ...section.tiles.map(renderTile))
          : el('p', { className: 'empty', textContent: 'No bookmarks yet.' }),
      ),
    ),
  );
}

function renderTile(tile: Tile): HTMLAnchorElement {
  const thumb = el('span', { className: 'thumb' });
  if (tile.image) {
    thumb.append(el('img', { src: `/img/${tile.image}`, alt: '', loading: 'lazy', decoding: 'async' }));
  } else {
    thumb.classList.add('letter');
    thumb.style.setProperty('--hue', String(hue(hostOf(tile.url))));
    thumb.textContent = initial(tile.title);
  }
  return el('a', { className: 'tile', href: tile.url, title: tile.url }, thumb, el('span', { className: 'title', textContent: tile.title }));
}

function hostOf(url: string): string {
  try {
    return new URL(url).hostname.replace(/^www\./, '');
  } catch {
    return url;
  }
}

function initial(title: string): string {
  return (/[\p{L}\p{N}]/u.exec(title)?.[0] ?? '?').toUpperCase();
}

/** Stable colour per site, so tiles for the same site share a colour. */
function hue(text: string): number {
  let h = 0;
  for (const ch of text) h = (h * 31 + ch.codePointAt(0)!) >>> 0;
  return h % 360;
}

const data = await loadData();
if (data) {
  render(data);
  addEventListener('hashchange', () => render(data));
  closeSwitcherOnOutsideClickOrEscape();
}

function closeSwitcherOnOutsideClickOrEscape(): void {
  const switcher = byId('switcher') as HTMLDetailsElement;
  document.addEventListener('click', (e) => {
    if (switcher.open && !switcher.contains(e.target as Node)) switcher.open = false;
  });
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') switcher.open = false;
  });
}
