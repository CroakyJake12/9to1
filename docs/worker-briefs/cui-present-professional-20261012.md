# 9to1 CUI Professional / Present: OpenCode worker brief (2026-10-12)

## Purpose
Refine the EXISTING shared CUI Professional theme and active Present.cui implementation. Keep the current document model and scene/editor runtime. The owner has approved use of the existing private Cloudflare Present preview for testing, not a production rollout. OpenCode agent: cui-present-professional (see .opencode/agents/cui-present-professional.md).

## Starting source and architecture
- Repo: CroakyJake12/9to1; isolated setup branch opencode/cui-present-professional-20261012 from main at 77f32cb609eb9b2c527c6172bd7e5efe06ebe845. Fetch and reconcile newer main intentionally, never overwrite it blindly.
- Active browser Present: apps/Web/Productivity/Present/Present.cui and PresentBrowserSession*, PresentBrowserSurface*, Scene/*; dedicated shared Editor/App components in framework/CUI/Components.
- CUI shared ownership: framework/CUI/Runtime/CuiNativeHost.cs, CuiControlTemplates.cs, CuiComponentStyles.cs, CuiPropertyPresentation.cs; theme palette/components in framework/CUI/Themes.
- Known issue: legacy native 9to1 Workspace/Present/PresentApplication.cs explicitly installs Avalonia.Themes.Fluent.FluentTheme and DefaultTheme.axaml. Shared CUI has its own templates; audit which startup path each runtime uses, then remove or isolate FLUENT VISUAL AUTHORITY without breaking mature native behavior.
- Authoritative V2 specification is the project owner’s living Google Doc. Existing browser Present README and tests describe established behavior, not full final acceptance. Do not claim full LibreOffice Impress parity solely from interchange or native focused tests.

## Screenshot specification (owner's intended look)
Owner screenshot supplied in chat 2026-10-12, not committed into this public repository. To permit pixel-level comparison, the owner can save it privately as local reference/present-target-20261012.png and set PRESENT_TARGET_IMAGE to that path (untracked).
- Large, quiet charcoal/superdark backdrop; no Windows blue-grey Fluent tint.
- Top line: round Present-red app badge, bold editable title and small 'Saved · Just Now'; on right small AI/comment/account actions only if real.
- Second line: sidebar toggle directly before File; compact horizontal File, Edit, Insert, Slides, Presentation, View or existing equivalent working menus with all inherited commands intact.
- Third line: single horizontal full-width floating capsule toolbar, dark slightly raised surface, Montserrat, compact rounded action fields, icon groups and separators, contextual typography. Keep functional icon overflow at narrow widths. No second toolbar.
- Left rail intentionally missing from FAST mockup: restore polished scrollable slide thumbnails and names with selected state and accurate previews. Right-click thumbnail > Rename slide updates metadata, not canvas objects.
- Huge white authored slide, gently rounded corners and exact 16:9 proportion, centered in the remaining workspace; no invented permanent slide-title box.
- Collapsed lower 'Slide Notes' bubble; clicking reveals functioning notes editor. The two old bottom buttons stay removed.
- Zoom in the screenshot is a pasted screenshot FROM OLD UI and must be replaced: consistent custom CUI pill containing zoom-out, slider, live %, zoom-in and Fit icon. Fit scales against actual available stage. Zoom not cosmetic.
- Present's default red accent must respect global/individual/none overrides. Other theme variants must survive the shared template fixes.
- Only Professional gets Mica/blur on backdrop-mounted chrome; slide content remains opaque and accurately rendered. No expensive per-frame filters.

## Run OpenCode (from repo root)
1. Fetch branch and open a clean isolated checkout or worktree. Do not interfere with concurrently running 9to1 tasks.
2. Ensure the intended OpenCode model is available. Existing .opencode/opencode.json pins the build agent to Sol and subagents to Luna; the custom worker does not hardcode a model so the operator may select one. Existing docs/9TO1_WORKER_DEPLOYMENT.md reports CLI/Desktop model catalogue mismatches; verify instead of silently falling back.
3. Use an environment variable pointing to a LOCAL COPY of the screenshot if possible, not a URL into a personal ChatGPT session.
4. Configure NINETOONE_PRESENT_PREVIEW_URL and NINETOONE_PRESENT_PREVIEW_WORKER in the user's local/CI environment. Use securely held CLOUDFLARE_ACCOUNT_ID / CLOUDFLARE_API_TOKEN only for scoped Cloudflare Worker deployments. Do NOT put the obscured hostname, login address, or any token in this public GitHub repo.
5. Invoke:
   opencode run --agent cui-present-professional "Inspect baseline, then implement the architecture-first Professional + Present refinement. Keep all acceptance gates active."
   Or start OpenCode in the repo and select cui-present-professional as the primary agent.
6. Before deploying, run: node tools/present-preview/verify-access.mjs. Only publish to the previously verified existing Worker after identifying and validating the actual cloud build/deploy mechanism and compiling the exact artifact. Refresh the private browser URL and test the served version, not the local copy.

## Delivery gates
- Phase 0: evidence of existing rendering path, baseline screenshots/functionality/speed, existing commands map and worker ownership.
- Phase 1: shared CUI template/theme authority; Professional visual tokens; no Fluent/Simple visual fallback in supported hosts, without breaking other apps.
- Phase 2: refine Present layout and functional components; preserve all existing editors/storage/features.
- Phase 3: native/wasm tests + actual signed-in browser interactions at compact/desktop sizes; independent screenshot review and performance comparison.
- Phase 4: checked preview publish behind unchanged Cloudflare Access policy, build hash/log and regression evidence. Handoff through branch/draft PR. Do not merge to main without review.

## Privacy and deployment caution
This is a PUBLIC code repo with a PRIVATE staging URL. Treat the URL as operational configuration; avoid adding it to committed files, screenshots or CI logs. Cloudflare Access is the real barrier, not an obscured hostname. The local access verifier sends only an unauthenticated request and fails closed when the preview returns a public 200. It is NOT a signed-in functional test. Deploying with Wrangler may replace script/assets/bindings: inspect the existing Worker and preserve its domain, Access application and last-known-good deployment before modifying anything.
