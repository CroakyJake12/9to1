using System.Runtime.InteropServices;

namespace Haven.Infrastructure;

/// <summary>Explicit, shared runtime-asset layout. A private development bundle is selected only by
/// HAVEN_LOCAL_LLAMA_BUNDLE_ROOT; relative values are app-relative, never working-directory-relative.
/// Selecting paths does not verify assets, admit a provider, or establish runtime readiness.</summary>
public sealed record LlamaCppRuntimeLayout(string RuntimeRoot, string BundleDirectory, string SeedDirectory, string ModelCtlPath)
{
    public const string BundleRootEnvironmentVariable = "HAVEN_LOCAL_LLAMA_BUNDLE_ROOT";

    public static LlamaCppRuntimeLayout ReadExplicitEnvironment(Func<string, string?>? read = null, string? applicationDirectory = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        var selected = read(BundleRootEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(selected))
            return new(LlamaCppPreviewBootstrap.RuntimeRoot, LlamaCppPreviewBootstrap.BundleDir,
                LlamaCppPreviewBootstrap.DefaultSeedDir, OperatingSystem.IsWindows()
                    ? Path.Combine(applicationDirectory ?? AppContext.BaseDirectory, "inference", "modelctl.py")
                    : LlamaCppPreviewBootstrap.ModelCtlScriptPath);

        var root = Path.GetFullPath(selected, applicationDirectory ?? AppContext.BaseDirectory);
        return new(root, Path.Combine(root, "inference", "bundle"), Path.Combine(root, "default-models"),
            Path.Combine(root, "inference", "modelctl.py"));
    }

    /// <summary>The production composition explicitly selects x64 only on a real Linux x64 process.
    /// Direct bootstrap callers retain the separately guarded ARM64 default.</summary>
    public LlamaCppPreviewBootstrap CreateBootstrap() => new(bundleDir: BundleDirectory, runtimeRoot: RuntimeRoot,
        seedDir: SeedDirectory, modelCtlPath: ModelCtlPath,
        target: OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64
            ? LlamaCppPreviewTarget.LinuxX64 : LlamaCppPreviewTarget.LinuxArm64);
}

public enum LlamaCppPreviewTarget { LinuxArm64, LinuxX64 }
