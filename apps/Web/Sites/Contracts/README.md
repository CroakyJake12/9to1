# Portable source-consumption closure

This project compiles the unchanged canonical `9to1 Workspace/Sites/Domain/SiteModels.cs` as a source link. That file contains `SiteProjectFormat`, all canonical Sites models/enums, `SiteApiError`, `SiteApiResult<T>`, `SiteOperationException` and `SiteWorkspaceSnapshot`. Its sole explicit dependency is `System.Text.Json` in the target framework. No native store, authoring service, Home authority, hosting service, AngleSharp or private replacement model is included.

The Web application may reference this portable model compilation. The native test executable instead references the existing `HavenOS.Sites` owner project and compiles the same browser controller against its actual owner types; it must not simultaneously reference both assemblies defining those canonical types. There is no wire or schema change and no app-side new model authority.
