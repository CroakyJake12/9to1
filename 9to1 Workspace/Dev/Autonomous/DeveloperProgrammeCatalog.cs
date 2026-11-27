using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace HavenOS.Apps.Dev.Autonomous;

public sealed record DeveloperProgrammeModuleSummary(
    string ModuleId,
    string Title,
    string Owner,
    string Priority,
    int Rank,
    int TotalRecords,
    IReadOnlyList<string> SourceCandidates,
    string? FirstActionId,
    string? FirstActionRequirement,
    string? FirstActionAcceptance);

public sealed record DeveloperProgrammeRecord(
    string RecordId,
    string ModuleId,
    string Kind,
    string Requirement,
    string? Acceptance,
    string SourceText,
    string? SourcePath,
    int? SourceLine,
    IReadOnlyList<string> RelatedIds,
    string ProgressState,
    bool IsExcludedProposal);

/// <summary>
/// Loads the approved 9to1 specification implementation programme (e.g. implementation-programme-v1
/// with 6,845 approved requirements and explicit excluded_pending_owner guards) or falls back cleanly
/// to scanning a repository's docs/specification/ directory when no programme manifest exists.
/// </summary>
public sealed class DeveloperProgrammeCatalog
{
    private readonly string _rootDirectory;
    private readonly bool _isStructuredManifest;
    private readonly Dictionary<string, DeveloperProgrammeModuleSummary> _modules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _moduleFiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _recordFiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _recordToModule = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _recordKinds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _progressStates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _excludedPendingOwner = new(StringComparer.Ordinal);
    private readonly List<string> _orderedModuleIds = [];
    private readonly Dictionary<string, List<DeveloperProgrammeRecord>> _fallbackRecords = new(StringComparer.Ordinal);

    private DeveloperProgrammeCatalog(string rootDirectory, bool isStructuredManifest)
    {
        _rootDirectory = rootDirectory;
        _isStructuredManifest = isStructuredManifest;
    }

    public string RootDirectory => _rootDirectory;
    public bool IsStructuredManifest => _isStructuredManifest;
    public int TotalApprovedRecords => _isStructuredManifest
        ? _recordToModule.Keys.Count(id => !_excludedPendingOwner.Contains(id))
        : _fallbackRecords.Values.Sum(list => list.Count);
    public int ExcludedProposalCount => _excludedPendingOwner.Count;
    public IReadOnlyList<DeveloperProgrammeModuleSummary> Modules =>
        _orderedModuleIds.Select(id => _modules[id]).ToArray();

    public static DeveloperProgrammeCatalog Load(string pathOrRepositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathOrRepositoryRoot);
        var full = Path.GetFullPath(pathOrRepositoryRoot);
        if (File.Exists(full) && Path.GetFileName(full).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            full = Path.GetDirectoryName(full)!;

        var manifestPath = Path.Combine(full, "manifest.json");
        if (File.Exists(manifestPath))
            return LoadStructuredManifest(full, manifestPath);

        var specRoot = Directory.Exists(Path.Combine(full, "docs", "specification"))
            ? Path.Combine(full, "docs", "specification")
            : full;
        if (!Directory.Exists(specRoot))
            throw new DirectoryNotFoundException($"Programme or specification directory not found: {pathOrRepositoryRoot}");

        return LoadSpecificationFallback(specRoot);
    }

    private static DeveloperProgrammeCatalog LoadStructuredManifest(string dir, string manifestPath)
    {
        var catalog = new DeveloperProgrammeCatalog(dir, isStructuredManifest: true);
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonObject
            ?? throw new InvalidDataException("Programme manifest.json must be a JSON object.");

        if (manifest["excluded_pending_owner"] is JsonArray excluded)
        {
            foreach (var item in excluded)
            {
                var id = item?.ToString();
                if (!string.IsNullOrWhiteSpace(id))
                    catalog._excludedPendingOwner.Add(id);
            }
        }

        if (manifest["modules"] is JsonObject modules)
        {
            var rawModules = new List<(string Id, DeveloperProgrammeModuleSummary Summary, string File)>();
            foreach (var (moduleId, node) in modules)
            {
                if (node is not JsonObject obj) continue;
                var candidates = obj["source_candidates"] is JsonArray arr
                    ? arr.Select(x => x?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray()
                    : Array.Empty<string>();
                var first = obj["first_action"] as JsonObject;
                var summary = new DeveloperProgrammeModuleSummary(
                    ModuleId: moduleId,
                    Title: obj["title"]?.ToString() ?? moduleId,
                    Owner: obj["owner"]?.ToString() ?? "unassigned",
                    Priority: obj["priority"]?.ToString() ?? "current",
                    Rank: int.TryParse(obj["rank"]?.ToString(), out var r) ? r : 999,
                    TotalRecords: int.TryParse(obj["count"]?.ToString(), out var c) ? c : 0,
                    SourceCandidates: candidates,
                    FirstActionId: first?["id"]?.ToString(),
                    FirstActionRequirement: first?["requirement"]?.ToString(),
                    FirstActionAcceptance: first?["acceptance"]?.ToString());
                var file = obj["file"]?.ToString() ?? $"modules/{moduleId}.json";
                rawModules.Add((moduleId, summary, file));
            }

            foreach (var entry in rawModules
                .OrderBy(x => x.Summary.Priority.Equals("current", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(x => x.Summary.Rank)
                .ThenBy(x => x.Id, StringComparer.Ordinal))
            {
                catalog._modules[entry.Id] = entry.Summary;
                catalog._moduleFiles[entry.Id] = entry.File;
                catalog._orderedModuleIds.Add(entry.Id);
            }
        }

        if (manifest["records"] is JsonObject records)
        {
            foreach (var (recordId, node) in records)
            {
                if (node is not JsonObject obj) continue;
                var ticket = obj["ticket"]?.ToString() ?? obj["module"]?.ToString() ?? "";
                var file = obj["file"]?.ToString() ?? "";
                var kind = obj["kind"]?.ToString() ?? "requirement";
                if (!string.IsNullOrWhiteSpace(ticket))
                    catalog._recordToModule[recordId] = ticket;
                if (!string.IsNullOrWhiteSpace(file))
                    catalog._recordFiles[recordId] = file;
                catalog._recordKinds[recordId] = kind;
            }
        }

        var progressPath = Path.Combine(dir, "progress.json");
        if (File.Exists(progressPath) && JsonNode.Parse(File.ReadAllText(progressPath)) is JsonObject progress
            && progress["records"] is JsonObject progressRecords)
        {
            foreach (var (recordId, node) in progressRecords)
            {
                if (node is JsonObject obj && obj["state"]?.ToString() is { Length: > 0 } state)
                    catalog._progressStates[recordId] = state;
            }
        }

        return catalog;
    }

    private static readonly Regex FallbackRequirementRegex = new(
        @"^(?:#+\s*)?((?:IX|ED|APP|STORE|UX|HOST|SPACE|BUILD|DEV)-[A-Z0-9-]+)\s+[—\-:]\s*(.+)$",
        RegexOptions.Compiled);

    private static DeveloperProgrammeCatalog LoadSpecificationFallback(string specRoot)
    {
        var catalog = new DeveloperProgrammeCatalog(specRoot, isStructuredManifest: false);
        var files = Directory.EnumerateFiles(specRoot, "*.md", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Take(250)
            .ToArray();

        var recordsByModule = new Dictionary<string, List<DeveloperProgrammeRecord>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(specRoot, file).Replace('\\', '/');
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var m = FallbackRequirementRegex.Match(lines[i].Trim());
                if (!m.Success) continue;
                var id = m.Groups[1].Value;
                var title = m.Groups[2].Value.Trim();
                var moduleId = id.Split('-').Length >= 2 ? $"{id.Split('-')[0]}-{id.Split('-')[1]}" : "SPEC";
                var excerptEnd = Math.Min(lines.Length, i + 12);
                var snippet = string.Join('\n', lines[i..excerptEnd]);
                var rec = new DeveloperProgrammeRecord(
                    RecordId: id,
                    ModuleId: moduleId,
                    Kind: "specification",
                    Requirement: title,
                    Acceptance: $"Implement and test requirement {id} strictly as specified in {rel}:{i + 1}.",
                    SourceText: snippet,
                    SourcePath: rel,
                    SourceLine: i + 1,
                    RelatedIds: [],
                    ProgressState: "UNVERIFIED_RECHECK_CURRENT_NATIVE",
                    IsExcludedProposal: false);
                if (!recordsByModule.TryGetValue(moduleId, out var list))
                {
                    list = [];
                    recordsByModule[moduleId] = list;
                }
                if (list.All(x => !x.RecordId.Equals(id, StringComparison.Ordinal)))
                    list.Add(rec);
            }
        }

        var rank = 1;
        foreach (var (moduleId, list) in recordsByModule.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            catalog._fallbackRecords[moduleId] = list;
            var first = list[0];
            catalog._modules[moduleId] = new DeveloperProgrammeModuleSummary(
                ModuleId: moduleId,
                Title: $"Specification module {moduleId}",
                Owner: "autonomous",
                Priority: "current",
                Rank: rank++,
                TotalRecords: list.Count,
                SourceCandidates: [],
                FirstActionId: first.RecordId,
                FirstActionRequirement: first.Requirement,
                FirstActionAcceptance: first.Acceptance);
            catalog._orderedModuleIds.Add(moduleId);
        }

        return catalog;
    }

    public bool IsExcludedProposal(string recordId) => _excludedPendingOwner.Contains(recordId);

    public IReadOnlyList<DeveloperProgrammeRecord> GetModuleRecords(
        string moduleId,
        bool includeControlBindings = false,
        int maxRecords = 50)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        if (!_modules.ContainsKey(moduleId))
            throw new KeyNotFoundException($"Unknown programme module '{moduleId}'.");

        if (!_isStructuredManifest)
            return _fallbackRecords.TryGetValue(moduleId, out var list) ? list.Take(maxRecords).ToArray() : [];

        var result = new List<DeveloperProgrammeRecord>();
        foreach (var rid in EnumerateStructuredRecordIds(moduleId, includeControlBindings))
        {
            var rec = TryReadRecord(rid);
            if (rec is not null)
                result.Add(rec);
            if (result.Count >= maxRecords)
                break;
        }
        return result;
    }

    private IEnumerable<string> EnumerateStructuredRecordIds(string moduleId, bool includeControlBindings)
    {
        var modPath = SafeChildPath(_rootDirectory, _moduleFiles[moduleId]);
        var modObj = JsonNode.Parse(File.ReadAllText(modPath)) as JsonObject
            ?? throw new InvalidDataException($"Invalid module file: {modPath}");

        var rowIds = new List<string>();
        if (modObj["rows"] is JsonArray rows)
        {
            foreach (var r in rows)
            {
                var id = r?.ToString();
                if (!string.IsNullOrWhiteSpace(id)) rowIds.Add(id);
            }
        }
        else if (modObj["original_queue_ticket"]?["row_ids"] is JsonArray qRows)
        {
            foreach (var r in qRows)
            {
                var id = r?.ToString();
                if (!string.IsNullOrWhiteSpace(id)) rowIds.Add(id);
            }
        }

        foreach (var rid in rowIds)
        {
            if (_excludedPendingOwner.Contains(rid))
                continue;
            if (!includeControlBindings && _recordKinds.TryGetValue(rid, out var kind)
                && kind.Equals("control-binding", StringComparison.OrdinalIgnoreCase))
                continue;
            yield return rid;
        }
    }

    public DeveloperProgrammeRecord? TryReadRecord(string recordId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);
        if (!_isStructuredManifest)
        {
            foreach (var list in _fallbackRecords.Values)
            {
                var found = list.FirstOrDefault(r => r.RecordId.Equals(recordId, StringComparison.Ordinal));
                if (found is not null) return found;
            }
            return null;
        }

        if (!_recordFiles.TryGetValue(recordId, out var relFile))
            return null;
        var full = SafeChildPath(_rootDirectory, relFile);
        if (!File.Exists(full))
            return null;

        var obj = JsonNode.Parse(File.ReadAllText(full)) as JsonObject;
        if (obj is null) return null;

        var orig = obj["original_register"] as JsonObject;
        var span = obj["source_span"] as JsonObject;
        var related = obj["related_ids"] is JsonArray relArr
            ? relArr.Select(x => x?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray()
            : Array.Empty<string>();
        var text = obj["source_text"]?.ToString();
        if (string.IsNullOrWhiteSpace(text) && obj["atlas_screen"] is { } atlas)
            text = atlas.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        var state = _progressStates.GetValueOrDefault(recordId, "UNVERIFIED_RECHECK_CURRENT_NATIVE");
        return new DeveloperProgrammeRecord(
            RecordId: recordId,
            ModuleId: _recordToModule.GetValueOrDefault(recordId, ""),
            Kind: obj["kind"]?.ToString() ?? _recordKinds.GetValueOrDefault(recordId, "requirement"),
            Requirement: orig?["requirement"]?.ToString() ?? obj["source_action"]?.ToString() ?? recordId,
            Acceptance: orig?["acceptance"]?.ToString(),
            SourceText: text ?? orig?["requirement"]?.ToString() ?? recordId,
            SourcePath: span?["path"]?.ToString(),
            SourceLine: int.TryParse(span?["line"]?.ToString(), out var ln) ? ln : null,
            RelatedIds: related,
            ProgressState: state,
            IsExcludedProposal: _excludedPendingOwner.Contains(recordId));
    }

    public (DeveloperProgrammeModuleSummary Module, DeveloperProgrammeRecord Record)? SelectNextUncoveredRequirement(
        ISet<string> completedRecordIds,
        ISet<string> blockedRecordIds,
        int startModuleCursor = 0,
        string? preferredOwner = null)
    {
        if (_orderedModuleIds.Count == 0)
            return null;

        var ordered = _orderedModuleIds.ToList();
        if (!string.IsNullOrWhiteSpace(preferredOwner))
        {
            var owned = ordered.Where(id => _modules[id].Owner.Equals(preferredOwner, StringComparison.OrdinalIgnoreCase)).ToList();
            if (owned.Count > 0)
                ordered = owned;
        }

        for (var offset = 0; offset < ordered.Count; offset++)
        {
            var modId = ordered[(Math.Max(0, startModuleCursor) + offset) % ordered.Count];
            var module = _modules[modId];
            // A bounded display preview cannot establish that an entire module is covered.
            // Traverse the complete source order lazily, stopping at the first eligible record.
            IEnumerable<DeveloperProgrammeRecord?> records = _isStructuredManifest
                ? EnumerateStructuredRecordIds(modId, includeControlBindings: false).Select(TryReadRecord)
                : _fallbackRecords.GetValueOrDefault(modId, []);
            foreach (var rec in records)
            {
                if (rec is null || rec.IsExcludedProposal) continue;
                if (completedRecordIds.Contains(rec.RecordId) || blockedRecordIds.Contains(rec.RecordId)) continue;
                if (rec.ProgressState is "VERIFIED_ACCEPTED" or "PENDING_OWNER_NOT_APPROVED") continue;
                return (module, rec);
            }
        }
        return null;
    }

    public string FormatModuleSummaryJson(string moduleId, ISet<string> completedIds, ISet<string> blockedIds)
    {
        var mod = _modules[moduleId];
        var records = GetModuleRecords(moduleId, includeControlBindings: false, maxRecords: 40);
        var payload = new
        {
            module = mod.ModuleId,
            title = mod.Title,
            owner = mod.Owner,
            priority = mod.Priority,
            source_candidates = mod.SourceCandidates,
            records = records.Select(r => new
            {
                id = r.RecordId,
                kind = r.Kind,
                requirement = r.Requirement,
                acceptance = r.Acceptance,
                state = completedIds.Contains(r.RecordId) ? "INTEGRATED_IN_MISSION"
                    : blockedIds.Contains(r.RecordId) ? "BLOCKED_IN_MISSION"
                    : r.ProgressState
            }).ToArray()
        };
        return JsonSerializer.Serialize(payload);
    }

    private static string SafeChildPath(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsafe programme relative path: {relative}");
        var fullRoot = Path.GetFullPath(root);
        var combined = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!combined.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException($"Programme path escapes root: {relative}");
        return combined;
    }
}
