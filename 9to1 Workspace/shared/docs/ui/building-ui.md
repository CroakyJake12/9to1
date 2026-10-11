# Building first-party UI with CUI

The 11 October 2026 V2 correction is authoritative for new work and migration. First-party application markup is `.cui`; templates and theme declarations use `.cui-prefab` and `.cui-theme`. Production compilation and loading reject `.axaml` and `.hui` application input, donor element tags and donor attached layout syntax. Renaming a file or embedding a whole legacy application in an Object does not establish conformance.

The source currently retains legacy hosts. Their inventory and unresolved migration status are recorded in `eng/cui/first-party-inventory.json` at repository root. They are migration work, not an alternative authoring contract or accepted exception.

## Page structure

```xml
<Cui id="example.workspace" version="1">
  <Theme Name="user_preference">
    <Page id="workspace">
      <Container Type="Grid" RowSizes="Auto 1fr Auto">
        <Container Type="Horizontal Stack" CellGap="8">
          <Text Text="Document" />
          <Button Action="SaveDocument" Text="Save" />
        </Container>
        <Container Row="1" Type="Overlay" VerticalScrolling="True">
          <Object Type="RegisteredDocumentSurface" />
        </Container>
        <Text Row="2" Text="{Binding Status}" AccessibleName="Document status" />
      </Container>
    </Page>
  </Theme>
</Cui>
```

The Object above requires a genuine registered renderer and typed content, selection, action, lifecycle, input, accessibility, theme and error contracts. It is not a fallback for ordinary controls, a generic document substitute or a wrapper around external donor UI.

The ten visual/structural elements are Page, Container, Text, Image, Audio, Video, Object, Button, Input and Layer. Anchor is nonvisual. Definitions, Types, Prefabs, variables, properties, Styles/actions, imports, conditionals, repeats and shorthand retain the canonical language semantics. Do not re-export Grid, StackPanel, WrapPanel, ScrollViewer, TextBlock or Border as Definition aliases.

## Layout and controls

Use Container Type="Grid", "Vertical Stack", "Horizontal Stack" or "Absolute", plus registered additional Types. Grid sizing uses RowSizes/ColumnSizes with Auto, logical pixels or fractional units such as 1fr; placement uses Row/Column and spans. CellGap/CellMargin and independent HorizontalScrolling/VerticalScrolling belong to CUI. Border, BorderColor, BorderWidth and CornerRadius are properties, not primitive tags.

Use real canonical menu, dropdown, dialog, inspector and toolbar Types/components. They must expose functioning CUI templates, keyboard/focus handling and states; a list of inert labels does not qualify. Keep app-specific workflows and normal contextual actions discoverable.

## Theme ownership

CUI is the sole presentation authority. Inherit Home's `user_preference` through CUI. Professional, Glow, Playful, Bubble and Cinematic are complete CUI themes; custom and nested themes retain their declared inheritance. Do not install FluentTheme/SimpleTheme beneath CUI or repair a page with donor selectors and local colour overrides.

Every effective property resolves through the shared CUI cascade: built-in default, inherited Theme, Type/Definition default, explicit instance property, scoped property region, then active Style/event overlay. Runtime assignments update their declared level; temporary states restore the underlying value. Use the effective-property inspector to verify values and sources.

Theme changes preserve content, selection, focus and work. User-authored document styling is content and is never recoloured to match application chrome. Keep Montserrat and user-selected font/accessibility preferences in the shared typography contract. Use semantic CUI resources for ordinary presentation; keep low-level native rendering behind adapters.

## Actions, lifetime and accessibility

Keep domain logic in the existing owning services. Named CUI actions and automation/API calls use the same typed domain operations. Preserve canonical IDs, permissions, scope checks, cancellation, revision fences, unknown outcomes, persistence, undo and recovery. Existing namespaces and storage/IPC identifiers remain compatibility contracts until explicitly migrated.

Accessible names, semantic roles, keyboard traversal, focus restoration, touch targets, scaling, contrast and reduced motion are acceptance requirements. Structural conditions retain their specified lifecycle; Active, Hidden and Interactive are distinct. Ordinary text is literal; only explicit Children/content operations parse authorised CUI.

## Migration and validation

The explicit one-time tool is `eng/cui/migrate_legacy.py`. It records proposed/applied source hashes and refuses unknown or ambiguous adapters. It is never called by a production loader. Inspect every changed surface and retain IDs, bindings, actions and service/engine custody. Frozen negative/historical fixtures may retain rejected syntax for their declared tests; positive application fixtures must use canonical CUI without weakening assertions.

Build the changed graph, launch the exact app route, exercise a meaningful user journey and inspect the resulting saved/exported artifact and screenshots. Include 1200×900, 800×600 and applicable narrow/mobile/scaling checks. Compiler parsing, a test host or a colour-only screenshot does not prove application conformance. All APP-AC gates and applicable donor/RCVP/Full requirements remain mandatory; missing native integration stays FAIL/BLOCKED/NOT RUN.
