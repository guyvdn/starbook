/** The accent colours a space can have. Keep in step with `SpaceValidator.Colors` in the API. */
export const PALETTE = [
  '#ff0000',
  '#fc4444',
  '#fc6404',
  '#fcd444',
  '#8cc43c',
  '#029658',
  '#1abc9c',
  '#5bc0de',
  '#2f64c1',
  '#6454ac',
  '#b178aa',
  '#f978aa',
] as const;

/** What a space without a colour looks like: the accent of the base theme in style.css. */
export const DEFAULT_ACCENT = '#20d9f5';

/**
 * Re-colour the whole app around one accent. Only `--accent` is set: style.css derives the backgrounds, cards,
 * borders and waves from it (see `:root[data-themed]`). `null` goes back to the default look. The API's start page
 * does the same before first paint, so this only has to keep up with later changes.
 */
export function applyTheme(color: string | null | undefined): void {
  const root = document.documentElement;
  if (color) {
    root.dataset.themed = '';
    root.style.setProperty('--accent', color);
  } else {
    delete root.dataset.themed;
    root.style.removeProperty('--accent');
  }
}
