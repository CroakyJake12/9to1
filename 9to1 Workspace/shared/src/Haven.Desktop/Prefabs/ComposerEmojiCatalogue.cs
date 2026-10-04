using System.Globalization;

namespace Haven.Desktop.Prefabs;

public sealed record ComposerEmoji(string Text, string Name, string Group);

/// <summary>Unicode 17 fully-qualified sequences, including modifiers and joined emoji.</summary>
public static class ComposerEmojiCatalogue
{
    public static IReadOnlyList<ComposerEmoji> Entries { get; } = Read();

    private static IReadOnlyList<ComposerEmoji> Read()
    {
        using var stream = typeof(ComposerEmojiCatalogue).Assembly.GetManifestResourceStream("Haven.Desktop.Resources.emoji-test-17.0.txt")
            ?? throw new InvalidOperationException("The Unicode emoji catalogue resource is missing.");
        using var reader = new StreamReader(stream);
        var result = new List<ComposerEmoji>();
        var group = "";
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("# group: ", StringComparison.Ordinal)) { group = line[9..]; continue; }
            if (line.StartsWith('#') || !line.Contains("; fully-qualified", StringComparison.Ordinal)) continue;
            var separator = line.IndexOf(';');
            var comment = line.IndexOf('#', separator);
            if (comment < 0) throw new InvalidDataException("Unicode emoji data is malformed.");
            var text = string.Concat(line[..separator].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => char.ConvertFromUtf32(int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture))));
            var description = line[(comment + 1)..].Trim();
            var version = description.IndexOf(" E", StringComparison.Ordinal);
            var nameStart = version < 0 ? -1 : description.IndexOf(' ', version + 2);
            if (nameStart < 0) throw new InvalidDataException("Unicode emoji data has no accessible description.");
            result.Add(new(text, description[(nameStart + 1)..], group));
        }
        return result.AsReadOnly();
    }
}
