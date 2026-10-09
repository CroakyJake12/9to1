using System.Runtime.CompilerServices;

// Only the configured product memory adapter may inspect the original internal canonical
// membership issuer. No public binding constructor or IDs-as-authority factory is added.
[assembly: InternalsVisibleTo("HavenOS.Apps.Assistants.Memory")]
[assembly: InternalsVisibleTo("HavenOS.Assistants.Memory.Tests")]
