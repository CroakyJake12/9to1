using System.Reflection;
using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace HavenOS.Apps.Spaces.Chat;

public static class ChatSpaceCuiDocument
{
    public const string ResourceName = "HavenOS.Apps.Spaces.UI.Chat.ChatSpace.cui";

    public static CuiDocument Load()
    {
        var assembly = typeof(ChatSpaceCuiDocument).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded Chat Space CUI resource '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "ChatSpace.cui");
        if (parser.Diagnostics.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidOperationException("The canonical Spaces interface could not be parsed: " +
                string.Join(Environment.NewLine, parser.Diagnostics.Diagnostics));
        return document;
    }

    public static CuiComponent FindById(CuiDocument document, string id)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return DescendantsAndSelf(document.Components)
            .Single(element => element.Name == id);
    }

    public static IReadOnlyList<CuiComponent> FindByAction(CuiDocument document, string action)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        return DescendantsAndSelf(document.Components)
            .Where(element => element.TryGetLiteralAttribute("action", out var value) && value == action)
            .ToArray();
    }

    public static IEnumerable<CuiComponent> DescendantsAndSelf(IReadOnlyList<CuiComponent> roots)
    {
        return roots.SelectMany(root => root.DescendantsAndSelf());
    }
}
