# Specification snapshot and interaction volumes

The core specification and its three linked normative interaction volumes were read from their native sources for this launch bundle. All 24 tabs are included, with raw native structures, readable per-tab Markdown, exact paragraph text and searchable indexes. Use requirement/control IDs to retrieve the relevant records without loading every file into an agent context.

| Document | Local entry | Tabs | Nonblank paragraphs |
| --- | --- | --- | --- |
| 9to1 Spec | [main/INDEX.md](main/INDEX.md) | 15 | 4651 |
| 9to1 Spec — Spaces interaction reference | [spaces/INDEX.md](spaces/INDEX.md) | 3 | 2518 |
| 9to1 Spec — Editor interaction reference | [editors/INDEX.md](editors/INDEX.md) | 3 | 3878 |
| 9to1 Spec — App interaction reference | [apps/INDEX.md](apps/INDEX.md) | 3 | 3271 |

## Reading order

For the first visible UI slice, start with core tabs 1, 2 and 15 as needed; use the shared interaction rules and the exact relevant screen/control records. The full interaction volumes are for behaviour fidelity and the later swarm, not an instruction to read thousands of records before making the first UI edit.

- `requirements-index.json` locates definitions, original paragraph indexes and local tab files. Repeated IDs, if present in distinct normative documents, are retained with source context.
- `links-index.json` preserves native link targets, including heading/tab references and the three interaction documents.
- Each `native.json` preserves the returned source structure, text runs, tables, heading IDs, lists, links and embedded-object metadata.
- Each `paragraphs.json` preserves all paragraph strings, including blank/image paragraphs, with tab/source indexes. `all-text.txt` is convenient for `rg` searches.
- Markdown is a readable projection. Native list numbering, merged-cell layout, images and rich formatting are governed by the original native structure. It is not a DOCX/PDF rendering or a claim of pixel-identical document export.
- The core source contains two inline Comic images. The portrait reference is included as `references/comic-owner.png`; the second image's source metadata remains in native JSON and the native document. The complete self-contained Atlas and the portable design-reference folder are the primary visual inputs. External image URIs inside native metadata may expire.

The core's 4,651 nonblank paragraphs match the separate text readback exactly at the same revision, including all 1,113 requirement definitions. Every linked volume tab and paragraph was mechanically enumerated from the returned native source; none was truncated into a summary. The existing source remains authoritative for rich document rendering.
