using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Automation;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Core.Forms;
using HavenOS.Forms;

namespace HavenOS.Forms.Tests;

[Collection("Forms native renderer")]
public sealed class FormsCuiWorkspaceTests
{
    [Fact]
    public async Task Row_limit_inspector_retains_invalid_and_conflicted_text_until_saved_or_discarded()
    {
        using var paths = new Paths(); var settings = new VersionedAtomicSettingsStore(paths);
        var publications = new FormPublicationService(settings, settings, new Authority(), new FormNativePublicationValidator(), actors: new PublicationActor());
        var authoring = new FormAuthoringService(publications);
        var surface = new FormsCuiWorkspace(publications, authoring, () => null, _ => true);
        await surface.DispatchAsync("9to1.Forms.Create", null);
        surface.TryGetValue("PaletteNames", out var palette);
        Assert.True(surface.TrySetValue("SelectedPaletteIndex", Array.IndexOf(Assert.IsType<string[]>(palette), "Table input")));
        await surface.DispatchAsync("9to1.Forms.AddField", null);
        var formID = surface.FormID!.Value; var before = (await publications.ReadAsync(formID)).Publication!;
        Assert.True(surface.TrySetValue("MinimumRows", "invalid"));
        Assert.False(surface.IsActionAvailable("9to1.Forms.Publish"));
        await Assert.ThrowsAsync<ArgumentException>(async () => await surface.DispatchAsync("9to1.Forms.SaveTableBounds", null));
        surface.TryGetValue("MinimumRows", out var retained); Assert.Equal("invalid", retained);
        Assert.Equal(before.Revision, (await publications.ReadAsync(formID)).Publication!.Revision);
        Assert.True(surface.TrySetValue("MinimumRows", "2")); Assert.True(surface.TrySetValue("MaximumRows", "1"));
        await Assert.ThrowsAsync<ArgumentException>(async () => await surface.DispatchAsync("9to1.Forms.SaveTableBounds", null));
        Assert.True(surface.TrySetValue("MaximumRows", "3"));
        await surface.DispatchAsync("9to1.Forms.SaveTableBounds", null);
        await surface.DispatchAsync("9to1.Forms.Publish", null);
        var publication = (await publications.ReadAsync(formID)).Publication!;
        var field = Assert.Single(Decode(publication.Draft).Fields);
        Assert.Equal(2, field.Table!.MinimumRows); Assert.Equal(3, field.Table.MaximumRows);
        Assert.False(surface.IsActionAvailable("9to1.Forms.ToggleAddedRows")); // No fixed rows can meet minimum 2.
        Assert.True(surface.TrySetValue("MaximumRows", "4"));
        Assert.True((await authoring.UpdateFieldAsync(formID, publication.Revision, field with { Label = "Concurrent edit" })).Success);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await surface.DispatchAsync("9to1.Forms.SaveTableBounds", null));
        surface.TryGetValue("MaximumRows", out retained); Assert.Equal("4", retained);
        Assert.False(surface.IsActionAvailable("9to1.Forms.Publish"));
        var current = (await publications.ReadAsync(formID)).Publication!;
        Assert.Equal(3, Assert.Single(Decode(Assert.Single(current.Versions).Project).Fields).Table!.MaximumRows);
        await surface.DispatchAsync("9to1.Forms.DiscardInspector", null);
        surface.TryGetValue("MaximumRows", out retained); Assert.Equal("3", retained);
    }

    [Fact]
    public async Task Fixed_row_authoring_keeps_stable_ids_and_published_rows_when_draft_changes()
    {
        using var paths = new Paths(); var settings = new VersionedAtomicSettingsStore(paths);
        var publications = new FormPublicationService(settings, settings, new Authority(), new FormNativePublicationValidator(), actors: new PublicationActor());
        var allowed = true;
        var surface = new FormsCuiWorkspace(publications, new FormAuthoringService(publications), () => null, _ => allowed);
        await surface.DispatchAsync("9to1.Forms.Create", null);
        surface.TryGetValue("PaletteNames", out var palette);
        Assert.True(surface.TrySetValue("SelectedPaletteIndex", Array.IndexOf(Assert.IsType<string[]>(palette), "Table input")));
        await surface.DispatchAsync("9to1.Forms.AddField", null);
        await surface.DispatchAsync("9to1.Forms.AddFixedRow", null);
        await surface.DispatchAsync("9to1.Forms.AddFixedRow", null);
        await surface.DispatchAsync("9to1.Forms.ToggleAddedRows", null);
        await surface.DispatchAsync("9to1.Forms.Publish", null);
        var publication = (await publications.ReadAsync(surface.FormID!.Value)).Publication!;
        var table = Assert.Single(Decode(publication.Draft).Fields).Table!;
        Assert.False(table.AllowAddedRows); Assert.Equal(2, table.FixedRowIDs!.Count);
        Assert.NotEqual(table.FixedRowIDs[0], table.FixedRowIDs[1]);
        Assert.True(surface.TrySetValue("SelectedFixedRowIndex", 0));
        await surface.DispatchAsync("9to1.Forms.RemoveFixedRow", null);
        var updated = (await publications.ReadAsync(surface.FormID.Value)).Publication!;
        Assert.Equal(table.FixedRowIDs[1], Assert.Single(Assert.Single(Decode(updated.Draft).Fields).Table!.FixedRowIDs!));
        Assert.Equal(table.FixedRowIDs, Assert.Single(Decode(Assert.Single(updated.Versions).Project).Fields).Table!.FixedRowIDs);
        allowed = false;
        Assert.False(surface.IsActionAvailable("9to1.Forms.AddFixedRow"));
        Assert.False(surface.TrySetValue("SelectedFixedRowIndex", 0));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await surface.DispatchAsync("9to1.Forms.ToggleAddedRows", null));
        Assert.Equal(updated.Revision, (await publications.ReadAsync(surface.FormID.Value)).Publication!.Revision);
    }

    [Fact]
    public async Task Table_column_inspector_persists_typed_stable_columns_and_preserves_conflicted_drafts()
    {
        Assert.NotNull(FormsCuiWorkspace.LoadDocument());
        using var paths = new Paths();
        var settings = new VersionedAtomicSettingsStore(paths);
        var publications = new FormPublicationService(settings, settings, new Authority(), new FormNativePublicationValidator(), actors: new PublicationActor());
        var authoring = new FormAuthoringService(publications);
        var allowed = true;
        var surface = new FormsCuiWorkspace(publications, authoring, () => null, _ => allowed);
        await surface.DispatchAsync("9to1.Forms.Create", null);
        surface.TryGetValue("PaletteNames", out var palette);
        Assert.True(surface.TrySetValue("SelectedPaletteIndex", Array.IndexOf(Assert.IsType<string[]>(palette), "Table input")));
        await surface.DispatchAsync("9to1.Forms.AddField", null);
        var formID = surface.FormID!.Value;
        var first = Assert.Single(Decode((await publications.ReadAsync(formID)).Publication!.Draft).Fields);
        var firstColumn = Assert.Single(first.Table!.Columns).ColumnID;
        Assert.False(surface.IsActionAvailable("9to1.Forms.RemoveColumn"));
        Assert.True(surface.TrySetValue("ColumnLabel", "Item"));
        Assert.True(surface.TrySetValue("SelectedColumnTypeIndex", 1));
        await surface.DispatchAsync("9to1.Forms.AddColumn", null);
        Assert.True(surface.TrySetValue("ColumnLabel", "Amount"));
        Assert.True(surface.TrySetValue("SelectedColumnIndex", 0));
        surface.TryGetValue("ColumnLabel", out var retained); Assert.Equal("Item", retained);
        await surface.DispatchAsync("9to1.Forms.SaveColumn", null);
        Assert.False(surface.IsActionAvailable("9to1.Forms.Publish")); // Other column has a dirty label.
        Assert.True(surface.TrySetValue("SelectedColumnIndex", 1));
        await surface.DispatchAsync("9to1.Forms.ToggleColumnRequired", null);
        surface.TryGetValue("ColumnLabel", out retained); Assert.Equal("Amount", retained);
        await surface.DispatchAsync("9to1.Forms.SaveColumn", null);
        await surface.DispatchAsync("9to1.Forms.Publish", null);
        var publication = (await publications.ReadAsync(formID)).Publication!;
        var table = Assert.Single(Decode(publication.Draft).Fields).Table!;
        Assert.Equal(firstColumn, table.Columns[0].ColumnID);
        Assert.Equal("Item", table.Columns[0].Label);
        Assert.Equal(FormTableCellType.Number, table.Columns[1].Type);
        Assert.True(table.Columns[1].Required);
        Assert.Equal("Amount", table.Columns[1].Label);
        Assert.NotEqual(firstColumn, table.Columns[1].ColumnID);
        await surface.DispatchAsync("9to1.Forms.RemoveColumn", null);
        var afterRemove = (await publications.ReadAsync(formID)).Publication!;
        Assert.Single(Assert.Single(Decode(afterRemove.Draft).Fields).Table!.Columns);
        Assert.Equal(2, Assert.Single(Decode(Assert.Single(afterRemove.Versions).Project).Fields).Table!.Columns.Count);
        Assert.True(surface.TrySetValue("ColumnLabel", "Retained local edit"));
        var currentField = Assert.Single(Decode(afterRemove.Draft).Fields);
        var concurrent = currentField with { Table = currentField.Table! with
            { Columns = [currentField.Table!.Columns[0] with { Label = "Concurrent column" }] } };
        Assert.True((await authoring.UpdateFieldAsync(formID, afterRemove.Revision, concurrent)).Success);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await surface.DispatchAsync("9to1.Forms.SaveColumn", null));
        surface.TryGetValue("ColumnLabel", out retained); Assert.Equal("Retained local edit", retained);
        allowed = false;
        Assert.False(surface.TrySetValue("ColumnLabel", "Denied"));
        Assert.False(surface.TrySetValue("SelectedColumnTypeIndex", 0));
    }

    [Fact]
    public async Task Palette_and_choice_inspector_preserve_typed_ids_dirty_edits_and_immutable_publication()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(RegexApplication));
        var originalFailures = new List<Exception>();
        try
        {
            var originalPaletteTask = session.Dispatch<bool>(async () =>
            {
                using var paths = new Paths();
                var settings = new VersionedAtomicSettingsStore(paths);
                var authority = new Authority();
                var mathematics = new Haven.Desktop.Mathematics.FormNativeMathematicsProvider();
                var publications = new FormPublicationService(settings, settings, authority, new FormNativePublicationValidator(mathematics), actors: new PublicationActor());
                var authoring = new FormAuthoringService(publications);
                var allowed = true;
                var surface = new FormsCuiWorkspace(publications, authoring, () => null, _ => allowed);
                await surface.DispatchAsync("9to1.Forms.Create", null);
                Assert.True(surface.TryGetValue("PaletteNames", out var paletteValue));
                var names = Assert.IsType<string[]>(paletteValue);
                Assert.Contains("Multiple choice", names); Assert.Contains("Email", names);
                for (var index = 0; index < names.Length; index++)
                {
                    Assert.True(surface.TrySetValue("SelectedPaletteIndex", index));
                    await surface.DispatchAsync("9to1.Forms.AddField", null);
                }
                Assert.False(surface.TrySetValue("SelectedPaletteIndex", names.Length));
                var formID = surface.FormID!.Value;
                var authored = Decode((await publications.ReadAsync(formID)).Publication!.Draft);
                Assert.Equal(names.Length, authored.Fields.Count);
                Assert.Equal(authored.Fields.Count, authored.Fields.Select(field => field.FieldID).Distinct().Count());
                var choiceIndex = authored.Fields.ToList().FindIndex(field => field.Kind == FormFieldKind.MultipleChoice);
                var original = authored.Fields[choiceIndex];
                var optionID = original.Options![0].OptionID;
                Assert.True(surface.TrySetValue("SelectedFieldIndex", choiceIndex));
                Assert.True(surface.TrySetValue("SelectedOptionIndex", 0));
                Assert.True(surface.TrySetValue("Label", "Unsaved question label"));
                Assert.True(surface.TrySetValue("OptionLabel", "Renamed choice"));
                Assert.True(surface.TrySetValue("SelectedFieldIndex", 0));
                Assert.True(surface.TrySetValue("SelectedFieldIndex", choiceIndex));
                Assert.True(surface.TryGetValue("Label", out var dirtyLabel)); Assert.Equal("Unsaved question label", dirtyLabel);
                Assert.True(surface.TryGetValue("OptionLabel", out var dirtyChoice)); Assert.Equal("Renamed choice", dirtyChoice);
                Assert.False(surface.IsActionAvailable("9to1.Forms.Preview"));
                await surface.DispatchAsync("9to1.Forms.UpdateChoice", null);
                Assert.False(surface.IsActionAvailable("9to1.Forms.Publish")); // Saving the option must not discard the question edit.
                await surface.DispatchAsync("9to1.Forms.SaveField", null);
                Assert.True(surface.IsActionAvailable("9to1.Forms.Preview"));
                await surface.DispatchAsync("9to1.Forms.AddChoice", null);
                Assert.True(surface.TrySetValue("OptionLabel", "Renamed choice"));
                await surface.DispatchAsync("9to1.Forms.UpdateChoice", null);
                await surface.DispatchAsync("9to1.Forms.Publish", null);
                var published = (await publications.ReadAsync(formID)).Publication!;
                var publishedField = Decode(Assert.Single(published.Versions).Project).Fields.Single(field => field.FieldID == original.FieldID);
                Assert.Equal("Unsaved question label", publishedField.Label);
                Assert.Equal(3, publishedField.Options!.Count);
                Assert.Equal(optionID, publishedField.Options[0].OptionID);
                Assert.Equal(3, publishedField.Options.Select(option => option.OptionID).Distinct().Count());
                Assert.Equal(publishedField.Options[0].Label, publishedField.Options[2].Label);
                Assert.True(surface.TrySetValue("SelectedOptionIndex", 0));
                Assert.True(surface.TrySetValue("OptionLabel", "Local conflict edit"));
                var concurrent = publishedField with { Options = publishedField.Options.Select(option => option.OptionID == optionID
                    ? option with { Label = "Concurrent edit" } : option).ToArray() };
                Assert.True((await authoring.UpdateFieldAsync(formID, published.Revision, concurrent)).Success);
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await surface.DispatchAsync("9to1.Forms.UpdateChoice", null));
                Assert.True(surface.TryGetValue("OptionLabel", out var retained)); Assert.Equal("Local conflict edit", retained);
                var after = (await publications.ReadAsync(formID)).Publication!;
                Assert.Equal("Concurrent edit", Decode(after.Draft).Fields.Single(field => field.FieldID == original.FieldID).Options![0].Label);
                Assert.Equal("Renamed choice", Decode(Assert.Single(after.Versions).Project).Fields.Single(field => field.FieldID == original.FieldID).Options![0].Label);
                allowed = false;
                Assert.False(surface.TrySetValue("OptionLabel", "Denied"));
                Assert.False(surface.TrySetValue("SelectedPaletteIndex", 0));
                Assert.False(surface.IsActionAvailable("9to1.Forms.UpdateChoice"));
                return true;
            }, CancellationToken.None);
            await originalPaletteTask;
        }
        catch (Exception original) { originalFailures.Add(original); }
        finally
        {
            try { await session.DisposeAsync(); }
            catch (Exception cleanup)
            {
                if (!originalFailures.Any(original => ReferenceEquals(original, cleanup)))
                    originalFailures.Add(cleanup);
            }
        }
        if (originalFailures.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(originalFailures[0]).Throw();
        if (originalFailures.Count > 1)
            throw new AggregateException("Original palette callback and native session disposal failed", originalFailures);
    }

    [Fact]
    public async Task Actual_native_publication_capabilities_preserve_advanced_draft_and_prior_active_version()
    {
        using var paths = new Paths();
        var settings = new VersionedAtomicSettingsStore(paths);
        var authority = new Authority();
        var publications = new FormPublicationService(settings, settings, authority, new FormNativePublicationValidator(), actors: new PublicationActor());
        var project = FormProjectEditor.Create("Publication capabilities", FormModeKind.Form, DateTimeOffset.UtcNow);
        var created = await publications.CreateAsync(project.FormID, FormProjectEditor.Project(project));
        Assert.True(created.Success);
        var published = await publications.PublishAsync(project.FormID, created.Publication!.Revision);
        Assert.True(published.Success);
        var advanced = project with { Revision = project.Revision + 1, ModifiedAt = project.ModifiedAt.AddSeconds(1),
            AccessPolicy = new(FormRespondentAccess.Public, []) };
        var edited = await publications.SaveDraftAsync(project.FormID, published.Publication!.Revision, FormProjectEditor.Project(advanced));
        Assert.True(edited.Success); // Authoring an advanced policy never silently discards it.
        var denied = await publications.PublishAsync(project.FormID, edited.Publication!.Revision);
        Assert.False(denied.Success);
        Assert.Equal("CapabilityUnavailable", denied.Code);
        var retained = (await publications.ReadAsync(project.FormID)).Publication!;
        Assert.Equal(edited.Publication.Revision, retained.Revision);
        Assert.Equal(published.Publication.ActiveVersionID, retained.ActiveVersionID);
        Assert.Equal(FormRespondentAccess.Public, Decode(retained.Draft).AccessPolicy.Respondents);
        Assert.Equal(FormRespondentAccess.OwnerOnly, Decode(Assert.Single(retained.Versions).Project).AccessPolicy.Respondents);
    }

    [Fact]
    public async Task Owning_builder_saves_and_moves_canonical_field_then_reopens_immutable_publication_and_preserves_dirty_edit_on_conflict()
    {
        Assert.NotNull(FormsCuiWorkspace.LoadDocument());
        using var paths = new Paths();
        var store = new VersionedAtomicSettingsStore(paths);
        var authority = new Authority();
        var publications = new FormPublicationService(store, store, authority, new FormNativePublicationValidator(), actors: new PublicationActor());
        var authoring = new FormAuthoringService(publications);
        Guid? selected = null;
        var allowed = true;
        FormNativePreview? seenPreview = null;
        var surface = new FormsCuiWorkspace(publications, authoring, () => selected, _ => allowed,
            (preview, token) => { seenPreview = preview; Assert.Equal(selected, preview.Response.FormID); return Task.CompletedTask; });
        Assert.True(surface.TrySetValue("Title", "Canonical survey"));
        await surface.DispatchAsync("9to1.Forms.Create", null);
        selected = surface.FormID;
        await surface.DispatchAsync("9to1.Forms.AddText", null);
        Assert.True(surface.TrySetValue("Label", "Your name"));
        await surface.DispatchAsync("9to1.Forms.SaveField", null);
        await surface.DispatchAsync("9to1.Forms.ToggleRequired", null);
        var initial = (await publications.ReadAsync(selected!.Value)).Publication!;
        var field = Assert.Single(Decode(initial.Draft).Fields);
        Assert.True(field.Required);
        await surface.DispatchAsync("9to1.Forms.AddPage", null);
        Assert.True(surface.TrySetValue("SelectedPageIndex", 0));
        Assert.True(surface.TrySetValue("SelectedPageIndex", 1));
        Assert.False(surface.TrySetValue("SelectedPageIndex", -1));
        Assert.False(surface.TrySetValue("SelectedFieldIndex", 999));
        await surface.DispatchAsync("9to1.Forms.MoveToPage", null);
        await surface.DispatchAsync("9to1.Forms.Preview", null);
        Assert.NotNull(seenPreview);
        Assert.Throws<ObjectDisposedException>(() => seenPreview.Submit());
        await surface.DispatchAsync("9to1.Forms.Publish", null);
        var published = (await publications.ReadAsync(selected.Value)).Publication!;
        var project = Decode(published.Draft);
        Assert.Empty(project.Pages[0].Children);
        Assert.Equal(field.FieldID, Assert.Single(project.Pages[1].Children).ID);
        Assert.Equal(field.FieldID, Assert.Single(project.Fields).FieldID);
        var version = Assert.Single(published.Versions);
        var reopenedStore = new VersionedAtomicSettingsStore(paths);
        var reopenedPublications = new FormPublicationService(reopenedStore, reopenedStore, authority, new FormNativePublicationValidator(), actors: new PublicationActor());
        var reopened = new FormsCuiWorkspace(reopenedPublications, new(reopenedPublications), () => selected, _ => true);
        await reopened.DispatchAsync("9to1.Forms.Open", null);
        Assert.True(reopened.TrySetValue("Label", "New draft label"));
        await reopened.DispatchAsync("9to1.Forms.SaveField", null);
        Assert.True(surface.TrySetValue("Label", "Unsaved local edit"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await surface.DispatchAsync("9to1.Forms.SaveField", null));
        Assert.True(surface.TryGetValue("Label", out var dirty));
        Assert.Equal("Unsaved local edit", dirty);
        var retained = (await reopenedPublications.ReadAsync(selected.Value)).Publication!;
        Assert.Equal("New draft label", Assert.Single(Decode(retained.Draft).Fields).Label);
        Assert.Equal("Your name", Assert.Single(Decode(Assert.Single(retained.Versions).Project).Fields).Label);
        Assert.Equal(version.FormVersionID, retained.ActiveVersionID);
        allowed = false;
        Assert.False(surface.TrySetValue("SelectedFieldIndex", 0));
        Assert.False(surface.TrySetValue("Label", "Denied local mutation"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await surface.DispatchAsync("9to1.Forms.Publish", null));
        authority.Allowed = false;
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await reopened.DispatchAsync("9to1.Forms.Close", null));
        authority.Allowed = true;
        Assert.Equal(retained.Revision, (await reopenedPublications.ReadAsync(selected.Value)).Publication!.Revision);
    }

    [Fact]
    public async Task Live_regex_tester_uses_runtime_semantics_without_changing_physical_form()
    {
        Assert.NotNull(FormsCuiWorkspace.LoadDocument());
        using var paths = new Paths(); var settings = new VersionedAtomicSettingsStore(paths);
        var publications = new FormPublicationService(settings, settings, new Authority(), new FormNativePublicationValidator(), actors: new PublicationActor());
        var allowed = true;
        var surface = new FormsCuiWorkspace(publications, new(publications), () => null, _ => allowed);
        Assert.False(surface.TrySetValue("RegexPattern", "CPU")); // No actual selected question.
        await surface.DispatchAsync("9to1.Forms.Create", null);
        await surface.DispatchAsync("9to1.Forms.AddText", null);
        var id = surface.FormID!.Value;
        var before = (await publications.ReadAsync(id)).Publication!;
        var bytes = Directory.GetFiles(paths.DataDirectory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);
        string Result() { Assert.True(surface.TryGetValue("RegexResult", out var result)); return Assert.IsType<string>(result); }
        Assert.True(surface.TrySetValue("RegexPattern", "CPU"));
        Assert.True(surface.TrySetValue("RegexExample", "a CPU unit"));
        Assert.Equal("Example does not match.", Result());
        Assert.True(surface.TrySetValue("RegexMatchMode", 1));
        Assert.Equal("Example matches.", Result());
        Assert.True(surface.TrySetValue("RegexExample", "cpu"));
        Assert.Equal("Example does not match.", Result());
        Assert.True(surface.TrySetValue("RegexCaseMode", 1));
        Assert.Equal("Example matches.", Result());
        Assert.True(surface.TrySetValue("RegexPattern", "["));
        Assert.Equal("Invalid or unsupported pattern.", Result());
        Assert.True(surface.TryGetValue("RegexPattern", out var retained)); Assert.Equal("[", retained);
        Assert.True(surface.TrySetValue("RegexPattern", "CPU")); Assert.Equal("Example matches.", Result());
        Assert.False(surface.TrySetValue("RegexMatchMode", 2));
        Assert.False(surface.TrySetValue("RegexExample", new string('x', 4097)));
        var after = (await publications.ReadAsync(id)).Publication!;
        Assert.Equal(before.Revision, after.Revision); Assert.Equal(before.Draft.GetRawText(), after.Draft.GetRawText());
        Assert.Equal(bytes.Keys.Order(), Directory.GetFiles(paths.DataDirectory, "*", SearchOption.AllDirectories).Order());
        foreach (var pair in bytes) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
        var reopenedStore = new VersionedAtomicSettingsStore(paths);
        var reopened = new FormPublicationService(reopenedStore, reopenedStore, new Authority(), new FormNativePublicationValidator(), actors: new PublicationActor());
        Assert.Equal(before.Draft.GetRawText(), (await reopened.ReadAsync(id)).Publication!.Draft.GetRawText());
        allowed = false;
        Assert.False(surface.TrySetValue("RegexPattern", "Denied"));
        Assert.True(surface.TryGetValue("RegexPattern", out retained)); Assert.Equal("CPU", retained);
    }

    [Fact]
    public async Task Mounted_regex_inputs_refresh_live_results_and_preserve_invalid_pattern()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(RegexApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var paths = new Paths(); var store = new VersionedAtomicSettingsStore(paths);
            var publications = new FormPublicationService(store, store, new Authority(), new FormNativePublicationValidator(), actors: new PublicationActor());
            var workspace = new FormsCuiWorkspace(publications, new(publications), () => null, _ => true);
            await workspace.DispatchAsync("9to1.Forms.Create", null);
            await workspace.DispatchAsync("9to1.Forms.AddText", null);
            var before = (await publications.ReadAsync(workspace.FormID!.Value)).Publication!;
            var window = await CuiSceneHost.CreateWindowAsync(new CuiNativeScene("regex-inspector", "Forms", "forms",
                FormsCuiWorkspace.LoadDocument(), workspace, workspace, new RegexReady()));
            using var host = Assert.IsType<CuiSceneHost>(window.Content);
            try
            {
                window.Show();
                TextBox Input(string label) => Assert.Single(host.GetVisualDescendants().OfType<TextBox>(), x => AutomationProperties.GetName(x) == label);
                ComboBox Choice(string label) => Assert.Single(host.GetVisualDescendants().OfType<ComboBox>(), x => AutomationProperties.GetName(x) == label);
                string Result() => Assert.Single(host.GetVisualDescendants().OfType<TextBlock>(), x => AutomationProperties.GetName(x) == "Regex test result").Text!;
                var pattern = Input("Regex test pattern"); var example = Input("Regex example input");
                pattern.Text = "CPU"; example.Text = "a CPU unit";
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                Assert.True(workspace.TryGetValue("RegexPattern", out var retainedPattern)); Assert.Equal("CPU", retainedPattern);
                Assert.True(workspace.TryGetValue("RegexExample", out var retainedExample)); Assert.Equal("a CPU unit", retainedExample);
                Assert.Equal("CPU", pattern.Text); Assert.Equal("a CPU unit", example.Text);
                Assert.Same(pattern, Input("Regex test pattern")); Assert.Same(example, Input("Regex example input"));
                Assert.Equal("Example does not match.", Result());
                Choice("Full or partial regex match").SelectedIndex = 1;
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                Assert.Equal("Example matches.", Result());
                pattern.Text = "[";
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                Assert.Equal("Invalid or unsupported pattern.", Result()); Assert.Equal("[", pattern.Text);
                pattern.Text = "CPU"; example.Text = "cpu";
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                Assert.Equal("Example does not match.", Result());
                Choice("Regex case matching").SelectedIndex = 1;
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                Assert.Equal("Example matches.", Result());
                var after = (await publications.ReadAsync(workspace.FormID.Value)).Publication!;
                Assert.Equal(before.Revision, after.Revision); Assert.Equal(before.Draft.GetRawText(), after.Draft.GetRawText());
            }
            finally { window.Close(); }
            return true;
        }, default);
    }
    public sealed class RegexApplication : Avalonia.Application
    {
        public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<RegexApplication>().UseSkia())
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
        public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this);
    }
    private sealed class RegexReady : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) =>
            ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "ready", "Ready"));
    }

    private static FormProject Decode(JsonElement value) => FormProjectCodec.Decode(Encoding.UTF8.GetBytes(value.GetRawText()));
    private sealed class Authority : IFormStoreCommitAuthority
    {
        public ValueTask<ISettingsCommitAdmission?> CaptureCommitAdmissionAsync(Guid storeID, Guid formID, long revision,
            string actionID, AuthenticatedResourceActor? expectedActor, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ISettingsCommitAdmission?>(new FixtureAdmission(() => Allowed));

        public bool Allowed { get; set; } = true;
        public ValueTask<bool> AuthorizeAsync(Guid storeID, Guid formID, long revision, string actionID, CancellationToken cancellationToken) => ValueTask.FromResult(Allowed);
    }
    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Directory.CreateTempSubdirectory("forms-cui-").FullName;
        public string DatabasePath => Path.Combine(DataDirectory, "data.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }
    private sealed class FixtureAdmission(Func<bool> allowed) : ISettingsCommitAdmission
    {
        public ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken cancellationToken) => ValueTask.FromResult(allowed());
    }


    private sealed class PublicationActor : IAuthenticatedResourceActorSource
    {
        private readonly AuthenticatedResourceActor _actor = new("forms-author", "forms-profile", null, null, "forms-login");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) => ValueTask.FromResult<AuthenticatedResourceActor?>(_actor);
    }
}
