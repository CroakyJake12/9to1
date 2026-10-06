namespace HavenOS.Home.Core;

public sealed partial class FileHomeCoreStateStore
{
    // SAME configured store's captured location. This is internal composition metadata;
    // only the domain's native owner can establish current private layout, never a string.
    internal string OriginalLocalDomainStatePath => _path;
}
