using System.Reflection;
using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace Haven.Desktop.Views.Pages.Chat;

public static class ChatCuiDocument
{
    public const string ResourceName = "Haven.Desktop.Chat.Chat.cui";

    public static CuiDocument Load()
    {
        var assembly = typeof(ChatCuiDocument).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded Chat CUI resource '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return new CuiRichParser().Parse(reader.ReadToEnd(), "Chat.cui");
    }
}