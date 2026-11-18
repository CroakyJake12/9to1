# Settings — complete page family — Settings; Home-managed CUI theme preference (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0498 · v2 lines 2500–2500

Settings

### V2-B0499 · v2 lines 2503–2503

Home Settings owns ecosystem-wide settings only. The Settings surface MUST include at least Account & Subscription, Appearance/CUI, AI & Models, Permissions & Trust, Apps & Updates, Storage, Mesh & Devices, Privacy, Accessibility, Notifications and Advanced/Diagnostics. App-specific preferences remain in the owning app unless a setting intentionally governs multiple apps.

### V2-B0500 · v2 lines 2506–2506

Settings changes MUST use typed settings keys/schemas with validation and explicit scope (device, account, workspace/team or organisation where applicable). Unknown or incompatible settings versions MUST NOT be silently discarded during upgrade/downgrade.

### V2-B0501 · v2 lines 2509–2511

V3 editorial amendment: V3-OWNER-005. Original wording is preserved in the replacement ledger.

Home-managed CUI theme preference
Home Settings → Appearance/CUI MUST let the user select their preferred complete CUI theme (Professional, Glow, Playful, Frosted Glass, Bubble, Metallic, Retro, Western or an available compatible custom theme; Cinematic remains an explicit legacy choice). Home owns the validated, versioned user-preference setting and exposes it via the canonical Home Core settings service and authorised typed get/set/subscribe APIs. Preference scope MUST be explicit: device-local when unauthenticated; when signed in, the applicable user/account preference MUST be available consistently to the user's supported 9to1 apps and devices via normal authorised settings synchronisation, without merging unrelated users' or organisations' preferences.
Changing the preferred theme in Home MUST propagate to compatible running 9to1 app windows and newly opened app surfaces without requiring each app to implement its own theme picker, persist a competing selection or restart where live retheming is supported. Apps MUST render the effective preference through shared CUI theme resolution, honour accessibility/reduced-motion settings, and retain the selected theme across app closes/restarts. Offline clients use their last valid locally cached preference; when no valid preference or theme exists, fall back deterministically to Professional and surface any unavailable custom-theme selection honestly.
