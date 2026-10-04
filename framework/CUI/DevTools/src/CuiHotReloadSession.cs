using CakeOS.Cui.Compiler;
using CakeOS.Cui.Language;

namespace Haven.CUI.DevTools;

public enum CuiHotReloadChangeKind
{
    Added,
    Removed,
    Replaced,
    Unchanged
}

public sealed record CuiHotReloadChange(string StableId, CuiHotReloadChangeKind Kind);

public sealed record CuiHotReloadResult(
    bool Applied,
    long Revision,
    CuiCompiledDocument? Current,
    IReadOnlyList<CuiDiagnostic> Diagnostics,
    IReadOnlyList<CuiHotReloadChange> Changes);

/// <summary>
/// Keeps the last valid compiled tree while edits are being compiled and computes
/// stable-identity changes for a host to apply without rebuilding unrelated nodes.
/// </summary>
public sealed class CuiHotReloadSession
{
    private readonly CuiCompiler _compiler;
    private readonly CuiCompilerOptions _options;
    private CuiCompiledDocument? _current;
    private long _revision;

    public CuiHotReloadSession(CuiCompiler compiler, CuiCompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(options);
        _compiler = compiler;
        _options = options;
    }

    public long Revision => _revision;

    public CuiCompiledDocument? Current => _current;

    public CuiHotReloadResult Update(string source, string sourceName)
    {
        var compilation = _compiler.Compile(source, sourceName, _options);
        if (!compilation.Succeeded || compilation.Output is null)
            return new CuiHotReloadResult(false, _revision, _current, compilation.Diagnostics, []);

        var next = compilation.Output;
        if (_current is not null && _current.Metadata != next.Metadata)
        {
            var diagnostic = new CuiDiagnostic("CUIH001", CuiDiagnosticSeverity.Error,
                "Hot reload cannot apply a document with different language, runtime ABI, registry, or capability metadata.",
                next.Semantics.Span);
            return new CuiHotReloadResult(false, _revision, _current,
                Array.AsReadOnly(compilation.Diagnostics.Append(diagnostic).ToArray()), []);
        }

        var changes = Diff(_current, next);
        _current = next;
        _revision = checked(_revision + 1);
        return new CuiHotReloadResult(true, _revision, _current, compilation.Diagnostics, changes);
    }

    private static IReadOnlyList<CuiHotReloadChange> Diff(CuiCompiledDocument? previous, CuiCompiledDocument next)
    {
        var oldNodes = Flatten(previous?.Components ?? []);
        var newNodes = Flatten(next.Components);
        var changes = new List<CuiHotReloadChange>();
        foreach (var (stableId, newNode) in newNodes)
        {
            if (!oldNodes.TryGetValue(stableId, out var oldNode))
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Added));
            else if (oldNode.Type != newNode.Type)
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Replaced));
            else if (!PropertiesEqual(oldNode, newNode))
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Replaced));
            else
                changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Unchanged));
        }
        foreach (var stableId in oldNodes.Keys.Except(newNodes.Keys, StringComparer.Ordinal))
            changes.Add(new CuiHotReloadChange(stableId, CuiHotReloadChangeKind.Removed));
        return Array.AsReadOnly(changes.OrderBy(change => change.StableId, StringComparer.Ordinal).ToArray());
    }

    private static Dictionary<string, CuiCompiledNode> Flatten(IEnumerable<CuiCompiledNode> roots)
    {
        var result = new Dictionary<string, CuiCompiledNode>(StringComparer.Ordinal);
        void Visit(IEnumerable<CuiCompiledNode> nodes)
        {
            foreach (var node in nodes)
            {
                result.Add(node.StableId, node);
                Visit(node.Children);
                Visit(node.ElseChildren);
            }
        }
        Visit(roots);
        return result;
    }

    private static bool PropertiesEqual(CuiCompiledNode first, CuiCompiledNode second) =>
        first.Properties.Count == second.Properties.Count
        && first.Properties.All(pair => second.Properties.TryGetValue(pair.Key, out var value)
                                        && pair.Value.LanguageType == value.LanguageType
                                        && pair.Value.Value == value.Value);
}
