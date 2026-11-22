# Shared 9to1 OS platform contracts

Use the app registry and router for app identity and lifecycle, the storage layout for durable data, and central grants for platform access. Native UI, packages and host-specific services consume these contracts through their own adapters. A registry entry identifies an app; verify the actual launched host when testing product behaviour.

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
