using System.Collections;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

// Presentation controls only. Internally constructed display rows/cursors do not
// qualify a store read or project choice; the owning real SQLite controls do that.
public sealed class AssistantsProjectCataloguePagingBindingsTests
{
    [Fact]
    public void More_metadata_without_an_original_cursor_cannot_enable_older_page_command()
    {
        var bindings = Available(); bindings.SetProjectCatalogue(new([], true, "Display metadata only."));
        Assert.False(bindings.IsActionAvailable("assistants.project.older"));
        Assert.True(bindings.IsActionAvailable("assistants.project.search"));
        bindings.SetProjectCatalogue(Page("Original cursor display", true));
        Assert.True(bindings.IsActionAvailable("assistants.project.older"));
    }

    [Fact]
    public void Search_ABA_rejects_old_page_publication_and_old_rows_after_current_page_replaces_them()
    {
        var bindings = Available(); var oldPage = Page("Old source page", true);
        var originalRevision = bindings.ProjectSearchRevision;
        bindings.SetProjectCatalogue(oldPage, originalRevision);
        var oldRow = Row(bindings);
        Assert.Same(oldPage.Candidates[0], bindings.DemandOriginalProjectCandidate(oldRow));
        Assert.True(bindings.TrySetValue("ProjectSearchText", "older"));
        Assert.True(bindings.TrySetValue("ProjectSearchText", ""));
        Assert.False(bindings.IsActionAvailable("assistants.project.choose"));
        Assert.False(bindings.IsActionAvailable("assistants.project.older"));
        Assert.Throws<InvalidOperationException>(() => bindings.DemandOriginalProjectCandidate(oldRow));
        bindings.SetProjectCatalogue(Page("Stale delayed page", true), originalRevision);
        Assert.True(bindings.TryGetValue("ProjectSelectionStatus", out var status));
        Assert.Equal("Old source page", status);
        var current = Page("Fresh current page", false);
        bindings.SetProjectCatalogue(current, bindings.ProjectSearchRevision);
        Assert.True(bindings.IsActionAvailable("assistants.project.choose"));
        Assert.False(bindings.IsActionAvailable("assistants.project.older"));
        Assert.Throws<InvalidOperationException>(() => bindings.DemandOriginalProjectCandidate(oldRow));
        Assert.Same(current.Candidates[0], bindings.DemandOriginalProjectCandidate(Row(bindings)));
    }

    [Fact]
    public void Busy_or_pending_creation_preserves_query_and_cannot_dispatch_another_page()
    {
        var bindings = Available(); bindings.SetProjectCatalogue(Page("Source page", true));
        Assert.True(bindings.TrySetValue("ProjectSearchText", "saved title"));
        bindings.SetProjectBusy(true);
        Assert.False(bindings.TrySetValue("ProjectSearchText", "replacement"));
        Assert.False(bindings.IsActionAvailable("assistants.project.search"));
        bindings.SetProjectBusy(false); bindings.SetProjectSubmission(true, false, "Original pending task");
        Assert.False(bindings.TrySetValue("ProjectSearchText", "replacement"));
        Assert.False(bindings.IsActionAvailable("assistants.project.older"));
        Assert.False(bindings.IsActionAvailable("assistants.project.refresh"));
        Assert.True(bindings.TryGetValue("ProjectSearchText", out var query)); Assert.Equal("saved title", query);
        bindings.Revoke(); Assert.True(bindings.TryGetValue("ProjectSearchText", out query)); Assert.Null(query);
        Assert.False(bindings.TrySetValue("ProjectSearchText", "retired"));
    }

    private static object Row(AssistantsCuiBindings bindings)
    {
        Assert.True(bindings.TryGetValue("ProjectChoices", out var value));
        return Assert.IsAssignableFrom<IEnumerable>(value).Cast<object>().Single();
    }
    private static AssistantOriginalProjectCatalogue Page(string status, bool more)
    {
        var reference = new DeveloperProjectReference(Guid.NewGuid(), 1, Guid.NewGuid(), 1, Guid.NewGuid());
        var candidate = new AssistantOriginalProjectCandidate(new object(), new object(), "Saved project", reference);
        return new([candidate], more, status)
        { NextContinuation = more ? new AssistantOriginalProjectCatalogueContinuation(new object(), new object()) : null };
    }
    private static AssistantsCuiBindings Available()
    {
        var bindings = new AssistantsCuiBindings((_, _, _) => ValueTask.CompletedTask, body => body(), () => true);
        var definition = new AssistantDefinitionSnapshot(new("fixture-den", "personal", "fixture-assistant"), 1,
            ConfiguredIdentityKind.Assistant, new() { Name = "Fixture Assistant" }, []);
        bindings.ApplySnapshot(AssistantsWorkspaceSnapshot.Empty with
        { Revision = 1, SelectedAssistant = definition, Assistants = [definition] });
        bindings.BeginProjectSelection(false); return bindings;
    }
}
