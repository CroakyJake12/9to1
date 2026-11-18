# Home — Apps, Dashboard and global systems — Package contract and lifecycle; Spaces QOL (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2182 · v2 lines 12950–12955

LE-02 — Package contract and lifecycle
The native package manifest MUST classify each stable PackageID/version as mandatory shared core, optional app, app-required dependency or optional feature dependency. The mandatory-shared rule takes precedence when a component is both shared and niche. Mandatory means installed, not eagerly started or loaded into memory. Record dependency version ranges, platform/architecture support, integrity/signature metadata, activation requirements and reverse dependencies using Home's existing package service.
The single trusted installer MUST install Home, all mandatory modules, the selected app and its app-required dependencies. Selecting a second app MUST reuse compatible shared packages. Internal platform package formats may be used; separate user-facing standalone MSIX installers for individual apps are not the distribution model. Native Windows and 9to1-OS use this contract through their supported packaging mechanisms. Browser apps use authenticated shared web services and MUST NOT require a local Home installation.
Before a niche feature needs an absent optional module, show the required component, download/storage impact and Install/Cancel controls. A cancelled or failed install preserves the user's work and intended action; retry is explicit and safe. A required dependency cannot be uninstalled while an installed app depends on it. Uninstalling an optional app preserves user artifacts and dependencies required elsewhere. Updates, repair and rollback use staged validation and atomic activation; interruption MUST preserve or recover the previous valid installation.
Opening an artifact or embedded app whose required app/module is absent MUST offer the same trusted install path, or an explicit unavailable state when installation is unsupported. It MUST NOT silently download unrelated apps, create a substitute viewer with lost capabilities, or claim a module is available solely because another Mesh device has it.
Acceptance: clean-install one chosen app without unrelated optional apps; add another without duplicated core; decline and retry a feature-module install; reject removal of an in-use dependency; interrupt an update and recover; uninstall/reinstall without losing artifacts; verify Home version negotiation and the browser/no-local-Home case.

### V2-B2183 · v2 lines 12958–12959

Spaces QOL
Screenshots taken from ChatGPT for reference, not exact design mockups.
