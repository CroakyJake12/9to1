using CakeOS.Cui;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Binding path resolution in <see cref="CuiViewModel"/>.
///
/// Found while porting the Chat Space to CUI: the document loaded with no diagnostics and
/// rendered only its literal text, because <c>Conversation.Title</c> and <c>Status.Message</c>
/// both resolved to whichever value was published last.
/// </summary>
public sealed class CuiViewModelPathResolutionTests
{
    [Fact]
    public void Two_dotted_paths_sharing_a_final_segment_stay_distinct()
    {
        var model = new CuiViewModel();
        model.Set("Conversation.Title", "Space chat");
        model.Set("Status.Message", "Ready");

        Assert.True(model.TryGetValue("Conversation.Title", out var title));
        Assert.Equal("Space chat", title);
        Assert.True(model.TryGetValue("Status.Message", out var message));
        Assert.Equal("Ready", message);
    }

    [Fact]
    public void An_exact_key_wins_over_a_colliding_final_segment()
    {
        var model = new CuiViewModel();
        model.Set("Title", "flat");
        model.Set("Conversation.Title", "qualified");

        Assert.True(model.TryGetValue("Conversation.Title", out var value));
        Assert.Equal("qualified", value);
        // A path with no exact match still falls back, so a model that only stores flat keys keeps
        // working exactly as it did before.
        Assert.True(model.TryGetValue("Something.Title", out var fallback));
        Assert.Equal("flat", fallback);
    }

    [Fact]
    public void An_unknown_path_resolves_to_nothing_rather_than_a_partial_match()
    {
        var model = new CuiViewModel();
        model.Set("Conversation.Title", "Space chat");

        Assert.False(model.TryGetValue("Composer.Title", out var value));
        Assert.Null(value);
    }

    [Fact]
    public void A_write_lands_on_the_same_key_a_read_resolved()
    {
        var model = new CuiViewModel();
        model.Set("Conversation.Title", "Space chat");

        Assert.True(model.TrySetValue("Conversation.Title", "Renamed"));
        Assert.Equal("Renamed", model.Get("Conversation.Title"));

        // Flat-key models keep round-tripping through the fallback.
        var flat = new CuiViewModel();
        flat.Set("Draft", "hello");
        Assert.True(flat.TrySetValue("Composer.Draft", "goodbye"));
        Assert.Equal("goodbye", flat.Get("Draft"));
    }

    [Fact]
    public void Writing_a_path_the_model_does_not_hold_is_refused()
    {
        var model = new CuiViewModel();
        model.Set("Conversation.Title", "Space chat");

        Assert.False(model.TrySetValue("Composer.Nonexistent", "x"));
    }
}