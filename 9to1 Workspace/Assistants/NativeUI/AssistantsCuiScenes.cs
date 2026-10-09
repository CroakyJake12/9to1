using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace HavenOS.Apps.Assistants.NativeUI;

public enum AssistantsCuiScene { Shell, Home, Configuration, Work, Conversation, LegacyMigration, Memory, MiniComputer }

/// <summary>Authored product scenes. Services, identities and actions remain supplied by the host.</summary>
public static class AssistantsCuiScenes
{
    public static string ReadSource(AssistantsCuiScene scene)
    {
        var file = scene switch
        {
            AssistantsCuiScene.Shell => "Assistants",
            AssistantsCuiScene.Home => "AssistantsHome",
            AssistantsCuiScene.Configuration => "AssistantConfiguration",
            AssistantsCuiScene.Work => "AssistantWork",
            AssistantsCuiScene.Conversation => "AssistantConversation",
            AssistantsCuiScene.Memory => "AssistantMemory",
            AssistantsCuiScene.MiniComputer => "AssistantMiniComputer",
            AssistantsCuiScene.LegacyMigration => "AssistantLegacyMigration",
            _ => throw new ArgumentOutOfRangeException(nameof(scene))
        };
        using var stream = typeof(AssistantsCuiScenes).Assembly.GetManifestResourceStream(
            $"HavenOS.Assistants.NativeUI.UI.{file}.cui")
            ?? throw new InvalidDataException("The authored Assistants scene is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static CuiDocument ReadDocument(AssistantsCuiScene scene)
    {
        var parser = new CuiRichParser();
        var document = parser.Parse(ReadSource(scene), $"Assistants.{scene}.cui");
        if (parser.Diagnostics.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The authored Assistants scene is invalid.");
        return document;
    }
}
