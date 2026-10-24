using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Present;
using Haven.Infrastructure;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class PresentAdvancedAuthoringTests
{
    [Fact]
    public void Undo_and_redo_restore_the_operation_selection_by_stable_slide_and_object_ids()
    {
        var document = PresentDocument.Create("Selection history");
        var editor = new PresentEditor(document);
        var slideId = editor.Selection.SlideId;
        var first = editor.AddShape(slideId);
        var second = editor.AddShape(slideId);
        editor.SelectElements([first.Id, second.Id]);

        var third = editor.AddShape(slideId);
        Assert.Equal(new[] { third.Id }, editor.Selection.ElementIds);
        Assert.True(editor.Undo());
        Assert.Equal(slideId, editor.Selection.SlideId);
        Assert.Equal(new[] { first.Id, second.Id }, editor.Selection.ElementIds);
        Assert.DoesNotContain(editor.SelectedSlide.Elements, value => value.Id == third.Id);

        Assert.True(editor.Redo());
        Assert.Equal(slideId, editor.Selection.SlideId);
        Assert.Equal(new[] { third.Id }, editor.Selection.ElementIds);
        Assert.Contains(editor.SelectedSlide.Elements, value => value.Id == third.Id);
    }

    [Fact]
    public async Task Undo_and_redo_preserve_the_latest_durable_revision_for_following_repository_saves()
    {
        using var paths = new PhysicalPaths();
        var repository = new PresentRepository(paths);
        var document = PresentDocument.Create("History revision CAS");
        await repository.SaveAsync(document, "Create history revision fixture", TestContext.Current.CancellationToken);
        var editor = new PresentEditor(document);
        var slideId = editor.Selection.SlideId;
        var shape = editor.AddShape(slideId);
        await repository.SaveAsync(editor.Document, "Persist shape before undo", TestContext.Current.CancellationToken);
        var durableShapeRevision = editor.Document.Version;

        Assert.True(editor.Undo());
        Assert.Equal(durableShapeRevision, editor.Document.Version);
        Assert.DoesNotContain(editor.SelectedSlide.Elements, value => value.Id == shape.Id);
        await repository.SaveAsync(editor.Document, "Persist undo after durable shape", TestContext.Current.CancellationToken);
        var afterUndo = Assert.IsType<PresentDocument>(await repository.LoadAsync(document.Id, TestContext.Current.CancellationToken));
        Assert.Equal(durableShapeRevision + 1, afterUndo.Version);
        Assert.DoesNotContain(afterUndo.Slides.Single(value => value.Id == slideId).Elements, value => value.Id == shape.Id);

        Assert.True(editor.Redo());
        Assert.Equal(afterUndo.Version, editor.Document.Version);
        Assert.Contains(editor.SelectedSlide.Elements, value => value.Id == shape.Id);
        await repository.SaveAsync(editor.Document, "Persist redo after undo revision", TestContext.Current.CancellationToken);
        var afterRedo = Assert.IsType<PresentDocument>(await repository.LoadAsync(document.Id, TestContext.Current.CancellationToken));
        Assert.Equal(afterUndo.Version + 1, afterRedo.Version);
        Assert.Contains(afterRedo.Slides.Single(value => value.Id == slideId).Elements, value => value.Id == shape.Id);
    }

    [Fact]
    public void Canvas_multi_selection_is_normalized_to_document_order_before_entering_history()
    {
        var document = PresentDocument.Create("Deterministic selection order");
        var editor = new PresentEditor(document);
        var slide = editor.SelectedSlide;
        var first = editor.AddShape(slide.Id);
        var second = editor.AddShape(slide.Id);
        var third = editor.AddShape(slide.Id);

        var ordered = PresentPage.NormalizeCanvasSelectionOrder(slide,
            [third.Id, first.Id, third.Id, Guid.NewGuid()]);
        editor.SelectElements(ordered);
        Assert.Equal(new[] { first.Id, third.Id }, editor.Selection.ElementIds);
        Assert.True(editor.SetSelectedOpacity(.5));
        Assert.True(editor.Undo());
        Assert.Equal(new[] { first.Id, third.Id }, editor.Selection.ElementIds);
        Assert.Equal(1, editor.SelectedSlide.Elements.Single(value => value.Id == first.Id).Opacity);
        Assert.Equal(1, editor.SelectedSlide.Elements.Single(value => value.Id == third.Id).Opacity);
        Assert.Equal(1, editor.SelectedSlide.Elements.Single(value => value.Id == second.Id).Opacity);
    }

    [AvaloniaFact]
    public async Task Object_navigator_multi_selection_has_stable_order_before_it_enters_editor_history()
    {
        var document = PresentDocument.Create("Object navigator selection order");
        using var page = new PresentPage(new HavenEventBus(), new FakePresentRepository(document), new FakePresentExporter());
        await page.InitializeAsync();
        var window = new Window { Width = 1400, Height = 920, Content = page };
        try
        {
            window.Show();
            window.UpdateLayout();
            var editor = page.Editor!;
            var slideId = editor.Selection.SlideId;
            var first = editor.AddShape(slideId);
            var second = editor.AddShape(slideId);
            var third = editor.AddShape(slideId);
            editor.SelectElements([third.Id]);
            page.Route.SetPhase2Document(editor.Document, 0, editor.Selection.ElementIds, editor.CanUndo, editor.CanRedo);
            window.UpdateLayout();

            var firstRow = Assert.Single(page.Route.ObjectNavigator.Children.OfType<Haven.UI.Components.Button>()
                .Where(value => value.Name == $"Present.Object.{first.Id:N}"));
            Click(new HavenInputRouter(page.SceneRoot), firstRow);

            Assert.Equal(new[] { first.Id, third.Id }, editor.Selection.ElementIds);
            Assert.True(editor.SetSelectedOpacity(.5));
            Assert.True(editor.Undo());
            Assert.Equal(new[] { first.Id, third.Id }, editor.Selection.ElementIds);
            Assert.Equal(1, editor.SelectedSlide.Elements.Single(value => value.Id == second.Id).Opacity);
        }
        finally
        {
            Assert.True(await page.PrepareToCloseAsync(cancellationToken: TestContext.Current.CancellationToken));
            window.Content = null;
            window.Close();
            await page.CloseAndDrainAsync();
        }
    }

    [Fact]
    public void Live_text_edit_undo_and_redo_preserve_the_selected_text_object()
    {
        var document = PresentDocument.Create("Text selection history");
        var editor = new PresentEditor(document);
        var slideId = editor.Selection.SlideId;
        var text = editor.AddText(slideId, "before");
        editor.SelectElements([text.Id]);
        var notifications = 0;
        editor.Changed += (_, _) => ++notifications;

        Assert.True(editor.PreviewElementText(slideId, text.Id, "after"));
        Assert.Equal(0, notifications);
        Assert.True(editor.Undo());
        Assert.Equal(1, notifications);
        Assert.False(editor.IsLiveTextEditActive);
        Assert.Equal(slideId, editor.Selection.SlideId);
        Assert.Equal(new[] { text.Id }, editor.Selection.ElementIds);
        Assert.Equal("before", editor.SelectedSlide.Elements.Single(value => value.Id == text.Id).Text);

        Assert.True(editor.Redo());
        Assert.Equal(2, notifications);
        Assert.False(editor.IsLiveTextEditActive);
        Assert.Equal(slideId, editor.Selection.SlideId);
        Assert.Equal(new[] { text.Id }, editor.Selection.ElementIds);
        Assert.Equal("after", editor.SelectedSlide.Elements.Single(value => value.Id == text.Id).Text);
        Assert.False(editor.CancelLiveTextEdit());
        Assert.Equal("after", editor.SelectedSlide.Elements.Single(value => value.Id == text.Id).Text);
    }

    [Fact]
    public void Cancelling_a_live_text_edit_restores_its_original_selection()
    {
        var document = PresentDocument.Create("Cancelled text selection");
        var editor = new PresentEditor(document);
        var originalSlideId = editor.Selection.SlideId;
        var text = editor.AddText(originalSlideId, "kept");
        var otherSlide = editor.AddSlide(originalSlideId);
        var firstOtherObject = editor.AddShape(otherSlide.Id);
        var secondOtherObject = editor.AddShape(otherSlide.Id);
        Assert.True(editor.SelectSlide(originalSlideId));
        editor.SelectElements([text.Id]);
        Assert.True(editor.PreviewElementText(originalSlideId, text.Id, "redo target"));
        Assert.True(editor.CommitLiveTextEdit());
        Assert.True(editor.Undo());
        Assert.True(editor.CanRedo);
        var canUndoBefore = editor.CanUndo;
        var canRedoBefore = editor.CanRedo;

        Assert.True(editor.PreviewElementText(originalSlideId, text.Id, "discarded"));
        Assert.True(editor.SelectSlide(otherSlide.Id));
        editor.SelectElements([secondOtherObject.Id, firstOtherObject.Id]);
        Assert.True(editor.CancelLiveTextEdit());

        Assert.Equal(otherSlide.Id, editor.Selection.SlideId);
        Assert.Equal(new[] { secondOtherObject.Id, firstOtherObject.Id }, editor.Selection.ElementIds);
        Assert.Equal("kept", editor.Document.Slides.Single(value => value.Id == originalSlideId).Elements.Single(value => value.Id == text.Id).Text);
        Assert.False(editor.CancelLiveTextEdit());
        Assert.Equal(canUndoBefore, editor.CanUndo);
        Assert.Equal(canRedoBefore, editor.CanRedo);
        Assert.True(editor.Redo());
        Assert.Equal("redo target", editor.Document.Slides.Single(value => value.Id == originalSlideId).Elements.Single(value => value.Id == text.Id).Text);
    }

    [Fact]
    public void A_live_preview_committed_at_redo_boundary_is_kept_and_invalidates_the_stale_redo_branch()
    {
        var document = PresentDocument.Create("Redo boundary");
        var editor = new PresentEditor(document);
        var slideId = editor.Selection.SlideId;
        var text = editor.AddText(slideId, "before");
        editor.SelectElements([text.Id]);

        Assert.True(editor.PreviewElementText(slideId, text.Id, "first preview"));
        Assert.True(editor.Undo());
        Assert.True(editor.CanRedo);
        Assert.Equal("before", editor.SelectedSlide.Elements.Single(value => value.Id == text.Id).Text);
        Assert.True(editor.PreviewElementText(slideId, text.Id, "new preview"));

        Assert.True(editor.CanUndo);
        Assert.False(editor.CanRedo);
        var notifications = 0;
        editor.Changed += (_, _) => ++notifications;
        Assert.False(editor.Redo());
        Assert.Equal(1, notifications);
        Assert.False(editor.IsLiveTextEditActive);
        Assert.False(editor.CanRedo);
        Assert.Equal(slideId, editor.Selection.SlideId);
        Assert.Equal(new[] { text.Id }, editor.Selection.ElementIds);
        Assert.Equal("new preview", editor.SelectedSlide.Elements.Single(value => value.Id == text.Id).Text);
        Assert.True(editor.Undo());
        Assert.Equal("before", editor.SelectedSlide.Elements.Single(value => value.Id == text.Id).Text);
        Assert.True(editor.Redo());
        Assert.Equal("new preview", editor.SelectedSlide.Elements.Single(value => value.Id == text.Id).Text);
    }

    [Fact]
    public void Previewed_slide_title_uses_the_same_undo_redo_boundary_as_live_element_text()
    {
        var document = PresentDocument.Create("Slide title history");
        var editor = new PresentEditor(document);
        var slideId = editor.Selection.SlideId;
        var originalTitle = editor.SelectedSlide.Title;

        Assert.False(editor.CanUndo);
        Assert.True(editor.PreviewSlideTitle(slideId, "Revision notes"));
        Assert.True(editor.CanUndo);
        Assert.False(editor.CanRedo);
        Assert.True(editor.Undo());
        Assert.False(editor.IsLiveTextEditActive);
        Assert.Equal(originalTitle, editor.SelectedSlide.Title);
        Assert.False(editor.CanUndo);
        Assert.True(editor.CanRedo);
        Assert.True(editor.Redo());
        Assert.Equal("Revision notes", editor.SelectedSlide.Title);
        Assert.False(editor.CancelLiveTextEdit());
        Assert.Equal("Revision notes", editor.SelectedSlide.Title);
    }

    [Fact]
    public void Speaker_notes_previews_commit_once_and_cancellation_preserves_later_selection()
    {
        var document = PresentDocument.Create("Speaker notes history");
        var editor = new PresentEditor(document);
        var originalSlide = editor.SelectedSlide;
        originalSlide.SpeakerNotes = "Original notes";
        var otherSlide = editor.AddSlide(originalSlide.Id);
        Assert.True(editor.SelectSlide(originalSlide.Id));
        var initialSelection = editor.Selection;

        Assert.True(editor.PreviewSpeakerNotes(originalSlide.Id, "Revised notes"));
        Assert.True(editor.PreviewSpeakerNotes(originalSlide.Id, "Final notes"));
        Assert.True(editor.IsLiveTextEditActive);
        Assert.True(editor.CanUndo);
        Assert.True(editor.CommitLiveTextEdit());
        Assert.False(editor.IsLiveTextEditActive);
        Assert.Equal(initialSelection.SlideId, editor.Selection.SlideId);
        Assert.Equal(initialSelection.ElementIds, editor.Selection.ElementIds);
        Assert.Equal("Final notes", editor.Document.Slides.Single(value => value.Id == originalSlide.Id).SpeakerNotes);

        Assert.True(editor.Undo());
        Assert.Equal("Original notes", editor.Document.Slides.Single(value => value.Id == originalSlide.Id).SpeakerNotes);
        Assert.True(editor.Redo());
        Assert.Equal("Final notes", editor.Document.Slides.Single(value => value.Id == originalSlide.Id).SpeakerNotes);

        Assert.True(editor.PreviewSpeakerNotes(originalSlide.Id, "Discard this draft"));
        Assert.True(editor.SelectSlide(otherSlide.Id));
        Assert.True(editor.CancelLiveTextEdit());
        Assert.Equal(otherSlide.Id, editor.Selection.SlideId);
        Assert.Equal("Final notes", editor.Document.Slides.Single(value => value.Id == originalSlide.Id).SpeakerNotes);
    }

    [Fact]
    public void Document_title_previews_share_live_history_and_returning_to_original_is_a_no_op()
    {
        var document = PresentDocument.Create("Title history");
        var editor = new PresentEditor(document);
        Assert.True(editor.PreviewDocumentTitle("Draft title"));
        Assert.True(editor.PreviewDocumentTitle("Final title"));
        Assert.True(editor.CanUndo);
        Assert.True(editor.CommitLiveTextEdit());
        Assert.Equal("Final title", editor.Document.Title);
        Assert.True(editor.Undo());
        Assert.Equal("Title history", editor.Document.Title);
        Assert.True(editor.Redo());
        Assert.Equal("Final title", editor.Document.Title);

        var canUndoBeforeNoOp = editor.CanUndo;
        var canRedoBeforeNoOp = editor.CanRedo;
        Assert.True(editor.PreviewDocumentTitle("Temporary title"));
        Assert.True(editor.PreviewDocumentTitle("Final title"));
        Assert.Equal(canUndoBeforeNoOp, editor.CanUndo);
        Assert.Equal(canRedoBeforeNoOp, editor.CanRedo);
        Assert.False(editor.CommitLiveTextEdit());
        Assert.Equal("Final title", editor.Document.Title);
    }

    [AvaloniaFact]
    public async Task Speaker_notes_input_groups_keystroke_previews_until_slide_navigation_into_one_undo_step()
    {
        var document = PresentDocument.Create("Speaker notes input history");
        document.Slides[0].SpeakerNotes = "Original";
        document.Slides.Add(PresentSlide.Create(1));
        document.Normalize();
        using var page = new PresentPage(new HavenEventBus(), new FakePresentRepository(document), new FakePresentExporter());
        await page.InitializeAsync();
        var window = new Window { Width = 1400, Height = 920, Content = page };
        try
        {
            window.Show(); window.UpdateLayout();
            page.Route.NotesInput.Text = "Draft one";
            page.Route.NotesInput.Text = "Draft two";
            Assert.True(page.Editor!.IsLiveTextEditActive);
            Assert.True(page.Editor.CanUndo);
            Assert.Equal("Draft two", page.Document!.Slides[0].SpeakerNotes);

            Click(new HavenInputRouter(page.SceneRoot), page.Route.NextSlideButton);
            Assert.False(page.Editor.IsLiveTextEditActive);
            Click(new HavenInputRouter(page.SceneRoot), page.Route.PreviousSlideButton);
            Assert.True(page.Editor.Undo());
            Assert.Equal("Original", page.Document.Slides[0].SpeakerNotes);
            Assert.Equal("Original", page.Route.NotesInput.Text);
        }
        finally
        {
            Assert.True(await page.PrepareToCloseAsync(cancellationToken: TestContext.Current.CancellationToken));
            window.Content = null; window.Close(); await page.CloseAndDrainAsync();
        }
    }

    [AvaloniaFact]
    public async Task Document_title_input_groups_keystroke_previews_until_slide_navigation_into_one_undo_step()
    {
        var document = PresentDocument.Create("Original title");
        document.Slides.Add(PresentSlide.Create(1));
        document.Normalize();
        using var page = new PresentPage(new HavenEventBus(), new FakePresentRepository(document), new FakePresentExporter());
        await page.InitializeAsync();
        var window = new Window { Width = 1400, Height = 920, Content = page };
        try
        {
            window.Show(); window.UpdateLayout();
            page.Route.DeckTitleInput.Text = "Draft one";
            page.Route.DeckTitleInput.Text = "Final title";
            Assert.True(page.Editor!.IsLiveTextEditActive);
            Assert.True(page.Editor.CanUndo);
            Assert.Equal("Final title", page.Document!.Title);

            Click(new HavenInputRouter(page.SceneRoot), page.Route.NextSlideButton);
            Assert.False(page.Editor.IsLiveTextEditActive);
            Click(new HavenInputRouter(page.SceneRoot), page.Route.PreviousSlideButton);
            Assert.True(page.Editor.Undo());
            Assert.Equal("Original title", page.Document!.Title);
            Assert.Equal("Original title", page.Route.DeckTitleInput.Text);
        }
        finally
        {
            Assert.True(await page.PrepareToCloseAsync(cancellationToken: TestContext.Current.CancellationToken));
            window.Content = null; window.Close(); await page.CloseAndDrainAsync();
        }
    }

    [Fact]
    public void Returning_a_preview_to_its_original_value_does_not_offer_a_spurious_undo()
    {
        var document = PresentDocument.Create("No-op preview");
        var editor = new PresentEditor(document);
        var slideId = editor.Selection.SlideId;
        var originalTitle = editor.SelectedSlide.Title;

        Assert.True(editor.PreviewSlideTitle(slideId, "Temporary title"));
        Assert.True(editor.CanUndo);
        Assert.True(editor.PreviewSlideTitle(slideId, originalTitle));
        Assert.False(editor.CanUndo);
        Assert.False(editor.CanRedo);
        Assert.False(editor.CommitLiveTextEdit());
        Assert.False(editor.Undo());
        Assert.Equal(originalTitle, editor.SelectedSlide.Title);
    }

    [Fact]
    public void Present_text_search_and_replace_use_stable_text_and_notes_targets_with_one_undo_step()
    {
        var document = PresentDocument.Create("Search revision");
        var slide = document.Slides[0];
        var editable = new PresentElement { Kind = PresentElementKind.Text, Text = "Alpha alpha" };
        var locked = new PresentElement { Kind = PresentElementKind.Text, Text = "Alpha locked", Locked = true };
        slide.Elements.Add(editable); slide.Elements.Add(locked); slide.SpeakerNotes = "Speaker ALPHA";
        var secondSlide = PresentSlide.Create(1);
        secondSlide.Elements.Add(new PresentElement { Kind = PresentElementKind.Text, Text = "Beta only" });
        document.Slides.Add(secondSlide); document.Normalize();

        var editor = new PresentEditor(document);
        editor.SelectSlide(slide.Id); editor.SelectElements([editable.Id, locked.Id]);
        var matches = editor.SearchText("alpha", PresentTextSearchCaseMode.Insensitive);
        Assert.Equal(4, matches.Count);
        Assert.Equal((slide.Id, editable.Id, PresentTextSearchTargetKind.SlideElement, 0, 5, "Alpha"),
            (matches[0].SlideId, matches[0].TargetId, matches[0].TargetKind, matches[0].Start, matches[0].Length, matches[0].MatchedText));
        Assert.Equal((slide.Id, editable.Id, PresentTextSearchTargetKind.SlideElement, 6, 5, "alpha"),
            (matches[1].SlideId, matches[1].TargetId, matches[1].TargetKind, matches[1].Start, matches[1].Length, matches[1].MatchedText));
        Assert.Equal(slide.NotesId, matches[3].TargetId);
        Assert.Equal(PresentTextSearchTargetKind.SpeakerNotes, matches[3].TargetKind);
        Assert.Equal(8, matches[3].Start);
        var caseSensitiveMatches = editor.SearchText("alpha", PresentTextSearchCaseMode.Sensitive);
        Assert.Single(caseSensitiveMatches);
        Assert.Equal(editable.Id, caseSensitiveMatches[0].TargetId);
        Assert.Equal(6, caseSensitiveMatches[0].Start);
        Assert.Throws<ArgumentException>(() => editor.SearchText(string.Empty, PresentTextSearchCaseMode.Insensitive));

        var result = editor.ReplaceAllText("alpha", "beta", PresentTextSearchCaseMode.Insensitive);
        Assert.Equal("Replaced", result.Code); Assert.Equal(3, result.ReplacedMatches);
        Assert.Equal(new[] { editable.Id }, result.ChangedElementIds);
        Assert.Equal(new[] { slide.NotesId }, result.ChangedNotesIds);
        Assert.Equal(new[] { locked.Id }, result.SkippedLockedElementIds);
        Assert.Equal("beta beta", editor.Document.Slides.Single(value => value.Id == slide.Id).Elements.Single(value => value.Id == editable.Id).Text);
        Assert.Equal("Alpha locked", editor.Document.Slides.Single(value => value.Id == slide.Id).Elements.Single(value => value.Id == locked.Id).Text);
        Assert.Equal("Speaker beta", editor.Document.Slides.Single(value => value.Id == slide.Id).SpeakerNotes);
        Assert.True(editor.Undo());
        Assert.Equal(new[] { editable.Id, locked.Id }, editor.Selection.ElementIds);
        Assert.Equal("Alpha alpha", editor.Document.Slides.Single(value => value.Id == slide.Id).Elements.Single(value => value.Id == editable.Id).Text);
        Assert.Equal("Speaker ALPHA", editor.Document.Slides.Single(value => value.Id == slide.Id).SpeakerNotes);
        Assert.True(editor.Redo());
        Assert.Equal("beta beta", editor.Document.Slides.Single(value => value.Id == slide.Id).Elements.Single(value => value.Id == editable.Id).Text);
    }

    [Fact]
    public void Present_search_navigation_selects_current_object_or_notes_range_and_refuses_stale_results()
    {
        var document = PresentDocument.Create("Search navigation");
        var editor = new PresentEditor(document);
        var firstSlide = editor.SelectedSlide;
        var anchor = editor.SelectedSlide.GetOrCreateBodyText();
        anchor.Text = "Other text";
        var secondSlide = editor.AddSlide(firstSlide.Id);
        var target = editor.AddText(secondSlide.Id, "Navigate to this phrase");
        secondSlide.SpeakerNotes = "Speaker phrase remains addressable";
        Assert.True(editor.SelectSlide(firstSlide.Id));
        editor.SelectElements([anchor.Id]);
        var beforeDocument = System.Text.Json.JsonSerializer.Serialize(editor.Document);
        var canUndoBefore = editor.CanUndo;
        var objectMatch = Assert.Single(editor.SearchText("this phrase", PresentTextSearchCaseMode.Sensitive));

        var objectNavigation = editor.NavigateToSearchMatch(objectMatch);
        Assert.True(objectNavigation.Navigated); Assert.Equal("Navigated", objectNavigation.Code);
        Assert.Equal(secondSlide.Id, editor.Selection.SlideId);
        Assert.Equal(new[] { target.Id }, editor.Selection.ElementIds);

        var notesMatch = Assert.Single(editor.SearchText("phrase remains", PresentTextSearchCaseMode.Sensitive));
        Assert.Equal(PresentTextSearchTargetKind.SpeakerNotes, notesMatch.TargetKind);
        Assert.Equal(secondSlide.NotesId, notesMatch.TargetId);
        var notesNavigation = editor.NavigateToSearchMatch(notesMatch);
        Assert.True(notesNavigation.Navigated);
        Assert.Equal(secondSlide.Id, editor.Selection.SlideId);
        Assert.Empty(editor.Selection.ElementIds);
        Assert.Equal((15, 14), (notesMatch.Start, notesMatch.Length));
        Assert.Equal(beforeDocument, System.Text.Json.JsonSerializer.Serialize(editor.Document));
        Assert.Equal(canUndoBefore, editor.CanUndo);

        target.Text = "The phrase has changed";
        var selectionBeforeStale = editor.Selection;
        var stale = editor.NavigateToSearchMatch(objectMatch);
        Assert.False(stale.Navigated); Assert.Equal("StaleSearchResult", stale.Code);
        Assert.Equal(selectionBeforeStale.SlideId, editor.Selection.SlideId);
        Assert.Equal(selectionBeforeStale.ElementIds, editor.Selection.ElementIds);

        Assert.True(editor.PreviewSpeakerNotes(secondSlide.Id, "Draft notes"));
        var editing = editor.NavigateToSearchMatch(notesMatch);
        Assert.False(editing.Navigated); Assert.Equal("TextEditInProgress", editing.Code);
        Assert.True(editor.CancelLiveTextEdit());
    }

    [Fact]
    public void Deleting_the_active_slide_moves_focus_deterministically_and_history_restores_both_selections()
    {
        var document = PresentDocument.Create("Slide selection history");
        var editor = new PresentEditor(document);
        var originalSlideId = editor.Selection.SlideId;
        var title = editor.AddText(originalSlideId, "Keep this selected");
        var nextSlide = editor.AddSlide(originalSlideId);
        Assert.True(editor.SelectSlide(originalSlideId));
        editor.SelectElements([title.Id]);

        Assert.True(editor.DeleteSlide(originalSlideId));
        Assert.Equal(nextSlide.Id, editor.Selection.SlideId);
        Assert.Empty(editor.Selection.ElementIds);
        Assert.True(editor.Undo());
        Assert.Equal(originalSlideId, editor.Selection.SlideId);
        Assert.Equal(new[] { title.Id }, editor.Selection.ElementIds);
        Assert.True(editor.Redo());
        Assert.Equal(nextSlide.Id, editor.Selection.SlideId);
        Assert.Empty(editor.Selection.ElementIds);
    }

    [Fact]
    public void Present_editor_design_transition_animation_and_media_settings_are_real_undoable_state()
    {
        var document = PresentDocument.Create("Advanced deck");
        var editor = new PresentEditor(document);
        var slideId = document.Slides[0].Id;

        Assert.True(editor.ApplyThemePreset("Midnight"));
        Assert.Equal("Midnight", document.Theme.Name);
        Assert.True(editor.SetSlideSizePreset(PresentSlideSizePreset.Standard4By3));
        Assert.Equal(PresentSlideSizePreset.Standard4By3, document.SlideSize.Preset);

        var slide = document.Slides[0];
        slide.LayoutId = null;
        var layout = Assert.Single(document.Layouts.Take(1));
        Assert.True(editor.SetSlideLayout(slideId, layout.Id));
        Assert.Equal(layout.Id, slide.LayoutId);
        Assert.True(editor.SetSlideBackgroundColor(slideId, "#111827"));
        Assert.Equal(PresentBackgroundKind.Solid, slide.Background.Kind);
        Assert.True(editor.SetSlideTransition(slideId, PresentTransitionKind.Push, .75, PresentEasingKind.EaseOut, PresentMotionDirection.Left));
        Assert.Equal(PresentTransitionKind.Push, slide.Transition.Kind);
        Assert.Equal(.75, slide.Transition.DurationSeconds, 3);

        var shape = editor.AddShape(slideId);
        editor.SelectElements([shape.Id]);
        var cues = editor.AddAnimationToSelection(PresentAnimationEffect.Fly, PresentAnimationTrigger.OnClick, .5, PresentMotionDirection.Up);
        Assert.Single(cues);
        Assert.Equal(shape.Id, slide.Animations.Single().TargetElementId);
        Assert.True(editor.SetSelectedAlternativeText("Diagram callout"));
        Assert.Equal("Diagram callout", shape.AlternativeText);

        var media = editor.AddMedia(slideId, "asset-video", "video/mp4", "Demo clip");
        editor.SelectElements([media.Id]);
        Assert.True(editor.SetSelectedMediaPlayback(autoPlay: true, loop: true, startSeconds: 2.5, endSeconds: 18));
        Assert.True(media.Media.AutoPlay);
        Assert.True(media.Media.Loop);
        Assert.Equal(2.5, media.Media.StartSeconds, 3);
        Assert.Equal(18, media.Media.EndSeconds);

        Assert.True(editor.SetSlideHidden(slideId, true));
        Assert.True(document.Slides[0].Hidden);
        Assert.True(editor.Undo());
        Assert.False(editor.Document.Slides.Single(value => value.Id == slideId).Hidden);
    }

    [AvaloniaFact]
    public async Task Present_advanced_inspector_changes_theme_transition_hidden_state_and_adds_animation()
    {
        var document = PresentDocument.Create("Inspector deck");
        var body = document.Slides[0].GetOrCreateBodyText();
        body.Text = "Animate this";
        using var page = new PresentPage(new HavenEventBus(), new FakePresentRepository(document), new FakePresentExporter());
        await page.InitializeAsync();
        var window = new Window { Width = 1400, Height = 920, Content = page };
        try
        {
            window.Show();
            window.UpdateLayout();

            page.Route.ThemeSelect.SelectedIndex = Array.IndexOf(page.Route.ThemeSelect.Items.ToArray(), "Midnight");
            Assert.Equal("Midnight", page.Document!.Theme.Name);

            page.Route.TransitionSelect.SelectedIndex = (int)PresentTransitionKind.Fade;
            Assert.Equal(PresentTransitionKind.Fade, page.Document.Slides[0].Transition.Kind);
            page.Route.HiddenSlideToggle.IsChecked = true;
            Assert.True(page.Document.Slides[0].Hidden);
            page.Route.HiddenSlideToggle.IsChecked = false;

            page.Editor!.SelectElements([body.Id]);
            page.Route.SetPhase2Document(page.Document, 0, page.Editor.Selection.ElementIds, page.Editor.CanUndo, page.Editor.CanRedo);
            Assert.Equal(HavenAccessibleRole.Image, page.Route.SlideCanvas.Accessibility.Role);
            Assert.True(page.Route.SlideCanvas.Accessibility.Focusable);
            Assert.Equal("Editable presentation slide canvas", page.Route.SlideCanvas.Accessibility.AccessibleName);
            Assert.False(string.IsNullOrWhiteSpace(page.Route.SlideCanvas.Accessibility.Description));
            var slideRouter = new HavenInputRouter(page.SceneRoot);
            slideRouter.Focus(page.Route.SlideCanvas);
            Assert.True(slideRouter.KeyDown(HavenKey.Enter, new HavenInputModifiers()));
            Assert.True(slideRouter.TextInput(" accessible"));
            Assert.True(slideRouter.KeyDown(HavenKey.Enter, new HavenInputModifiers(Control: true)));
            Assert.Contains(" accessible", page.Document.Slides[0].Elements.Single(element => element.Id == body.Id).Text, StringComparison.Ordinal);
            page.Route.AnimationEffectSelect.SelectedIndex = (int)PresentAnimationEffect.Appear;
            page.Route.AnimationTriggerSelect.SelectedIndex = (int)PresentAnimationTrigger.OnClick;
            window.UpdateLayout();
            page.Route.InspectorPane.ScrollY = page.Route.InspectorPane.MaxScrollY;
            window.UpdateLayout();
            Click(new HavenInputRouter(page.SceneRoot), page.Route.AddAnimationButton);
            Assert.Contains(page.Document.Slides[0].Animations, cue => cue.TargetElementId == body.Id && cue.Effect == PresentAnimationEffect.Appear);
            Assert.Equal(HavenAccessibleRole.List, page.Route.ThemeSelect.Accessibility.Role);
            Assert.False(string.IsNullOrWhiteSpace(page.Route.ThemeSelect.Accessibility.AccessibleName));
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Present_mode_opens_real_presenter_reveals_animation_then_advances_and_exits()
    {
        var document = PresentDocument.Create("Playback deck");
        var first = document.Slides[0];
        first.Title = "Opening";
        first.SpeakerNotes = "Explain the evidence";
        var body = first.GetOrCreateBodyText();
        body.Text = "Animated point";
        first.Animations.Add(new PresentAnimationCue
        {
            TargetElementId = body.Id,
            Effect = PresentAnimationEffect.Appear,
            Trigger = PresentAnimationTrigger.OnClick,
            Order = 0
        });
        var second = PresentSlide.Create(1);
        second.Title = "Second slide";
        second.GetOrCreateBodyText().Text = "Follow-up";
        document.Slides.Add(second);
        document.Normalize();

        using var page = new PresentPage(new HavenEventBus(), new FakePresentRepository(document), new FakePresentExporter());
        await page.InitializeAsync();
        var window = new Window { Width = 1400, Height = 920, Content = page };
        try
        {
            window.Show();
            window.UpdateLayout();
            var router = new HavenInputRouter(page.SceneRoot);

            Click(router, page.Route.PresentButton);
            window.UpdateLayout();
            Assert.Equal(HavenVisibility.Visible, page.Route.PresenterHost.GetValue<HavenVisibility>(HavenProperties.Visibility));
            Assert.Contains("Slide 1 of 2", page.Route.PresenterStatus.Content, StringComparison.Ordinal);
            Assert.Contains("reveal 0/1", page.Route.PresenterStatus.Content, StringComparison.Ordinal);
            Assert.Equal("Explain the evidence", page.Route.PresenterNotes.Content);
            Assert.DoesNotContain(new HavenSceneRenderer().Render(page.SceneRoot).OfType<HavenTextCommand>(), command => command.Layout.Text == "Animated point");

            Click(router, page.Route.PresenterAdvanceButton);
            window.UpdateLayout();
            Assert.Contains("reveal 1/1", page.Route.PresenterStatus.Content, StringComparison.Ordinal);
            Assert.Contains(new HavenSceneRenderer().Render(page.SceneRoot).OfType<HavenTextCommand>(), command => command.Layout.Text == "Animated point");

            Click(router, page.Route.PresenterAdvanceButton);
            window.UpdateLayout();
            Assert.Contains("Slide 2 of 2", page.Route.PresenterStatus.Content, StringComparison.Ordinal);
            Assert.Contains(new HavenSceneRenderer().Render(page.SceneRoot).OfType<HavenTextCommand>(), command => command.Layout.Text == "Follow-up");

            Click(router, page.Route.PresenterExitButton);
            window.UpdateLayout();
            Assert.Equal(HavenVisibility.Collapsed, page.Route.PresenterHost.GetValue<HavenVisibility>(HavenProperties.Visibility));
            Assert.Equal(HavenVisibility.Visible, page.Route.SlideCanvas.GetValue<HavenVisibility>(HavenProperties.Visibility));
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static void Click(HavenInputRouter router, HavenElement element)
    {
        var point = new HavenPoint(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);
        var hit = router.HitTest(point);
        Assert.True(ReferenceEquals(element, hit), $"Expected pointer hit {element.Name}, hit {hit?.Name ?? "<none>"}. Bounds {element.Bounds}.");
        router.PointerPressed(point);
        Assert.True(router.PointerReleased(point));
    }

    private sealed class PhysicalPaths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "present-history-revision-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "Browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }

    private sealed class FakePresentRepository(params PresentDocument[] documents) : IPresentRepository
    {
        private readonly List<PresentDocument> _documents = [.. documents];
        public Task<IReadOnlyList<PresentDocumentSummary>> ListAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<PresentDocumentSummary> result = _documents.Select(value => new PresentDocumentSummary(value.Id, value.Title, value.UpdatedAt, value.Version, value.Slides.Count, false)).ToArray();
            return Task.FromResult(result);
        }
        public Task<PresentDocument?> LoadAsync(Guid documentId, CancellationToken cancellationToken) => Task.FromResult(_documents.FirstOrDefault(value => value.Id == documentId));
        public Task<PresentSaveResult> SaveAsync(PresentDocument document, string reason, CancellationToken cancellationToken)
        {
            var index = _documents.FindIndex(value => value.Id == document.Id);
            if (index < 0) _documents.Add(document); else _documents[index] = document;
            document.Version++;
            var root = Path.Combine(Path.GetTempPath(), "present-advanced-fake");
            return Task.FromResult(new PresentSaveResult(document.Id, document.Version, DateTimeOffset.UtcNow, Path.Combine(root, "current.json"), Path.Combine(root, "previous.json")));
        }
        public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken) { _documents.RemoveAll(value => value.Id == documentId); return Task.CompletedTask; }
    }

    private sealed class FakePresentExporter : IPresentExportService
    {
        public IReadOnlyList<string> ExportExtensions { get; } = [".pptx"];
        public Task<string> ExportAsync(PresentDocument document, string destinationPath, CancellationToken cancellationToken) => Task.FromResult(destinationPath);
    }
}
