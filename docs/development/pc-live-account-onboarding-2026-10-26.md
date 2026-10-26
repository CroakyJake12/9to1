# Linux development account and onboarding repair

The 26 October local development copy uses the existing native CAKE ID public-client
PKCE/session owner for Home and onboarding. The owner confirmed the configured
CakeMods service is online; Linux previously had no registered browser/Access helper.

- Reviewed cloudflared 2026.9.3 helpers are selected by actual process platform:
  Windows x64, Linux x64 and Linux ARM64. Each has its own exact length/SHA-256 pin.
  Linux requires an executable, read-only helper in `auth/cloudflared` under the
  immutable runtime. ARM helper execution remains NOT RUN.
- Interactive Access acquisition precedes short HTTP timeouts within the original
  session operation's five-minute cancellation deadline. Cloudflare Access protection,
  PKCE/loopback validation, server-authoritative identity and normal close/drain stay
  with the existing owners. Browser authentication requires the user's interaction.
- Onboarding borrows that same session and waits for a current authenticated result.
  Failed sign-in stays actionable; Not Now still permits local-only completion.
  Completion records actual shared-session state rather than a fixed signed-out value.
- The authored CUI places steps and status in one vertical container so conditional
  lowering cannot overlay the status on the active step.

The recommendation-manifest source and inline recommended-model download flow remain
unfinished. No authoritative versioned `9to1.uk` manifest endpoint has been supplied.
Home's existing Hugging Face browser/install path remains the local-model management
route. Bundled seed/runtime assets require genuine canonical manifests/receipts and
licence notices; a downloaded GGUF alone does not establish inference readiness.

Source checkpoint: focused checks, authenticated sign-in and native reload are NOT RUN
for these changes at authorship. Exact later execution receipts live in the existing
private coordinator ledger. Full independent acceptance remains PAUSED.
