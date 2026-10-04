using System.Reflection;
using Haven.UI;
using Haven.UI.Components;

namespace CakeOS.Apps.Boards.Hui;

internal static class HavenBoardsMarkup
{
    internal static Page Load(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("A CUI resource name is required.", nameof(fileName));

        var assembly = typeof(HavenBoardsMarkup).Assembly;
        var suffix = $".Views.{fileName}";
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(suffix, StringComparison.Ordinal))
            .ToArray();
        if (resources.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one embedded Boards CUI resource ending in '{suffix}', found {resources.Length}.");
        }

        using var stream = assembly.GetManifestResourceStream(resources[0])
            ?? throw new InvalidOperationException($"Could not open embedded Boards CUI resource '{resources[0]}'.");
        using var reader = new StreamReader(stream);
        return (Page)new HavenMarkupParser().Parse(reader.ReadToEnd(), fileName);
    }
}
