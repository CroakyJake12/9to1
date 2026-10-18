using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace CakeOS.Cui.Build.Tasks;

/// <summary>
/// MSBuild task that processes .cui resource files into an embedded resource stream.
/// Replaces Avalonia's GenerateAvaloniaResourcesTask for CUI markup. Duplicate logical
/// resource identities are rejected with an error before the output bundle is replaced,
/// so an existing bundle is never overwritten with an ambiguous one.
/// </summary>
public class GenerateCuiResourcesTask : Task
{
    [Required]
    public string Output { get; set; } = "";

    [Required]
    public string Root { get; set; } = "";

    [Required]
    public ITaskItem[] Resources { get; set; } = [];

    public string ReportImportance { get; set; } = "low";

    public override bool Execute()
    {
        var log = new MsBuildResourceLog(Log);
        var entries = CuiResourceBundle.CollectSources(
            Root,
            Resources.Select(item => new CuiResourceSource(item.GetMetadata("FullPath"),
                string.IsNullOrEmpty(item.GetMetadata("CuiOriginalFullPath")) ? item.GetMetadata("FullPath") : item.GetMetadata("CuiOriginalFullPath"))),
            log);

        if (log.HasErrors) return false;

        CuiResourceBundle.Write(Output, entries, log);
        return true;
    }
}

/// <summary>
/// Legacy task entrypoint retained for binary compatibility. It reports the required
/// canonical build-target migration instead of pretending to compile an assembly.
/// </summary>
public class CompileCuiMarkupTask : Task
{
    [Required]
    public string AssemblyFile { get; set; } = "";

    [Required]
    public string[] References { get; set; } = [];

    public string RefAssemblyFile { get; set; } = "";

    public string ProjectDirectory { get; set; } = "";

    public bool SkipXamlCompilation { get; set; }

    public override bool Execute()
    {
        // Preserve the old task's binary/API shape for existing project imports, but
        // never equate an assembly's existence (or a donor skip flag) with compilation.
        Log.LogError("CUI", "CUIB012", null, ProjectDirectory, 0, 0, 0, 0,
            "The legacy CUI post-compilation task cannot validate application source. " +
            "Import CakeOS.Cui.targets and run CompileCuiSources with the canonical .NET 10 toolchain.");
        return false;
    }
}
