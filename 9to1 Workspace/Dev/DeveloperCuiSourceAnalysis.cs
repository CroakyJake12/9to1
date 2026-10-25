using System.Security.Cryptography;
using System.Text;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using Haven.Core;

namespace HavenOS.Apps.Dev;

public sealed record DeveloperCuiSourceElement(string Name, string Type, int Depth, CodeRange Range);
public sealed record DeveloperCuiSourceObservation(Guid Id, string RelativePath, string ContentSha256,
    IReadOnlyList<CodeDiagnostic> Problems, IReadOnlyList<DeveloperCuiSourceElement> Outline, bool HasMoreElements);

/// <summary>Authoring analysis of text already owned by the editor. Uses the authoritative CUI
/// parser and action-event contract, never regex language semantics. Does not resolve imports,
/// access source files, render content, start a language server or claim a successful build.</summary>
public static class DeveloperCuiSourceAnalysis
{
    public const int MaximumAnalysisCharacters = 256_000;
    public const int MaximumOutlineElements = 500;
    public static bool Supports(string path, string text) => CuiProjectItems.IsCuiSourceFamily(path) &&
        text.Length <= MaximumAnalysisCharacters;

    public static DeveloperCuiSourceObservation Analyze(string relativePath, string currentText, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath); ArgumentNullException.ThrowIfNull(currentText);
        token.ThrowIfCancellationRequested();
        if (!CuiProjectItems.IsCuiSourceFamily(relativePath))
            throw new NotSupportedException("Draft syntax checks support .cui, .cui-theme and .cui-prefab. Other languages need their configured language provider.");
        if (currentText.Length > MaximumAnalysisCharacters)
            throw new NotSupportedException("This draft is in large-file mode. CUI draft checks accept up to 256,000 characters; use the project's build for complete diagnostics.");
        var parser = new CuiRichParser();
        var document = parser.Parse(currentText, relativePath);
        token.ThrowIfCancellationRequested();
        var problems = parser.Diagnostics.Diagnostics.Concat(CuiActionEventContract.Validate(document)).Select(diagnostic =>
            new CodeDiagnostic(relativePath, Range(diagnostic.Span), diagnostic.Severity switch
            {
                CuiDiagnosticSeverity.Error => CodeDiagnosticSeverity.Error,
                CuiDiagnosticSeverity.Warning => CodeDiagnosticSeverity.Warning,
                _ => CodeDiagnosticSeverity.Information
            }, diagnostic.Code, "CUI draft", diagnostic.Message)).ToArray();
        var elements = new List<DeveloperCuiSourceElement>(); var more = false;
        Visit(document.Components, 0);
        foreach (var template in document.Templates.Values) Visit(template.Content, 0);
        token.ThrowIfCancellationRequested();
        return new(Guid.NewGuid(), relativePath, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(currentText))).ToLowerInvariant(),
            Array.AsReadOnly(problems), Array.AsReadOnly(elements.ToArray()), more);

        void Visit(IEnumerable<CuiComponent> components, int depth)
        {
            foreach (var component in components)
            {
                token.ThrowIfCancellationRequested();
                if (elements.Count >= MaximumOutlineElements) { more = true; return; }
                elements.Add(new(component.Name ?? component.Type, component.Type, depth, Range(component.Span)));
                Visit(component.Children, depth + 1); Visit(component.ElseChildren, depth + 1);
            }
        }
    }

    private static CodeRange Range(CuiSourceSpan span) => new(new(span.Start.Line - 1, span.Start.Column - 1),
        new(span.End.Line - 1, span.End.Column - 1));
}
