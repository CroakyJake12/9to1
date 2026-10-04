using Haven.Desktop.Prefabs;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class ChatComposerControllerTests
{
    [Fact]
    public void Bundled_catalogue_includes_joined_sequences_modifiers_and_accessible_names()
    {
        Assert.True(ComposerEmojiCatalogue.Entries.Count > 3000);
        Assert.Contains(ComposerEmojiCatalogue.Entries, entry => entry.Text.Contains('\u200d'));
        Assert.Contains(ComposerEmojiCatalogue.Entries, entry => entry.Text.Contains("🏽", StringComparison.Ordinal));
        Assert.All(ComposerEmojiCatalogue.Entries, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Name)));
    }
}
