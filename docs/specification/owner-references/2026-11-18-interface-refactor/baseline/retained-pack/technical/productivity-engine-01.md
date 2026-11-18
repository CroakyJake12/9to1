# Shared productivity engine and canonical embeds — Shared Productivity Engine; Canonical object identity, schemas and style registry (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0474 · v2 lines 2418–2418

Shared Productivity Engine

### V2-B0475 · v2 lines 2421–2421

Home owns the Shared Productivity Engine used by Write, Present, Canvas, Boards and Spaces for overlapping document/object behaviours. The engine is a shared canonical object/rendering/action layer, not five copied implementations. App shells MAY provide different workflows and layout chrome, but a shared object type MUST use the same schema, formatting semantics, rendering rules and typed object actions wherever that type is supported.

### V2-B0476 · v2 lines 2424–2424

The initial required shared object families include rich text/paragraphs, headings, lists/checklists, code blocks, equations, tables, images/media, drawings/ink, graph objects and typed embedded 9to1 artifact references. The registry MUST be extensible so a new shared object type can be implemented once and enabled in multiple supporting surfaces without rewriting its core editor/renderer.

### V2-B0477 · v2 lines 2427–2427

App-specific document structures remain allowed. Present still owns slides and presentation-level layout; Canvas still owns its infinite spatial surface; Boards owns Board/Page hierarchy and Board-specific layout. The shared engine standardises reusable embedded/shared objects rather than forcing one top-level document model.

### V2-B0478 · v2 lines 2430–2430

Canonical object identity, schemas and style registry

### V2-B0479 · v2 lines 2433–2433

Every shared object instance MUST have a stable ObjectID within its owning artifact plus a versioned ObjectType/schema identifier. Object content, formatting, accessibility metadata, asset references and app-neutral layout properties MUST be serialisable independently from transient UI state. Unknown required schema versions MUST fail explicitly rather than being silently flattened or reinterpreted.

### V2-B0480 · v2 lines 2436–2436

Shared object schemas MUST preserve enough information for semantically identical rendering/editing across participating apps. If an app cannot support a property, it MUST retain that property in the object payload when round-tripping and surface the limitation instead of destructively stripping it.

### V2-B0481 · v2 lines 2439–2439

The engine MUST provide a shared versioned style registry for reusable text/paragraph/object style definitions. Style definitions use stable StyleID identity independent of display name so renaming a style does not detach existing objects. App-specific style extensions MAY exist but MUST preserve shared base properties when transferred.

### V2-B0482 · v2 lines 2442–2442

Cross-app copy, paste and embeds

### V2-B0483 · v2 lines 2445–2445

The engine MUST provide a versioned ProductivityObjectBundle clipboard/transfer format containing shared object payloads, style definitions, asset references, accessibility metadata and a portable fallback representation. Copying a supported object from one participating app and pasting it into another MUST preserve the same shared-object semantics and appearance rather than converting it to an image or lossy app-specific approximation.

### V2-B0484 · v2 lines 2448–2448

When the destination supports the same shared object type, paste creates/imports a native shared object instance using the same schema. When the destination does not support that object type but supports embedded 9to1 artifacts, the UI MUST offer an embed/reference path. Destructive flattening is allowed only as an explicit export/paste-as-static action.

### V2-B0485 · v2 lines 2451–2451

Spaces MUST use the Shared Productivity Engine for rich pasted/embedded content so tables, code blocks, drawings, graphs and other shared objects preserve their structure instead of degrading to plain text. Spaces may present a conversational shell around the object, but editing MUST route through the same shared object actions.

### V2-B0486 · v2 lines 2454–2454

Rendering, editing, undo and actions

### V2-B0487 · v2 lines 2457–2457

The engine MUST provide shared selection, focus, formatting, object insertion/removal, resize/transform where applicable, clipboard, accessibility and undoable action primitives for shared object types. Shared actions MUST have stable typed representations so compatible apps can participate in undo/recovery and AI/Automation execution without reproducing hidden per-app commands.

### V2-B0488 · v2 lines 2460–2460

App-specific shells MAY add contextual commands, but MUST NOT fork the underlying object semantics into incompatible versions. Object actions MUST be exposed through typed APIs and usable by UI, Spaces, Automations and Dulche-driven AI without raw UI simulation.

### V2-B0489 · v2 lines 2463–2463

The shared engine MUST emit semantic selection/context information for the persistent AI bar and app-specific contextual toolbars. This context MUST contain stable object/selection identity rather than requiring consumers to infer meaning from rendered pixels.

### V2-B0490 · v2 lines 2466–2466

Drawing engine

### V2-B0491 · v2 lines 2469–2469

The shared engine MUST expose the Rnote-derived drawing/ink engine as a reusable shared object/layer so Canvas and Boards use the same stroke model, pen/eraser behaviours and serialisation primitives. Other productivity surfaces MAY opt into the same drawing object without implementing a separate ink stack.

### V2-B0492 · v2 lines 2472–2472

Deployment boundary and compatibility

### V2-B0493 · v2 lines 2475–2475

Home Core MUST expose the Shared Productivity Engine as a versioned shared platform capability. Apps may link/embed approved shared engine libraries/components as part of their package for rendering performance, but the canonical schema registry, compatibility negotiation and shared service/version authority remain Home-owned. Apps MUST validate compatible engine/schema versions during startup.

### V2-B0494 · v2 lines 2478–2478

Shared object schemas and ProductivityObjectBundle MUST be versioned independently of individual app versions. Home MUST expose compatibility negotiation so an older app can detect a newer unsupported shared object type/property before editing. Compatible unknown optional properties MUST be preserved; incompatible required features MUST produce a structured compatibility state rather than silent data loss.

### V2-B0495 · v2 lines 2481–2481

Shared Productivity Engine API

### V2-B0496 · v2 lines 2484–2494

9to1.ProductivityEngine.ListObjectTypes();
9to1.ProductivityEngine.GetObjectSchema(objectType);
9to1.ProductivityEngine.ListStyles(scope?);
9to1.ProductivityEngine.GetStyle(styleID);
9to1.ProductivityEngine.SerializeSelection(context);
9to1.ProductivityEngine.Paste(bundle, targetContext);
9to1.ProductivityEngine.CanPaste(bundle, targetContext);
9to1.ProductivityEngine.Convert(object, targetType, options?);
9to1.ProductivityEngine.GetSelection(context);
9to1.ProductivityEngine.ApplyAction(context, action);
9to1.ProductivityEngine.GetCompatibility(appID, appVersion);

### V2-B0497 · v2 lines 2497–2497

The above are minimum cross-app entry points. Each shared object type MUST also expose its typed creation/update/query actions through the owning app/API surface required by the global API-parity rule.
