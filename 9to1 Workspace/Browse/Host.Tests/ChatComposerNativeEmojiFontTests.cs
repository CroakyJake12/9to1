using Haven.Desktop.Prefabs;
using Haven.Desktop.Views.Pages.Chat;
using Xunit;

namespace HavenOS.Browse.Host.Tests;

public sealed class ChatComposerNativeEmojiFontTests
{
    private sealed class Source : IChatComposerSource
    {
        public string Draft { get; set; } = string.Empty;
        public string Placeholder => string.Empty;
        public bool IsSending => false;
        public bool IsSafetyLocked => false;
        public bool HasAdd => false;
        public bool CanSend => true;
        public bool IsEditing => false;
        public IReadOnlyList<ChatComposerChip> Chips => [];
        public ValueTask Send() => ValueTask.CompletedTask;
        public ValueTask Stop() => ValueTask.CompletedTask;
        public ValueTask Add() => ValueTask.CompletedTask;
        public ValueTask OpenSettings() => ValueTask.CompletedTask;
        public ValueTask CloseSettings() => ValueTask.CompletedTask;
        public ValueTask SetActionMode(string mode) => ValueTask.CompletedTask;
        public ValueTask SetResponseMode(string mode) => ValueTask.CompletedTask;
        public ValueTask InvokeChip(string id) => ValueTask.CompletedTask;
        public ValueTask RemoveChip(string id) => ValueTask.CompletedTask;
        public ValueTask FormatSelection(string command) => ValueTask.CompletedTask;
        public ValueTask InsertEmoji(string emoji) => ValueTask.CompletedTask;
        public ValueTask CancelEdit() => ValueTask.CompletedTask;
    }

    [Fact]
    public void Composer_selects_installed_device_emoji_family_and_keeps_unicode_candidates()
    {
        var bindings = new ChatComposerBindings(new Source());

        Assert.True(bindings.TryGetValue("Composer.EmojiFontFamily", out var value));
        var family = Assert.IsType<string>(value);
        var platformFamily = OperatingSystem.IsWindows() ? "Segoe UI Emoji"
            : OperatingSystem.IsMacOS() ? "Apple Color Emoji"
            : OperatingSystem.IsLinux() ? "Noto Color Emoji"
            : null;
        if (platformFamily is null)
            Assert.Equal("Montserrat", family);
        else
            Assert.Contains(family, new[] { "Montserrat", platformFamily });

        Assert.True(bindings.TryGetValue("Composer.EmojiIconCandidates", out var candidatesValue));
        var candidates = Assert.IsAssignableFrom<IReadOnlyList<string>>(candidatesValue);
        Assert.NotEmpty(candidates);
        Assert.All(candidates, candidate =>
        {
            Assert.Contains(ComposerEmojiCatalogue.Entries, entry => entry.Text == candidate);
            Assert.DoesNotContain(".svg", candidate);
        });
    }
}
