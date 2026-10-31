#!/usr/bin/env python3
"""Write the Boards hand-assessment: one state per MUST requirement, with evidence and a grounding note.

Method, stated so the number can be argued with:
  * every Boards MUST row from the pinned register gets an explicit state - nothing is left implicitly counted;
  * states: IMPLEMENTED / PARTIAL / NOT-IMPLEMENTED / NOT-ASSESSED. NOT-ASSESSED is used where this pass did not
    ground a claim, so the total stays honest instead of flattering;
  * evidence is a named artifact I verified exists in this checkout (a file path in the repository, or a test
    method name read out of a Boards test file);
  * several NOT-IMPLEMENTED calls are grounded by a measured ABSENCE, and the grep that established the absence is
    recorded in the reason so it can be re-run and falsified.

This is a code-and-test reading pass. It is not on-screen observation: no Boards surface was rendered or driven in
this pass, so no row here is acceptance evidence, and the states say nothing about whether the behaviour is right.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path("/home/jacob/Documents/GitHub/9to1")
sys.path.insert(0, str(ROOT / "eng" / "spec-coverage"))
import spec_register as register_tool  # noqa: E402

rows = [r for r in register_tool.measure()[0] if r["level"] == "MUST" and r["app"] == "Boards"]
by_cluster: dict[str, list[dict[str, object]]] = {}
for row in rows:
    parts = [p for p in str(row["section"]).split(" > ") if p]
    by_cluster.setdefault(parts[2] if len(parts) > 2 else parts[-1], []).append(row)

ABSENT = lambda terms, scope="Boards": (f"grounded absence: no match for /{'|'.join(terms)}/ in "
                                        f"9to1 Workspace/{scope} first-party source (measured 31 Oct 2026)")

CLUSTER = {
    "Product role and donor contract": ("PARTIAL", "9to1 Workspace/Boards/DonorParityManifest.md",
        "the manifest exists and names the selected AppFlowy revision with classifications; the parity it records "
        "is itself incomplete, so the contract is held but the coverage is not"),
    "Visual shell": ("PARTIAL", "9to1 Workspace/Boards/hui/Views/Boards.cui; app/ShellChrome.cs; "
                                "app.tests/BoardsVisualTests.cs",
        "CUI views and visual tests exist, but the presentation claims in this cluster (no paper/card floating, "
        "integrated editor heading, coherent chrome/sidebar/content hierarchy, icon coherence) were not observed "
        "on screen in this pass"),
    "Landing page": ("PARTIAL", "9to1 Workspace/Boards/app/RecentBoardCatalog.cs; "
                                "app.tests/RecentBoardCatalogTests.cs; app.tests/BoardsLandingRegressionTests.cs",
        "recent-boards surface and landing regressions are covered; the global launch-screen contract itself is "
        "Home-owned and Boards' conformance to it was not observed"),
    "Sidebar and board switching": ("PARTIAL", "9to1 Workspace/Boards/app/NavBuilder.cs; app/BlockGripCui.cs; "
                                               "app.tests/BoardsHomeBoardSwitcherTests.cs; "
                                               "app.tests/BoardsTreeMoveUndoRegression.cs",
        "switcher, tree and grip behaviour exist with tests; the presentation rows in this cluster (header area, "
        "independent scroll, collapsed narrow state, visual distinction of selection/nesting/disclosure) were not "
        "observed rendered, and the grip menu has no on-screen verification at all"),
    "Canonical Board and Page model": ("PARTIAL", "9to1 Workspace/Boards/contract/HavenRichBoardV3.cs; "
                                                  "contract/HavenBoardContract.cs; DonorParityManifest.md",
        "a versioned rich board/page contract with stable ids exists; the same manifest records that the canonical "
        "Board/Page schema the specification requires is not yet established (Boards still rides a NotesDocument "
        "with ProductKey=boards in the shared service)"),
    "Shared Productivity Engine": ("PARTIAL", "9to1 Workspace/Boards/DonorParityManifest.md; "
                                              "9to1 Workspace/shared/src/Haven.Application/Notes/"
                                              "NotesDocumentValidator.cs",
        "shared productivity bundles are validated in the shared service, but Boards has its own board contract, "
        "so the engine is shared in places and forked in others"),
    "Edit and View modes": ("PARTIAL", "measured presence: ViewMode/EditMode/EditView in 62 Boards files",
        "mode machinery is present; the persistent top-right Edit/View switch and the read-only-with-respect-to-"
        "content semantics were not observed and are not covered by a named test I found"),
    "Locked and Unlocked layout modes": ("IMPLEMENTED", "9to1 Workspace/Boards/app.tests/"
                                                        "BoardsFreeformPresentationTests.cs "
                                                        "(Free_placement_requires_unlocked_mode_and_valid_geometry; "
                                                        "Layout_mode_and_absolute_geometry_round_trip_without_"
                                                        "mode_switch_conversion)",
        "the locked/unlocked distinction, the free-placement gate and the no-conversion round trip are asserted by "
        "named tests"),
    "Free drawing and ink": ("PARTIAL", "9to1 Workspace/Boards/tests/HavenRichBoardV3Tests.cs "
                                        "(Ink_tools_pressure_eraser_selection_and_view_survive_reopen)",
        "ink with pressure, eraser and selection survives reopen in Boards' own contract tests; DrawingLayer naming "
        + ABSENT(["DrawingLayer", "InkLayer"], "Boards") + ", and the strokes are not carried by the Home "
        "shared-engine ink layer"),
    "Floating contextual formatting island": ("PARTIAL", "9to1 Workspace/Boards/app/ToolbarBuilder.cs; "
                                                         "app/BoardsViewModel.Toolbar.cs; "
                                                         "app.tests/BoardsSingleToolbarTests.cs; "
                                                         "app.tests/BoardsBlockConversionTests.cs",
        "there is a single toolbar with conversion and selection behaviour under test; " + ABSENT(["Island"]) +
        ", so the authored requirement for a persistent FLOATING contextual island is not met by that naming, and "
        "its positioning/scroll/accessibility rows were not observed"),
    "Persistent AI bar": ("PARTIAL", "9to1 Workspace/Boards/contract/HavenBoardGenerativePlan.cs; "
                                     "tests/HavenBoardGenerativePlanTests.cs; "
                                     "hui/HavenBoardGenerativeUiCoordinator.cs",
        "a generative plan and review flow exists with tests; " + ABSENT(["AIBar", "AiBar"]) + " in Boards, so "
        "Boards' integration with the global persistent AI bar and its semantic context (active board/page/"
        "selection/layout mode) is not established"),
    "Block system and AppFlowy block parity": ("PARTIAL", "9to1 Workspace/Boards/contract/"
                                                          "AppFlowyBoardEventAdapter.cs; tests/HavenRichBoardTests.cs; "
                                                          "app.tests/BoardsBlockConversionTests.cs",
        "donor CRUD is mapped onto typed commands and block conversion is tested, but full non-proprietary block "
        "parity is not demonstrated, and the parity manifest is a hand-kept list, which the requirement explicitly "
        "forbids as the sole mechanism"),
    "Graph block": ("IMPLEMENTED", "9to1 Workspace/Boards/tests/HavenRichBoardV3Tests.cs "
                                   "(Graph_block_full_lifecycle_survives_reopen_editable; "
                                   "Graph_expression_evaluator_is_deterministic_and_safe; "
                                   "Graph_slider_definitions_persist_and_drive_existing_expression_evaluator; "
                                   "Graph_inequalities_resolve_boundary_and_shaded_side_without_evaluating_"
                                   "comparisons_as_curves)",
        "multiple named tests cover the graph lifecycle, expression safety, sliders and inequality handling"),
    "Enhanced image formatting": ("PARTIAL", "9to1 Workspace/Boards/tests/HavenRichBoardTests.cs "
                                             "(Image_and_divider_blocks_survive_reopen)",
        "image blocks round-trip; crop, rotate, aspect-ratio, fit/fill and the non-destructive-source guarantee "
        "are not covered by any test or code I located"),
    "Enhanced table formatting": ("PARTIAL", "measured: TableBlock appears in 4 Boards files",
        "table blocks exist, but the required row/column add-remove-reorder-resize-merge and the "
        "formatting-separated-from-data guarantee are not evidenced"),
    "Cross-app parity blocks": ("PARTIAL", "9to1 Workspace/Boards/app/IBoardsEmbeddedArtifactHost.cs; "
                                           "app.tests/EmbeddedPresentationLifecycleTests.cs",
        "embedded hosts and their lifecycle are modelled and tested; the shared-engine block equivalence across "
        "Write/Present/Canvas/Data was not demonstrated from Boards' side"),
    "Write and Canvas page types": ("PARTIAL", "9to1 Workspace/Boards/tests/"
                                               "HavenBoardFreeformContractTests.cs (Canvas_page_accepts_only_a_"
                                               "canvas_owner_reference; Canvas_objects_move_resize_retext_and_"
                                               "remove_with_reopen)",
        "Canvas pages are real and tested; a Write page type in Boards was not evidenced"),
    "Chat page type": ("NOT-IMPLEMENTED", "measured absence: no Chat PageType value and no Chat page surface in Boards first-party source",
        "Boards' page kinds do not include a Chat page in this checkout; the typed-action machinery exists in the "
        "chat service elsewhere, so the Boards-side requirement is unmet"),
    "Scoped Chat page type": ("NOT-IMPLEMENTED", "measured absence: repository-wide grep", ABSENT(["ScopedChat"], "shared") +
                              " - the term does not occur anywhere in first-party source"),
    "Chat source subpages": ("NOT-IMPLEMENTED", "measured absence: no Chat page type to host subpages, and no Pending/Ready/Failed/Removed ingestion states in Boards",
        "depends on the Chat page type above, which is not implemented, so ingestion states (Pending/Ready/Failed/"
        "Removed) and revocation semantics have no Boards surface to live on"),
    "Data-backed structured items": ("PARTIAL", "9to1 Workspace/Boards/app/IBoardsDatabaseSource.cs; "
                                                "app.tests/BoardsDatabase{Insertion,Presentation,Reference,Close}"
                                                "Tests.cs",
        "a database-backed source with insertion, presentation, reference and close behaviour under test; the "
        "donor-facing filtering/sorting/grouping/view-selection workflow is not demonstrated"),
    "Board document-space coordinates": ("PARTIAL", "9to1 Workspace/Boards/tests/"
                                                    "HavenRichBoardPlacementTests.cs; app.tests/"
                                                    "BoardsFreeformInsertionTests.cs",
        "absolute placement is stored and round-tripped; the explicit statement that persistence uses Board "
        "document-space rather than CUI's proportional -1000..1000 range, and the view-transform-only guarantee for "
        "zoom/DPI/pan, were not directly verified"),
    "Embedded artifact ownership and lifecycle": ("PARTIAL", "9to1 Workspace/Boards/app/"
                                                             "IBoardsEmbeddedArtifactHost.cs; "
                                                             "contract/ContentAddressedHavenBoardAttachmentStore.cs",
        "artifact hosts and content-addressed attachments exist; explicit ownership independence, attach-without-"
        "transfer and delete-board-not-artifact were not each evidenced"),
    "Collaboration and concurrency": ("PARTIAL", "9to1 Workspace/Boards/contract/HavenBoardCollaboration.cs; "
                                                 "hui/HavenBoardCollaborationCoordinator.cs; tests/"
                                                 "HavenBoardCollaborationContractTests.cs (Disabled_sync_adapter_"
                                                 "never_claims_remote_delivery_or_returns_remote_data)",
        "collaboration contract and coordinator are tested, including truthfulness when sync is disabled; "
        "narrowest-entity conflict resolution, deterministic ordering under simultaneous moves, ephemeral presence "
        "state and reconnect replay identity were not each shown"),
    "Platform capability behaviour": ("PARTIAL", "measured: capability wording in 8 Boards files, "
                                                 "capabilityMatrix/PlatformCapability identifiers absent",
        "some capability awareness exists; an explicit queryable capability matrix and the closest-useful-"
        "alternative behaviour were not found"),
    "Performance and resource behaviour": ("NOT-IMPLEMENTED", "measured absence: repository-wide grep",
        ABSENT(["Virtualiz", "virtualis"]) + " in Boards; large-board lazy loading, stroke culling and the "
        "representative performance-acceptance scenarios have no implementation or harness in this checkout"),
    "Persistence, history and recovery": ("PARTIAL", "9to1 Workspace/Boards/contract/LocalBoardWriteLease.cs; "
                                                     "contract/JsonFileHavenBoardStore.cs; tests/"
                                                     "HavenBoardContractTests.cs (Corrupt_primary_recovers_previous_"
                                                     "durable_backup; Invalid_save_is_rejected_and_previous_valid_"
                                                     "file_survives; Failed_SaveAs_file_promotion_preserves_old_"
                                                     "identity); app.tests/BoardsSaveDurabilityRegression.cs",
        "crash-safe revisioned persistence and backup recovery are asserted by named tests; recoverable delete "
        "semantics " + ABSENT(["Trash", "undelete", "RetainedBoard"], "Boards") + " (retention exists in shared, "
        "not wired for Boards) and the dirty/saving/saved/failed status distinction were not evidenced"),
    "Boards API": ("NOT-IMPLEMENTED", "measured absence: repository-wide grep",
        ABSENT(["9to1\\.Boards\\."]) + " - the canonical Boards action namespace required by the API-parity rule "
        "does not occur anywhere in first-party source; typed command reduction exists inside the board contract, "
        "but it is not an exposed 9to1.Boards.* surface"),
    "Permissions and risk": ("PARTIAL", "measured: permission wording in 13 Boards files, confirm in 7",
        "permission and confirmation vocabulary is present; distinct read/edit/share/external/destructive "
        "permissions and the gated-approval behaviour for purge and bulk destruction were not individually "
        "evidenced"),
    "Failure behaviour": ("NOT-IMPLEMENTED", "measured absence: repository-wide grep",
        ABSENT(["BoardNotFound", "PageNotFound", "BlockNotFound"]) + " in Boards (one incidental hit in shared); "
        "the structured failure vocabulary the specification names is not what the contract returns, though "
        "rejection tests for duplicate/blank/oversized ids do exist"),
    "Acceptance requirements": ("NOT-IMPLEMENTED", "measured absence: repository-wide grep",
        ABSENT(["Install Home", "InstallHome"]) + ": the Home-absent bootstrap flow the acceptance row requires is "
        "not present in Boards' launch path"),
}

EXCEPTIONS = {
    # rows I judged separately from their cluster
    "G0:L9403-cd72717752": ("IMPLEMENTED", "9to1 Workspace/Boards/app/RecentBoardCatalog.cs; app.tests/"
                                           "RecentBoardCatalogTests.cs",
                            "a real Recent view backed by the catalogue, with tests"),
    "G0:L9403-1f9708fe43": ("IMPLEMENTED", "9to1 Workspace/Boards/app/RecentBoardCatalog.cs",
                            "the catalogue is populated from actual board files; the test "
                            "Empty_filtered_Boards_Library_uses_the_existing_create_and_audit_retry_host (in "
                            "Haven.Desktop.Tests) pins the no-fabrication behaviour"),
    "G0:L9431-cf9fbd10f0": ("NOT-IMPLEMENTED", "9to1 Workspace/Boards/DonorParityManifest.md line 20",
                            "the manifest states plainly that the current shared service stores Boards as a "
                            "NotesDocument with ProductKey=boards and 'does not yet establish the canonical "
                            "Board/Page schema required by the specification'; Boards rides its own contract, so "
                            "Home's Shared Productivity Engine is not used as required"),
    "G0:L9459-e928925526": ("NOT-IMPLEMENTED", "eng/donor-sources.json Boards/rnote binding",
                            "the Rnote-derived ink ENGINE is not consumed by Boards at any revision (Canvas pins "
                            "v0.14.2 and our fork changes at 44ff1089 are not consumed either), so free drawing "
                            "over the page via the Home shared engine is not met; Boards' own ink support exists "
                            "but is not that engine"),
    "G0:L9473-8e7b020bf7": ("NOT-IMPLEMENTED", "9to1 Workspace/Boards/app/ToolbarBuilder.cs",
                            "the toolbar builder exposes block/text tooling; there are no Rnote-derived pen/eraser/"
                            "ink controls in it, consistent with the engine gap above"),
    "G0:L9485-7b1c09bf6f": ("IMPLEMENTED", "9to1 Workspace/Boards/contract/AppFlowyBoardEventAdapter.cs",
                            "donor CRUD shapes are mapped onto typed 9to1 commands rather than calling donor "
                            "services; no hidden AppFlowy AI invocation path was found"),
    "G0:L9485-1f68b493c4": ("NOT-IMPLEMENTED", "measured absence: repository-wide grep",
                            "AI mutations cannot route through 9to1.Boards.* because that namespace does not exist "
                            "(measured absence); the generative plan applies typed commands internally, which is "
                            "close but not the required API surface"),
    "G0:L9491-7f7e2c0691": ("IMPLEMENTED", "9to1 Workspace/Boards/tests/HavenRichBoardV3Tests.cs "
                                           "(Heading_4_to_6_style_ids_round_trip_and_v2_migration_seeds_them)",
                            "Heading 4/5/6 styles exist with a migration path, asserted by name"),
    "G0:L9491-fbe72e7379": ("IMPLEMENTED", "9to1 Workspace/Boards/tests/HavenRichBoardV3Tests.cs "
                                           "(Custom_style_lifecycle_apply_persist_reopen_delete_fallback; "
                                           "Law_board_custom_styles_persist_with_applications)",
                            "custom styles are versioned definitions referenced by stable ids, under test"),
    "G0:L9565-7a7a88da0f": ("IMPLEMENTED", "9to1 Workspace/Boards/tests/HavenRichBoardPlacementTests.cs; "
                                           "app.tests/Layout_mode_and_absolute_geometry_round_trip_without_mode_"
                                           "switch_conversion",
                            "placement is stored as document-space absolute geometry and survives a mode switch "
                            "without conversion"),
    "G0:L9591-0b691f3976": ("NOT-IMPLEMENTED", "measured absence: no performance harness for Boards in this checkout",
                            "there is no large-Board/large-page/drawing-heavy/collaboration performance "
                            "acceptance harness, so this acceptance-facing requirement is unmet rather than "
                            "merely unmeasured"),
    "G0:L9577-1bd8a8172a": ("NOT-ASSESSED", "not grounded in this pass",
                            "I did not verify the narrowest-entity resolution claim either way; recorded as "
                            "unassessed rather than guessed"),
    "G0:L9579-21638f3a9b": ("NOT-ASSESSED", "not grounded in this pass",
                            "reconnect/offline replay identity not verified in this pass"),
    "G0:L9585-57039a1c74": ("NOT-ASSESSED", "not grounded in this pass",
                            "closest-useful-alternative behaviour not verified in this pass"),
    "G0:L9599-dfec1c02d3": ("NOT-ASSESSED", "not grounded in this pass",
                            "revision/conflict semantics for concurrent edits to the same canonical object not "
                            "verified in this pass"),
    "G0:L9601-96e9249c21": ("NOT-ASSESSED", "not grounded in this pass",
                            "dirty/saving/saved/failed status truthfulness not verified in this pass"),
    "G0:L9681-ff7c34925c": ("NOT-ASSESSED", "not grounded in this pass",
                            "behaviour when Home or the engine becomes unavailable after opening was not verified"),
    "G0:L9681-c2503369cb": ("PARTIAL", "9to1 Workspace/Boards/contract/JsonFileHavenBoardStore.cs",
                            "one storage implementation is in place; whether it can silently fall back to an "
                            "incompatible private engine was not tested either way"),
    "G0:L9669-322a0d6075": ("PARTIAL", "9to1 Workspace/Boards/contract/HavenBoardContract.cs",
                            "stable ids and structured rejections exist inside the contract, but there is no "
                            "exposed API surface, so revision checks and partial/error results are not "
                            "externally observable"),
}

entries = []
for cluster, items in by_cluster.items():
    default = CLUSTER[cluster]
    for row in items:
        state, evidence, reason = EXCEPTIONS.get(str(row["id"]), default)
        entries.append({"id": str(row["id"]), "state": state, "evidence": evidence,
                        "reason": reason, "basis": "cluster" if str(row["id"]) not in EXCEPTIONS else "row",
                        "cluster": cluster, "assessed": "2026-10-31 agent code/test reading pass"})

out = ROOT / "eng/spec-coverage/assessments/Boards.json"
out.parent.mkdir(parents=True, exist_ok=True)
out.write_text(json.dumps({
    "_meaning": "Hand states for every Boards MUST, overriding the text-matching probe. basis=cluster means the "
                "state came from a cluster judgement with named evidence rather than a row-by-row verification; "
                "basis=row means I judged that row individually. Both are code-and-test readings: no Boards "
                "surface was rendered in this pass, so nothing here is acceptance evidence.",
    "_app": "Boards",
    "_measured_on": "2026-10-31",
    "_rows": len(entries),
    "assessments": entries,
}, indent=2) + "\n")

from collections import Counter
counts = Counter(e["state"] for e in entries)
print(f"Boards rows written: {len(entries)} across {len(by_cluster)} clusters")
print("state counts:", dict(counts))
assessed = sum(v for k, v in counts.items() if k != "NOT-ASSESSED")
print(f"assessed coverage: {assessed}/{len(entries)}")
