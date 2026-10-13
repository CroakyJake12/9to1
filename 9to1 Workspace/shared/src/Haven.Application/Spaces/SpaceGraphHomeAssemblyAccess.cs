using System.Runtime.CompilerServices;

// Compile-time integration only. The original Home graph owner still validates
// privately issued claims and live actor/store/revision under the real writer.
[assembly: InternalsVisibleTo("HavenOS.Home")]
