using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace HavenOS.Apps.Spaces;

public static class ChatWorkspaceCuiDocument
{
    public static CuiDocument Load()
    {
        using var stream = typeof(ChatWorkspaceCuiDocument).Assembly.GetManifestResourceStream("HavenOS.Apps.Spaces.UI.Chat.ChatWorkspace.cui")
            ?? throw new InvalidDataException("The canonical Chat workspace resource is missing.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "ChatWorkspace.cui");
        if (parser.Diagnostics.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The canonical Chat workspace has syntax errors.");
        return document;
    }
}
