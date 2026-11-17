// A local, tab-session notice preference. No account data or network requests.
export const PREVIEW_STORAGE_KEY = 'website-preview-notice-v1';

export function previewNotice(brand) {
  const availability = brand === 'cake'
    ? 'The community links take you to Reddit or Discord. Features on this website are still being built.'
    : 'The apps are still in development. Public downloads and account registration are not open.';
  const description = 'This website is a very early scaffold. Significant functionality is currently missing, and some pages and features are incomplete.';
  return `
    <details id="preview-information" class="preview-fallback">
      <summary>Preview — this website is an early scaffold</summary>
      <p>${description}</p><p>${availability}</p>
    </details>
    <dialog id="site-preview-dialog" class="preview-dialog" aria-labelledby="preview-title" aria-describedby="preview-description preview-availability">
      <div class="preview-dialog-top"><span class="preview-label">Website preview</span><button class="preview-close" type="button" data-preview-close aria-label="Close preview information"><span aria-hidden="true">×</span></button></div>
      <h2 id="preview-title">You’re viewing an early preview.</h2>
      <p id="preview-description">${description}</p>
      <p id="preview-availability" class="preview-availability">${availability}</p>
      <button class="button primary preview-continue" type="button" data-preview-close autofocus>Continue to preview <span aria-hidden="true">→</span></button>
      <p class="preview-help">You can reopen this notice using <strong>Preview</strong> in the header.</p>
    </dialog>`;
}

export const previewClientScript = `
(() => {
  const dialog = document.getElementById('site-preview-dialog');
  if (!dialog || typeof dialog.showModal !== 'function') return;
  const triggers = [...document.querySelectorAll('[data-preview-open]')];
  const fallback = document.getElementById('preview-information');
  let returnFocus = triggers[0];
  const openNotice = (trigger) => {
    if (dialog.open) return;
    returnFocus = trigger || triggers[0];
    dialog.showModal();
    document.body.classList.add('preview-is-open');
    dialog.querySelector('.preview-continue').focus({preventScroll: true});
  };
  for (const trigger of triggers) {
    trigger.hidden = false;
    trigger.addEventListener('click', () => openNotice(trigger));
  }
  for (const button of dialog.querySelectorAll('[data-preview-close]')) {
    button.addEventListener('click', () => dialog.close());
  }
  dialog.addEventListener('close', () => {
    document.body.classList.remove('preview-is-open');
    try { sessionStorage.setItem('${PREVIEW_STORAGE_KEY}', 'dismissed'); } catch {}
    if (returnFocus && returnFocus.isConnected) returnFocus.focus({preventScroll: true});
  });
  if (fallback) fallback.hidden = true;
  let dismissed = false;
  try { dismissed = sessionStorage.getItem('${PREVIEW_STORAGE_KEY}') === 'dismissed'; } catch {}
  if (!dismissed) openNotice();
})();
`;
