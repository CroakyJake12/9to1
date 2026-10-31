# Exact Spaces test correction rationale

Production files are unchanged in this unit. Full suite: 294 pass / 10 fail, new archive/source tests all pass. This patch preserves all exercised cases and adds the disabled-inheritance counterpart.

- `ChatComposerCompiledTransportTests`, two admission cases: bundle now includes multiple documents. Codec checks all envelope/document metadata for consistency. Change every document consistently for the unsupported-host-version cases; retain exact CUIR064/CUIR066 expectations and the separate CUIR075 inconsistent-envelope case. Source: shared `CuiCompiledArtifactCodec` admission contracts; these were malformed fixtures, not relaxed admission.
- Composer sizing/action cases: owner `docs/specification/owner-references/2026-10-25-ui-report/requirements.md`, UI-RPT-001–006 explicitly replaces single Send with 56px action +14px rear extension, backed by genuine selected runtime action. Keep 44px ordinary tools and packaged glyph checks; assert 56/70/14 shelf dimensions and bound front icon/name/tooltip/visibility plus real shelf activation command. Source `Spaces/UI/Chat/ChatComposer.cui:88–111`.
- `SpaceDetailsControllerTests`: instructions change canonical active configuration; `SpaceRegistry.MutateSpaceAsync:590–598` increments active revision, updates no-draft base and appends immutable history. Assert all three effects and preserved old history, plus exact original unexposed fields. Do not pretend metadata is unchanged.
- `SpacesHomeNativeRenderingTests`, two sizes: actual header owns 72px; details start at row1. Assert details top equals header bottom and details bottom equals full rail bottom, preserving all real wheel/hit-test/independent-scroll checks. Source `SpacesHome.cui:61,276`. This does NOT bless the separate 390px width bug.
- `HavenChatSpaceBackendScopeTests`: canonical v2 User-created Spaces and nesting requires explicit/inspectable inheritance. `SpaceInheritancePolicy.ModelPolicy` defaults false. Enable it in the explicitly inherited-default case and add converse proving disabled inheritance leaves parent default unselected. Do not broaden production inheritance.
- `ChatSpaceConcurrencyAcceptanceTests`: exact partial persistence failure message starts sentence with capital `Partial`; only assertion capitalization changes. All accepted-input/replay/draft/visible-partial/storage error requirements remain asserted. Source `ChatSpaceController.Sending.cs:208`.
- `SpaceDevelopmentReferenceCatalogTests`: per-page Montserrat was deliberately removed in favor of shared configurable CUI theme. Assert absence of overriding page font; keep typed action whitelist/absence checks. Shared `CuiThemeResourceApplier.cs:588` resolves user override, `CuiCustomThemeResourceApplier.cs:49` custom main font. This source test does not claim rendered-font verification.

## Replacement action-shelf guard evidence

CUI owner added `framework/CUI/Runtime.Tests/CuiActionShelfAvailabilityTests.cs`. Its joined actual
loader/button case passed in `cui-bounded-full-trx/_7da2d9b32d40_2026-10-31_22_21_50_net10.0.trx`
under `/workspace/scratch/boards-sprint-20261031`: empty source cannot dispatch; populated visible
button still cannot dispatch when its current owner refuses availability; enabling that same owner
dispatches the exact `composer.send` and original parameter once. This replaces the obsolete authored
single-Send `Interactive=Composer.CanSend` assertion with runtime coverage of the current shelf guard.
Full CUI result was 774 passed / 5 existing failures; this is not a whole-framework pass.
