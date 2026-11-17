import {assetVersion} from './site-assets.mjs';
import {escapeHtml} from './security.mjs';
import {CAKEMODS_LOGO_URL, NINE_LOGO_URL} from './brand-assets.mjs';
import {previewNotice} from './preview.mjs';

export function shell(page, brand, nav = [], path = '/', origin = '') {
  const cake = brand === 'cake';
  const name = cake ? 'CakeMods' : '9to1';
  const logoUrl = cake ? CAKEMODS_LOGO_URL : NINE_LOGO_URL;
  const logoSize = cake ? 'width="2626" height="2626"' : 'width="460" height="435"';
  const logoMark = (lazy = false) => `<img class="brand-logo" src="${logoUrl}" alt="" ${logoSize}${lazy ? ' loading="lazy"' : ''}>${name}`;
  const title = page.title.includes(name) ? page.title : `${page.title} · ${name}`;
  const navLink = (item) => {
    const current = path === item.href || (item.href !== '/' && item.href.startsWith('/') && path.startsWith(item.href));
    return `<a href="${escapeHtml(item.href)}"${current ? ' aria-current="page"' : ''}>${escapeHtml(item.label)}</a>`;
  };
  return `<!doctype html>
<html lang="en-GB">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <title>${escapeHtml(title)}</title>
  <meta name="description" content="${escapeHtml(page.description)}">
  <meta name="theme-color" content="#ffffff">
  <link rel="canonical" href="${escapeHtml(origin + path)}">
  <meta property="og:title" content="${escapeHtml(title)}">
  <meta property="og:description" content="${escapeHtml(page.description)}">
  <meta property="og:type" content="website">
  <link rel="icon" href="${logoUrl}" type="image/png">
  <link rel="stylesheet" href="/assets/site.css?v=${assetVersion}">
  <script defer src="/assets/site.js?v=${assetVersion}"></script>
</head>
<body class="brand-${brand}">
  <a class="skip-link" href="#main">Skip to content</a>
  <header class="site-header">
    <a href="/" class="wordmark" aria-label="${name} home">${logoMark()}</a>
    <nav aria-label="Main navigation">${nav.map(navLink).join('')}</nav>
    <div class="header-utilities">
      <button class="preview-badge" type="button" data-preview-open aria-haspopup="dialog" aria-controls="site-preview-dialog" hidden><span class="preview-dot" aria-hidden="true"></span>Preview</button>
      <noscript><a class="preview-badge" href="#preview-information">Preview</a></noscript>
      <a class="header-account" href="https://9to1.uk/account" aria-label="9to1 Account, private testing">Account${cake ? ' <span aria-hidden="true">↗</span>' : ''}</a>
    </div>
  </header>
  ${previewNotice(brand)}
  <main id="main" tabindex="-1">${page.body}</main>
  <footer class="site-footer">
    <div class="footer-main">
      <a class="wordmark" href="/" aria-label="${name} home">${logoMark(true)}</a>
      <p>${cake ? 'Communities on Reddit and Discord. Projects in the making.' : 'Creative and productivity apps, in development.'}</p>
      <a href="${cake ? 'https://9to1.uk' : 'https://cakemods.com'}">${cake ? '9to1 apps' : 'Made by CakeMods'} <span aria-hidden="true">↗</span></a>
    </div>
    <div class="footer-bottom">
      <span>© ${new Date().getFullYear()} CakeMods</span>
      <div><a href="/privacy/">Privacy</a><a href="/terms/">Terms</a><a href="/cookies/">Cookies</a><a href="https://9to1.uk/account">Account</a><a href="mailto:support@9to1.uk">Support</a><a href="https://trello.com/b/GB0oqo1e/cake-roadmap">Roadmap <span aria-hidden="true">↗</span></a></div>
      <span>Website preview</span>
    </div>
  </footer>
</body>
</html>`;
}
