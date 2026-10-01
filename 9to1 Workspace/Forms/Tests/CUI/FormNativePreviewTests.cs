using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Core.Forms;
using HavenOS.Forms;

namespace HavenOS.Forms.Tests;

[Collection("Forms native renderer")]
public sealed class FormNativePreviewTests
{
    [Fact]
    public async Task Native_ranking_preserves_option_identity_order_and_blocks_empty_required_draft()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(PreviewApplication));
        await session.Dispatch(async () =>
        {
            var now = DateTimeOffset.UtcNow;
            var project = FormProjectEditor.Create("Ranking", FormModeKind.Form, now);
            var options = new[] { new FormChoiceOption(Guid.NewGuid(), "Same label"), new FormChoiceOption(Guid.NewGuid(), "Same label") };
            var field = new FormField(Guid.NewGuid(), FormFieldKind.Ranking, "Order the choices", null,
                JsonSerializer.SerializeToElement(new { }), true, new(), Options: options);
            project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID, field, now);
            using var preview = new FormNativePreview(project);
            var registry = new CuiControlRegistry(); preview.Register(registry);
            var window = await CuiSceneHost.CreateWindowAsync(new CuiNativeScene("forms-ranking", "Ranking", "forms",
                preview.CreateDocument(), preview, preview, new Ready()) { ControlRegistry = registry });
            using var host = Assert.IsType<CuiSceneHost>(window.Content);
            window.Show();
            Button FindButton(string text) => Assert.Single(host.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, text));
            static void Click(Button button) => button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Guid[] Read() => Assert.Single(preview.Response.Answers).Value.EnumerateArray().Select(item => item.GetGuid()).ToArray();
            var list = Assert.Single(host.GetVisualDescendants().OfType<ListBox>());
            Click(FindButton("Add to ranking"));
            Assert.Equal(new[] { options[0].OptionID }, Read());
            Click(FindButton("Add to ranking"));
            Assert.False(FindButton("Add to ranking").IsEnabled);
            Assert.Equal(options.Select(option => option.OptionID), Read());
            Click(FindButton("Move up"));
            Assert.Equal(options.Reverse().Select(option => option.OptionID), Read());
            Assert.False(FindButton("Move up").IsEnabled);
            var boundaryRevision = preview.Response.Revision;
            Click(FindButton("Move up"));
            Assert.Equal(boundaryRevision, preview.Response.Revision);
            Click(FindButton("Move down"));
            Assert.Equal(options.Select(option => option.OptionID), Read());
            Click(FindButton("Remove from ranking"));
            Assert.Equal(new[] { options[0].OptionID }, Read());
            Click(FindButton("Remove from ranking"));
            Assert.False(preview.Submit().Success);
            Click(FindButton("Use ranking"));
            Assert.False(preview.Submit().Success);
            Click(FindButton("Add to ranking"));
            Assert.True(preview.Submit().Success);
            var revision = preview.Response.Revision;
            Click(FindButton("Remove from ranking"));
            Assert.Equal(revision, preview.Response.Revision);
            var addButton = FindButton("Add to ranking");
            window.Close();
            list.SelectedIndex = 0;
            Click(addButton);
            Assert.Equal(revision, preview.Response.Revision);
            return true;
        }, default);
    }

    [Theory]
    [InlineData(FormFieldKind.Number)]
    [InlineData(FormFieldKind.Decimal)]
    [InlineData(FormFieldKind.Currency)]
    [InlineData(FormFieldKind.Rating)]
    public async Task Invalid_visible_numeric_text_cannot_submit_last_valid_value(FormFieldKind kind)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(PreviewApplication));
        await session.Dispatch(async () =>
        {
            var now = DateTimeOffset.UtcNow;
            var project = FormProjectEditor.Create("Numeric draft", FormModeKind.Form, now);
            project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID,
                new(Guid.NewGuid(), kind, "Amount", null, JsonSerializer.SerializeToElement(new { }), true, new()), now);
            using var preview = new FormNativePreview(project);
            var registry = new CuiControlRegistry(); preview.Register(registry);
            var window = await CuiSceneHost.CreateWindowAsync(new CuiNativeScene("forms-number", "Number", "forms",
                preview.CreateDocument(), preview, preview, new Ready()) { ControlRegistry = registry });
            using var host = Assert.IsType<CuiSceneHost>(window.Content);
            window.Show();
            var number = Assert.Single(host.GetVisualDescendants().OfType<NumericUpDown>());
            var numericText = Assert.Single(number.GetVisualDescendants().OfType<TextBox>());
            number.Value = 3;
            Assert.Equal(3, Assert.Single(preview.Response.Answers).Value.GetDecimal());
            numericText.Text = "invalid";
            Assert.False(preview.Submit().Success);
            Assert.Equal(FormResponseState.InProgress, preview.Response.State);
            numericText.Text = "4";
            Assert.True(preview.Submit().Success);
            Assert.Equal(4, Assert.Single(preview.Response.Answers).Value.GetDecimal());
            window.Close();
            return true;
        }, default);
    }

    [Fact]
    public async Task Native_table_edits_typed_cells_preserves_row_ids_and_blocks_stale_valid_submission()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(PreviewApplication));
        await session.Dispatch(async () =>
        {
            var now = DateTimeOffset.UtcNow;
            var project = FormProjectEditor.Create("Expenses", FormModeKind.Form, now);
            var fieldID = Guid.NewGuid(); var amountID = Guid.NewGuid(); var dateID = Guid.NewGuid();
            var choiceID = Guid.NewGuid(); var fixedRow = Guid.NewGuid();
            var table = new FormTableInputDefinition(fieldID,
                [new(amountID, "Amount", FormTableCellType.Number, true, 0),
                 new(dateID, "Date", FormTableCellType.Date, true),
                 new(choiceID, "Category", FormTableCellType.Choice, true, ChoiceIDs: ["travel", "food"])],
                1, 2, FixedRowIDs: [fixedRow], UniqueColumnIDs: [dateID]);
            var field = new FormField(fieldID, FormFieldKind.TableInput, "Expenses", null,
                JsonSerializer.SerializeToElement(new { }), true, new(), Table: table);
            project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID, field, now);
            using var preview = new FormNativePreview(project);
            var registry = new CuiControlRegistry(); preview.Register(registry);
            var window = await CuiSceneHost.CreateWindowAsync(new CuiNativeScene("forms-table", "Expenses", "forms",
                preview.CreateDocument(), preview, preview, new Ready()) { ControlRegistry = registry });
            using var host = Assert.IsType<CuiSceneHost>(window.Content);
            window.Show();
            T Named<T>(string name) where T : Control => Assert.Single(host.GetVisualDescendants().OfType<T>(),
                control => Avalonia.Automation.AutomationProperties.GetName(control) == name);
            Button FindButton(string text) => Assert.Single(host.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, text));
            static void Click(Button button) => button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            FormTableInputResponse Read() => Assert.Single(preview.Response.Answers).Value.Deserialize<FormTableInputResponse>()!;
            Assert.False(FindButton("Remove row").IsEnabled);
            Assert.False(preview.Submit().Success);
            Named<NumericUpDown>("Amount").Value = 12.50m;
            Named<TextBox>("Date").Text = "2026-09-30";
            Named<ComboBox>("Category").SelectedIndex = 0;
            Assert.Equal(fixedRow, Assert.Single(Read().Rows).RowID);
            Assert.Equal(12.50m, Read().Rows[0].Cells[amountID].GetDecimal());
            Assert.Equal("travel", Read().Rows[0].Cells[choiceID].GetString());
            Named<NumericUpDown>("Amount").Text = "invalid";
            Assert.False(preview.Submit().Success);
            Named<NumericUpDown>("Amount").Text = "12.50";
            var oldNumber = Named<NumericUpDown>("Amount");
            Click(FindButton("Add row"));
            Assert.False(FindButton("Add row").IsEnabled);
            Assert.False(preview.Submit().Success); // New incomplete row cannot submit prior one-row answer.
            Named<NumericUpDown>("Amount").Value = 5m;
            Named<TextBox>("Date").Text = "2026-09-30";
            Named<ComboBox>("Category").SelectedIndex = 1;
            Assert.False(preview.Submit().Success); // Shared duplicate-column rule, not a local approximation.
            Named<TextBox>("Date").Text = "2026-10-01";
            var secondRow = Read().Rows[1].RowID;
            Assert.NotEqual(fixedRow, secondRow);
            var revision = preview.Response.Revision;
            oldNumber.Value = 999;
            Assert.Equal(revision, preview.Response.Revision); // Replaced cell subscriptions detached.
            Named<ComboBox>("Table row").SelectedIndex = 0;
            Assert.Equal(12.50m, Named<NumericUpDown>("Amount").Value);
            Named<ComboBox>("Table row").SelectedIndex = 1;
            Assert.Equal(secondRow, Read().Rows[1].RowID);
            Named<TextBox>("Date").Text = "invalid";
            Assert.False(preview.Submit().Success);
            Click(FindButton("Remove row")); // Removing invalid draft must clear that draft's validation.
            Assert.Equal(fixedRow, Assert.Single(Read().Rows).RowID);
            Assert.True(preview.Submit().Success);
            revision = preview.Response.Revision;
            Named<NumericUpDown>("Amount").Value = 42;
            Click(FindButton("Add row"));
            Assert.Equal(revision, preview.Response.Revision);
            window.Close(); oldNumber.Value = 123;
            Assert.Equal(revision, preview.Response.Revision);
            return true;
        }, default);
    }

    [Theory]
    [InlineData(FormFieldKind.MultipleChoice)]
    [InlineData(FormFieldKind.CheckboxSet)]
    public async Task Actual_native_set_choices_validate_required_answers_and_mark_stable_ids(FormFieldKind kind)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(PreviewApplication));
        await session.Dispatch(async () =>
        {
            var now = DateTimeOffset.UtcNow;
            var project = FormProjectEditor.Create("Choice assessment", FormModeKind.Test, now);
            var options = new[] { new FormChoiceOption(Guid.NewGuid(), "Same label"), new FormChoiceOption(Guid.NewGuid(), "Same label") };
            var field = new FormField(Guid.NewGuid(), kind, "Select both", null, JsonSerializer.SerializeToElement(new { }),
                true, new(), Options: options, Assessment: new(2, 1,
                    [new(Guid.NewGuid(), FormMarkingRuleKind.ChoiceSet, 2, ChoiceIDs: options.Select(option => option.OptionID.ToString("D")).ToArray())]));
            project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID, field, now);
            using var preview = new FormNativePreview(project);
            var registry = new CuiControlRegistry(); preview.Register(registry);
            var scene = new CuiNativeScene("forms-choices", "Choices", "forms", preview.CreateDocument(), preview, preview, new Ready()) { ControlRegistry = registry };
            var window = await CuiSceneHost.CreateWindowAsync(scene);
            using var host = Assert.IsType<CuiSceneHost>(window.Content);
            window.Show();
            var checks = host.GetVisualDescendants().OfType<CheckBox>().ToArray();
            Assert.Equal(2, checks.Length);
            checks[0].IsChecked = true; checks[1].IsChecked = true;
            Assert.Equal(options.Select(option => option.OptionID), Assert.Single(preview.Response.Answers).Value.EnumerateArray().Select(item => item.GetGuid()));
            Assert.Empty(preview.Response.ReleasedResults);
            checks[0].IsChecked = false; checks[1].IsChecked = false;
            Assert.False(preview.Submit().Success); // Invalid empty UI cannot submit the last valid set.
            checks[0].IsChecked = true; checks[1].IsChecked = true;
            Assert.True(preview.Submit().Success);
            Assert.Equal(2, preview.Response.AwardedPoints);
            var revision = preview.Response.Revision;
            checks[0].IsChecked = false;
            Assert.Equal(revision, preview.Response.Revision);
            window.Close();
            checks[1].IsChecked = false;
            Assert.Equal(revision, preview.Response.Revision);
            return true;
        }, default);
    }

    [Theory]
    [InlineData(FormFieldKind.Email, "person@example.test")]
    [InlineData(FormFieldKind.Date, "2026-09-30")]
    [InlineData(FormFieldKind.Time, "12:30:00")]
    [InlineData(FormFieldKind.DateTime, "2026-09-30T12:30:00.0000000+00:00")]
    [InlineData(FormFieldKind.Duration, "00:00:01")]
    public async Task Clearing_optional_typed_native_input_removes_prior_value_without_stale_submission(FormFieldKind kind, string valid)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(PreviewApplication));
        await session.Dispatch(async () =>
        {
            var now = DateTimeOffset.UtcNow;
            var project = FormProjectEditor.Create("Optional input", FormModeKind.Form, now);
            var field = new FormField(Guid.NewGuid(), kind, "Optional value", null, JsonSerializer.SerializeToElement(new { }), false, new());
            project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID, field, now);
            using var preview = new FormNativePreview(project);
            var registry = new CuiControlRegistry(); preview.Register(registry);
            var window = await CuiSceneHost.CreateWindowAsync(new CuiNativeScene("forms-optional", "Optional", "forms",
                preview.CreateDocument(), preview, preview, new Ready()) { ControlRegistry = registry });
            using var host = Assert.IsType<CuiSceneHost>(window.Content);
            window.Show();
            var input = Assert.Single(host.GetVisualDescendants().OfType<TextBox>());
            input.Text = valid;
            Assert.Equal(valid, Assert.Single(preview.Response.Answers).Value.GetString());
            input.Text = "";
            Assert.Equal(JsonValueKind.Null, Assert.Single(preview.Response.Answers).Value.ValueKind);
            Assert.True(preview.Submit().Success);
            Assert.Equal(JsonValueKind.Null, Assert.Single(preview.Response.Answers).Value.ValueKind);
            window.Close();
            return true;
        }, default);
    }

    [Fact]
    public async Task Actual_native_quiz_fields_keep_option_identity_question_release_and_detached_control_isolation()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(PreviewApplication));
        await session.Dispatch(async () =>
        {
            var now = DateTimeOffset.UtcNow;
            var project = FormProjectEditor.Create("Native quiz", FormModeKind.Quiz, now);
            var question = new FormField(Guid.NewGuid(), FormFieldKind.ShortText, "Processor", null,
                JsonSerializer.SerializeToElement(new { }), true, new(), Assessment: new(1, 1,
                    [new(Guid.NewGuid(), FormMarkingRuleKind.AcceptedText, 1, AcceptedTexts: ["CPU"])], FormResultRelease.AfterQuestion));
            var first = new FormChoiceOption(Guid.NewGuid(), "First"); var second = new FormChoiceOption(Guid.NewGuid(), "Second");
            var choice = new FormField(Guid.NewGuid(), FormFieldKind.SingleChoice, "Select an option", null,
                JsonSerializer.SerializeToElement(new { }), true, new(), Options: [first, second]);
            project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID, question, now);
            project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID, choice, now);
            using var preview = new FormNativePreview(project);
            var registry = new CuiControlRegistry(); preview.Register(registry);
            var scene = new CuiNativeScene("forms-preview", "Forms preview", "forms", preview.CreateDocument(), preview, preview, new Ready())
                { ControlRegistry = registry };
            var window = await CuiSceneHost.CreateWindowAsync(scene);
            using var host = Assert.IsType<CuiSceneHost>(window.Content);
            window.Show();
            var input = Assert.Single(host.GetVisualDescendants().OfType<TextBox>());
            var selector = Assert.Single(host.GetVisualDescendants().OfType<ComboBox>());
            input.Text = "CPU";
            Assert.Empty(preview.Response.ReleasedResults);
            Assert.False(selector.IsEnabled);
            input.Text = "";
            Assert.False(preview.Advance().Success);
            Assert.Equal(question.FieldID, preview.Response.CurrentFieldID);
            input.Text = "CPU";
            Assert.True(preview.Advance().Success);
            Assert.False(input.IsEnabled);
            Assert.True(selector.IsEnabled);
            Assert.Single(preview.Response.ReleasedResults);
            selector.SelectedIndex = 1;
            Assert.Equal(second.OptionID, preview.Response.Answers.Single(answer => answer.FieldID == choice.FieldID).Value.GetGuid());
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame); Assert.True(frame.PixelSize.Width > 0);
            var beforeDetach = preview.Response;
            window.Close();
            selector.SelectedIndex = 0;
            input.Text = "Changed after close";
            Assert.Equal(beforeDetach.Revision, preview.Response.Revision);
            Assert.Equal(second.OptionID, preview.Response.Answers.Single(answer => answer.FieldID == choice.FieldID).Value.GetGuid());
            Assert.True(preview.Submit().Success);
            Assert.Equal(FormResponseState.Submitted, preview.Response.State);
            return true;
        }, default);
    }
    public sealed class PreviewApplication : Application
    {
        public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<PreviewApplication>().UseSkia())
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
        public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this);
    }
    private sealed class Ready : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken ct) =>
            ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "ready", "Ready"));
    }
}
