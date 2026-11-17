// CakeMods public pages. Shared shell and account services are composed by the Worker.
import { NINE_LOGO_URL } from './brand-assets.mjs';

export const navigation = [
  { label: "Home", href: "/" },
  { label: "Communities", href: "/communities/" },
  { label: "Projects", href: "/products/" },
  { label: "Roadmap", href: "https://trello.com/b/GB0oqo1e/cake-roadmap" },
];
export const cakeNavigation = navigation;
const roadmap = "https://trello.com/b/GB0oqo1e/cake-roadmap";
const arrow = '<span aria-hidden="true">↗</span>';
const go = '<span aria-hidden="true">→</span>';
const escapeAttribute = value => value.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');
const link = (href, label, className = 'cake-text-link') => '<a class="' + className + '" href="' + href + '">' + label + (/^https?:/.test(href) ? arrow : go) + '</a>';
const eyebrow = label => '<p class="cake-eyebrow">' + label + '</p>';
const communityData = [
  { slug: "cakemods", name: "r/CakeMods", platform: "Reddit", category: "CakeMods", description: "The main CakeMods community. Follow the conversation around CakeMods and the things we’re building.", href: "https://www.reddit.com/r/CakeMods/" },
  { slug: "9to1", name: "r/9to1", platform: "Reddit", category: "9to1", description: "A focused community for 9to1, its products and the ideas behind them.", href: "https://www.reddit.com/r/9to1/" },
  { slug: "all-fortnite", name: "r/AllFortnite", platform: "Reddit", category: "Gaming", description: "Every corner of Fortnite. Share discoveries, talk about the game and find your people.", href: "https://www.reddit.com/r/AllFortnite/" },
  { slug: "fortnite-memes", name: "r/FortniteMemes", platform: "Reddit", category: "Gaming", description: "The funny side of Fortnite, shared by the people who play it.", href: "https://www.reddit.com/r/FortniteMemes/" },
  { slug: "dyspraxia", name: "r/Dyspraxia", platform: "Reddit", category: "Life & support", description: "A welcoming space for the dyspraxia community to share experiences and connect.", href: "https://www.reddit.com/r/Dyspraxia/" },
  { slug: "casualty", name: "r/Casualty", platform: "Reddit", category: "Television", description: "Episode discussions and conversation for viewers of BBC Casualty.", href: "https://www.reddit.com/r/Casualty/" },
  { slug: "dyspraxia-hub", name: "Dyspraxia Hub", platform: "Discord", category: "Life & support", description: "Keep the conversation going with the dyspraxia community on Discord.", href: "https://discord.gg/7Jkb9xaPyE" },
  { slug: "all-fortnite-discord", name: "All Fortnite", platform: "Discord", category: "Gaming", description: "A place to chat about Fortnite with other players on Discord.", href: "https://discord.gg/sZYer5Ffd7" },
];
function pageIntro(kicker, title, description, home = false) {
  return '<section class="cake-intro cake-container' + (home ? ' cake-intro-home' : '') + '">' + eyebrow(kicker) + '<div class="cake-intro-columns"><h1>' + title + '</h1><p class="cake-lead">' + description + '</p></div></section>';
}

function communityCard(item, index) {
  const search = escapeAttribute((item.name + ' ' + item.platform + ' ' + item.category + ' ' + item.description).toLowerCase());
  return '<article class="cake-community-card" data-community-card data-community-search="' + search + '" data-community-platform="' + item.platform + '" data-community-category="' + escapeAttribute(item.category) + '">' +
    '<div class="cake-card-top"><p class="cake-card-meta"><span class="cake-platform">' + item.platform + '</span><span>' + item.category + '</span></p><span class="cake-index" aria-hidden="true">' + String(index + 1).padStart(2, '0') + '</span></div>' +
    '<h3><a href="' + item.href + '">' + item.name + arrow + '</a></h3><p class="cake-card-description">' + item.description + '</p>' +
    link('/communities/' + item.slug + '/', 'Community details<span class="cake-sr-only"> for ' + item.name + '</span>', 'cake-details-link') + '</article>';
}

function directory() {
  const categories = [...new Set(communityData.map(item => item.category))];
  return '<section class="cake-container cake-directory" id="directory" aria-labelledby="cake-directory-title">' +
    '<div class="cake-section-heading"><h2 id="cake-directory-title">The community directory</h2><p id="cake-community-count" role="status" aria-live="polite" aria-atomic="true">8 communities</p></div>' +
    '<div class="cake-directory-controls" id="cake-directory-controls" hidden><div class="cake-search"><label for="cake-community-search">Find a community</label><input id="cake-community-search" type="search" autocomplete="off" placeholder="Name, topic or keyword"></div>' +
    '<div class="cake-filter"><label for="cake-community-platform">Platform</label><select id="cake-community-platform"><option value="">All platforms</option><option>Reddit</option><option>Discord</option></select></div>' +
    '<div class="cake-filter"><label for="cake-community-category">Topic</label><select id="cake-community-category"><option value="">All topics</option>' + categories.map(category => '<option>' + category.replace(/&/g, '&amp;') + '</option>').join('') + '</select></div>' +
    '<button class="cake-reset" id="cake-community-reset" type="button" disabled>Clear filters</button></div>' +
    '<div class="cake-community-grid">' + communityData.map(communityCard).join('') + '</div>' +
    '<p class="cake-empty" id="cake-community-empty" hidden>No communities found. Try another search or clear the filters.</p>' +
    '<p class="cake-directory-note">These communities live on Reddit and Discord. Read each community’s rules before joining in.</p></section>';
}

function projectList(heading = 'h3') {
  return '<div class="cake-project-grid"><article class="cake-project"><div class="cake-project-top"><img src="' + NINE_LOGO_URL + '" alt="" width="460" height="435" loading="lazy"><span class="cake-status">In development</span></div>' +
    '<p class="cake-project-type">Software</p><' + heading + '>9to1</' + heading + '><p>A family of apps for documents, design, planning and development. Explore the app family and its current development status.</p>' + link('https://9to1.uk/', 'Explore 9to1') + '</article>' +
    '<article class="cake-project"><div class="cake-project-top"><span class="cake-project-label">A CakeMods project</span><span class="cake-status">In development</span></div>' +
    '<p class="cake-project-type">Gaming</p><' + heading + '>Rocket Racing Refuelled</' + heading + '><p>A racing project taking shape. Follow the ideas and development on the public CakeMods roadmap.</p>' + link(roadmap, 'Follow the project') + '</article></div>';
}

function roadmapStrip() {
  return '<section class="cake-container cake-roadmap-strip" aria-labelledby="cake-roadmap-title"><div><h2 id="cake-roadmap-title">What’s being worked on?</h2><p>Follow project plans and progress on the public roadmap.</p></div>' + link(roadmap, 'Open the roadmap') + '</section>';
}

function home() {
  return {
    title: "CakeMods — Communities & projects",
    description: "Eight communities on Reddit and Discord. Find conversations about Fortnite, dyspraxia, television and the projects being built at CakeMods.",
    body: pageIntro('CakeMods / Communities &amp; projects', 'Eight communities.<br>Plenty to talk about.', 'Fortnite, dyspraxia, television and the things we’re building. Find a conversation that interests you.', true) +
      directory() +
      '<section class="cake-container cake-projects-section" aria-labelledby="cake-projects-title"><div class="cake-section-heading"><div>' + eyebrow('Also in the works') + '<h2 id="cake-projects-title">Projects from CakeMods</h2></div>' + link('/products/', 'About the projects') + '</div>' + projectList() + '</section>' + roadmapStrip(),
  };
}

function communities() {
  return {
    title: "Communities — CakeMods",
    description: "Find CakeMods communities on Reddit and Discord, from Fortnite to dyspraxia and BBC Casualty.",
    body: pageIntro('Reddit &amp; Discord', 'Find your next<br>conversation.', 'Browse all eight communities. Search by name or interest, or narrow the list by platform and topic.') + directory() +
      '<section class="cake-container cake-guidance"><h2>Every community has its own rules.</h2><p>Each space has its own moderators, posting rules and ways to get in touch. Open its community details for guidance before you join.</p>' + link('https://www.reddit.com/r/CakeMods/', 'Visit r/CakeMods') + '</section>',
  };
}

function community(item) {
  const isReddit = item.platform === 'Reddit';
  return {
    title: item.name + " — CakeMods Communities",
    description: item.description,
    body: '<nav class="cake-container cake-breadcrumb" aria-label="Breadcrumb"><a href="/communities/">Communities</a><span aria-hidden="true">/</span><span aria-current="page">' + item.name + '</span></nav>' +
      '<section class="cake-container cake-community-detail"><div>' + eyebrow('Part of the CakeMods directory') + '<h1>' + item.name + '</h1><p class="cake-lead">' + item.description + '</p><div class="cake-actions">' + link(item.href, isReddit ? 'Visit the subreddit' : 'Join on Discord', 'cake-button') + '</div><p class="cake-small">You’ll continue to ' + item.platform + ', where this community lives.</p></div>' +
      '<aside class="cake-community-facts" aria-label="Community information"><dl><div><dt>Platform</dt><dd>' + item.platform + '</dd></div><div><dt>Topic</dt><dd>' + item.category + '</dd></div></dl>' + link('/communities/', 'All communities') + '</aside></section>' +
      '<section class="cake-container cake-community-info" aria-labelledby="cake-joining-title"><div>' + eyebrow('Before you join') + '<h2 id="cake-joining-title">A little consideration<br>goes a long way.</h2></div><div class="cake-prose"><h3>Community rules</h3><p>' + (isReddit ? 'Read the rules in the subreddit’s sidebar or About section before posting. Its moderators manage posts, comments and reports.' : 'Read the server’s rules and welcome channels before joining a conversation. Its moderation team manages the server.') + '</p><h3>Contacting the moderators</h3><p>' + (isReddit ? 'Use “Message Mods” on Reddit for questions, reports or appeals. Your 9to1 Account does not replace a Reddit account.' : 'Follow the contact or reporting instructions inside the Discord server. Your 9to1 Account does not replace a Discord account.') + '</p></div></section>',
  };
}

function products() {
  return {
    title: "Projects — CakeMods",
    description: "Explore 9to1 and Rocket Racing Refuelled, two projects in development at CakeMods.",
    body: pageIntro('Made by CakeMods', 'A couple of things<br>we’re working on.', 'Alongside the communities, we’re developing apps and games. Both projects are in development; their websites and roadmap share the current direction.') +
      '<section class="cake-container cake-projects-page" aria-label="CakeMods projects">' + projectList('h2') + '</section>' + roadmapStrip() +
      '<section class="cake-container cake-guidance"><h2>Looking for the communities?</h2><p>Our eight community spaces are open on Reddit and Discord, covering gaming, life and support, television, and CakeMods projects.</p>' + link('/communities/', 'Browse communities') + '</section>',
  };
}

export function renderCake(pathname) {
  if (typeof pathname !== 'string') return null;
  const path = pathname === '/' ? '/' : pathname.replace(/\/+$/, '') + '/';
  if (path === '/') return home();
  if (path === '/communities/') return communities();
  if (path === '/products/') return products();
  const item = communityData.find(item => path === '/communities/' + item.slug + '/');
  return item ? community(item) : null;
}

// Local, progressively enhanced directory controls; no requests or persistence.
export const cakeClientScript = `
(() => {
  const input = document.getElementById('cake-community-search');
  if (!input) return;
  const platform = document.getElementById('cake-community-platform');
  const category = document.getElementById('cake-community-category');
  const reset = document.getElementById('cake-community-reset');
  const cards = [...document.querySelectorAll('[data-community-card]')];
  const count = document.getElementById('cake-community-count');
  const empty = document.getElementById('cake-community-empty');
  const update = () => {
    const terms = input.value.trim().toLocaleLowerCase().split(/\\s+/).filter(Boolean);
    let visible = 0;
    for (const card of cards) {
      const matches = terms.every(term => card.dataset.communitySearch.includes(term)) &&
        (!platform.value || card.dataset.communityPlatform === platform.value) &&
        (!category.value || card.dataset.communityCategory === category.value);
      card.hidden = !matches;
      if (matches) visible++;
    }
    count.textContent = visible + (visible === 1 ? ' community' : ' communities');
    empty.hidden = visible !== 0;
    reset.disabled = !input.value && !platform.value && !category.value;
  };
  input.addEventListener('input', update);
  platform.addEventListener('change', update);
  category.addEventListener('change', update);
  reset.addEventListener('click', () => {
    input.value = '';
    platform.value = '';
    category.value = '';
    update();
    input.focus();
  });
  document.getElementById('cake-directory-controls').hidden = false;
  update();
})();
`;
