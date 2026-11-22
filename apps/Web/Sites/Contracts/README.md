# Portable Sites model contracts

The contracts project links the original `SiteModels.cs` and exposes its project format, domain types, API results and workspace snapshot with the target framework’s JSON support. Reference the portable assembly in the browser or the canonical Sites project in native tests. Keep one definition of those types in each test or runtime graph. [Linked project](NineToOne.Sites.Browser.Contracts.csproj) · [Canonical models](../../../../9to1%20Workspace/Sites/Domain/SiteModels.cs)

The portable graph uses `System.Text.Json` and includes `SiteProjectFormat`, `SiteApiResult<T>`, `SiteOperationException` and `SiteWorkspaceSnapshot`. Hosting, authoring and private storage belong to the native or browser owner that consumes the contracts.

The Web application may reference this portable model compilation. The native test executable instead references the existing `HavenOS.Sites` owner project and compiles the same browser controller against its actual owner types; it must not simultaneously reference both assemblies defining those canonical types. There is no wire or schema change and no app-side new model authority.
