using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CakeOS.Cui.Compiler;

namespace CakeOS.Cui;

/// <summary>
/// Distinct typed V2 compiled CUI artifact. It carries a full validated semantic payload per
/// document (never source text); the real consumer reconstructs the canonical typed model from it
/// without reparsing XML or invoking the compiler. Stateful documents require the stateful ABI.
/// </summary>
public sealed record CuiCompiledArtifact(
    string Format,
    int FormatVersion,
    string CompilerSha256,
    CuiCompilationMetadata Metadata,
    IReadOnlyList<CuiCompiledArtifactDocument> Documents);

/// <summary>One compiled document in a V2 artifact: its source identity, per-document metadata and
/// the full typed semantic payload plus source-map/dependency projections.</summary>
public sealed record CuiCompiledArtifactDocument(
    string Source,
    string Sha256,
    CuiCompilationMetadata Metadata,
    CuiV2Semantics Semantics,
    IReadOnlyList<CuiV2SourceMapEntry> SourceMap,
    CuiV2DependencyGraph Dependencies);

/// <summary>
/// Shared typed V2 codec. Emitters (Build.Tasks) and the real consumer agree on this wire format
/// without a dependency inversion through the compiler assembly. Admission is strict: unknown
/// formats/versions, value/node kinds, missing required fields, duplicates, malformed payloads and
/// metadata/payload contradictions are rejected before any effects.
/// </summary>
public static class CuiCompiledArtifactCodec
{
    public const string FormatName = "cui-build-compilation";
    public const int FormatVersion = 2;

    /// <summary>Distinct V2 resource identity (vs. genuine V1's "CUI.Compilation.v1").</summary>
    public const string ResourceLogicalName = "CUI.Compilation.v2";

    /// <summary>Genuine V1 resource identity, retained for the stateless compatibility path.</summary>
    public const string V1ResourceLogicalName = "CUI.Compilation.v1";

    /// <summary>Stateful runtime ABI required for V2 stateful documents.</summary>
    public const string V2RuntimeAbiVersion = "2";

    /// <summary>Minimum ABI required by genuine stateless (V1-eligible) compiled documents. This is
    /// deliberately decoupled from <see cref="V2RuntimeAbiVersion"/> and from whatever ABI the
    /// runtime advertises, so a stateful-aware runtime can advertise the higher current ABI without
    /// stamping stateless documents as stateful.</summary>
    public const string V1RuntimeAbiVersion = "1";

    /// <summary>Stateful feature capability names derived from the actual typed semantics (never from
    /// a capability string alone): a pure-local Toggle action and a choose-once RandomOnce variable.
    /// A stripped capability string cannot downgrade these genuine requirements.</summary>
    public const string LocalStateToggleCapability = "local-state-toggle";
    public const string LocalStateRandomSelectCapability = "local-state-random-select";

    /// <summary>Semantic feature capability for a bounded paged QueryViewport scope, derived from the
    /// actual component/template semantics wherever a QueryViewport container occurs. It is a genuine
    /// runtime requirement independent of local-state Variables/Toggle, so a query-only document can
    /// never evade the admission gate by omitting the capability string.</summary>
    public const string QueryViewportCapability = "query-viewport";

    /// <summary>Semantic feature capability for declarative auto-invocation effects. It is derived
    /// from the actual typed semantics (a non-empty document-root Effects block), so a stripped
    /// capability string cannot downgrade a document whose orchestration depends on effects. A host
    /// that predates effects must reject such a document at admission rather than silently dropping
    /// the authored auto-flow.</summary>
    public const string DeclarativeEffectsCapability = "declarative-effects";

    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static byte[] WriteArtifact(CuiCompiledArtifact artifact) =>
        JsonSerializer.SerializeToUtf8Bytes(artifact, Options);

    /// <summary>Decodes and hard-validates a V2 artifact. Throws <see cref="CuiV2AdmissionException"/>
    /// with a stable code on any structural or consistency failure.</summary>
    public static CuiCompiledArtifact ReadArtifact(byte[] utf8)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        // Reject duplicate exact JSON symbol keys before deserialization collapses them (the HashSet
        // admission checks below cannot detect ambiguity that was already lost). Case-distinct Ordinal
        // keys (Flag/flag) remain distinct.
        RejectDuplicateJsonKeys(utf8);
        CuiCompiledArtifact artifact;
        try
        {
            artifact = JsonSerializer.Deserialize<CuiCompiledArtifact>(utf8, Options)
                ?? throw new InvalidDataException("The compiled CUI artifact is empty.");
        }
        catch (Exception error) when (error is JsonException or NotSupportedException or InvalidDataException)
        {
            throw new CuiV2AdmissionException("CUIR070", $"The compiled CUI artifact payload is malformed: {error.Message}.");
        }

        Validate(artifact);
        return artifact;
    }

    /// <summary>Strict structural + envelope/document consistency admission.</summary>
    public static void Validate(CuiCompiledArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        if (!string.Equals(artifact.Format, FormatName, StringComparison.Ordinal))
            throw new CuiV2AdmissionException("CUIR060", $"Unsupported compiled CUI artifact format '{artifact.Format}'.");
        if (artifact.FormatVersion != FormatVersion)
            throw new CuiV2AdmissionException("CUIR061", $"Unsupported compiled CUI artifact format version {artifact.FormatVersion}; expected V2 ({FormatVersion}).");
        if (artifact.Documents is null)
            throw new CuiV2AdmissionException("CUIR072", "The compiled CUI artifact has no document list.");

        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        string? maxAbi = null;
        var envelopeCaps = new HashSet<string>(StringComparer.Ordinal);
        string? firstLanguage = null;
        string? firstRegistry = null;

        foreach (var document in artifact.Documents)
        {
            if (document is null)
                throw new CuiV2AdmissionException("CUIR072", "The compiled CUI artifact contains a null document.");
            if (string.IsNullOrWhiteSpace(document.Source))
                throw new CuiV2AdmissionException("CUIR072", "A compiled document is missing its source identity.");
            if (string.IsNullOrWhiteSpace(document.Sha256))
                throw new CuiV2AdmissionException("CUIR072", $"Compiled document '{document.Source}' is missing its source hash.");
            if (document.Metadata is null)
                throw new CuiV2AdmissionException("CUIR072", $"Compiled document '{document.Source}' is missing its compatibility metadata.");
            if (!sourceIds.Add(document.Source))
                throw new CuiV2AdmissionException("CUIR073", $"Duplicate compiled document source identity '{document.Source}'.");

            if (document.Semantics is null)
                throw new CuiV2AdmissionException("CUIR072", $"Compiled document '{document.Source}' has no semantic payload.");
            ValidateSemantics(document.Semantics, document.Source);

            if (document.SourceMap is null)
                throw new CuiV2AdmissionException("CUIR072", $"Compiled document '{document.Source}' has no source-map projection.");
            foreach (var entry in document.SourceMap)
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.StableId))
                    throw new CuiV2AdmissionException("CUIR072",
                        $"Compiled document '{document.Source}' has an invalid source-map entry.");
                if (entry.Source is null)
                    throw new CuiV2AdmissionException("CUIR072",
                        $"Compiled document '{document.Source}' has a source-map entry without a source span.");
            }

            if (document.Dependencies is null)
                throw new CuiV2AdmissionException("CUIR072", $"Compiled document '{document.Source}' has no dependency graph.");
            if (document.Dependencies.Entries is null)
                throw new CuiV2AdmissionException("CUIR072", $"Compiled document '{document.Source}' has a null dependency entry list.");
            foreach (var dependency in document.Dependencies.Entries)
            {
                if (dependency is null || string.IsNullOrWhiteSpace(dependency.StableId)
                    || string.IsNullOrWhiteSpace(dependency.Name)
                    || !ValidDependencyKinds.Contains(dependency.Kind))
                    throw new CuiV2AdmissionException("CUIR071",
                        $"Compiled document '{document.Source}' carries an unsupported or malformed dependency kind '{dependency?.Kind}'.");
            }

            if (firstLanguage is null)
            {
                firstLanguage = document.Metadata.LanguageVersion;
                firstRegistry = document.Metadata.ComponentRegistryVersion;
            }
            else
            {
                if (!string.Equals(firstLanguage, document.Metadata.LanguageVersion, StringComparison.Ordinal))
                    throw new CuiV2AdmissionException("CUIR075",
                        $"Compiled document '{document.Source}' language version '{document.Metadata.LanguageVersion}' disagrees with sibling '{firstLanguage}'.");
                if (!string.Equals(firstRegistry, document.Metadata.ComponentRegistryVersion, StringComparison.Ordinal))
                    throw new CuiV2AdmissionException("CUIR075",
                        $"Compiled document '{document.Source}' component registry '{document.Metadata.ComponentRegistryVersion}' disagrees with sibling '{firstRegistry}'.");
            }

            var stateful = RequiresAbi2(document.Semantics);
            if (stateful)
            {
                if (!string.Equals(document.Metadata.RuntimeAbiVersion, V2RuntimeAbiVersion, StringComparison.Ordinal))
                    throw new CuiV2AdmissionException("CUIR074",
                        $"Compiled document '{document.Source}' carries stateful typed semantics but declares ABI '{document.Metadata.RuntimeAbiVersion}'; stateful documents require ABI {V2RuntimeAbiVersion}.");
            }
            // Required capabilities are checked independently of statefulness: a query-only document
            // (QueryViewport scope, no local-state Variables/Toggle) must still declare the derived
            // query capability or be rejected before any effect.
            foreach (var capability in DeriveRequiredCapabilities(document.Semantics))
                if (!CapsOrEmpty(document.Metadata).Contains(capability))
                    throw new CuiV2AdmissionException("CUIR076",
                        $"Compiled document '{document.Source}' performs required feature '{capability}' but does not declare it; a stripped capability string cannot downgrade it.");

            maxAbi = MaxAbi(maxAbi, document.Metadata.RuntimeAbiVersion);
            envelopeCaps.UnionWith(CapsOrEmpty(document.Metadata));
        }

        if (artifact.Metadata is null)
            throw new CuiV2AdmissionException("CUIR072", "The compiled CUI artifact is missing envelope metadata.");
        var expectedMaxAbi = maxAbi ?? artifact.Metadata.RuntimeAbiVersion;
        if (!string.Equals(artifact.Metadata.RuntimeAbiVersion, expectedMaxAbi, StringComparison.Ordinal))
            throw new CuiV2AdmissionException("CUIR075",
                $"The artifact envelope ABI '{artifact.Metadata.RuntimeAbiVersion}' does not equal the maximum document ABI '{expectedMaxAbi}'.");
        if (!CapsOrEmpty(artifact.Metadata).ToHashSet(StringComparer.Ordinal).SetEquals(envelopeCaps))
            throw new CuiV2AdmissionException("CUIR075",
                "The artifact envelope capability set does not equal the union of document requirements.");
        if (firstLanguage is not null && !string.Equals(firstLanguage, artifact.Metadata.LanguageVersion, StringComparison.Ordinal))
            throw new CuiV2AdmissionException("CUIR075",
                $"The artifact envelope language version '{artifact.Metadata.LanguageVersion}' does not match its documents ('{firstLanguage}').");
        if (firstRegistry is not null && !string.Equals(firstRegistry, artifact.Metadata.ComponentRegistryVersion, StringComparison.Ordinal))
            throw new CuiV2AdmissionException("CUIR075",
                $"The artifact envelope component registry version '{artifact.Metadata.ComponentRegistryVersion}' does not match its documents ('{firstRegistry}').");
    }

    /// <summary>Statefulness is determined by inspecting typed semantics (document-root local
    /// variables, pure-local actions, or declarative auto-invocation effects), independent of any
    /// capability string.</summary>
    public static bool RequiresAbi2(CuiV2Semantics semantics)
    {
        ArgumentNullException.ThrowIfNull(semantics);
        return (semantics.Variables is { Count: > 0 })
            || (semantics.Actions is not null && semantics.Actions.Values.Any(action => action.LocalTarget is not null))
            || (semantics.Effects is { Count: > 0 });
    }

    /// <summary>Derives the mandatory feature capabilities required by the actual typed semantics
    /// (a pure-local Toggle targets declared local state; a choose-once RandomOnce variable selects
    /// from a source pool once; a QueryViewport scope performs bounded paged query/pagination).
    /// These genuine requirements can never be waived by stripping capability strings from the
    /// payload.</summary>
    private static HashSet<string> DeriveRequiredCapabilities(CuiV2Semantics semantics)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        if (semantics.Actions is not null)
            foreach (var action in semantics.Actions.Values)
                if (action?.LocalTarget is not null)
                    required.Add(LocalStateToggleCapability);
        if (semantics.Variables is not null)
            foreach (var variable in semantics.Variables.Values)
                if (variable is not null && !string.IsNullOrEmpty(variable.Selection))
                    required.Add(LocalStateRandomSelectCapability);
        if (ContainsQueryViewport(semantics))
            required.Add(QueryViewportCapability);
        if (semantics.Effects is { Count: > 0 })
            required.Add(DeclarativeEffectsCapability);
        return required;
    }

    /// <summary>True if any component/template route in the typed semantics (component tree,
    /// definition bodies, template content, else-children/closure) contains a QueryViewport scope.</summary>
    private static bool ContainsQueryViewport(CuiV2Semantics semantics)
    {
        if (semantics.Components is not null)
            foreach (var component in semantics.Components)
                if (ComponentUsesQueryViewport(component))
                    return true;
        if (semantics.Templates is not null)
            foreach (var template in semantics.Templates.Values)
                if (template?.Content is not null)
                    foreach (var component in template.Content)
                        if (ComponentUsesQueryViewport(component))
                            return true;
        return false;
    }

    /// <summary>Recursively tests a typed V2 component (and its children/else-children closure) for
    /// a QueryViewport container: either a component whose native type is QueryViewport, or a
    /// Container whose authored Type literal is QueryViewport. List/Repeat template bodies live in
    /// the semantics template table and are covered separately.</summary>
    private static bool ComponentUsesQueryViewport(CuiV2Component component)
    {
        if (component is null) return false;
        if (string.Equals(component.Type, "QueryViewport", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(component.Type, "Container", StringComparison.OrdinalIgnoreCase)
            && component.Properties is not null)
        {
            var typeValue = component.Properties
                .FirstOrDefault(pair => pair.Key.Equals("Type", StringComparison.OrdinalIgnoreCase)).Value;
            if (typeValue is not null
                && string.Equals(typeValue.Kind, CuiV2ValueKind.Literal, StringComparison.Ordinal)
                && string.Equals(typeValue.LiteralValue, "QueryViewport", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        if (component.Children is not null)
            foreach (var child in component.Children)
                if (ComponentUsesQueryViewport(child))
                    return true;
        if (component.ElseChildren is not null)
            foreach (var child in component.ElseChildren)
                if (ComponentUsesQueryViewport(child))
                    return true;
        return false;
    }

    private static IReadOnlyList<string> CapsOrEmpty(CuiCompilationMetadata metadata) =>
        metadata.RequiredCapabilities ?? Array.Empty<string>();

    /// <summary>Rejects duplicate exact JSON object keys before System.Text.Json deserialization
    /// collapses them (for example two sibling symbol definitions with the same key, or duplicate
    /// identity fields). Case-distinct Ordinal keys remain distinct and are preserved.</summary>
    private static void RejectDuplicateJsonKeys(byte[] utf8)
    {
        try
        {
            using var document = JsonDocument.Parse(utf8,
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
            Walk(document.RootElement);
        }
        catch (JsonException)
        {
            // Malformed JSON is reported as CUIR070 during deserialization in ReadArtifact.
        }

        static void Walk(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!seen.Add(property.Name))
                        throw new CuiV2AdmissionException("CUIR073", $"Duplicate JSON object key '{property.Name}'.");
                    Walk(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray()) Walk(item);
            }
        }
    }

    private static readonly HashSet<string> ValidDependencyKinds = new(StringComparer.Ordinal)
    {
        CuiV2DependencyKind.Definition,
        CuiV2DependencyKind.Template,
        CuiV2DependencyKind.Resource,
        CuiV2DependencyKind.Variable,
        CuiV2DependencyKind.Action,
        CuiV2DependencyKind.Import,
    };

    private static string? MaxAbi(string? current, string candidate)
    {
        if (current is null) return candidate;
        if (int.TryParse(current, out var a) && int.TryParse(candidate, out var b))
            return b > a ? candidate : current;
        return string.Compare(candidate, current, StringComparison.Ordinal) > 0 ? candidate : current;
    }

    private static void ValidateSemantics(CuiV2Semantics semantics, string source)
    {
        if (string.IsNullOrWhiteSpace(semantics.SourceName))
            throw new CuiV2AdmissionException("CUIR072", $"Document '{source}' has no source name.");
        if (semantics.Span is null)
            throw new CuiV2AdmissionException("CUIR072", $"Document '{source}' is missing its root source span.");
        if (semantics.Components is null)
            throw new CuiV2AdmissionException("CUIR072", $"Document '{source}' has no component tree.");
        if (semantics.Resources is null || semantics.Actions is null || semantics.Templates is null
            || semantics.RootProperties is null || semantics.Imports is null || semantics.Variables is null
            || semantics.Styles is null)
            throw new CuiV2AdmissionException("CUIR072", $"Document '{source}' is missing a required semantic collection.");

        foreach (var import in semantics.Imports)
        {
            if (import is null || string.IsNullOrWhiteSpace(import.Source))
                throw new CuiV2AdmissionException("CUIR072", $"Document '{source}' has a null/unsourced import.");
            if (import.Span is null)
                throw new CuiV2AdmissionException("CUIR072", $"Import '{import.Source}' in '{source}' is missing its source span.");
        }

        foreach (var style in semantics.Styles) ValidateStyle(style, source);

        var variableTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, variable) in semantics.Variables)
            if (variable is not null && !string.IsNullOrWhiteSpace(name))
                variableTypes[name] = variable.Type ?? string.Empty;

        var actionNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, action) in semantics.Actions)
        {
            if (string.IsNullOrWhiteSpace(name) || action is null)
                throw new CuiV2AdmissionException("CUIR072", $"Document '{source}' has a null/unnamed action.");
            if (!actionNames.Add(name))
                throw new CuiV2AdmissionException("CUIR073", $"Document '{source}' declares duplicate action '{name}'.");
            if (!string.Equals(action.Name, name, StringComparison.Ordinal))
                throw new CuiV2AdmissionException("CUIR073", $"Document '{source}' action key '{name}' disagrees with its identity '{action.Name}'.");
            if (action.Span is null)
                throw new CuiV2AdmissionException("CUIR072", $"Action '{name}' in '{source}' is missing its source span.");
            if (action.Parameter is { } parameter) ValidateValue(parameter, source);
            if (action.ResultTarget is { } resultTarget)
            {
                if (!variableTypes.TryGetValue(resultTarget, out var resultType)
                    || !string.Equals(resultType, "commandstate", StringComparison.OrdinalIgnoreCase))
                    throw new CuiV2AdmissionException("CUIR073",
                        $"Action '{name}' in '{source}' references result target '{resultTarget}', which is not a declared commandstate variable.");
            }
            if (action.Sets is not null)
            {
                foreach (var set in action.Sets)
                {
                    if (set is null || string.IsNullOrWhiteSpace(set.Target))
                        throw new CuiV2AdmissionException("CUIR072",
                            $"Action '{name}' in '{source}' has a null/unnamed Set target.");
                    if (!variableTypes.TryGetValue(set.Target, out var setType)
                        || (!string.Equals(setType, "bool", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(setType, "boolean", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(setType, "string", StringComparison.OrdinalIgnoreCase)))
                        throw new CuiV2AdmissionException("CUIR073",
                            $"Action '{name}' in '{source}' Set target '{set.Target}' is not a declared settable bool/string variable.");
                    if (set.Value is not null) ValidateValue(set.Value, source);
                    if (set.When is { } when) ValidateValue(when.Test, source);
                }
            }
        }

        var variableNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, variable) in semantics.Variables)
        {
            if (string.IsNullOrWhiteSpace(name) || variable is null)
                throw new CuiV2AdmissionException("CUIR072", $"Document '{source}' has a null/unnamed variable.");
            if (!variableNames.Add(name))
                throw new CuiV2AdmissionException("CUIR073", $"Document '{source}' declares duplicate variable '{name}'.");
            if (!string.Equals(variable.Name, name, StringComparison.Ordinal))
                throw new CuiV2AdmissionException("CUIR073", $"Document '{source}' variable key '{name}' disagrees with its identity '{variable.Name}'.");
            if (variable.Span is null)
                throw new CuiV2AdmissionException("CUIR072", $"Variable '{name}' in '{source}' is missing its source span.");
            variableTypes[name] = variable.Type ?? string.Empty;
        }

        var resourceKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, resource) in semantics.Resources)
        {
            if (string.IsNullOrWhiteSpace(key) || resource is null)
                throw new CuiV2AdmissionException("CUIR072", $"Document '{source}' has a null/unnamed resource.");
            if (!resourceKeys.Add(key))
                throw new CuiV2AdmissionException("CUIR073", $"Document '{source}' declares duplicate resource '{key}'.");
            if (!string.Equals(resource.Key, key, StringComparison.Ordinal))
                throw new CuiV2AdmissionException("CUIR073", $"Document '{source}' resource key '{key}' disagrees with its identity '{resource.Key}'.");
            ValidateValue(resource.Value, source);
            if (resource.Span is null)
                throw new CuiV2AdmissionException("CUIR072",
                    $"Resource '{key}' in '{source}' is missing its source span.");
        }

        var templateNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, template) in semantics.Templates)
        {
            if (string.IsNullOrWhiteSpace(name) || template is null)
                throw new CuiV2AdmissionException("CUIR072", $"Document '{source}' has a null/unnamed template.");
            if (!templateNames.Add(name))
                throw new CuiV2AdmissionException("CUIR073", $"Document '{source}' declares duplicate template '{name}'.");
            if (!string.Equals(template.Name, name, StringComparison.Ordinal))
                throw new CuiV2AdmissionException("CUIR073", $"Document '{source}' template key '{name}' disagrees with its identity '{template.Name}'.");
            if (template.Parameters is null || template.Content is null)
                throw new CuiV2AdmissionException("CUIR072", $"Template '{name}' in '{source}' is missing parameters/content.");
            if (template.Span is null)
                throw new CuiV2AdmissionException("CUIR072", $"Template '{name}' in '{source}' is missing its source span.");
            foreach (var component in template.Content) ValidateComponent(component, source);
        }

        if (semantics.Effects is not null)
        {
            foreach (var effect in semantics.Effects)
            {
                if (effect is null || string.IsNullOrWhiteSpace(effect.Name))
                    throw new CuiV2AdmissionException("CUIR072", $"Document '{source}' has a null/unnamed effect.");
                if (string.IsNullOrWhiteSpace(effect.ActionName))
                    throw new CuiV2AdmissionException("CUIR072", $"Effect '{effect.Name}' in '{source}' references no action.");
                if (!actionNames.Contains(effect.ActionName))
                    throw new CuiV2AdmissionException("CUIR073", $"Document '{source}' effect '{effect.Name}' references undeclared action '{effect.ActionName}'.");
                if (effect.Span is null)
                    throw new CuiV2AdmissionException("CUIR072", $"Effect '{effect.Name}' in '{source}' is missing its source span.");
                if (effect.Condition is null)
                    throw new CuiV2AdmissionException("CUIR072", $"Effect '{effect.Name}' in '{source}' is missing its condition.");
                else
                {
                    ValidateValue(effect.Condition.Test, source);
                    if (effect.Condition.Span is null)
                        throw new CuiV2AdmissionException("CUIR072",
                            $"Condition on effect '{effect.Name}' in '{source}' is missing its source span.");
                    if (effect.Condition.Test.Kind == CuiV2ValueKind.Binding
                        && effect.Condition.Test.BindingPath is { } conditionPath)
                    {
                        var root = conditionPath.Split('.')[0];
                        if (!variableTypes.TryGetValue(root, out var conditionRootType)
                            || !string.Equals(conditionRootType, "commandstate", StringComparison.OrdinalIgnoreCase))
                            throw new CuiV2AdmissionException("CUIR073",
                                $"Effect '{effect.Name}' in '{source}' condition path '{conditionPath}' does not reference a declared commandstate result variable.");
                    }
                }
            }
        }

        foreach (var component in semantics.Components) ValidateComponent(component, source);
        foreach (var (_, value) in semantics.RootProperties) ValidateValue(value, source);
    }

    private static void ValidateComponent(CuiV2Component component, string source)
    {
        if (component is null)
            throw new CuiV2AdmissionException("CUIR072", $"Document '{source}' contains a null component.");
        if (string.IsNullOrWhiteSpace(component.Type))
            throw new CuiV2AdmissionException("CUIR072", $"Document '{source}' contains a component without a type.");
        if (component.Span is null)
            throw new CuiV2AdmissionException("CUIR072",
                $"Component '{component.Type}' in '{source}' is missing its source span.");
        if (component.Classes is null || component.Properties is null || component.Actions is null
            || component.Children is null || component.ElseChildren is null || component.TextParts is null)
            throw new CuiV2AdmissionException("CUIR072",
                $"Component '{component.Type}' in '{source}' is missing a required collection.");

        foreach (var (_, value) in component.Properties) ValidateValue(value, source);
        foreach (var (_, actionRef) in component.Actions)
        {
            if (string.IsNullOrWhiteSpace(actionRef?.Name))
                throw new CuiV2AdmissionException("CUIR072",
                    $"Component '{component.Type}' in '{source}' has a null/unnamed action reference.");
            if (actionRef!.Span is null)
                throw new CuiV2AdmissionException("CUIR072",
                    $"Action reference '{actionRef.Name}' on '{component.Type}' in '{source}' is missing its source span.");
        }
        if (component.Condition is { } condition)
        {
            ValidateValue(condition.Test, source);
            if (condition.Span is null)
                throw new CuiV2AdmissionException("CUIR072",
                    $"Condition on '{component.Type}' in '{source}' is missing its source span.");
        }
        if (component.Repeat is { } repeat)
        {
            ValidateValue(repeat.Source, source);
            ValidateValue(repeat.Key, source);
            if (repeat.Span is null)
                throw new CuiV2AdmissionException("CUIR072",
                    $"Repeat on '{component.Type}' in '{source}' is missing its source span.");
            if (repeat.Template is { } template) ValidateValue(template, source);
        }
        if (component.List is { } list)
        {
            ValidateValue(list.Items, source);
            if (list.Span is null)
                throw new CuiV2AdmissionException("CUIR072",
                    $"List on '{component.Type}' in '{source}' is missing its source span.");
        }
        if (component.PropertyRegion is { } region)
        {
            ValidateValue(region.Value, source);
            if (region.Span is null)
                throw new CuiV2AdmissionException("CUIR072",
                    $"Property region on '{component.Type}' in '{source}' is missing its source span.");
        }
        foreach (var part in component.TextParts)
        {
            if (part is null || string.IsNullOrWhiteSpace(part.Kind))
                throw new CuiV2AdmissionException("CUIR071",
                    $"Component '{component.Type}' in '{source}' has a null/unknown text part.");
            if (part.Span is null)
                throw new CuiV2AdmissionException("CUIR072",
                    $"Text part on '{component.Type}' in '{source}' is missing its source span.");
            if (part.Kind == CuiV2TextPartKind.Expression && part.ExpressionValue is { } expressionValue)
                ValidateValue(expressionValue, source);
            else if (part.Kind != CuiV2TextPartKind.Literal)
                throw new CuiV2AdmissionException("CUIR071",
                    $"Component '{component.Type}' in '{source}' has unsupported text part kind '{part.Kind}'.");
        }
        foreach (var child in component.Children) ValidateComponent(child, source);
        foreach (var child in component.ElseChildren) ValidateComponent(child, source);
    }

    private static void ValidateStyle(CuiV2Style style, string source)
    {
        if (style is null)
            throw new CuiV2AdmissionException("CUIR072", $"Document '{source}' contains a null style.");
        if (string.IsNullOrWhiteSpace(style.Selector) || style.Setters is null)
            throw new CuiV2AdmissionException("CUIR072",
                $"A style in '{source}' is missing its selector or setter list.");
        if (style.Span is null)
            throw new CuiV2AdmissionException("CUIR072", $"Style '{style.Selector}' in '{source}' is missing its source span.");
        foreach (var setter in style.Setters)
        {
            if (setter is null || string.IsNullOrWhiteSpace(setter.Property) || setter.Span is null)
                throw new CuiV2AdmissionException("CUIR072",
                    $"Style '{style.Selector}' in '{source}' has an invalid setter.");
            ValidateValue(setter.Value, source);
        }
    }

    private static void ValidateValue(CuiV2Value value, string source)
    {
        if (value is null)
            throw new CuiV2AdmissionException("CUIR072", $"Document '{source}' contains a null value.");
        if (value.Span is null)
            throw new CuiV2AdmissionException("CUIR072", $"A value in '{source}' is missing its source span.");
        switch (value.Kind)
        {
            case CuiV2ValueKind.Literal:
                if (value.LiteralValue is null)
                    throw new CuiV2AdmissionException("CUIR072", $"A literal value in '{source}' is missing its string.");
                break;
            case CuiV2ValueKind.Binding:
                if (value.BindingPath is null || value.BindingMode is null)
                    throw new CuiV2AdmissionException("CUIR072", $"A binding value in '{source}' is missing its path/mode.");
                if (!Enum.IsDefined(typeof(CuiBindingMode), value.BindingMode.Value))
                    throw new CuiV2AdmissionException("CUIR071", $"Unknown CUI binding mode {value.BindingMode} in '{source}'.");
                break;
            case CuiV2ValueKind.Resource:
                if (string.IsNullOrWhiteSpace(value.ResourceKey))
                    throw new CuiV2AdmissionException("CUIR072", $"A resource value in '{source}' is missing its key.");
                break;
            case CuiV2ValueKind.Invalid:
                if (value.InvalidCode is null || value.InvalidRaw is null)
                    throw new CuiV2AdmissionException("CUIR072", $"An invalid value in '{source}' is missing its payload.");
                break;
            default:
                throw new CuiV2AdmissionException("CUIR071", $"Unsupported CUI value kind '{value.Kind}' in '{source}'.");
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new CuiCompilationMetadataConverter());
        return options;
    }

    /// <summary>Manual converter for <see cref="CuiCompilationMetadata"/>: its record constructor
    /// parameter (IEnumerable&lt;string&gt;) intentionally differs from the property type
    /// (IReadOnlyList&lt;string&gt;), which System.Text.Json's parameterized-constructor binding
    /// may reject. Reading explicitly keeps the shared contract simple and robust.</summary>
    private sealed class CuiCompilationMetadataConverter : JsonConverter<CuiCompilationMetadata>
    {
        public override CuiCompilationMetadata Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            string Language(string name) => root.TryGetProperty(name, out var el) ? el.GetString() ?? "" : "";
            var capabilities = new List<string>();
            if (root.TryGetProperty("RequiredCapabilities", out var capsEl) && capsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in capsEl.EnumerateArray())
                    capabilities.Add(item.GetString() ?? "");
            }
            return new CuiCompilationMetadata(
                Language("LanguageVersion"), Language("RuntimeAbiVersion"), Language("ComponentRegistryVersion"), capabilities);
        }

        public override void Write(Utf8JsonWriter writer, CuiCompilationMetadata value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("LanguageVersion", value.LanguageVersion);
            writer.WriteString("RuntimeAbiVersion", value.RuntimeAbiVersion);
            writer.WriteString("ComponentRegistryVersion", value.ComponentRegistryVersion);
            writer.WriteStartArray("RequiredCapabilities");
            foreach (var capability in value.RequiredCapabilities) writer.WriteStringValue(capability);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }
}
