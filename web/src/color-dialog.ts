import { el } from './dom';
import { applyTheme, DEFAULT_ACCENT, PALETTE } from './theme';

/**
 * Pick a space's colour. Choosing a swatch re-colours the app straight away; Save keeps it, Cancel (or Escape)
 * puts the current colour back.
 */
export function openColorDialog(current: string | null | undefined, onSave: (color: string | null) => void): void {
  let chosen = current ?? null;

  const swatches = [null, ...PALETTE].map((color) =>
    el('button', {
      type: 'button',
      className: 'swatch',
      title: color ?? 'Default',
      onclick: () => {
        chosen = color;
        applyTheme(chosen);
        mark();
      },
    }),
  );
  swatches.forEach((node, i) => node.style.setProperty('--swatch', [DEFAULT_ACCENT, ...PALETTE][i]!));

  function mark(): void {
    swatches.forEach((node, i) => {
      const selected = ([null, ...PALETTE][i] ?? null) === chosen;
      node.classList.toggle('selected', selected);
      node.setAttribute('aria-pressed', String(selected));
    });
  }

  let saved = false;
  const dialog: HTMLDialogElement = el(
    'dialog',
    { className: 'tile-dialog color-dialog' },
    el(
      'form',
      { method: 'dialog' },
      el('h2', { textContent: 'Space colour' }),
      el('div', { className: 'swatches' }, ...swatches),
      el(
        'div',
        { className: 'actions' },
        el('span', { className: 'spacer' }),
        el('button', { type: 'button', textContent: 'Cancel', onclick: () => dialog.close() }),
        el('button', {
          type: 'submit',
          className: 'primary',
          textContent: 'Save',
          onclick: () => (saved = true),
        }),
      ),
    ),
  );
  dialog.addEventListener('close', () => {
    dialog.remove();
    if (saved) onSave(chosen);
    else applyTheme(current);
  });

  mark();
  document.body.append(dialog);
  dialog.showModal();
}
