import './style.css';
import { api } from './api';
import { byId, el, present, toast } from './dom';
import {
  findTile,
  moveSection,
  moveTab,
  moveTile,
  newId,
  newSection,
  newTab,
  removeTile,
  slugify,
  tileCount,
} from './model';
import { openTileDialog } from './tile-dialog';
import type { Section, Space, SpaceSummary, StartPageData, Tab, Tile } from './types';

// ---------------------------------------------------------------------------------------------------------------
// State and saving
// ---------------------------------------------------------------------------------------------------------------

let space: Space;
let spaces: SpaceSummary[];
let editing = false;

let dirty = false;
let saving = false;

/** Apply a change locally, render it, and save in the background. Rapid edits coalesce into one save. */
function commit(change: (space: Space) => void): void {
  change(space);
  dirty = true;
  render();
  if (!saving) void flush();
}

async function flush(): Promise<void> {
  saving = true;
  while (dirty) {
    dirty = false;
    const outcome = await api.saveSpace(structuredClone(space));
    if (outcome.kind === 'saved') {
      // Keep local edits made while the save was in flight; only adopt the new version.
      space.version = outcome.space.version;
      if (!dirty) space = outcome.space;
    } else if (outcome.kind === 'conflict') {
      space = outcome.space;
      dirty = false;
      render();
      toast('This space was changed elsewhere. Reloaded the latest version; redo your last change.');
    } else {
      dirty = false;
      toast(`Not saved: ${outcome.message}`);
      space = await api.getSpace(space.slug).catch(() => space);
      render();
    }
  }
  saving = false;
}

addEventListener('beforeunload', (e) => {
  if (dirty || saving) e.preventDefault();
});

// ---------------------------------------------------------------------------------------------------------------
// Rendering
// ---------------------------------------------------------------------------------------------------------------

/** The active tab lives in the URL hash, so it survives a reload. */
function activeTab(): Tab | undefined {
  const id = decodeURIComponent(location.hash.slice(1));
  return space.tabs.find((t) => t.id === id) ?? space.tabs[0];
}

function selectTab(tab: Tab): void {
  history.replaceState(null, '', `#${encodeURIComponent(tab.id)}`);
  render();
}

function render(): void {
  document.title = space.name;
  document.body.classList.toggle('editing', editing);
  byId('space-name').textContent = space.name;
  renderSpaces();
  renderTabs();
  renderContent();
  byId('edit-toggle').textContent = editing ? 'Done' : 'Edit';
}

function renderSpaces(): void {
  const others = spaces.filter((s) => s.slug !== space.slug);
  byId('switcher').classList.toggle('single', others.length === 0 && !editing);
  byId('spaces').replaceChildren(
    ...present([
    ...others.map((s) => el('a', { href: `/s/${encodeURIComponent(s.slug)}`, className: 'space', textContent: s.name })),
    editing ? el('hr') : null,
    editing ? el('button', { className: 'space', textContent: 'Rename this space…', onclick: renameSpace }) : null,
    editing ? el('button', { className: 'space', textContent: 'Fetch missing icons', onclick: fillMissingIcons }) : null,
    editing ? el('button', { className: 'space', textContent: 'New space…', onclick: createSpace }) : null,
    editing ? el('button', { className: 'space danger', textContent: 'Delete this space…', onclick: deleteSpace }) : null,
    ]),
  );
}

function renderTabs(): void {
  const current = activeTab();
  byId('tabs').replaceChildren(
    ...present([
    ...space.tabs.map((tab) => {
      const link = el('a', {
        href: `#${encodeURIComponent(tab.id)}`,
        className: tab === current ? 'tab active' : 'tab',
        textContent: tab.title,
      });
      if (editing) {
        link.draggable = true;
        link.addEventListener('click', (e) => {
          e.preventDefault();
          if (tab === current) renameTab(tab);
          else selectTab(tab);
        });
        if (tab === current) link.title = 'Click to rename';
        dragSource(link, { kind: 'tab', id: tab.id });
        dropTarget(link, 'x', (d) => d.kind === 'tab' || d.kind === 'tile', (d, after) => {
          if (d.kind === 'tab') commit((s) => moveTab(s, d.id, s.tabs.indexOf(tab) + (after ? 1 : 0)));
          else commit((s) => moveTile(s, d.id, tab.sections[0]!.id, Number.MAX_SAFE_INTEGER));
        });
      }
      return link;
    }),
    editing && current ? el('button', { className: 'tab-action', title: 'Delete this tab', textContent: '×', onclick: () => deleteTab(current) }) : null,
    editing ? el('button', { className: 'tab-action', title: 'Add a tab', textContent: '+', onclick: addTab }) : null,
    ]),
  );
}

function renderContent(): void {
  const content = byId('content');
  const current = activeTab();
  if (!current) {
    content.replaceChildren(el('p', { className: 'empty', textContent: editing ? 'Add a tab with + above.' : 'No tabs yet.' }));
    return;
  }

  content.replaceChildren(
    ...present([
      ...current.sections.map((section) => renderSection(current, section)),
      editing ? el('button', { className: 'add-section', textContent: '+ Add section', onclick: () => addSection(current) }) : null,
    ]),
  );
}

function renderSection(tab: Tab, section: Section): HTMLElement {
  const heading =
    section.title || editing
      ? el(
          'div',
          { className: 'section-head' },
          el('h2', { textContent: section.title ?? 'Untitled section', className: section.title ? '' : 'muted' }),
          editing ? el('button', { className: 'icon', title: 'Rename section', textContent: 'Rename', onclick: () => renameSection(section) }) : null,
          editing ? el('button', { className: 'icon', title: 'Delete section', textContent: 'Delete', onclick: () => deleteSection(tab, section) }) : null,
        )
      : null;

  const grid = el(
    'div',
    { className: 'grid' },
    ...section.tiles.map((tile) => renderTile(tile)),
    editing ? el('button', { className: 'tile add', textContent: '+', title: 'Add a bookmark', onclick: () => addTile(section) }) : null,
  );
  if (!editing && section.tiles.length === 0) grid.append(el('p', { className: 'empty', textContent: 'No bookmarks yet.' }));

  const node = el('section', {}, heading, grid);

  if (editing) {
    heading!.draggable = true;
    dragSource(heading!, { kind: 'section', id: section.id });
    dropTarget(node, 'y', (d) => d.kind === 'section', (d, after) =>
      commit((s) => moveSection(s, d.id, tab.sections.indexOf(section) + (after ? 1 : 0))),
    );
    // Dropping a tile on empty grid space appends it to this section.
    dropTarget(grid, null, (d) => d.kind === 'tile', (d) => commit((s) => moveTile(s, d.id, section.id, Number.MAX_SAFE_INTEGER)));
  }
  return node;
}

function renderTile(tile: Tile): HTMLElement {
  const thumb = el('span', { className: 'thumb' });
  if (tile.image) {
    thumb.append(el('img', { src: `/img/${tile.image}`, alt: '', loading: 'lazy', decoding: 'async', draggable: false }));
  } else {
    thumb.classList.add('letter');
    thumb.style.setProperty('--letter', `var(--a${hash(hostOf(tile.url)) % 7})`);
    thumb.textContent = initial(tile.title);
  }
  const node = el('a', { className: 'tile', href: tile.url, title: tile.url }, thumb, el('span', { className: 'title', textContent: tile.title }));

  if (editing) {
    node.title = 'Click to edit, drag to move';
    node.addEventListener('click', (e) => {
      e.preventDefault();
      editTile(tile);
    });
    dragSource(node, { kind: 'tile', id: tile.id });
    dropTarget(node, 'x', (d) => d.kind === 'tile' && d.id !== tile.id, (d, after) =>
      commit((s) => {
        const target = findTile(s, tile.id);
        if (target) moveTile(s, d.id, target.section.id, target.section.tiles.indexOf(target.tile) + (after ? 1 : 0));
      }),
    );
  }
  return node;
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

/** Stable accent per site, so tiles for the same site share a colour. */
function hash(text: string): number {
  let h = 0;
  for (const ch of text) h = (h * 31 + ch.codePointAt(0)!) >>> 0;
  return h;
}

// ---------------------------------------------------------------------------------------------------------------
// Drag and drop: tiles within and across sections and onto tabs, sections within a tab, tabs in the bar.
// ---------------------------------------------------------------------------------------------------------------

interface Drag {
  kind: 'tile' | 'section' | 'tab';
  id: string;
}

let dragging: Drag | null = null;

function clearDropMarks(): void {
  for (const node of document.querySelectorAll('.drop-before, .drop-after, .drop-into'))
    node.classList.remove('drop-before', 'drop-after', 'drop-into');
}

function dragSource(node: HTMLElement, drag: Drag): void {
  node.addEventListener('dragstart', (e) => {
    e.stopPropagation();
    dragging = drag;
    e.dataTransfer!.effectAllowed = 'move';
    e.dataTransfer!.setData('text/plain', drag.id);
    node.classList.add('dragging');
  });
  node.addEventListener('dragend', () => {
    dragging = null;
    node.classList.remove('dragging');
    clearDropMarks();
  });
}

/**
 * @param axis Which half of the target decides "before" or "after"; `null` for a plain "into" target.
 */
function dropTarget(
  node: HTMLElement,
  axis: 'x' | 'y' | null,
  accepts: (d: Drag) => boolean,
  onDrop: (d: Drag, after: boolean) => void,
): void {
  const after = (e: DragEvent): boolean => {
    const r = node.getBoundingClientRect();
    return axis === 'x' ? e.clientX > r.left + r.width / 2 : axis === 'y' ? e.clientY > r.top + r.height / 2 : true;
  };
  node.addEventListener('dragover', (e) => {
    if (!dragging || !accepts(dragging)) return;
    e.preventDefault();
    e.stopPropagation();
    e.dataTransfer!.dropEffect = 'move';
    clearDropMarks();
    const into = axis === null || (dragging.kind === 'tile' && node.classList.contains('tab'));
    node.classList.add(into ? 'drop-into' : after(e) ? 'drop-after' : 'drop-before');
  });
  node.addEventListener('dragleave', (e) => {
    if (!node.contains(e.relatedTarget as Node | null)) node.classList.remove('drop-before', 'drop-after', 'drop-into');
  });
  node.addEventListener('drop', (e) => {
    if (!dragging || !accepts(dragging)) return;
    e.preventDefault();
    e.stopPropagation();
    const drag = dragging;
    dragging = null;
    clearDropMarks();
    onDrop(drag, after(e));
  });
}

// ---------------------------------------------------------------------------------------------------------------
// Editing actions
// ---------------------------------------------------------------------------------------------------------------

function otherSpaces(): SpaceSummary[] {
  return spaces.filter((s) => s.slug !== space.slug);
}

function addTile(section: Section): void {
  openTileDialog({
    otherSpaces: otherSpaces(),
    onSave: (tile) => commit((s) => findSectionIn(s, section.id)?.tiles.push(tile)),
  });
}

function editTile(tile: Tile): void {
  openTileDialog({
    tile,
    otherSpaces: otherSpaces(),
    onSave: (edited) =>
      commit((s) => {
        const found = findTile(s, tile.id);
        if (found) found.section.tiles[found.section.tiles.indexOf(found.tile)] = edited;
      }),
    onDelete: () => commit((s) => removeTile(s, tile.id)),
    onTransfer: async (edited, slug, mode) => {
      await addToOtherSpace(slug, { ...edited, id: newId('t') });
      if (mode === 'move') commit((s) => removeTile(s, tile.id));
      toast(`${mode === 'move' ? 'Moved' : 'Copied'} to ${spaces.find((x) => x.slug === slug)?.name ?? slug}.`);
    },
  });
}

/** Append to the first section of the other space's first tab. Retries once if that space just changed. */
async function addToOtherSpace(slug: string, tile: Tile): Promise<void> {
  for (let attempt = 0; attempt < 2; attempt++) {
    const target = await api.getSpace(slug);
    if (target.tabs.length === 0) target.tabs.push(newTab('Home'));
    target.tabs[0]!.sections[0]!.tiles.push(tile);
    const outcome = await api.saveSpace(target);
    if (outcome.kind === 'saved') return;
    if (outcome.kind === 'error') throw new Error(outcome.message);
  }
  throw new Error('The other space keeps changing; try again.');
}

function findSectionIn(s: Space, id: string): Section | undefined {
  for (const tab of s.tabs) for (const section of tab.sections) if (section.id === id) return section;
  return undefined;
}

function addTab(): void {
  const title = prompt('Name of the new tab')?.trim();
  if (!title) return;
  const tab = newTab(title);
  commit((s) => s.tabs.push(tab));
  selectTab(tab);
}

function renameTab(tab: Tab): void {
  const title = prompt('Rename tab', tab.title)?.trim();
  if (title && title !== tab.title) commit((s) => (s.tabs.find((t) => t.id === tab.id)!.title = title));
}

function deleteTab(tab: Tab): void {
  const count = tileCount(tab.sections);
  if (count > 0 && !confirm(`Delete tab "${tab.title}" and its ${count} bookmark(s)?`)) return;
  commit((s) => s.tabs.splice(s.tabs.findIndex((t) => t.id === tab.id), 1));
}

function addSection(tab: Tab): void {
  const title = prompt('Name of the new section (leave empty for none)');
  if (title === null) return;
  commit((s) => s.tabs.find((t) => t.id === tab.id)!.sections.push(newSection(title.trim() || null)));
}

function renameSection(section: Section): void {
  const title = prompt('Rename section (leave empty for none)', section.title ?? '');
  if (title === null) return;
  commit((s) => (findSectionIn(s, section.id)!.title = title.trim() || null));
}

function deleteSection(tab: Tab, section: Section): void {
  if (tab.sections.length === 1) {
    toast('A tab needs at least one section. Delete the tab instead.');
    return;
  }
  if (section.tiles.length > 0 && !confirm(`Delete section "${section.title ?? 'Untitled'}" and its ${section.tiles.length} bookmark(s)?`)) return;
  commit((s) => {
    const t = s.tabs.find((x) => x.id === tab.id)!;
    t.sections.splice(t.sections.findIndex((x) => x.id === section.id), 1);
  });
}

function renameSpace(): void {
  const name = prompt('Rename space', space.name)?.trim();
  if (!name || name === space.name) return;
  spaces = spaces.map((s) => (s.slug === space.slug ? { ...s, name } : s));
  commit((s) => (s.name = name));
}

async function createSpace(): Promise<void> {
  const name = prompt('Name of the new space')?.trim();
  if (!name) return;
  const slug = prompt('Address of the new space: /s/…', slugify(name))?.trim();
  if (!slug) return;
  try {
    const created = await api.createSpace(slug, name);
    location.href = `/s/${created.slug}?edit`;
  } catch (e) {
    toast(e instanceof Error ? e.message : String(e));
  }
}

async function deleteSpace(): Promise<void> {
  const count = space.tabs.reduce((n, t) => n + tileCount(t.sections), 0);
  if (!confirm(`Delete space "${space.name}" with ${space.tabs.length} tab(s) and ${count} bookmark(s)?`)) return;
  try {
    await api.deleteSpace(space.slug);
    location.href = '/';
  } catch (e) {
    toast(e instanceof Error ? e.message : String(e));
  }
}

async function fillMissingIcons(): Promise<void> {
  const before = countImages(space);
  toast('Fetching missing icons…');
  try {
    const updated = await api.fillMissingIcons(space.slug);
    if (dirty || saving) {
      toast('Icons fetched, but you edited meanwhile. Reload the page to see them.');
      return;
    }
    space = updated;
    render();
    const missing = updated.tabs.reduce((n, t) => n + tileCount(t.sections), 0) - countImages(updated);
    toast(`Found ${countImages(updated) - before} icon(s).${missing ? ` ${missing} tile(s) still have none.` : ''}`);
  } catch (e) {
    toast(e instanceof Error ? e.message : String(e));
  }
}

function countImages(s: Space): number {
  return s.tabs.flatMap((t) => t.sections).flatMap((x) => x.tiles).filter((t) => t.image).length;
}

function toggleEditing(): void {
  editing = !editing;
  render();
}

// ---------------------------------------------------------------------------------------------------------------
// Startup
// ---------------------------------------------------------------------------------------------------------------

/**
 * In production the API embeds the space in the page. Under the Vite dev server there is no embedded data,
 * so fetch it instead.
 */
async function loadData(): Promise<StartPageData | null> {
  const embedded = document.getElementById('space-data');
  if (embedded?.textContent) return JSON.parse(embedded.textContent) as StartPageData;

  const list = await api.listSpaces();
  const slug = /^\/s\/([^/]+)/.exec(location.pathname)?.[1];
  if (!slug) {
    if (list[0]) location.replace(`/s/${list[0].slug}`);
    return null;
  }
  return { space: await api.getSpace(decodeURIComponent(slug)), spaces: list };
}

function wireSwitcher(): void {
  const switcher = byId<HTMLDetailsElement>('switcher');
  document.addEventListener('click', (e) => {
    if (switcher.open && !switcher.contains(e.target as Node)) switcher.open = false;
  });
  byId('spaces').addEventListener('click', (e) => {
    if ((e.target as HTMLElement).closest('button')) switcher.open = false;
  });
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') switcher.open = false;
  });
}

const data = await loadData();
if (data) {
  space = data.space;
  spaces = data.spaces;
  const params = new URLSearchParams(location.search);
  if (params.has('edit')) {
    editing = true;
    history.replaceState(null, '', location.pathname + location.hash);
  }
  byId('edit-toggle').addEventListener('click', toggleEditing);
  addEventListener('hashchange', render);
  wireSwitcher();
  render();
}
