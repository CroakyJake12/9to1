# Native CUI candidate verification

Canonical source: `4ee0c10e6d7d2274202acc9aba735f7c648b9ad0`. Clean tracked source before and after. This receipt replaces reliance on the older native9 result for the current native sidebar icon, shared header row and composer-padding changes.

Actual runner result: **92 passed, 0 failed, 0 skipped**, exit0. Successful build emitted no compiler warning/error diagnostics. Forty-three recorded source files remained byte-identical throughout. All157 DLLs in the exact native test output are hashed in the adjacent manifest. No source edits, commits, capture or deployment occurred in this lane.

Command/configuration:

```text
DOTNET_CLI_HOME=/workspace/.tools/dotnet-cli
NUGET_PACKAGES=/workspace/.tools/nuget/packages
AVALONIA_TELEMETRY_OPTOUT=1
HAVEN_VISUAL_CAPTURE_DIR unset
/workspace/.tools/dotnet/dotnet test '9to1 Workspace/shared/tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj' -c Release -f net10.0 -p:Platform=x64 -p:UseSharedCompilation=false -m:1 -nodeReuse:false --no-restore --filter 'FullyQualifiedName~ChatCuiSceneTests|FullyQualifiedName~NativeCuiThemePreferencesTests|FullyQualifiedName~NativeChatSidebarCuiAdapterTests|FullyQualifiedName~NativeChatSidebarNewChatTests|FullyQualifiedName~LegacyTemporaryConversationTests|FullyQualifiedName~TemporaryConversationPageTests'
```

This portable maintained native target compiles the actual Desktop application together with the Spaces canonical .cui resources, Spaces Chat runtime, shared CUI Runtime/Themes, source-linked BrowserCanonicalIcons and shared Haven.UI icon catalog. It uses the source-vendored UI framework and existing package assets; no dependency versions or release audit policy changed.

Included test ownership:

| Owner | Cases | Exercised behavior |
|---|---:|---|
| ChatCuiSceneTests |5| Real composer keyboard/send ownership, no delayed replay, keyed transcript identity, original message action, composer bounds at1200/800/390 in Professional and Glow |
| NativeCuiThemePreferencesTests |1| Same local saved preference owner publishes theme/appearance/accent without a second store |
| NativeChatSidebarCuiAdapterTests |9| Actual CUI row/input binding, canonical destination identity, collapse acknowledgement, retained pending navigation/prompt/close, live self-join refusal and completed-context cleanup |
| NativeChatSidebarNewChatTests |15| Actual coordinator admission/acknowledgement ordering, refusal preserves selection/unread/scope, canonical Space isolation, original failures and retirement joins |
| LegacyTemporaryConversationTests |34| Existing Temporary history/memory/save/mutation guards plus24 bounded model readiness/selection regressions |
| TemporaryConversationPageTests, including its privacy/frame partial files |28| Actual Page send/save/recovery/close boundaries, strict Temporary persistence/events, and MainView's maintained shared-frame mount/transfer/retirement seam |

The partial files `TemporaryWorkspacePrivacyTests.cs` and `TemporarySpacesFrameTests.cs` belong to the TemporaryConversationPageTests class and are included by that filter. They are not additional counts beyond28.

Evidence: `native-cui-candidate-4ee0c10e-tests.log` and `native-cui-candidate-4ee0c10e-manifest.json`. The exact executable script and its hash are recorded there. Source HEAD/tree and clean status are recorded before/after; no build input changed.

Limits: this is Linux headless behavior with controlled provider/repository/lifecycle boundaries. The native transcript message-action test invokes the actual dispatcher directly; native pointer traversal of that specific strip is not inferred. Shared RevealWith pointer, keyboard, original-control and disposal behavior is separately covered by the current335-case CUI Runtime receipt. No native screenshots or pixel comparison were generated. Header/icon geometry fidelity, screen readers, real Windows/Android devices, actual sign-in/provider generation and full application cold-start acceptance remain separate. No whole Spaces acceptance follows from this batch.

The serial .NET slot was released to root immediately after source/binary evidence was captured. No compiler server or native test process remained in this lane.
