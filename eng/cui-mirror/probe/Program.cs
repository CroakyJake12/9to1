// Compile-checks a Stage A donor mirror corpus against the real CUI compiler.
// This is a verification harness only: it compiles CUI documents and reports the compiler's own
// diagnostics. It never renders, runs, installs or accepts anything, and a clean run here is compile
// evidence, not on-screen evidence.
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

// Argument parsing is explicit so an option's value is never mistaken for an input path.
var paths = new List<string>();
bool strict = false;
int show = 0;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--strict") strict = true;
    else if (args[i] == "--show" && i + 1 < args.Length && int.TryParse(args[i + 1], out int n)) { show = n; i++; }
    else if (args[i].StartsWith("--")) { Console.Error.WriteLine($"unknown option {args[i]}"); return 2; }
    else paths.Add(args[i]);
}

if (paths.Count == 0)
{
    Console.Error.WriteLine("usage: CuiMirrorProbe <dir-or-file>... [--strict] [--show N]");
    return 2;
}

var options = new CuiCompilerOptions(new CuiCompilationMetadata(
    "1.0", "1.0", CuiControlRegistry.ComponentRegistryVersion));
var files = new List<string>();
foreach (var path in paths)
{
    if (Directory.Exists(path)) files.AddRange(Directory.EnumerateFiles(path, "*.cui", SearchOption.AllDirectories));
    else if (File.Exists(path)) files.Add(path);
    else { Console.Error.WriteLine($"MISSING INPUT: {path}"); return 2; }
}
files.Sort(StringComparer.Ordinal);

int documents = 0, clean = 0, withErrors = 0, withWarnings = 0;
var codes = new SortedDictionary<string, int>(StringComparer.Ordinal);
// Diagnostics repeat across a corpus of generated files, so count message shapes as well as codes:
// that is what tells a framework extension from an emitter bug.
var shapes = new SortedDictionary<string, int>(StringComparer.Ordinal);
var samples = new List<string>();

string ShapeOf(CuiDiagnostic diagnostic)
{
    var text = diagnostic.Message ?? string.Empty;
    var quoted = System.Text.RegularExpressions.Regex.Matches(text, "'[^']*'");
    var parts = quoted.Select(q => q.Value).ToList();
    var stem = System.Text.RegularExpressions.Regex.Replace(text, "'[^']*'", "'_'");
    stem = System.Text.RegularExpressions.Regex.Replace(stem, "[0-9]+", "#");
    return parts.Count > 1 ? $"{diagnostic.Code} {stem} [{string.Join(",", parts.Select(p => p.Trim('\'')))}]"
                           : $"{diagnostic.Code} {stem}";
}

foreach (var file in files)
{
    documents++;
    var source = File.ReadAllText(file);
    CuiCompilation result;
    try
    {
        result = new CuiCompiler().Compile(source, Path.GetFileName(file), options);
    }
    catch (Exception ex)
    {
        withErrors++;
        codes["PROBE-EXCEPTION"] = codes.GetValueOrDefault("PROBE-EXCEPTION") + 1;
        if (samples.Count < show) samples.Add($"{file}: PROBE-EXCEPTION {ex.GetType().Name}: {ex.Message}");
        continue;
    }

    var errors = result.Diagnostics.Where(d => d.Severity == CuiDiagnosticSeverity.Error).ToList();
    var warnings = result.Diagnostics.Where(d => d.Severity == CuiDiagnosticSeverity.Warning).ToList();
    if (errors.Count == 0) clean++; else { withErrors++; }
    if (warnings.Count > 0) withWarnings++;
    foreach (var diagnostic in errors.Concat(warnings))
    {
        codes[diagnostic.Code] = codes.GetValueOrDefault(diagnostic.Code) + 1;
        shapes[ShapeOf(diagnostic)] = shapes.GetValueOrDefault(ShapeOf(diagnostic)) + 1;
    }

    if (show > 0)
        foreach (var diagnostic in errors.Take(3))
            if (samples.Count < show)
                samples.Add($"{file}: {diagnostic.Code} {diagnostic.Message}");
}

Console.WriteLine($"CUI compile probe: documents={documents} without-errors={clean} " +
                  $"with-errors={withErrors} with-warnings={withWarnings} " +
                  $"registry={CuiControlRegistry.ComponentRegistryVersion}");
Console.WriteLine("diagnostic codes:");
foreach (var (code, count) in codes.OrderByDescending(kv => kv.Value))
    Console.WriteLine($"  {code,-16}{count}");
Console.WriteLine("message shapes (what to fix: emitter bug vs CUI framework gap):");
foreach (var (shape, count) in shapes.OrderByDescending(kv => kv.Value).Take(25))
    Console.WriteLine($"  {count,6}  {shape}");
foreach (var sample in samples) Console.WriteLine("  sample: " + sample);
return strict && withErrors > 0 ? 1 : 0;
