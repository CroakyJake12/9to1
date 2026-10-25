using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperDraftHistoryTests
{
    [Fact]
    public void Undo_redo_and_divergent_edits_retain_exact_text_including_empty_unicode_and_line_endings()
    {
        var text = ""; var history = new DeveloperDraftHistory(text);
        void Edit(string next) { history.RecordChange(text, next); text = next; }
        void Apply(string next) => text = next;
        Edit("A\r\n😀"); Edit("A\r\n😀\nB");
        Assert.True(history.TryUndo(text, Apply)); Assert.Equal("A\r\n😀", text);
        Assert.True(history.TryUndo(text, Apply)); Assert.Equal("", text);
        Assert.False(history.CanUndo(text)); Assert.True(history.TryRedo(text, Apply));
        Assert.Equal("A\r\n😀", text); Edit("different\rtext");
        Assert.False(history.CanRedo(text)); Assert.False(history.TryRedo(text, Apply));
        Assert.True(history.TryUndo(text, Apply)); Assert.Equal("A\r\n😀", text);
    }

    [Fact]
    public void Stale_source_resets_history_and_a_refused_owner_does_not_consume_undo()
    {
        var history = new DeveloperDraftHistory("saved"); history.RecordChange("saved", "typed");
        Assert.Throws<InvalidOperationException>(() => history.TryUndo("typed", _ => throw new InvalidOperationException("stale revision")));
        Assert.True(history.CanUndo("typed"));
        Assert.False(history.TryUndo("recovered current source", _ => throw new InvalidOperationException("must not apply")));
        Assert.False(history.CanRedo("recovered current source"));
        history.RecordChange("recovered current source", "new edit");
        string? undone = null; Assert.True(history.TryUndo("new edit", value => undone = value));
        Assert.Equal("recovered current source", undone);
    }

    [Fact]
    public void History_prunes_old_states_by_count_and_character_budget_without_truncating_current_text()
    {
        var history = new DeveloperDraftHistory("a", maximumStates: 3, maximumRetainedCharacters: 10);
        history.RecordChange("a", "bb"); history.RecordChange("bb", "ccc"); history.RecordChange("ccc", "dddd");
        var text = "dddd";
        Assert.True(history.TryUndo(text, value => text = value)); Assert.Equal("ccc", text);
        Assert.True(history.TryUndo(text, value => text = value)); Assert.Equal("bb", text);
        Assert.False(history.TryUndo(text, value => text = value));
        history.RecordChange(text, new string('x', 20)); text = new string('x', 20);
        Assert.False(history.CanUndo(text)); Assert.False(history.CanRedo(text));
        history.RecordChange(text, ""); text = "";
        Assert.False(history.CanUndo(text)); // oversized prior state was evicted, current empty text survives
    }
}
