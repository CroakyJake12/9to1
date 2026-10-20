using System.Runtime.CompilerServices;

// The public compatibility record CakeOS.Cui.Compiler.CuiCompilationMetadata historically lived in
// the Compiler assembly. It now lives in the Core assembly (below the compiler) so the Build.Tasks
// emitter and runtime consumers can share it without a dependency inversion. This forwarder
// preserves its CLR identity for precompiled binaries that referenced it in the Compiler assembly.
[assembly: TypeForwardedTo(typeof(CakeOS.Cui.Compiler.CuiCompilationMetadata))]
