using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Core.Forms;
using HavenOS.Forms;

namespace HavenOS.Forms.Tests;

public sealed class FormNativePreviewTests
{
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
