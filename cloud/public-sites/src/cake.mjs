// Public CakeMods content. Shared shell/account services are composed by the Worker.
// Community destinations verified against live CakeMods content and the owner's roadmap.
import { CAKEMODS_LOGO_URL, NINE_LOGO_URL } from './brand-assets.mjs';
export const navigation = [
  { label: "Home", href: "/" },
  { label: "Communities", href: "/communities/" },
  { label: "Products", href: "/products/" },
  { label: "Roadmap", href: "https://trello.com/b/GB0oqo1e/cake-roadmap" },
];
export const cakeNavigation = navigation;
const roadmap = "https://trello.com/b/GB0oqo1e/cake-roadmap";
const arrow = '<span aria-hidden="true">↗</span>';
const go = '<span aria-hidden="true">→</span>';
const button = (href, label, secondary = false) => '<a class="cake-button' + (secondary ? ' cake-button-secondary' : '') + '" href="' + href + '">' + label + go + '</a>';
const eyebrow = (label) => '<p class="cake-eyebrow">' + label + '</p>';
const communityData = [
  { slug: "cakemods", name: "r/CakeMods", platform: "Reddit", category: "CakeMods", description: "The main CakeMods community. Follow the conversation around CakeMods and the things we’re building.", href: "https://www.reddit.com/r/CakeMods/", mark: "c.", tone: "coral" },
  { slug: "9to1", name: "r/9to1", platform: "Reddit", category: "9to1", description: "A focused community for 9to1, its products and the ideas behind them.", href: "https://www.reddit.com/r/9to1/", mark: "9:1", tone: "lime" },
  { slug: "all-fortnite", name: "r/AllFortnite", platform: "Reddit", category: "Gaming", description: "Every corner of Fortnite. Share discoveries, talk about the game and find your people.", href: "https://www.reddit.com/r/AllFortnite/", mark: "AF", tone: "coral" },
  { slug: "fortnite-memes", name: "r/FortniteMemes", platform: "Reddit", category: "Gaming", description: "The funny side of Fortnite, shared by the people who play it.", href: "https://www.reddit.com/r/FortniteMemes/", mark: ":)", tone: "lime" },
  { slug: "dyspraxia", name: "r/Dyspraxia", platform: "Reddit", category: "Life & support", description: "A welcoming space for the dyspraxia community to share experiences and connect.", href: "https://www.reddit.com/r/Dyspraxia/", mark: "dy", tone: "lavender" },
  { slug: "casualty", name: "r/Casualty", platform: "Reddit", category: "Television", description: "Episode discussions and conversation for viewers of BBC Casualty.", href: "https://www.reddit.com/r/Casualty/", mark: "+", tone: "blue" },
  { slug: "dyspraxia-hub", name: "Dyspraxia Hub", platform: "Discord", category: "Life & support", description: "Keep the conversation going with the dyspraxia community on Discord.", href: "https://discord.gg/7Jkb9xaPyE", mark: "dh", tone: "lavender" },
  { slug: "all-fortnite-discord", name: "All Fortnite", platform: "Discord", category: "Gaming", description: "A place to chat about Fortnite with other players on Discord.", href: "https://discord.gg/sZYer5Ffd7", mark: "AF", tone: "coral" },
];
function communityMark(item) {
  if (item.slug === 'cakemods') return '<img src="' + CAKEMODS_LOGO_URL + '" alt="" width="2626" height="2626" loading="lazy">';
  if (item.slug === '9to1') return '<img src="' + NINE_LOGO_URL + '" alt="" width="460" height="435" loading="lazy">';
  return '<span>' + item.mark + '</span>';
}
function communityCard(item, searchable = false) {
  return '<article class="cake-community-card"' + (searchable ? ' data-community-card data-community-search="' + (item.name + ' ' + item.platform + ' ' + item.category + ' ' + item.description).toLowerCase().replace(/"/g, '&quot;') + '"' : '') + '><div class="cake-community-art cake-tone-' + item.tone + '" aria-hidden="true">' + communityMark(item) + '<i></i></div><div class="cake-community-copy"><p class="cake-card-meta">' + item.platform + ' <span> / </span> ' + item.category + '</p><h3><a href="/communities/' + item.slug + '/">' + item.name + arrow + '</a></h3><p>' + item.description + '</p></div></article>';
}
function pageIntro(kicker, title, description) {
  return '<section class="cake-page-intro cake-container">' + eyebrow(kicker) + '<h1>' + title + '</h1><p class="cake-lead">' + description + '</p></section>';
}
function accountStrip() {
  return '<section class="cake-account-strip cake-container" aria-labelledby="cake-account-title"><div>' + eyebrow('9to1 Account') + '<h2 id="cake-account-title">Your profile, across both sites.</h2><p>One 9to1 Account for CakeMods and 9to1. Account access is currently in private testing.</p></div>' + button('https://9to1.uk/account', '9to1 Account') + '</section>';
}
function productsGrid() {
  return '<div class="cake-product-grid"><a class="cake-product cake-product-studio" href="https://9to1.uk/"><div class="cake-product-top"><span class="cake-tag">APPS IN DEVELOPMENT</span>' + arrow + '</div><div class="cake-studio-mark" aria-hidden="true"><img src="' + NINE_LOGO_URL + '" alt="" width="460" height="435" loading="lazy"></div><div><h3>9to1</h3><p>A family of apps for documents, design, planning and development.</p><span class="cake-product-link">Explore the app family ' + go + '</span></div></a>' +
    '<a class="cake-product cake-product-racing" href="' + roadmap + '"><div class="cake-product-top"><span class="cake-tag">IN DEVELOPMENT</span>' + arrow + '</div><div class="cake-racing-mark" aria-hidden="true"><i></i><i></i><i></i><span>RR</span></div><div><h3>Rocket Racing <br>Refuelled.</h3><p>Follow the racing project as it takes shape on the CakeMods roadmap.</p><span class="cake-product-link">Follow its development ' + go + '</span></div></a>' +
    '<a class="cake-product cake-product-community" href="/communities/"><div class="cake-product-top"><span class="cake-tag">REDDIT & DISCORD</span>' + arrow + '</div><div class="cake-community-symbol" aria-hidden="true"><span></span><span></span><span></span></div><div><h3>CakeMods communities</h3><p>Eight spaces for gaming, everyday life and television conversations.</p><span class="cake-product-link">Browse communities ' + go + '</span></div></a></div>';
}
function communityBoard() {
  const featured = [communityData[2], communityData[4], communityData[5]];
  return '<aside class="cake-hero-board" aria-labelledby="cake-board-title"><div class="cake-board-heading">' +
    eyebrow('A FEW PLACES TO START') + '<h2 id="cake-board-title">What are you into?</h2></div><div class="cake-board-list">' +
    featured.map(item => '<a class="cake-board-community cake-tone-' + item.tone + '" href="/communities/' + item.slug + '/"><span class="cake-board-category">' + item.category + '</span><strong>' + item.name + '</strong><span class="cake-board-platform">' + item.platform + ' ' + go + '</span></a>').join('') +
    '</div><a class="cake-text-link cake-board-all" href="/communities/">All ' + communityData.length + ' communities ' + go + '</a></aside>';
}
function home() {
  return {
    title: "CakeMods — People, projects & possibilities",
    description: "Find your community. Discover the products and ideas taking shape at CakeMods and 9to1.",
    body: '<section class="cake-hero cake-container" aria-labelledby="cake-home-title"><div class="cake-hero-copy">' +
      eyebrow('CakeMods · COMMUNITIES & PROJECTS') +
      '<h1 id="cake-home-title">Find your<br><em>community.</em></h1><p class="cake-lead">Fortnite, dyspraxia, television and more. Join a conversation, or follow the apps and games we’re building.</p><div class="cake-actions">' +
      button('/communities/', 'Browse communities') + '<a class="cake-text-link" href="/products/">See our projects ' + go + '</a></div></div>' +
      communityBoard() + '</section>' +
      '<section class="cake-section cake-container" id="products" aria-labelledby="cake-products-title"><div class="cake-section-heading"><div>' + eyebrow('MADE BY CakeMods') + '<h2 id="cake-products-title">Apps, games &amp;<br>shared spaces.</h2></div><p>Explore 9to1, follow Rocket Racing Refuelled, or find a community to join.</p></div>' +
      productsGrid() + '</section>' +
      '<section class="cake-community-section"><div class="cake-container"><div class="cake-section-heading"><div>' + eyebrow('FROM THE DIRECTORY') + '<h2>Find a conversation<br>worth joining.</h2></div><a class="cake-text-link" href="/communities/">Browse all communities ' + go + '</a></div><div class="cake-community-grid">' + [communityData[2], communityData[3], communityData[4], communityData[5]].map(x => communityCard(x)).join('') + '</div></div></section>' +
      '<section class="cake-roadmap-strip cake-container"><div>' + eyebrow('OUT IN THE OPEN') + '<h2>See what comes next.</h2><p>Follow the ideas, projects and progress on our public roadmap.</p></div><a class="cake-text-link" href="' + roadmap + '">Explore the roadmap ' + arrow + '</a></section>' +
      accountStrip(),
  };
}
function communities() {
  return {
    title: "Communities — CakeMods",
    description: "Find CakeMods communities on Reddit and Discord, from Fortnite to dyspraxia and BBC Casualty.",
    body: pageIntro('REDDIT & DISCORD', 'Find your <em>community.</em>', 'Browse eight communities covering gaming, everyday life and television. Search by name, interest or platform.') +
      '<section class="cake-container cake-directory" aria-labelledby="cake-directory-title"><div class="cake-directory-toolbar"><div><h2 id="cake-directory-title">The community directory</h2><p id="cake-community-count" aria-live="polite">8 communities</p></div><div class="cake-search"><label for="cake-community-search">Find a community</label><div><span aria-hidden="true">⌕</span><input id="cake-community-search" type="search" autocomplete="off" placeholder="Search names, interests or platforms" aria-describedby="cake-search-help"></div><p id="cake-search-help">Try “Fortnite”, “Reddit” or “support”.</p></div></div><div class="cake-community-grid">' + communityData.map(x => communityCard(x, true)).join('') + '</div><p class="cake-empty" id="cake-community-empty" hidden>No communities match that search. Try a different name, interest or platform.</p></section>' +
      '<section class="cake-note cake-container"><div><h2>Make yourself at home.</h2><p>Every community has its own moderators and rules. You’ll find the details on each community’s page.</p></div>' + button('https://www.reddit.com/r/CakeMods/', 'Visit r/CakeMods', true) + '</section>',
  };
}
function community(item) {
  const isReddit = item.platform === "Reddit";
  return {
    title: item.name + " — CakeMods Communities",
    description: item.description,
    body: '<div class="cake-container cake-breadcrumb"><a href="/communities/">Communities</a><span aria-hidden="true">/</span><span>' + item.name + '</span></div>' +
      '<section class="cake-container cake-community-detail"><div class="cake-community-detail-art cake-community-art cake-tone-' + item.tone + '" aria-hidden="true">' + communityMark(item) + '<i></i></div><div>' + eyebrow(item.platform.toUpperCase() + ' / ' + item.category.toUpperCase()) + '<h1>' + item.name + '</h1><p class="cake-lead">' + item.description + '</p><div class="cake-actions">' + button(item.href, isReddit ? 'Visit the subreddit' : 'Join on Discord') + '</div><p class="cake-small">You’ll continue to ' + item.platform + ', where this community lives.</p></div></section>' +
      '<section class="cake-container cake-community-info"><div><p class="cake-eyebrow">BEFORE YOU JOIN</p><h2>Good conversation starts <br>with consideration.</h2></div><div class="cake-prose"><h3>Community rules</h3><p>' + (isReddit ? 'Read the rules in the subreddit’s sidebar or About section before posting. Its moderators manage posts, comments and reports.' : 'Read the server’s rules and welcome channels before joining a conversation. Its moderation team manages the server.') + '</p><h3>Contacting the moderators</h3><p>' + (isReddit ? 'Use “Message Mods” on Reddit for questions, reports or appeals. Your 9to1 Account does not replace a Reddit account.' : 'Follow the contact or reporting instructions inside the Discord server. Your 9to1 Account does not replace a Discord account.') + '</p></div></section>' +
      '<section class="cake-note cake-container"><div><h2>There’s more to explore.</h2><p>Find another space that shares your interests.</p></div>' + button('/communities/', 'All communities', true) + '</section>',
  };
}
function products() {
  return {
    title: "Products & projects — CakeMods",
    description: "Explore 9to1, Rocket Racing Refuelled and the communities connected through CakeMods.",
    body: pageIntro('MADE BY CakeMods', 'Our apps, games<br>&amp; <em>communities.</em>', 'Meet the projects we’re developing and the communities you can join today.') +
      '<section class="cake-container cake-products-page" aria-label="CakeMods products and projects">' + productsGrid() + '</section>' +
      '<section class="cake-roadmap-strip cake-container"><div>' + eyebrow('FOLLOW THE PROGRESS') + '<h2>Ideas don’t stand still.</h2><p>See what we’re exploring, building and improving on the public CakeMods roadmap.</p></div>' + button(roadmap, 'See the roadmap', true) + '</section>' + accountStrip(),
  };
}
export function renderCake(pathname) {
  if (typeof pathname !== "string") return null;
  const path = pathname === "/" ? "/" : pathname.replace(/\/+$/, "") + "/";
  if (path === "/") return home();
  if (path === "/communities/") return communities();
  if (path === "/products/") return products();
  const item = communityData.find(x => path === "/communities/" + x.slug + "/");
  return item ? community(item) : null;
}

// Include once in the shared shell's client bundle; it performs no requests or persistence.
export const cakeClientScript = `
(() => {
  const input = document.getElementById('cake-community-search');
  if (!input) return;
  const cards = [...document.querySelectorAll('[data-community-card]')];
  const count = document.getElementById('cake-community-count');
  const empty = document.getElementById('cake-community-empty');
  input.addEventListener('input', () => {
    const terms = input.value.trim().toLocaleLowerCase().split(/\\s+/).filter(Boolean);
    let visible = 0;
    for (const card of cards) {
      const matches = terms.every(term => card.dataset.communitySearch.includes(term));
      card.hidden = !matches;
      if (matches) visible++;
    }
    count.textContent = visible + (visible === 1 ? ' community' : ' communities');
    empty.hidden = visible !== 0;
  });
})();
`;



