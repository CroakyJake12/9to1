using System.Collections.Generic;
using CakeOS.Cui;
using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperWorkbenchCuiDocumentTests
{
    [Fact]
    public void Git_inspection_buttons_use_actual_shared_availability_and_common_cancellable_output_surface()
    {
        var nodes = Descendants(DeveloperWorkbenchCuiDocument.Load().Components).ToArray();
        foreach (var id in new[] { "dev-git-status", "dev-git-diff", "dev-git-branches" })
        {
            var button = Assert.Single(nodes, node => node.Name == id);
            Assert.Contains(button.Properties.Values, value => value is CuiBindingValue binding && binding.Path == "CanInspectGit");
        }
        var stop = Assert.Single(nodes, node => node.Name == "dev-stop-manual-run");
        Assert.Contains(stop.Actions.Values, action => action.Name == "dev.stop-manual-run");
        Assert.Contains(stop.Properties.Values, value => value is CuiBindingValue binding && binding.Path == "CanStopManualRun");
    }

    [Fact]
    public void Draft_line_ending_inspection_and_conversions_are_real_actions_with_current_draft_status()
    {
        var nodes = Descendants(DeveloperWorkbenchCuiDocument.Load().Components).ToArray();
        foreach (var name in new[] { "check-line-endings", "use-lf-endings", "use-crlf-endings" })
        {
            var command = Assert.Single(nodes, node => node.Name == "dev-" + name);
            Assert.Contains(command.Actions.Values, action => action.Name == "dev." + name);
        }
        Assert.Contains(nodes, node => node.Properties.Values.Any(value => value is CuiBindingValue binding && binding.Path == "LineEndingsStatus"));
    }

    [Fact]
    public void Designer_Text_child_controls_edit_inputs_and_stage_the_same_source_draft()
    {
        var nodes = Descendants(DeveloperWorkbenchCuiDocument.Load().Components).ToArray();
        foreach (var (id, binding) in new[] { ("dev-cui-new-text-id", "CuiNewTextId"), ("dev-cui-new-text-value", "CuiNewTextValue") })
        {
            var input = Assert.Single(nodes, node => node.Name == id); Assert.Equal("Input", input.Type);
            Assert.Contains(input.Properties.Values, value => value is CuiBindingValue actual && actual.Path == binding);
        }
        var add = Assert.Single(nodes, node => node.Name == "dev-add-cui-text-child");
        Assert.Contains(add.Actions.Values, action => action.Name == "dev.add-cui-text-child");
        Assert.Contains(add.Properties.Values, value => value is CuiBindingValue binding && binding.Path == "CanAddCuiTextChild");
    }

    [Fact]
    public void Current_project_go_to_file_has_bound_query_results_and_actual_open_and_page_actions()
    {
        var nodes = Descendants(DeveloperWorkbenchCuiDocument.Load().Components).ToArray();
        var input = Assert.Single(nodes, node => node.Name == "dev-file-navigation-query");
        Assert.Equal("Input", input.Type);
        Assert.Contains(input.Properties.Values, value => value is CuiBindingValue binding && binding.Path == "FileNavigationQuery");
        var results = Assert.Single(nodes, node => node.Name == "dev-file-navigation-matches");
        Assert.NotNull(results.Repeat); Assert.Equal("fileMatch", results.Repeat!.ItemName);
        foreach (var name in new[] { "find-project-file", "open-file-navigation-result", "file-navigation-previous", "file-navigation-next" })
        {
            var node = Assert.Single(nodes, value => value.Name == "dev-" + name);
            Assert.Contains(node.Actions.Values, action => action.Name == "dev." + name);
        }
    }

    [Fact]
    public void Static_draft_preview_has_a_typed_native_slot_explicit_subset_and_actual_diagnostics()
    {
        var nodes = Descendants(DeveloperWorkbenchCuiDocument.Load().Components).ToArray();
        var preview = Assert.Single(nodes, node => node.Name == "dev-cui-draft-preview");
        Assert.Equal("Object", preview.Type);
        Assert.True(preview.TryGetLiteralAttribute("type", out var renderer)); Assert.Equal("DeveloperCuiDraftPreview", renderer);
        var action = Assert.Single(nodes, node => node.Name == "dev-preview-cui-draft");
        Assert.Contains(action.Actions.Values, value => value.Name == "dev.preview-cui-draft");
        Assert.Contains(action.Properties.Values, value => value is CuiBindingValue binding && binding.Path == "CanPreviewCuiDraft");
        foreach (var binding in new[] { "CuiDraftPreviewSubset", "CuiDraftPreviewStatus", "CuiDraftPreviewDiagnostics" })
            Assert.Contains(nodes, node => node.Properties.Values.Any(value => value is CuiBindingValue actual && actual.Path == binding));
    }

    [Fact]
    public void Reported_problems_use_typed_current_selection_and_explicit_source_limits()
    {
        var nodes = Descendants(DeveloperWorkbenchCuiDocument.Load().Components).ToArray();
        var button = Assert.Single(nodes, node => node.Name == "dev-open-build-problem");
        Assert.Contains(button.Actions.Values, action => action.Name == "dev.open-build-problem");
        Assert.Contains(button.Properties.Values, value => value is CuiBindingValue binding && binding.Path == "problem.CanOpen");
        var repeat = Assert.Single(nodes, node => node.Name == "dev-build-problems");
        Assert.NotNull(repeat.Repeat); Assert.Equal("problem", repeat.Repeat!.ItemName);
        Assert.Equal("BuildProblems", Assert.IsType<CuiBindingValue>(repeat.Repeat.Source).Path);
        Assert.Contains(nodes, node => node.Properties.Values.Any(value => value is CuiBindingValue binding && binding.Path == "BuildProblemsStatus"));
    }

    [Fact]
    public void Maintained_document_parses_and_exposes_all_real_project_editor_and_command_actions()
    {
        var document = DeveloperWorkbenchCuiDocument.Load();
        var nodes = Descendants(document.Components).ToArray();
        var commands = nodes.SelectMany(value => value.Actions.Values).Select(value => value.Name).ToArray();
        string[] expected = [
            "dev.refresh", "dev.tree", "dev.git-status", "dev.git-diff",
            "dev.git-branches", "dev.project-folder-up", "dev.open-project-entry", "dev.project-entries-previous",
            "dev.project-entries-next", "dev.review-new-source-file", "dev.create-new-source-file", "dev.find-project-file", "dev.open-file-navigation-result", "dev.file-navigation-previous", "dev.file-navigation-next", "dev.open-source", "dev.search", "dev.open-search-result",
            "dev.select-source-tab", "dev.close-source-tab", "dev.toggle-source-tab-pin", "dev.inspect-recovered-draft",
            "dev.use-merged-recovered-draft", "dev.reconcile-recovered-draft", "dev.go-to-source-location", "dev.find-previous-source",
            "dev.find-next-source", "dev.replace-source-match", "dev.replace-all-source", "dev.undo-source-edit",
            "dev.redo-source-edit", "dev.analyze-cui-draft", "dev.preview-cui-draft", "dev.go-to-cui-problem", "dev.go-to-cui-element", "dev.select-cui-property", "dev.change-cui-property", "dev.add-cui-text-child", "dev.check-line-endings", "dev.use-lf-endings", "dev.use-crlf-endings", "dev.preview-edit",
            "dev.apply-edit", "dev.save-reviewed", "dev.revert-pass", "dev.select-run-target", "dev.new-run-target", "dev.edit-run-target", "dev.review-run-target", "dev.save-run-target", "dev.run-selected-target", "dev.stop-manual-run", "dev.run-command",
            "dev.run-tests", "dev.select-build-stage", "dev.run-stage", "dev.open-build-problem", "dev.create-branch",
            "dev.switch-branch"];
        Assert.Equal(expected.OrderBy(value => value), commands.OrderBy(value => value));
        Assert.Equal(nodes.Length, nodes.Select(value => value.StableId).Distinct().Count());
    }
    [Fact]
    public void Exact_pass_and_process_output_use_trusted_readonly_objects_while_the_editor_remains_writable()
    {
        var nodes = Descendants(DeveloperWorkbenchCuiDocument.Load().Components).ToArray();
        var expectedBindings = new Dictionary<string, string>
        {
            ["dev-pass-before"] = "BeforeText",
            ["dev-pass-after"] = "AfterText",
            ["dev-terminal-output"] = "ProcessOutput",
            ["dev-recovery-current"] = "RecoveredCurrentSource",
            ["dev-recovery-retained"] = "RecoveredRetainedDraft",
        };
        foreach (var (id, binding) in expectedBindings)
        {
            var node = Assert.Single(nodes, value => value.Name == id);
            Assert.Equal("Object", node.Type);
            // The neutral shared readonly source contract, not a Host/Dev-specific renderer name.
            Assert.True(node.TryGetLiteralAttribute("type", out var actual));
            Assert.Equal("ReadOnlyText", actual);
            Assert.Contains(node.Properties, pair => pair.Key.Equals("font-family", StringComparison.OrdinalIgnoreCase)
                && pair.Value is CuiLiteralValue font && font.Value == "monospace");
            // Read-only is structural to the shared renderer contract, never authored per-node, so a
            // writable/driver edge can never turn the source review into an editable surface.
            Assert.DoesNotContain(node.Properties.Keys, value => value.Equals("readonly", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(node.Properties.Keys, value => value.Equals("value", StringComparison.OrdinalIgnoreCase));
            // The exact initial/live owner binding is preserved so Before/After/ProcessOutput always
            // reflect the genuine canonical owner state (never a duplicate or sanitised copy).
            Assert.Contains(node.Properties, pair => pair.Value is CuiBindingValue bound && bound.Path == binding);
        }
        // The full source editor remains a genuine writable text Input.
        var editor = Assert.Single(nodes, value => value.Name == "dev-code-editor");
        Assert.Equal("Input", editor.Type);
        Assert.Contains(editor.Properties, pair => pair.Key.Equals("Type", StringComparison.OrdinalIgnoreCase)
            && pair.Value is CuiLiteralValue type && type.Value == "Text");
        Assert.Contains(editor.Properties, pair => pair.Key.Equals("Multiline", StringComparison.OrdinalIgnoreCase)
            && pair.Value is CuiLiteralValue multiline && bool.TryParse(multiline.Value, out var enabled) && enabled);
        Assert.Contains(editor.Properties, pair => pair.Key.Equals("font-family", StringComparison.OrdinalIgnoreCase)
            && pair.Value is CuiLiteralValue font && font.Value == "monospace");
        Assert.Contains(editor.Properties, pair => pair.Key.Equals("Value", StringComparison.OrdinalIgnoreCase)
            && pair.Value is CuiBindingValue draft && draft.Path == "DraftText");
        Assert.Contains(editor.Properties, pair => pair.Key.Equals("TextSelection", StringComparison.OrdinalIgnoreCase)
            && pair.Value is CuiBindingValue selection && selection.Path == "EditorTextSelection");
    }
    [Fact]
    public void Recovery_merge_is_a_real_editable_CUI_input_with_explicit_adoption_and_readonly_originals()
    {
        var nodes = Descendants(DeveloperWorkbenchCuiDocument.Load().Components).ToArray();
        var merge = Assert.Single(nodes, value => value.Name == "dev-recovery-merged");
        Assert.Equal("Input", merge.Type);
        Assert.Contains(merge.Properties, pair => pair.Key.Equals("Value", StringComparison.OrdinalIgnoreCase)
            && pair.Value is CuiBindingValue binding && binding.Path == "RecoveredMergedDraft");
        Assert.Contains(merge.Properties, pair => pair.Key.Equals("Multiline", StringComparison.OrdinalIgnoreCase)
            && pair.Value is CuiLiteralValue literal && bool.TryParse(literal.Value, out var enabled) && enabled);
        var action = Assert.Single(nodes, value => value.Name == "dev-use-merged-recovered-draft");
        Assert.Contains(action.Actions.Values, value => value.Name == "dev.use-merged-recovered-draft");
        foreach (var id in new[] { "dev-recovery-current", "dev-recovery-retained" })
        {
            var original = Assert.Single(nodes, value => value.Name == id);
            Assert.Equal("Object", original.Type);
            Assert.Contains(original.Properties, pair => pair.Key.Equals("Type", StringComparison.OrdinalIgnoreCase)
                && pair.Value is CuiLiteralValue type && type.Value == "ReadOnlyText");
        }
    }

    [Fact]
    public void Saved_stage_choices_are_inspectable_and_Run_is_bound_to_real_selection_availability()
    {
        var nodes = Descendants(DeveloperWorkbenchCuiDocument.Load().Components).ToArray();
        var picker = Assert.Single(nodes, value => value.Name == "dev-select-build-stage");
        Assert.Contains(picker.Actions.Values, value => value.Name == "dev.select-build-stage");
        var run = Assert.Single(nodes, value => value.Name == "dev-build-stage");
        Assert.Contains(run.Actions.Values, value => value.Name == "dev.run-stage");
        Assert.Contains(run.Properties, pair => pair.Key.Equals("Interactive", StringComparison.OrdinalIgnoreCase)
            && pair.Value is CuiBindingValue binding && binding.Path == "CanRunSavedStage");
    }

    [Fact]
    public void Literal_find_and_replace_use_the_real_current_draft_actions_and_selection_availability()
    {
        var nodes = Descendants(DeveloperWorkbenchCuiDocument.Load().Components).ToArray();
        foreach (var (id, binding) in new[] { ("dev-source-find", "SourceFindQuery"), ("dev-source-replacement", "SourceReplaceText") })
        {
            var input = Assert.Single(nodes, value => value.Name == id);
            Assert.Equal("Input", input.Type);
            Assert.Contains(input.Properties, pair => pair.Key.Equals("Value", StringComparison.OrdinalIgnoreCase)
                && pair.Value is CuiBindingValue value && value.Path == binding);
        }
        var replace = Assert.Single(nodes, value => value.Name == "dev-replace-source-match");
        Assert.Contains(replace.Actions.Values, value => value.Name == "dev.replace-source-match");
        Assert.Contains(replace.Properties, pair => pair.Key.Equals("Interactive", StringComparison.OrdinalIgnoreCase)
            && pair.Value is CuiBindingValue value && value.Path == "CanReplaceSourceMatch");
    }

    [Fact]
    public void Draft_undo_and_redo_are_bound_to_the_selected_documents_real_history_availability()
    {
        var nodes = Descendants(DeveloperWorkbenchCuiDocument.Load().Components).ToArray();
        foreach (var (id, command, availability) in new[] {
            ("dev-undo-source-edit", "dev.undo-source-edit", "CanUndoSourceEdit"),
            ("dev-redo-source-edit", "dev.redo-source-edit", "CanRedoSourceEdit") })
        {
            var button = Assert.Single(nodes, node => node.Name == id);
            Assert.Contains(button.Actions.Values, action => action.Name == command);
            Assert.Contains(button.Properties, property => property.Key.Equals("Interactive", StringComparison.OrdinalIgnoreCase)
                && property.Value is CuiBindingValue binding && binding.Path == availability);
        }
    }

    [Fact]
    public void Cui_draft_checks_problems_and_outline_use_actual_source_actions_and_observed_keys()
    {
        var document = DeveloperWorkbenchCuiDocument.Load();
        var nodes = Descendants(document.Components).ToArray();
        var check = Assert.Single(nodes, node => node.Name == "dev-analyze-cui-draft");
        Assert.Contains(check.Actions.Values, action => action.Name == "dev.analyze-cui-draft");
        Assert.Contains(check.Properties, property => property.Key.Equals("Interactive", StringComparison.OrdinalIgnoreCase)
            && property.Value is CuiBindingValue binding && binding.Path == "CanAnalyzeCuiDraft");
        foreach (var (id, command, parameter) in new[] { ("dev-go-to-cui-problem", "dev.go-to-cui-problem", "problem.SelectionKey"), ("dev-go-to-cui-element", "dev.go-to-cui-element", "element.SelectionKey") })
        {
            var location = Assert.Single(nodes, node => node.Name == id);
            var action = Assert.Single(location.Actions.Values, value => value.Name == command);
            Assert.True(document.Actions.TryGetValue(action.Name, out var definition));
            Assert.Equal(command, definition!.Command);
            Assert.Equal(parameter, Assert.IsType<CuiBindingValue>(definition.Parameter).Path);
        }
    }

    [Fact]
    public void Component_property_edits_use_observed_property_keys_and_the_same_current_draft_actions()
    {
        var document = DeveloperWorkbenchCuiDocument.Load();
        var nodes = Descendants(document.Components).ToArray();
        var choose = Assert.Single(nodes, node => node.Name == "dev-select-cui-property");
        var select = Assert.Single(choose.Actions.Values, action => action.Name == "dev.select-cui-property");
        Assert.True(document.Actions.TryGetValue(select.Name, out var definition));
        Assert.Equal("dev.select-cui-property", definition!.Command);
        Assert.Equal("property.SelectionKey", Assert.IsType<CuiBindingValue>(definition.Parameter).Path);
        var input = Assert.Single(nodes, node => node.Name == "dev-cui-property-value");
        Assert.Equal("Input", input.Type);
        Assert.Contains(input.Properties, pair => pair.Key.Equals("Value", StringComparison.OrdinalIgnoreCase)
            && pair.Value is CuiBindingValue binding && binding.Path == "CuiPropertyDraftValue");
        var update = Assert.Single(nodes, node => node.Name == "dev-change-cui-property");
        Assert.Contains(update.Actions.Values, action => action.Name == "dev.change-cui-property");
        Assert.Contains(update.Properties, pair => pair.Key.Equals("Interactive", StringComparison.OrdinalIgnoreCase)
            && pair.Value is CuiBindingValue binding && binding.Path == "CanChangeCuiProperty");
    }

    [Fact]
    public void New_source_creation_is_explicitly_reviewed_and_template_content_remains_readonly_until_publication()
    {
        var nodes = Descendants(DeveloperWorkbenchCuiDocument.Load().Components).ToArray();
        foreach (var (id, command, availability) in new[] { ("dev-review-new-source-file", "dev.review-new-source-file", "CanReviewNewFile"),
            ("dev-create-new-source-file", "dev.create-new-source-file", "CanCreateNewFile") })
        {
            var button = Assert.Single(nodes, node => node.Name == id);
            Assert.Contains(button.Actions.Values, action => action.Name == command);
            Assert.Contains(button.Properties, property => property.Key.Equals("Interactive", StringComparison.OrdinalIgnoreCase)
                && property.Value is CuiBindingValue binding && binding.Path == availability);
        }
        var source = Assert.Single(nodes, node => node.Name == "dev-new-file-source");
        Assert.Equal("Object", source.Type); Assert.True(source.TryGetLiteralAttribute("type", out var renderer)); Assert.Equal("ReadOnlyText", renderer);
        Assert.DoesNotContain(source.Properties.Keys, key => key.Equals("Value", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Saved_Run_targets_use_the_actual_selected_target_action_and_typed_observed_key()
    {
        var document = DeveloperWorkbenchCuiDocument.Load(); var nodes = Descendants(document.Components).ToArray();
        var selector = Assert.Single(nodes, node => node.Name == "dev-select-run-target");
        var reference = Assert.Single(selector.Actions.Values, action => action.Name == "dev.select-run-target");
        Assert.Equal("target.SelectionKey", Assert.IsType<CuiBindingValue>(document.Actions[reference.Name].Parameter).Path);
        var run = Assert.Single(nodes, node => node.Name == "dev-run-selected-target");
        Assert.Contains(run.Actions.Values, action => action.Name == "dev.run-selected-target");
        Assert.Contains(run.Properties, property => property.Key.Equals("Interactive", StringComparison.OrdinalIgnoreCase)
            && property.Value is CuiBindingValue binding && binding.Path == "CanRunSelectedTarget");
        var stop = Assert.Single(nodes, node => node.Name == "dev-stop-manual-run");
        Assert.Contains(stop.Actions.Values, action => action.Name == "dev.stop-manual-run");
        Assert.Contains(stop.Properties, property => property.Key.Equals("Interactive", StringComparison.OrdinalIgnoreCase)
            && property.Value is CuiBindingValue binding && binding.Path == "CanStopManualRun");
    }

    [Fact]
    public void Run_target_authoring_uses_exact_review_save_actions_typed_inputs_and_readonly_review()
    {
        var nodes = Descendants(DeveloperWorkbenchCuiDocument.Load().Components).ToArray();
        foreach (var (id, command) in new[] { ("dev-new-run-target", "dev.new-run-target"), ("dev-edit-run-target", "dev.edit-run-target"),
            ("dev-review-run-target", "dev.review-run-target"), ("dev-save-run-target", "dev.save-run-target") })
            Assert.Contains(Assert.Single(nodes, node => node.Name == id).Actions.Values, action => action.Name == command);
        foreach (var (id, path) in new[] { ("dev-run-edit-id", "RunEditId"), ("dev-run-edit-command", "RunEditCommand"), ("dev-run-edit-timeout", "RunEditTimeout") })
            Assert.Contains(Assert.Single(nodes, node => node.Name == id).Properties, property => property.Key.Equals("Value", StringComparison.OrdinalIgnoreCase)
                && property.Value is CuiBindingValue binding && binding.Path == path);
        var review = Assert.Single(nodes, node => node.Name == "dev-run-edit-review");
        Assert.Equal("Object", review.Type); Assert.True(review.TryGetLiteralAttribute("type", out var renderer)); Assert.Equal("ReadOnlyText", renderer);
        Assert.DoesNotContain(review.Properties.Keys, key => key.Equals("Value", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<CuiComponent> Descendants(IEnumerable<CuiComponent> nodes)
    {
        foreach (var node in nodes)
        { yield return node; foreach (var child in Descendants(node.Children)) yield return child; }
    }
}
