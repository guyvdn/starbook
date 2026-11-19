import { api } from './api';
import { el, toast } from './dom';
import { newId, normalizeUrl } from './model';
import type { SpaceSummary, Tile } from './types';

export interface TileDialogOptions {
  tile?: Tile;
  /** Spaces a tile can be moved or copied to (not the current one). */
  otherSpaces: SpaceSummary[];
  /** For a new tile in a tab with several sections: which one to add it to. The last entry is the default. */
  sections?: { id: string; title: string }[];
  onSave(tile: Tile, sectionId?: string): void;
  onDelete?(): void;
  onTransfer?(tile: Tile, slug: string, mode: 'move' | 'copy'): Promise<void>;
}

/**
 * Add or edit a tile. Entering a URL looks up the site's icon and title; an image can be uploaded or pasted
 * instead, which is how several tiles for the same site are told apart.
 */
export function openTileDialog(options: TileDialogOptions): void {
  const { tile } = options;
  let image = tile?.image ?? null;
  let imageChosen = !!tile; // don't replace an existing tile's image behind the user's back
  let titleTouched = !!tile;
  let lookup: Promise<void> | null = null;

  const url = el('input', { type: 'text', required: true, value: tile?.url ?? '', placeholder: 'https://…', autocomplete: 'off' });
  const title = el('input', { type: 'text', placeholder: 'From the page if left empty', value: tile?.title ?? '', autocomplete: 'off' });
  const preview = el('span', { className: 'thumb preview' });
  const file = el('input', { type: 'file', accept: 'image/*', hidden: true });
  const status = el('span', { className: 'muted' });
  const logoQuery = el('input', { type: 'text', placeholder: 'Search logos…', autocomplete: 'off' });
  const logoGrid = el('div', { className: 'logo-grid' });
  const logoPanel = el(
    'div',
    { className: 'logo-search', hidden: true },
    el(
      'div',
      { className: 'logo-query' },
      logoQuery,
      el('button', { type: 'button', textContent: 'Search', onclick: () => void searchLogos() }),
    ),
    logoGrid,
  );

  function showImage(): void {
    preview.replaceChildren(image ? el('img', { src: `/img/${image}`, alt: '' }) : el('span', { className: 'muted', textContent: 'No image' }));
  }

  function fetchIcon(force: boolean): void {
    const value = url.value.trim();
    if (!value || (imageChosen && !force)) return;
    status.textContent = 'Looking up…';
    lookup = api
      .lookupIcon(normalizeUrl(value))
      .then((result) => {
        if (result.image) {
          image = result.image;
          imageChosen = true;
          showImage();
        }
        if (result.title && !titleTouched && !title.value.trim()) title.value = result.title;
        status.textContent = result.image ? '' : 'No icon found. Upload or paste one.';
      })
      .catch((e: unknown) => {
        status.textContent = e instanceof Error ? e.message : String(e);
      })
      .finally(() => (lookup = null));
  }

  function openLogoSearch(): void {
    logoPanel.hidden = false;
    if (!logoQuery.value.trim()) {
      let host = '';
      try {
        host = new URL(normalizeUrl(url.value)).hostname.replace(/^www\./, '');
      } catch {
        /* the URL is not typed yet */
      }
      logoQuery.value = title.value.trim() || host;
    }
    logoQuery.focus();
    if (logoQuery.value.trim()) void searchLogos();
  }

  async function searchLogos(): Promise<void> {
    const query = logoQuery.value.trim();
    if (!query) return;
    logoGrid.replaceChildren(el('span', { className: 'muted', textContent: 'Searching…' }));
    try {
      const ids = await api.searchLogos(query);
      logoGrid.replaceChildren(
        ...(ids.length
          ? ids.map((id) =>
              el(
                'button',
                { type: 'button', className: 'logo-choice', title: id.replace(':', ' · '), onclick: () => void pickLogo(id) },
                el('img', { src: `/api/logos/svg?id=${encodeURIComponent(id)}`, alt: id, loading: 'lazy' }),
              ),
            )
          : [el('span', { className: 'muted', textContent: 'No logos found. Try another name.' })]),
      );
    } catch (e) {
      logoGrid.replaceChildren(el('span', { className: 'muted', textContent: e instanceof Error ? e.message : String(e) }));
    }
  }

  async function pickLogo(id: string): Promise<void> {
    try {
      const response = await fetch(`/api/logos/svg?id=${encodeURIComponent(id)}`);
      if (!response.ok) throw new Error('Could not download that logo.');
      await useImage(await response.blob());
      logoPanel.hidden = true;
    } catch (e) {
      status.textContent = e instanceof Error ? e.message : String(e);
    }
  }

  async function useImage(blob: Blob): Promise<void> {
    status.textContent = 'Uploading…';
    try {
      image = await api.uploadImage(blob);
      imageChosen = true;
      status.textContent = '';
      showImage();
    } catch (e) {
      status.textContent = e instanceof Error ? e.message : String(e);
    }
  }

  url.addEventListener('change', () => fetchIcon(false));
  title.addEventListener('input', () => (titleTouched = true));
  logoQuery.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') {
      e.preventDefault(); // would submit the tile form
      void searchLogos();
    }
  });
  file.addEventListener('change', () => file.files?.[0] && void useImage(file.files[0]));

  const sectionSelect = el(
    'select',
    {},
    ...(options.sections ?? []).map((x, i, all) => el('option', { value: x.id, textContent: x.title, selected: i === all.length - 1 })),
  );

  const transferSelect = el(
    'select',
    {},
    el('option', { value: '', textContent: 'Move or copy to…' }),
    ...options.otherSpaces.map((s) => el('option', { value: s.slug, textContent: s.name })),
  );

  const form = el(
    'form',
    { method: 'dialog' },
    el('h2', { textContent: tile ? 'Edit bookmark' : 'Add bookmark' }),
    el('label', {}, 'URL', url),
    el('label', {}, 'Title', title),
    !tile && (options.sections?.length ?? 0) > 1 ? el('label', {}, 'Section', sectionSelect) : null,
    el(
      'div',
      { className: 'image-row' },
      preview,
      el(
        'div',
        { className: 'image-actions' },
        el('button', { type: 'button', textContent: 'Fetch icon', onclick: () => fetchIcon(true) }),
        el('button', { type: 'button', textContent: 'Search logos…', onclick: openLogoSearch }),
        el('button', { type: 'button', textContent: 'Upload…', onclick: () => file.click() }),
        el('button', {
          type: 'button',
          textContent: 'Remove',
          onclick: () => {
            image = null;
            imageChosen = true;
            showImage();
          },
        }),
        el('span', { className: 'muted', textContent: 'or paste an image' }),
      ),
      file,
    ),
    logoPanel,
    status,
    tile && options.onTransfer && options.otherSpaces.length > 0
      ? el(
          'div',
          { className: 'transfer' },
          transferSelect,
          el('button', { type: 'button', textContent: 'Move', onclick: () => void transfer('move') }),
          el('button', { type: 'button', textContent: 'Copy', onclick: () => void transfer('copy') }),
        )
      : null,
    el(
      'div',
      { className: 'actions' },
      tile && options.onDelete
        ? el('button', {
            type: 'button',
            className: 'danger',
            textContent: 'Delete',
            onclick: () => {
              options.onDelete!();
              dialog.close();
            },
          })
        : null,
      el('span', { className: 'spacer' }),
      el('button', { type: 'button', textContent: 'Cancel', onclick: () => dialog.close() }),
      el('button', { type: 'submit', className: 'primary', textContent: 'Save' }),
    ),
  );

  function current(): Tile {
    return { id: tile?.id ?? newId('t'), title: title.value.trim(), url: normalizeUrl(url.value), image };
  }

  async function transfer(mode: 'move' | 'copy'): Promise<void> {
    if (!transferSelect.value) {
      transferSelect.focus();
      return;
    }
    try {
      await options.onTransfer!(current(), transferSelect.value, mode);
      dialog.close();
    } catch (e) {
      status.textContent = e instanceof Error ? e.message : String(e);
    }
  }

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    let parsed: URL;
    try {
      parsed = new URL(normalizeUrl(url.value));
    } catch {
      toast('That is not a valid URL.');
      return;
    }
    if (lookup) await lookup;
    if (!title.value.trim()) title.value = parsed.hostname.replace(/^www\./, '');
    options.onSave(current(), sectionSelect.value || undefined);
    dialog.close();
  });

  const dialog: HTMLDialogElement = el('dialog', { className: 'tile-dialog' }, form);
  dialog.addEventListener('paste', (e) => {
    const item = [...(e.clipboardData?.items ?? [])].find((i) => i.type.startsWith('image/'));
    const blob = item?.getAsFile();
    if (blob) {
      e.preventDefault();
      void useImage(blob);
    }
  });
  dialog.addEventListener('close', () => dialog.remove());

  showImage();
  document.body.append(dialog);
  dialog.showModal();
  (tile ? title : url).focus();
}
