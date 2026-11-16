// Public product information. These pages deliberately do not advertise a download
// or an app launch until a public release has been independently verified.
import {CAKEMODS_LOGO_URL,NINE_LOGO_URL} from './brand-assets.mjs';
export const navigation = [
  { href: "/apps/", label: "Explore apps" },
  { href: "/ai/", label: "Dulche AI" },
  { href: "/about/", label: "About" },
];

export const nineNavigation = navigation;

const arrow = '<span aria-hidden="true">↗</span>';
const next = '<span aria-hidden="true">→</span>';

const appGroups = [
  {
    id: "create", number: "01", name: "Creative apps", label: "Create",
    description: "Writing, design, images, audio and video.",
    apps: [
      ["W", "Write", "A word processor for writing, editing and shaping complete documents.", "paper"],
      ["P", "Present", "Presentation creation and delivery, bringing slides and visual storytelling together.", "blue"],
      ["C", "Canvas", "A freeform space for handwriting, sketches and spatial ideas.", "lime"],
      ["Pi", "Picture", "Image creation and editing across raster and vector workflows.", "pink"],
      ["Wa", "Wave", "Audio recording, multitrack editing, mixing and sound design.", "orange"],
      ["M", "Motion", "Video editing, animation, compositing and motion graphics.", "purple"],
    ],
  },
  {
    id: "organise", number: "02", name: "Workspace apps", label: "Organise",
    description: "Files, notes, data, planning and communication.",
    apps: [
      ["F", "Files", "File management, storage, sharing and revision history.", "blue"],
      ["B", "Boards", "Notes, pages, projects and structured information in a flexible visual workspace.", "orange"],
      ["S", "Spaces", "Persistent contexts for conversations, research, study and agent-assisted work.", "purple"],
      ["D", "Data", "Spreadsheets, structured data, databases and analysis.", "lime"],
      ["Pl", "Planner", "Calendars, schedules, deadlines and assignment planning connected to your work.", "pink"],
      ["Co", "Connect", "A communication hub for messaging, calls and collaboration.", "paper"],
    ],
  },
  {
    id: "build", number: "03", name: "Development apps", label: "Build",
    description: "Software, source control, websites and games.",
    apps: [
      ["Dv", "Dev", "Software development with editing, debugging, builds, tests and project tools.", "purple"],
      ["St", "Stacks", "Source control and software collaboration, with coordination for parallel development.", "blue"],
      ["Si", "Sites", "Visual and source-backed website authoring, previews and publishing.", "lime"],
      ["G", "Games", "Game development and real-time 3D creation with scenes, assets and scripting.", "orange"],
    ],
  },
];

function appCard([initial, name, description, colour]) {
  return `<article class="n9-app-card">
    <span class="n9-app-icon n9-icon-${colour}" aria-hidden="true">${initial}</span>
    <div><h3>${name}</h3><p>${description}</p></div>
  </article>`;
}

function accountCallout() {
  return `<section class="n9-account-band n9-container" aria-labelledby="n9-account-heading">
    <img class="n9-id-mark" src="${NINE_LOGO_URL}" alt="" width="460" height="435" loading="lazy">
    <div><p class="n9-eyebrow">9to1 Account · Private testing</p><h2 id="n9-account-heading">One 9to1 Account.</h2>
      <p>One profile for CakeMods and 9to1. Access is limited to private testing; public registration is closed.</p></div>
    <a class="n9-button n9-button-dark" href="https://9to1.uk/account">Account information ${next}</a>
  </section>`;
}

function appFamilyIndex() {
  return `<section class="n9-family-index" aria-labelledby="n9-family-heading">
    <div class="n9-family-heading"><img src="${NINE_LOGO_URL}" alt="" width="460" height="435"><div><h2 id="n9-family-heading">Planned app family</h2><p>In development · No public downloads</p></div></div>
    <nav aria-label="Explore the planned app family">
      <a class="n9-family-home" href="/apps/#home"><span><strong>Home</strong><span>The shared starting point</span></span>${next}</a>
      ${appGroups.map(group => `<a class="n9-family-group n9-family-${group.id}" href="/apps/#${group.id}"><span class="n9-family-name">${group.label}${next}</span><span class="n9-family-apps">${group.apps.map(app => app[1]).join(" · ")}</span></a>`).join("")}
    </nav>
  </section>`;
}

function home() {
  return {
    title: "9to1 — Creative, productivity and development apps",
    description: "Explore the creative, productivity and development apps CakeMods is building as 9to1. The app family and Dulche AI are in development; public downloads are not available.",
    body: `<section class="n9-hero n9-container" aria-labelledby="n9-hero-title">
      <div class="n9-hero-copy"><p class="n9-eyebrow">9to1 · A CakeMods project</p>
        <h1 id="n9-hero-title">Create, organise<br><em>and build.</em></h1>
        <p class="n9-lede">A family of apps for documents, design, media, planning and software development. We’re building shared foundations so those tools can work together.</p>
        <p class="n9-hero-status">The apps are in development. This site introduces what’s planned.</p>
        <div class="n9-actions"><a class="n9-button n9-button-blue" href="/apps/">Explore the app catalogue ${next}</a><a class="n9-text-link" href="/about/">About the project ${arrow}</a></div>
      </div>${appFamilyIndex()}
    </section>
    <section class="n9-intro-strip n9-container" aria-labelledby="n9-home-intro"><div><p class="n9-eyebrow">THE SHARED FOUNDATION</p><h2 id="n9-home-intro">Start with Home.</h2></div><p>9to1 Home is planned as the place for installed apps, navigation, settings and shared services.</p><a href="/apps/#home" class="n9-text-link">Read about Home ${arrow}</a></section>
    <section class="n9-dulche n9-container" aria-labelledby="n9-dulche-heading"><div><p class="n9-eyebrow">DULCHE · IN DEVELOPMENT</p><h2 id="n9-dulche-heading">Optional AI,<br>across the family.</h2></div><div><p>Dulche is the planned shared AI service for 9to1. Manual tools come first; assistance is intended to be a choice.</p><p class="n9-dulche-status">There is no live AI service on this website.</p><a class="n9-button n9-button-light" href="/ai/">Read the Dulche plans ${arrow}</a></div></section>
    ${accountCallout()}`,
  };
}

function apps() {
  return {
    title: "Explore the apps — 9to1",
    description: "Explore the 9to1 product family: creative tools, workspaces and development apps. All listed apps are currently in development.",
    body: `<section class="n9-page-hero n9-container"><p class="n9-eyebrow">THE 9TO1 FAMILY</p><h1>The planned<br>app catalogue.</h1><p class="n9-lede">Creative tools, workspaces and development apps, with a shared starting point in Home.</p><p class="n9-release-note">All apps are in development. Public downloads are not available.</p></section>
      <nav class="n9-category-nav n9-container" aria-label="App categories"><a href="#home">Start with Home</a>${appGroups.map(group => `<a href="#${group.id}">${group.label} ${next}</a>`).join("")}</nav>
      <section id="home" class="n9-home-feature n9-container" aria-labelledby="n9-home-heading"><span class="n9-home-logo" aria-hidden="true">⌂</span><div><p class="n9-eyebrow">THE STARTING POINT</p><h2 id="n9-home-heading">9to1 Home</h2><p>The planned home for your installed apps, shared navigation, settings and services. It’s the foundation connecting the rest of 9to1.</p><span class="n9-small-status">In development</span></div></section>
      ${appGroups.map(group => `<section id="${group.id}" class="n9-container n9-catalogue-section n9-category-${group.id}" aria-labelledby="n9-${group.id}-heading"><div class="n9-section-heading"><div><p class="n9-eyebrow">${group.number} / ${group.label.toUpperCase()}</p><h2 id="n9-${group.id}-heading">${group.name}</h2><p class="n9-category-status">In development · No public downloads</p></div><p>${group.description}</p></div><div class="n9-app-grid">${group.apps.map(appCard).join("")}</div></section>`).join("")}
      <section class="n9-container n9-faq" aria-labelledby="n9-faq-heading"><p class="n9-eyebrow">A FEW THINGS TO KNOW</p><h2 id="n9-faq-heading">Availability and accounts.</h2><details><summary>Can I download 9to1 now?</summary><p>There is no public download on this site yet. The catalogue describes the product direction, and individual apps remain in development. You can follow the work on the <a href="https://trello.com/b/GB0oqo1e/cake-roadmap">CakeMods roadmap</a>.</p></details><details><summary>Can I create a shared 9to1 Account?</summary><p>9to1 Account is shared by CakeMods and 9to1, but access is limited to private testing. Public registration is closed. The <a href="https://9to1.uk/account">account information page</a> explains current access.</p></details><details><summary>Do these apps require AI?</summary><p>The product direction is built around complete manual tools. Dulche is intended to add optional assistance to those workflows. Neither the apps nor Dulche are publicly available from this website.</p></details></section>
      ${accountCallout()}`,
  };
}

function about() {
  return {
    title: "The idea behind 9to1",
    description: "9to1 is a CakeMods project exploring a more connected family of creative and productivity tools, with shared identity and optional AI assistance.",
    body: `<section class="n9-page-hero n9-container"><p class="n9-eyebrow">ABOUT 9TO1</p><h1>The idea<br>behind 9to1.</h1><p class="n9-lede">9to1 is a family of creative, productivity and development apps being built by CakeMods.</p></section>
      <section class="n9-about-story n9-container"><div class="n9-about-mark" aria-hidden="true"><img src="${NINE_LOGO_URL}" alt="" width="460" height="435" loading="lazy"></div><div><p class="n9-eyebrow">WHY WE’RE BUILDING IT</p><h2>Ideas rarely stay<br>in one place.</h2><p>A note becomes a plan. A plan becomes a document, a design or a piece of software. We’re building 9to1 to connect those different kinds of work.</p><p>The apps are in development, with shared foundations planned for identity, files, navigation and services. Each tool has its own purpose in the wider workspace.</p><a class="n9-text-link" href="/apps/">Discover the product family ${arrow}</a></div></section>
      <section class="n9-container n9-section" aria-labelledby="n9-principles-heading"><div class="n9-section-heading"><div><p class="n9-eyebrow">WHAT GUIDES THE WORK</p><h2 id="n9-principles-heading">Useful by design.</h2></div></div><div class="n9-principle-grid"><article><span>01</span><h3>Tools with a purpose.</h3><p>Good manual workflows come first. You should be able to write, create and organise without relying on AI.</p></article><article><span>02</span><h3>A connected foundation.</h3><p>Shared services are intended to make moving between apps feel consistent and familiar.</p></article><article><span>03</span><h3>Room for your choices.</h3><p>Optional assistance, clear permissions and personal settings are part of the product direction.</p></article></div></section>
      <section class="n9-cake-bridge n9-container"><div><p class="n9-eyebrow">PART OF CakeMods</p><h2>Made with a<br>community mindset.</h2><p>CakeMods connects independent communities and creative projects. 9to1 is part of that wider home.</p><div class="n9-actions"><a class="n9-button n9-button-dark" href="https://cakemods.com">Meet CakeMods ${arrow}</a><a class="n9-text-link" href="https://trello.com/b/GB0oqo1e/cake-roadmap">Follow the roadmap ${arrow}</a></div></div><img class="n9-cake-word" src="${CAKEMODS_LOGO_URL}" alt="" width="2626" height="2626" loading="lazy"></section>
      ${accountCallout()}`,
  };
}

function ai() {
  return {
    title: "Dulche — AI in the 9to1 product family",
    description: "Meet Dulche, the shared AI framework being developed for 9to1. Explore the direction for optional assistance, local models and connected workflows.",
    body: `<section class="n9-page-hero n9-container n9-ai-hero"><p class="n9-eyebrow">DULCHE · IN DEVELOPMENT</p><h1>Optional AI<br>for the app family.</h1><p class="n9-lede">Dulche is the shared AI framework being built for 9to1, with planned support for local and cloud models.</p><p class="n9-release-note">There is no live AI service on this website.</p><a class="n9-button n9-button-blue" href="/apps/">Explore the app family ${next}</a></section>
      <section class="n9-ai-vision n9-container" aria-label="Dulche product direction"><div class="n9-ai-graphic" aria-hidden="true"><div class="n9-ai-core">D</div><span class="n9-ai-node n9-ai-node-one">Create</span><span class="n9-ai-node n9-ai-node-two">Explore</span><span class="n9-ai-node n9-ai-node-three">Organise</span><span class="n9-ai-node n9-ai-node-four">Build</span></div><div><p class="n9-eyebrow">SHARED ACROSS THE WORKSPACE</p><h2>Assistance that<br>has a place.</h2><p>The direction brings model management and agent capabilities into a shared service, rather than a separate AI system for every app.</p><p>Local and cloud model choices, permission-aware tools and persistent work contexts are part of the planned foundation.</p></div></section>
      <section class="n9-container n9-section" aria-labelledby="n9-ai-principles"><div class="n9-section-heading"><div><p class="n9-eyebrow">THE APPROACH</p><h2 id="n9-ai-principles">Your work stays central.</h2></div></div><div class="n9-principle-grid"><article><span>01</span><h3>Optional by intention.</h3><p>AI is intended to support complete manual tools, giving you another way to approach a task.</p></article><article><span>02</span><h3>Connected in context.</h3><p>Shared workspaces and authorised integrations are designed to connect assistance with the task at hand.</p></article><article><span>03</span><h3>Choices made visible.</h3><p>Model selection, permissions and inspectable actions are part of the design direction.</p></article></div><p class="n9-release-note"><span class="n9-status-dot" aria-hidden="true"></span>Dulche is in development. This page introduces the product; it is not a live AI service.</p></section>
      ${accountCallout()}`,
  };
}

const pages = new Map([["/", home], ["/apps/", apps], ["/about/", about], ["/ai/", ai]]);

export function renderNine(pathname) {
  if (typeof pathname !== "string" || !pathname.startsWith("/")) return null;
  const canonical = pathname === "/" ? "/" : `${pathname.replace(/\/+$/, "")}/`;
  return pages.get(canonical)?.() ?? null;
}


