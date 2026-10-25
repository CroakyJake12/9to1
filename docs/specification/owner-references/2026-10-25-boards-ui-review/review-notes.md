# Boards UI design review — 25 October 2026

The owner supplied `Boards_UI_Design_Review.pdf` during ongoing Boards development. It compares seven images and a short recording; it explicitly did not inspect source or execute the application. Its reported confidence is not runtime evidence. The accompanying source identity is retained in `source.json`.

## Authority and interpretation

The file is a design reference and review, not a replacement instruction source. Its labels such as “Approved target” report the author’s interpretation. Existing direct owner instructions, the authoritative development specification and the implementation-first/acceptance-paused decision continue to govern. The earlier owner warning about dated inspected UI means historical findings need current rechecks. This PDF is dated25October; compare its S1 screenshot with actual current source, not older screenshots alone.

Keep the same canonical Boards app standalone and embedded in Home. Keep selected themes, semantic appearance tokens and professional theme rules. Do not move or duplicate Home’s shared profile/model/notification controls solely because this reference places user icons in a Boards footer. That placement needs reconciliation with existing surface-specific chrome requirements. Do not copy AppFlowy branding or unsupported AI actions. Preserve real user content.

## Implementation workstreams

- Navigation/shell: consistent nested rows and indentation, independent disclosure/selection, pinned Add page/section control with explicit destination, board selector with distinct real operations, Boards branding, consolidated persistent formatting toolbar, accurate edit/lock/save state. Preserve existing hierarchy, file identity, content and undo.
- Editor geometry: fixed block-handle dimensions independent of table height, reserved quieter gutter, compact checkbox-to-label spacing and wrapped-label alignment; deliberate content alignment and honest component screenshots.
- Existing supported insertion and navigation actions remain real. Slash insertion and selector options must use canonical implementations; do not expose unsupported items as working.

The suggested widths, heights, indentation and spacing are starting design values rather than mandatory measurements. Screenshot sample colours are diagnostics rather than a palette. No clipping, missing tooltip, inaccessible target or persistence failure is established solely by these pictures.

## Verification boundary

Run affected compilation and focused authentic smoke/geometry checks while implementation continues. Keep full independent acceptance paused. Capture the same actual populated view when applicable; label isolated component images as such. Record source revision, theme, viewport and rendering environment. The earlier merged-table editor capture showed freeform content and is not table evidence; retain it without laundering it into a passing screenshot.
