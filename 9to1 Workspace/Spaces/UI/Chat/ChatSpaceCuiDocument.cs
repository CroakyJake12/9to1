using System.Reflection;
using CakeOS.Cui.Markup;

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
        return new CuiMarkupParser().Parse(reader.ReadToEnd(), "ChatSpace.cui");
    }

    public static CuiElement FindById(CuiDocument document, string id)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return DescendantsAndSelf(document.Root)
            .Single(element => element.Attributes.GetValueOrDefault("id") == id);
    }

    public static IReadOnlyList<CuiElement> FindByAction(CuiDocument document, string action)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        return DescendantsAndSelf(document.Root)
            .Where(element => element.Attributes.GetValueOrDefault("action") == action)
            .ToArray();
    }

    public static IEnumerable<CuiElement> DescendantsAndSelf(CuiElement element)
    {
        yield return element;
        foreach (var child in element.Children)
        foreach (var descendant in DescendantsAndSelf(child))
            yield return descendant;
    }
}
