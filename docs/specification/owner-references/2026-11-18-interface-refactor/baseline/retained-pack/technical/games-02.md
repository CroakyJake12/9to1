# Games — scene and runtime development — Games and creation workspaces (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2327 · v2 lines 13487–13487

### UX27-OTHER-015 — Games and creation workspaces

### V2-B2328 · v2 lines 13489–13489

**B/D.** Development and Creation/Rendering are workspaces over the same canonical project/assets. Scene hierarchy, viewport selection, inspector, asset browser and timeline/animation tools share stable object identities. Selecting in the viewport highlights the corresponding hierarchy node; changing a property updates the real scene and is undoable. Play/test mode is explicitly distinct from authoring and does not silently save runtime state over the source scene.

### V2-B2329 · v2 lines 13491–13491

Build/export, debugging, modelling, materials and animation retain their donor/baseline scope. Preview rendering settings and final output settings are distinct. This UI expansion introduces no new gameplay content or special permissions; it preserves ordinary safe creation and testing boundaries.
