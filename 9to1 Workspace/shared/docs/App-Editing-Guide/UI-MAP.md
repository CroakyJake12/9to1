# Find the native UI owner

| Surface | Source and state owner |
| --- | --- |
| Shared application tabs | Browse native workspace and its current tab projection |
| Dashboard and New Tab | Distinct native destinations sharing Haven’s shell |
| Global Settings and Appearance | Home CUI and the device-profile preference service |
| Chat and Temporary | Spaces Chat and the active conversation controller |
| Shared controls, keyboard behaviour and appearance | CUI control loader, templates and theme resources |

Only the top application strip receives the left-aligned tab treatment. Keep internal editor tabs and component controls within their own layouts. A theme change should derive its colours from semantic material roles, with sufficient text contrast, regardless of the active app accent.

Before changing a legacy host layout, trace the active route from Haven. An old AXAML file or a screenshot from an earlier build alone cannot identify the owner of the visible UI.

[Top tabs](../../../Browse/NativeUI/UI/BrowseWorkspace.cui) · [Home settings](../../../Home/UI/Home.cui) · [Chat](../../../Spaces/UI/Chat/ChatSpace.cui) · [Control loader](../../../../framework/CUI/Runtime/CuiControlLoader.cs) · [Atlas routes](../../../../docs/specification/owner-references/2026-11-22-atlas-v4/README.md)
