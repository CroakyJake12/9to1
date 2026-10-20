using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using NineToOne.Cui.AI;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>
/// PROPOSED production wiring (host layer) — not part of framework/CUI/AI.
///
/// Loads the packaged authored AI access selector CUI from the AI assembly's embedded manifest
/// resource, using the same real resource identity convention as
/// <see cref="FloatingAiBarScene"/> (the existing <c>&lt;EmbeddedResource Include="UI\**\*.cui"/&gt;</c>
/// wildcard packages <c>UI/AiAccessSelector.cui</c> as
/// <c>NineToOne.Cui.AI.UI.AiAccessSelector.cui</c>).
/// </summary>
public static class AiAccessSelectorScene
{
    public const string ResourceName = "NineToOne.Cui.AI.UI.AiAccessSelector.cui";

    public static string ReadSource()
    {
        using var stream = typeof(AppAiAccessSelector).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The authored shared AI access selector is not packaged.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Parses the packaged selector once. Throws InvalidDataException when invalid.</summary>
    public static CuiDocument Load() => new CuiDocumentSource(ReadSource(), ResourceName).Document;
}
