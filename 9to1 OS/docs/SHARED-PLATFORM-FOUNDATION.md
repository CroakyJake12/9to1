# Shared CakeOS platform foundation

This foundation is platform-neutral application infrastructure. It owns no application surface, GNOME UI integration, provider process, Linux secret store, audio stack, portal adapter, package, image, or release schema.

## Registry and routing

`CakeOS.Platform.AppRegistryContract` is the sole application registry identity:

- `registryId`: `cakeos.shared-app-registry`
- `registrySchemaVersion`: `1`

`IAppRegistry` owns runtime application enumeration and lifecycle metadata. `IAppRouter` resolves only registrations from that registry and rejects mismatched identities. Release metadata may reference the two identity values, but must not reproduce registry entries, lifecycle semantics, or routing rules.

## HUI Linux host ABI

`HuiLinuxHostAbi` identifies ABI `cakeos.hui.linux-root-provider` version `1`. Every `IHuiRootProvider` supplies `Abi`; the existing Linux host validates it before mounting the returned platform-neutral HUI `Page`. This remains an unprivileged Avalonia host process and is not a GNOME Shell, Mutter, GDM, or session migration.

## Storage and grants

`XdgPlatformStorageLayout` owns one durable root: `$XDG_DATA_HOME/haven`, falling back to `$HOME/.local/share/haven`. Shared settings are `$XDG_DATA_HOME/haven/platform/settings.v1.json`; the existing model store remains `$XDG_DATA_HOME/haven/models`. A future Windows adapter implements `IPlatformStorageLayout` without changing settings, permissions, governance, or notification APIs.

`PermissionService` is the one central scoped-grant authority. It stores grants in the shared versioned settings file and returns allow/ask decisions only. It never invokes polkit, portals, secret storage, audio, or another OS backend.

## Providers and notifications

`ProviderRegistry` reads provider-owned JSON. `HUI/llamacpp-provider.json` is the sole llama.cpp descriptor consumed by this foundation; no llama.cpp descriptor is redefined in C#. `ModelGovernance` stores explicit provider fallback order and delegates each model-use decision to `IPermissionService`. It does not choose a model or implement picker UI.

`NotificationService` owns a durable platform event model. UI surfaces may subscribe to events and render them, but the foundation does not select a desktop notification transport.
