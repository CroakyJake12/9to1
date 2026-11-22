# Frequent native integration defects

| Symptom | What to inspect |
| --- | --- |
| An edit works in one entry point only | Confirm both standalone and Haven hosts use the same implementation and action |
| A setting changes on screen but reverts on reopening | Trace the owning profile, revision and durable write receipt |
| A theme change makes text unreadable | Inspect the resolved surface, text and active accent under the current mode and accessibility settings |
| Closing a page repeats or loses an action | Track the original asynchronous operation through retirement |
| UI says a provider is available before use | Obtain the provider’s actual capability and current permission |

Keep input, focus, overflow and saved state in ordinary test coverage. A screenshot establishes a visible state and should be paired with the action or persistence evidence relevant to the fix.

[Home theme adapter](../../src/Haven.Desktop/Services/NativeHomeThemePreferences.cs) · [Testing](testing.md) · [UI owners](../App-Editing-Guide/UI-MAP.md)
