using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Haven.Desktop.Views.Pages.Chat;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>
/// The shared composer's Voice &amp; Vision seam: the authored waveform toggle is projected from
/// <see cref="IChatComposerSource.HasVoice"/>, action availability is gated truthfully by whether a
/// real opener is wired, and dispatching the voice action reaches the source's OpenVoice — never a
/// fabricated default model or a permissive local-only claim.
/// </summary>
public sealed class ChatComposerVoiceSeamTests
{
    [Fact]
    public async Task Voice_toggle_projects_hasvoice_gates_availability_and_dispatches_open()
    {
        var source = new FakeSource { HasVoice = true };
        var bindings = new ChatComposerBindings(source);

        Assert.True(bindings.TryGetValue("Composer.HasVoice", out var hasVoice));
        Assert.Equal(true, hasVoice);
        Assert.Equal(true, bindings.IsActionAvailable("composer.voice"));

        await bindings.DispatchAsync("composer.voice", null, CancellationToken.None);
        Assert.True(source.OpenedVoice);
    }

    [Fact]
    public void Voice_toggle_is_unavailable_when_no_opener_is_wired()
    {
        var source = new FakeSource { HasVoice = false };
        var bindings = new ChatComposerBindings(source);

        Assert.True(bindings.TryGetValue("Composer.HasVoice", out var hasVoice));
        Assert.Equal(false, hasVoice);
        Assert.Equal(false, bindings.IsActionAvailable("composer.voice"));
    }

    private sealed class FakeSource : IChatComposerSource
    {
        public bool HasVoice { get; set; }
        public bool OpenedVoice { get; private set; }
        public string Draft { get; set; } = string.Empty;
        public string Placeholder => "Ask Haven anything";
        public bool IsSending => false;
        public bool IsSafetyLocked => false;
        public bool HasAdd => true;
        public bool CanSend => false;
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
        public ValueTask OpenVoice() { OpenedVoice = true; return ValueTask.CompletedTask; }
    }
}
