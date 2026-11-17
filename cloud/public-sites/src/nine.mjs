// Public project information. A planned app is not a public release.
import {CAKEMODS_LOGO_URL,NINE_LOGO_URL} from './brand-assets.mjs';

export const navigation = [
  { href: "/apps/", label: "Apps" },
  { href: "/ai/", label: "Dulche AI" },
  { href: "/about/", label: "About" },
];
export const nineNavigation = navigation;

const arrow = '<span aria-hidden="true">↗</span>';
const next = '<span aria-hidden="true">→</span>';

const appGroups = [
  {
    id: "create", number: "01", label: "Create",
    description: "Tools for documents, design and media.",
    apps: [
      ["Write", "A word processor for writing, editing and shaping complete documents."],
      ["Present", "Presentation creation and delivery, bringing slides and visual storytelling together."],
      ["Canvas", "A freeform space for handwriting, sketches and spatial ideas."],
      ["Picture", "Image creation and editing across raster and vector workflows."],
      ["Wave", "Audio recording, multitrack editing, mixing and sound design."],
      ["Motion", "Video editing, animation, compositing and motion graphics."],
    ],
  },
  {
    id: "organise", number: "02", label: "Organise",
    description: "Tools for files, information and everyday work.",
    apps: [
      ["Files", "File management, storage, sharing and revision history."],
      ["Boards", "Notes, pages, projects and structured information in a flexible visual workspace."],
      ["Spaces", "Persistent contexts for conversations, research, study and agent-assisted work."],
      ["Data", "Spreadsheets, structured data, databases and analysis."],
      ["Planner", "Calendars, schedules, deadlines and assignment planning connected to your work."],
      ["Connect", "A communication hub for messaging, calls and collaboration."],
    ],
  },
  {
    id: "build", number: "03", label: "Build",
    description: "Tools for software, websites and games.",
    apps: [
      ["Build", "One app for software projects, code editing, debugging and testing, website authoring, source control and collaboration."],
      ["Games", "Game development and real-time 3D creation with scenes, assets and scripting."],
    ],
  },
];

function categoryOverview() {
  return `<section class="n9-container n9-family" aria-labelledby="n9-family-heading">
    <div class="n9-section-heading"><h2 id="n9-family-heading">The planned app family</h2><p>Three areas of work. A shared foundation.</p></div>
    <div class="n9-category-grid">${appGroups.map(group => `<article>
      <span class="n9-index" aria-hidden="true">${group.number}</span>
      <h3><a href="/apps/#${group.id}">${group.label} ${next}</a></h3>
      <p>${group.description}</p>
      <p class="n9-app-names">${group.apps.map(app => app[0]).join(" · ")}</p>
    </article>`).join("")}</div>
  </section>`;
}

function projectStatus() {
  return `<section class="n9-container n9-status-section" aria-labelledby="n9-status-heading">
    <div class="n9-section-heading"><h2 id="n9-status-heading">Where things stand</h2><a class="n9-text-link" href="https://trello.com/b/GB0oqo1e/cake-roadmap">Project roadmap ${arrow}</a></div>
    <dl class="n9-definition-list">
      <div><dt>App availability</dt><dd>The app family is in development. There are no public downloads on this website.</dd></div>
      <div><dt>Dulche AI</dt><dd>Optional shared AI is planned. <a href="/ai/">Read the approach ${next}</a></dd></div>
      <div><dt>9to1 Account</dt><dd>Shared identity for CakeMods and 9to1 is in private testing. Public registration is closed. <a href="https://9to1.uk/account">Account information ${next}</a></dd></div>
    </dl>
  </section>`;
}

function home() {
  return {
    title: "9to1 — Creative, productivity and development apps",
    description: "9to1 is a planned family of creative, productivity and development apps. Explore the app catalogue, Home and Files. In development; no public downloads.",
    body: `<section class="n9-hero n9-container" aria-labelledby="n9-hero-title">
      <div class="n9-kicker"><p class="n9-eyebrow">A CakeMods software project</p><span class="n9-status">In development</span></div>
      <h1 id="n9-hero-title">A connected workspace,<br>in the making.</h1>
      <p class="n9-lede">A planned family of creative, productivity and development apps for documents, design, media, files and software projects.</p>
      <div class="n9-actions"><a class="n9-button" href="/apps/">Explore the app catalogue ${next}</a><a class="n9-text-link" href="/about/">About the project ${next}</a></div>
      <p class="n9-availability">The apps are being developed. Public downloads are not available.</p>
    </section>
    ${categoryOverview()}
    <section class="n9-foundation" aria-labelledby="n9-foundation-heading"><div class="n9-container">
      <div class="n9-section-heading"><div><p class="n9-eyebrow">The shared foundation</p><h2 id="n9-foundation-heading">Start with Home. Keep your work in Files.</h2></div><p>Two central parts of the planned workspace.</p></div>
      <div class="n9-foundation-grid"><article><div class="n9-foundation-title"><img src="${NINE_LOGO_URL}" alt="" width="460" height="435" loading="lazy"><h3>Home</h3></div><p>The starting point for installed apps, navigation, settings and shared services.</p><a class="n9-text-link" href="/apps/#home">About Home ${next}</a></article>
      <article><div class="n9-foundation-title"><h3>Files</h3><span class="n9-small-label">Organise</span></div><p>File management, storage, sharing and revision history, connecting work across the family.</p><a class="n9-text-link" href="/apps/#files">About Files ${next}</a></article></div>
    </div></section>
    ${projectStatus()}`,
  };
}

function apps() {
  return {
    title: "The planned app catalogue — 9to1",
    description: "Explore planned 9to1 apps for creative work, organisation and development, with Home as the shared starting point. In development; public downloads are not available.",
    body: `<section class="n9-page-hero n9-container"><p class="n9-eyebrow">App catalogue</p><h1>The planned<br>app catalogue.</h1><p class="n9-lede">Home is the shared starting point. Explore the planned apps for creating, organising and building below.</p><p class="n9-availability"><span class="n9-status">In development</span> All listed apps are being developed. No public downloads are available.</p></section>
      <nav class="n9-category-nav n9-container" aria-label="App categories"><a href="#home">Home ${next}</a>${appGroups.map(group => `<a href="#${group.id}">${group.label} ${next}</a>`).join("")}</nav>
      <section id="home" class="n9-home-feature n9-container" aria-labelledby="n9-home-heading"><div class="n9-home-identity"><img src="${NINE_LOGO_URL}" alt="" width="460" height="435"><div><p class="n9-eyebrow">The starting point</p><h2 id="n9-home-heading">9to1 Home</h2></div></div><div><p>The planned home for installed apps, shared navigation, settings and services. It is the foundation connecting the rest of 9to1.</p><p class="n9-small-label">In development</p></div></section>
      ${appGroups.map(group => `<section id="${group.id}" class="n9-container n9-catalogue-section" aria-labelledby="n9-${group.id}-heading"><div class="n9-section-heading"><div><p class="n9-eyebrow">${group.number} / App family</p><h2 id="n9-${group.id}-heading">${group.label}</h2></div><p>${group.description}<br><span class="n9-small-label">All apps in development</span></p></div><div class="n9-app-list">${group.apps.map(([name, description]) => `<article class="n9-app-row"${name === "Files" ? ' id="files"' : ''}><h3>${name}</h3><p>${description}</p></article>`).join("")}</div></section>`).join("")}
      <section class="n9-container n9-faq" aria-labelledby="n9-faq-heading"><div class="n9-section-heading"><h2 id="n9-faq-heading">Before you get started</h2><p>Current availability and access.</p></div><details><summary>Can I download 9to1 now?</summary><p>There is no public download on this site yet. This catalogue describes the product direction, and individual apps remain in development. You can follow the work on the <a href="https://trello.com/b/GB0oqo1e/cake-roadmap">CakeMods roadmap ${arrow}</a>.</p></details><details><summary>Can I create a 9to1 Account?</summary><p>9to1 Account is shared by CakeMods and 9to1, but access is limited to private testing. Public registration is closed. See <a href="https://9to1.uk/account">account information ${next}</a> for current access.</p></details><details><summary>Do these apps require AI?</summary><p>The product direction is built around complete manual tools. Dulche is intended to add optional assistance to those workflows. Neither the apps nor Dulche are publicly available from this website.</p></details></section>`,
  };
}

function about() {
  return {
    title: "About the project — 9to1",
    description: "9to1 is a CakeMods project developing a connected family of creative, productivity and development tools. Learn about the project and its current status.",
    body: `<section class="n9-page-hero n9-container"><p class="n9-eyebrow">About the project</p><h1>A software family<br>from CakeMods.</h1><p class="n9-lede">9to1 is a CakeMods project building a family of apps for the different forms a piece of work can take.</p></section>
      <section class="n9-container n9-project-story" aria-labelledby="n9-story-heading"><h2 id="n9-story-heading">The idea behind the family</h2><div><p>A note becomes a plan. A plan becomes a document, a design or a piece of software. The aim is to connect those workflows while giving each tool a clear purpose.</p><p>Home is the planned shared starting point, with navigation, settings and services. Files provides file management and organisation. Creative, workspace and development apps build on that foundation.</p><a class="n9-text-link" href="/apps/">Explore the planned apps ${next}</a></div></section>
      <section class="n9-foundation" aria-labelledby="n9-principles-heading"><div class="n9-container"><div class="n9-section-heading"><div><p class="n9-eyebrow">Project principles</p><h2 id="n9-principles-heading">What guides the work</h2></div></div><div class="n9-principle-grid"><article><h3>Complete manual tools</h3><p>Writing, creating and organising should be useful workflows in their own right. AI assistance is intended to be optional.</p></article><article><h3>Shared foundations</h3><p>Navigation, files and services are intended to connect the apps, so different kinds of work can belong to the same workspace.</p></article><article><h3>Visible choices</h3><p>Personal settings and clear permissions are part of the product direction, including control over optional AI assistance.</p></article></div></div></section>
      <section class="n9-container n9-project-status" aria-labelledby="n9-about-status"><div><p class="n9-eyebrow">Current stage</p><h2 id="n9-about-status">Still in development</h2></div><div><p>This site introduces the planned product family. The apps are being developed, and public downloads are not available. 9to1 Account is limited to private testing; public registration is closed.</p><a class="n9-text-link" href="https://trello.com/b/GB0oqo1e/cake-roadmap">Follow the CakeMods roadmap ${arrow}</a></div></section>
      <section class="n9-container n9-cake-bridge" aria-label="Part of CakeMods"><img src="${CAKEMODS_LOGO_URL}" alt="" width="2626" height="2626" loading="lazy"><div><h2>Part of CakeMods</h2><p>CakeMods is home to independent communities and creative projects, including 9to1.</p></div><a class="n9-text-link" href="https://cakemods.com">Visit CakeMods ${arrow}</a></section>`,
  };
}

function ai() {
  return {
    title: "Dulche — Planned AI assistance for 9to1",
    description: "Dulche is the optional shared AI service planned for 9to1, with local and cloud model choices and permission-aware tools. It is in development, not a live service.",
    body: `<section class="n9-page-hero n9-container"><p class="n9-eyebrow">Dulche AI / In development</p><h1>Optional assistance.<br>Shared across 9to1.</h1><p class="n9-lede">Dulche is the AI service being developed for the app family. The aim is to bring model choices and assistance into the same workspace as your tools.</p><p class="n9-availability">There is no live AI service on this website.</p></section>
      <section class="n9-container n9-ai-plans" aria-labelledby="n9-ai-plans-heading"><div class="n9-section-heading"><h2 id="n9-ai-plans-heading">What is planned</h2><p>Product direction, with implementation still in progress.</p></div><dl class="n9-definition-list"><div><dt>Model choices</dt><dd>Shared model management, with planned support for local and cloud models.</dd></div><div><dt>Connected assistance</dt><dd>Assistance within creative, productivity and development workflows, using authorised tools and work contexts.</dd></div><div><dt>Visible permissions</dt><dd>Permission-aware access to tools and information, with model selection and inspectable actions as part of the design.</dd></div></dl></section>
      <section class="n9-foundation" aria-labelledby="n9-ai-approach-heading"><div class="n9-container n9-project-story"><h2 id="n9-ai-approach-heading">The manual workflow comes first</h2><div><p>Dulche is intended to add another way to approach a task. The apps are designed around complete manual tools, so assistance is a choice.</p><p>Local models, cloud providers and shared context bring different requirements. Model choices, permissions and clear actions are part of the planned service. These capabilities are still in development.</p><a class="n9-text-link" href="/apps/">See the planned app family ${next}</a></div></div></section>
      <section class="n9-container n9-project-status" aria-labelledby="n9-ai-status"><div><p class="n9-eyebrow">Availability</p><h2 id="n9-ai-status">In development</h2></div><div><p>Dulche and the 9to1 apps are being developed. There is no public download, hosted assistant or model access offered by this website.</p><a class="n9-text-link" href="https://trello.com/b/GB0oqo1e/cake-roadmap">Follow the project roadmap ${arrow}</a></div></section>`,
  };
}

const pages = new Map([["/", home], ["/apps/", apps], ["/about/", about], ["/ai/", ai]]);

export function renderNine(pathname) {
  if (typeof pathname !== "string" || !pathname.startsWith("/")) return null;
  const canonical = pathname === "/" ? "/" : `${pathname.replace(/\/+$/, "")}/`;
  return pages.get(canonical)?.() ?? null;
}
