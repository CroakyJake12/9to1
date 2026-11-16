/*
 * Public factual notices for the present brochure / invitation-only account beta.
 * Controller identity and support address were explicitly supplied by the user.
 * These content revisions DO NOT authorize account opening or populate the
 * identity PUBLIC_TERMS_VERSION / PUBLIC_PRIVACY_VERSION environment variables.
 *
 * INTERNAL PUBLICATION / OPERATIONAL REVIEW — unresolved facts, not assurances:
 * - Record the controller's purpose-by-purpose legitimate-interest assessment,
 *   including the particular interests/rights of invited users aged 13–17.
 * - Confirm accepted Cloudflare/Google contractual terms, processing entities,
 *   international transfer mechanism and any required EEA representative. No
 *   signed DPA, adequacy reliance or transfer assessment has been verified here.
 * - Confirm actual Cloudflare edge/Access/Turnstile cookie settings in deployed
 *   browser journeys, including names and lifetimes of conditional provider cookies.
 * - Confirm log, backup, support-message and inactive-account retention. Source
 *   cleanup thresholds are not evidence that a production schedule has run.
 * - Confirm operator coverage for rights, complaints, accessibility and incidents.
 * - Company registration is the user's gate for public accounts. Paid plans,
 *   jurisdictions, consumer/tax obligations and all AI/parental-control runtime
 *   gates remain separate. Never convert these notices into compliance approval.
 *
 * Source inventory: security.mjs/auth.mjs/session-engine.mjs; identity/src/auth.ts,
 * auth-tabs.ts, limiter.ts, invitations.ts, public-policy.ts, maintenance.ts;
 * pinned Better Auth 1.7.7 cookies/index.mjs, state.mjs, context/create-context.mjs;
 * pinned OAuth provider logout-confirmation cookie implementation.
 * Legal source baseline: evidence/legal-research.md and outputs/launch-requirements.md.
 */

export const legalContentVersions = Object.freeze({
  privacy: 'privacy-2026-11-15-v1',
  terms: 'invite-beta-terms-2026-11-15-v1',
  cookies: 'cookies-2026-11-15-v1',
});

const contact = '<a href="mailto:support@9to1.uk">support@9to1.uk</a>';
const stamp = '<p class="small">Last updated 15 November 2026.</p>';
const related = '<nav aria-label="Legal information"><a href="/privacy/">Privacy</a> · <a href="/terms/">Beta terms</a> · <a href="/cookies/">Cookies</a></nav>';

export const privacyPage = {
  title: 'Privacy',
  description: 'How Jacob Bailey uses information for CakeMods, 9to1 and the invitation-only 9to1 Account beta.',
  body: `<article class="prose-page"><p class="eyebrow">YOUR INFORMATION</p><h1>Your privacy.</h1>
    <p class="lead">CakeMods and 9to1 are operated personally by Jacob Bailey, based in the United Kingdom. This notice explains the public websites and the limited 9to1 Account beta.</p>
    ${stamp}${related}
    <h2>Who is responsible?</h2>
    <p>Jacob Bailey is the controller of the personal information described here. CakeMods, 9to1 and 9to1 Account are service names; they do not identify a separate registered company. For privacy questions, rights requests or complaints, email ${contact}.</p>
    <h2>What is available now?</h2>
    <p>You can browse product information and links to communities without an account. Account sign-in is restricted to the approved private test. New accounts and invitation redemption remain closed. Public registration remains closed. Existing account or invitation records from restricted testing are also covered by this notice.</p>
    <p>This release does not offer paid subscriptions, an AI chat service, user uploads or public posts. A roadmap describes plans, not features you can currently use.</p>
    <h2>Information we use</h2>
    <ul>
      <li><strong>Website requests:</strong> an IP address, request time, requested address, browser/device information and security-related request information needed to deliver and protect the websites.</li>
      <li><strong>Accounts:</strong> account identifiers, name, username, email address, email-verification status, account dates and a password hash for password sign-in. Optional profile information can include a picture, pronouns, work description and appearance preferences.</li>
      <li><strong>Invitations and account setup:</strong> the invited email address, invitation status and dates, the inviting administrator, a selected age group and records of acknowledged terms/privacy versions where account setup is available. An inviter may supply your email address before you contact us.</li>
      <li><strong>Sign-in and security:</strong> session identifiers, expiry and device information, provider connections, verification/recovery records, and attempts to use account functions. Account-session records can include the IP address and browser information for the session. Abuse controls also use derived identifiers and challenge results.</li>
      <li><strong>Support:</strong> your address, message, any attachments you choose to send and our correspondence. Please do not send passwords, sign-in codes or unnecessary sensitive information.</li>
    </ul>
    <p>Required account details are used to identify and protect your beta account. If you do not provide them, account creation or recovery may not be possible. You do not need to fill optional profile fields. Please do not put health information or other sensitive personal details in your profile or routine support messages.</p>
    <h2>Why we use it</h2>
    <p>We use this information to deliver the websites and requested beta account features, connect your profile across CakeMods and 9to1, check invitations and eligibility, prevent abuse, send verification and recovery messages, investigate problems and answer requests.</p>
    <p>For these limited activities, our lawful basis is our legitimate interests in operating a requested service, protecting people and accounts, and responding to enquiries. We also process information where necessary to meet a legal obligation, such as responding to an applicable data-protection request. These interests must be balanced against your rights, particularly where children are involved. You can object to processing based on legitimate interests by contacting us.</p>
    <p>Reading this notice or accepting beta terms is not consent to marketing. We do not use these websites to sell personal information, run targeted advertising or add audience-analytics tracking. Account emails are for the service, not automatic enrolment in a marketing list.</p>
    <h2>Google sign-in, when offered</h2>
    <p>If you choose Google sign-in, Google supplies a Google account identifier, name, email address, verification status and a profile image where provided. We use that information to authenticate or connect your invited account. The requested basic sign-in permissions do not give us access to your Gmail messages or Google Drive files. Password sign-in remains an alternative.</p>
    <p>Google processes your interaction with its own sign-in service under its <a href="https://policies.google.com/privacy">privacy policy</a>. Google sign-in does not bypass an invitation or other account eligibility requirements.</p>
    <h2>Who receives information?</h2>
    <p><strong>Cloudflare</strong> provides hosting, request handling, account databases and session infrastructure. It also provides email delivery/routing and, where enabled, account security checks. Security checks can send Cloudflare technical signals such as an IP address, browser information and the page origin. See <a href="https://www.cloudflare.com/privacypolicy/">Cloudflare’s privacy policy</a> and its <a href="https://www.cloudflare.com/turnstile-privacy-policy/">Turnstile privacy information</a>.</p>
    <p><strong>Google Gmail</strong> hosts the support inbox. Messages sent to ${contact} are forwarded to that inbox, so Google processes the message and related email information. This support-email arrangement is separate from optional Google sign-in.</p>
    <p>Jacob Bailey and people specifically authorised to help operate the service may access information when needed for support, security or administration. Information may also be disclosed where a legal obligation requires it. We do not publish account profiles or support messages as community content.</p>
    <p>Links to Reddit, Discord, Trello and other websites take you to services with their own operators and privacy practices. Clicking a link does not give that service access to your 9to1 Account profile through this website.</p>
    <h2>International processing</h2>
    <p>Cloudflare and Google operate internationally. Information handled by those services may be processed outside the UK or EEA, including in the United States. We do not represent that all information stays in the UK or EEA.</p>
    <p>Provider arrangements and the requirements for a broader international account launch are still under review. Public registration remains closed. Contact ${contact} for information about the processing locations and arrangements relevant to your information. This notice does not claim that a particular transfer safeguard or representative appointment has been verified.</p>
    <h2>How long information is kept</h2>
    <p>Our retention criteria are the time needed to provide the beta account, resolve support or rights requests, investigate a security issue, and meet a specific legal obligation. Account and profile records are retained while needed for the account or those purposes; you can ask us to close the account or erase information.</p>
    <p>Sign-in cookies and verification links have expiry limits. Those limits do not mean every related server record is immediately erased. Provider logs, backups and support correspondence can have different lifecycles. A single fixed retention period has not been confirmed for all of those copies. We will explain any information retained and the reason when handling an erasure request, rather than promise instant removal from every backup.</p>
    <h2>Your choices and rights</h2>
    <p>The <a href="/account">account page</a> currently explains the testing status and how to contact us. Profile, session and download controls are available to the approved private test account. A self-service account download, when available, is a limited summary rather than a substitute for a wider access request.</p>
    <p>Email ${contact} to request access, correction, erasure, restriction, an applicable portable copy, or to object to processing. You can also ask about closing an account. We may need proportionate information to verify identity or a representative’s authority, but we will never ask for your password. Rights depend on the applicable law and can have exceptions.</p>
    <p>We respond within the applicable legal deadline. UK access and erasure requests usually have a one-calendar-month deadline, subject to permitted extensions or other applicable rules. Contact us if you need an accessible way to make a request.</p>
    <h2>Children and young people</h2>
    <p>The account beta is intended for invited people aged 13 or over. Do not create an account if you are under 13. If you believe an under-13 account exists, a parent or guardian can contact us so we can review it. A selected age group is not proof of identity or a promise that age-assurance checks are complete.</p>
    <p>AI features are not available in this release. We do not claim that parental controls or AI safety protections have been implemented or tested for an available AI service. Any future service for young people will need its own safeguards and clear information before it is offered.</p>
    <h2>Questions or complaints</h2>
    <p>Please contact ${contact} if something concerns you. We will acknowledge a UK data-protection complaint within 30 days, investigate without undue delay and explain the outcome. You can also complain to the <a href="https://ico.org.uk/make-a-complaint/">UK Information Commissioner’s Office</a> or an applicable data-protection authority where you live.</p>
    <p>We will update this notice when the operator, processing or available services change. Significant new uses of information will be explained before they begin.</p>
  </article>`,
};

export const termsPage = {
  title: 'Website and beta account terms',
  description: 'Terms for the current CakeMods and 9to1 websites and invitation-only 9to1 Account beta, operated by Jacob Bailey.',
  body: `<article class="prose-page"><p class="eyebrow">THE CURRENT BETA</p><h1>Using the beta.</h1>
    <p class="lead">These terms cover the public CakeMods and 9to1 websites and access to the invitation-only 9to1 Account beta.</p>
    ${stamp}${related}
    <h2>Who operates the service?</h2>
    <p>The operator is Jacob Bailey, acting personally and based in the United Kingdom. CakeMods and 9to1 are service names, not a statement that a separate company has been registered. You can contact the operator at ${contact}.</p>
    <h2>What these terms cover</h2>
    <p>The websites currently provide information about products and projects and links to external communities. Account sign-in is restricted to the approved private test; new accounts and invitation redemption remain closed. The account provisions below apply when the operator provides beta access. They do not promise that invitations are currently open.</p>
    <p>Public account registration remains closed while the operator prepares for a future company-based launch.</p>
    <p>This release does not sell subscriptions or provide a live AI service. Roadmap items, illustrations and product descriptions describe development plans; they are not a promise of availability, a release date or a paid entitlement. Future paid or AI services will require clear additional information and applicable terms before they are offered.</p>
    <h2>Invitation-only accounts</h2>
    <p>Account access is limited to people invited to the beta who are at least 13 years old and meet the account setup requirements. Use the email address your invitation was issued to. Do not sell, share, transfer or try to reuse an invitation.</p>
    <p>Provide accurate account information and choose only an age group that applies to you. Keep your password and recovery access secure. Do not share your account or attempt to access somebody else’s. Tell us promptly if you suspect your account has been compromised.</p>
    <p>Where Google sign-in is offered, it is an optional sign-in method, not a way to obtain an invitation or bypass account checks. A 9to1 Account does not replace the separate account or rules of Reddit, Discord or another linked platform.</p>
    <h2>Using the service responsibly</h2>
    <p>Do not use the service for unlawful activity, harassment, impersonation, malware, unauthorised access or attempts to disrupt the service. Do not bypass security or usage restrictions. Only submit information you are entitled to provide, and avoid putting sensitive information in optional profile fields.</p>
    <p>If you discover a security issue, report it to ${contact}. Do not access other people’s information or carry out disruptive testing. We can agree an appropriate scope before further investigation.</p>
    <h2>Changes, availability and account restrictions</h2>
    <p>This is a developing beta. Features may change, be interrupted or be withdrawn. Please do not rely on the beta as your only store for important information. We will explain significant changes through the website or, where appropriate, account email.</p>
    <p>We may restrict an account where necessary to address a security issue, misuse, a legal requirement or the end of the beta. Where practical, we will explain the reason and how to ask for a review. Urgent protective action may be taken first. You can contact us to challenge a restriction.</p>
    <h2>Your information and leaving the beta</h2>
    <p>You retain rights in the information you provide. Our <a href="/privacy/">privacy notice</a> explains how we use it to operate the service. Accepting these terms does not subscribe you to marketing or waive data-protection rights.</p>
    <p>You can stop using the beta at any time and contact ${contact} to request account closure or erasure. We will explain any information that must be retained for a particular legal or security reason and how backups are handled.</p>
    <h2>External services and important rights</h2>
    <p>External communities and linked services are run under their own terms. Their operators and moderators are responsible for those services. We do not promise that an external service will remain available.</p>
    <p>Nothing in these terms excludes or limits rights or responsibilities that cannot lawfully be excluded or limited, including applicable consumer and data-protection rights. These terms do not claim that the beta is certified secure or legally compliant in every country.</p>
    <h2>Questions and updates</h2>
    <p>For support, accessibility needs, complaints or questions about these terms, contact ${contact}. If the operator changes to a registered entity, or the service changes materially, we will publish the relevant details and terms before asking you to use the new offering.</p>
  </article>`,
};

export const cookiesPage = {
  title: 'Cookies and browser storage',
  description: 'The sign-in, security and notice-preference storage used by CakeMods, 9to1 and 9to1 Account.',
  body: `<article class="prose-page"><p class="eyebrow">BROWSER STORAGE</p><h1>Cookies &amp; storage.</h1>
    <p class="lead">The public information pages do not add advertising or audience-analytics cookies. Invitation-only account access is being prepared; ordinary browsing does not start an account sign-in session.</p>
    ${stamp}${related}
    <h2>Why these are used</h2>
    <p>The account entries below describe the configured sign-in flows for restricted testing and future invited access. They are not a statement that public sign-in or invitation redemption is currently open.</p>
    <p>A sign-in cookie lets the service recognise the session you asked to start. Short-lived security cookies connect the beginning and end of a sign-in attempt. The account notice’s “Got it” control dismisses information; it is not agreement to advertising or optional tracking.</p>
    <p>We rely on the relevant necessary-service/security exceptions for purpose-limited sign-in storage. If optional tracking is introduced later, it will be assessed separately and consent requested where required before it runs.</p>
    <h2>Website sign-in cookies</h2>
    <p>These cookies are separate for each website. They are sent over HTTPS, are not readable by page scripts and are restricted to the host that sets them.</p>
    <dl>
      <dt><strong><code>__Host-cake_session</code></strong></dt><dd><p>On cakemods.com or 9to1.uk after successful website sign-in. Holds an opaque reference to the website session, rather than the account’s access or refresh tokens. Configured lifetime: up to 7 days. It can end sooner through sign-out, expiry or revocation.</p></dd>
      <dt><strong><code>__Host-cake_oauth</code></strong></dt><dd><p>On the website where you begin sign-in. Protects the return from the account service by tying it to that attempt. Configured lifetime: up to 10 minutes; cleared when the website processes the callback.</p></dd>
    </dl>
    <h2>9to1 Account cookies</h2>
    <p>The account service currently uses <code>cake-id-release-validation.<wbr>jcbailey008.workers.dev</code>. Its cookie names retain the authentication software’s name; this does not change the service name, 9to1 Account.</p>
    <dl>
      <dt><strong><code>__Secure-better-auth.<wbr>session_token</code></strong></dt><dd><p>Recognises an authenticated account session. Configured session lifetime: 30 days, with renewal during use. Sign-out or revocation can end access earlier.</p></dd>
      <dt><strong><code>__Secure-better-auth.<wbr>state</code></strong></dt><dd><p>Used by the database-backed external sign-in flow, such as optional Google sign-in, to check the returning browser. Configured cookie lifetime: 5 minutes; removed when that flow is processed.</p></dd>
      <dt><strong><code>__Secure-better-auth.<wbr>dont_remember</code></strong></dt><dd><p>May be set when a supported authentication request asks for a non-persistent session. This is a browser-session preference, not an advertising identifier.</p></dd>
      <dt><strong><code>__Secure-better-auth.<wbr>session_token.<wbr>oauth_logout_confirmation</code></strong></dt><dd><p>May be used by an identity-provider sign-out confirmation flow. Configured lifetime: up to 5 minutes.</p></dd>
    </dl>
    <p>These are sign-in/security cookies on the account-service host. Not every flow sets every cookie. Browser session restoration can affect how long session-only storage remains available.</p>
    <h2>Notice preference</h2>
    <p>The account page uses a session-storage entry named <code>nine-account-cookie-notice-v1</code> to remember that you dismissed its notice in that browser tab. It contains the word “dismissed”, not your account information. It normally lasts for the tab’s session. You can reopen the notice using “Cookie information” in the account footer.</p>
    <h2>Cloudflare security checks and restricted areas</h2>
    <p>Where an account form uses Cloudflare Turnstile, the security check processes browser/network signals and a one-time challenge token. Cloudflare can also use security cookies when a challenge or access-control feature requires them. For example, <code>cf_clearance</code> is conditional on challenge/pre-clearance settings; restricted pages can use Cloudflare Access authentication cookies. Their presence and lifetime depend on the applicable security configuration.</p>
    <p>See <a href="https://www.cloudflare.com/turnstile-privacy-policy/">Cloudflare’s Turnstile privacy information</a> and <a href="https://developers.cloudflare.com/cloudflare-challenges/concepts/clearance/">challenge-cookie explanation</a>, or contact ${contact} about a security cookie you see and its configured lifetime.</p>
    <h2>Google and other external websites</h2>
    <p>Choosing Google sign-in takes you to Google, where Google may use its own cookies under its <a href="https://policies.google.com/privacy">privacy policy</a>. Following a link to a community or another external website can also result in that service using its own storage. Those cookies are not set by the CakeMods or 9to1 brochure pages.</p>
    <h2>Your controls</h2>
    <p>You can inspect or remove cookies and site storage through your browser settings. Blocking sign-in or security storage can prevent account access or force you to sign in again. Clearing browser storage does not itself delete your server-side account; contact ${contact} if you want to request account closure or erasure.</p>
    <p>Cookie lifetimes describe browser expiry, not the retention of every related database record, provider log or backup. Read the <a href="/privacy/">privacy notice</a> for the distinction.</p>
  </article>`,
};
