# Repository structure for shared native code

The native solution is under the shared workspace. Core owns domain contracts, Application owns use cases, Infrastructure supplies implementations, and Desktop composes the window. Browser functionality has shared service and native host components. App-specific CUI, domain code and tests can also live outside the solution directory under their owning workspace.

Keep dependencies directed toward their contracts. Put a platform-specific filesystem, browser or account implementation behind the existing owner. A route that shares a provider should borrow the original provider and its lifetime.

For a presentation change, locate the actual host first. For a change affecting multiple hosts, identify the common service or CUI document and verify both normal entry points.

[Haven solution](../../Haven.sln) · [Core](../../src/Haven.Core) · [Application](../../src/Haven.Application) · [Infrastructure](../../src/Haven.Infrastructure) · [Desktop](../../src/Haven.Desktop) · [CUI](../../../../framework/CUI)
