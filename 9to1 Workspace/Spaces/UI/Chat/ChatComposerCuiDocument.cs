using System.Reflection;
using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace HavenOS.Apps.Spaces.Chat;

/// <summary>
/// Loads the single shared canonical Chat composer component. This is the one composer source
/// used by both consumers: the newer ChatSpace CUI surface and the Haven Chat native scene.
/// It lives in the Spaces project (its owning project) and is mounted by the shared Desktop
/// <c>ChatComposerCuiView</c>.
/// </summary>
public static class ChatComposerCuiDocument
{
    public const string ResourceName = "HavenOS.Apps.Spaces.UI.Chat.ChatComposer.cui";

    public static CuiDocument Load()
    {
        var assembly = typeof(ChatComposerCuiDocument).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded Chat Composer CUI resource '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return new CuiRichParser().Parse(reader.ReadToEnd(), "ChatComposer.cui");
    }
}
