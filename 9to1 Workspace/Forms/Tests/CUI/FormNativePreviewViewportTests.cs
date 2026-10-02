using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Core.Forms;
using HavenOS.Forms;

namespace HavenOS.Forms.Tests;

[Collection("Forms native renderer")]
public sealed class FormNativePreviewViewportTests
{
    [Fact]
    public async Task Actual_native_viewport_switch_preserves_same_canonical_response_and_invalid_numeric_draft()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(FormNativePreviewTests.PreviewApplication));
        await session.Dispatch<bool>(async () =>
        {
            var now = DateTimeOffset.UtcNow;
            var project = FormProjectEditor.Create("Viewport response", FormModeKind.Form, now);
            project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID,
                new(Guid.NewGuid(), FormFieldKind.Number, "Amount", null, JsonSerializer.SerializeToElement(new { }), true, new()), now);
            var originalProject = JsonSerializer.Serialize(project);
            using var preview = new FormNativePreview(project);
            var registry = new CuiControlRegistry(); preview.Register(registry);
            var window = await CuiSceneHost.CreateWindowAsync(new CuiNativeScene("forms-viewport", "Layout preview", "forms",
                preview.CreateDocument(), preview, preview, new Ready()) { ControlRegistry = registry });
            using var host = Assert.IsType<CuiSceneHost>(window.Content); window.Show();
            try
            {
                var viewport = Assert.Single(host.GetVisualDescendants().OfType<StackPanel>(), control => control.Name == "forms-preview-viewport");
                var number = Assert.Single(host.GetVisualDescendants().OfType<NumericUpDown>());
                var draft = Assert.Single(number.GetVisualDescendants().OfType<TextBox>());
                number.Value = 3; draft.Text = "invalid";
                var original = preview.Response;
                foreach (var (kind, width) in new[] { (FormPreviewViewportKind.Tablet, 768d), (FormPreviewViewportKind.Mobile, 390d),
                             (FormPreviewViewportKind.Embedded, 280d), (FormPreviewViewportKind.Desktop, 1200d) })
                {
                    Assert.True(preview.IsActionAvailable("9to1.Forms.Preview.Viewport." + kind));
                    await preview.DispatchAsync("9to1.Forms.Preview.Viewport." + kind, null);
                    Assert.Equal(width, viewport.MaxWidth);
                    Assert.Same(number, Assert.Single(host.GetVisualDescendants().OfType<NumericUpDown>()));
                    Assert.Equal("invalid", draft.Text);
                    Assert.Equal(original.ResponseID, preview.Response.ResponseID);
                    Assert.Equal(original.Revision, preview.Response.Revision);
                    Assert.Equal(3, Assert.Single(preview.Response.Answers).Value.GetDecimal());
                    Assert.False(preview.Submit().Success);
                }
                Assert.Equal(originalProject, JsonSerializer.Serialize(project));
                draft.Text = "4"; Assert.True(preview.Submit().Success);
                var submitted = preview.Response;
                await preview.DispatchAsync("9to1.Forms.Preview.Viewport.Mobile", null);
                Assert.Equal(submitted.Revision, preview.Response.Revision);
                Assert.Equal(FormResponseState.Submitted, preview.Response.State);
                preview.Dispose();
                Assert.False(preview.IsActionAvailable("9to1.Forms.Preview.Viewport.Desktop"));
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await preview.DispatchAsync("9to1.Forms.Preview.Viewport.Desktop", null));
            }
            finally { window.Close(); }
            return true;
        }, default);
    }
    [Fact]
    public async Task Actual_preview_columns_reflow_retained_canonical_fields_and_response_without_rebuilding_inputs()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(FormNativePreviewTests.PreviewApplication));
        await native.Dispatch<bool>(async () =>
        {
            var now = DateTimeOffset.UtcNow;
            var project = FormProjectEditor.Create("Column preview", FormModeKind.Form, now);
            foreach (var label in new[] { "First", "Second", "Third" })
                project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID,
                    new(Guid.NewGuid(), FormFieldKind.ShortText, label, "Original help", JsonSerializer.SerializeToElement(new { }), true, new()), now);
            project = project with { Pages = [project.Pages[0] with { Layout = new(Columns: 2) }] };
            var originalProject = JsonSerializer.Serialize(project);
            using var preview = new FormNativePreview(project);
            var registry = new CuiControlRegistry(); preview.Register(registry);
            var window = await CuiSceneHost.CreateWindowAsync(new CuiNativeScene("forms-preview-columns", "Column preview", "forms",
                preview.CreateDocument(), preview, preview, new Ready()) { ControlRegistry = registry });
            using var host = Assert.IsType<CuiSceneHost>(window.Content); window.Show();
            try
            {
                var columns = Assert.Single(host.GetVisualDescendants().OfType<FormNativePageColumns>());
                var inputs = host.GetVisualDescendants().OfType<TextBox>().ToArray();
                Assert.Equal(new[] { "First", "Second", "Third" }, inputs.Select(Avalonia.Automation.AutomationProperties.GetName));
                inputs[0].Text = "Ada";
                var original = preview.Response;
                foreach (var width in new[] { 640d, 320d, 640d })
                {
                    columns.InvalidateMeasure(); columns.Measure(new Avalonia.Size(width, double.PositiveInfinity));
                    columns.Arrange(new Avalonia.Rect(0, 0, width, columns.DesiredSize.Height));
                    Assert.Equal(3, columns.Children.Count);
                    Assert.True(columns.Children[2].Bounds.Y > columns.Children[0].Bounds.Y);
                    if (width == 640) Assert.True(columns.Children[1].Bounds.X > columns.Children[0].Bounds.X);
                    else Assert.True(columns.Children[1].Bounds.Y > columns.Children[0].Bounds.Y);
                    var current = host.GetVisualDescendants().OfType<TextBox>().ToArray();
                    for (var i = 0; i < inputs.Length; i++) Assert.Same(inputs[i], current[i]);
                    Assert.Equal("Ada", inputs[0].Text);
                    Assert.Equal(original.ResponseID, preview.Response.ResponseID); Assert.Equal(original.Revision, preview.Response.Revision);
                    Assert.Equal(originalProject, JsonSerializer.Serialize(project));
                }
                inputs[1].Text = "B"; inputs[2].Text = "C"; Assert.True(preview.Submit().Success);
                Assert.Equal(FormResponseState.Submitted, preview.Response.State);
            }
            finally { window.Close(); }
            Assert.False(preview.IsActionAvailable("9to1.Forms.Preview.Submit"));
            return true;
        }, default);
    }
    [Theory]
    [InlineData(FormModeKind.Form)]
    [InlineData(FormModeKind.Quiz)]
    public async Task Actual_window_width_reflows_visible_fields_in_order_and_quiz_advance_reserves_no_hidden_slots(FormModeKind mode)
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(FormNativePreviewTests.PreviewApplication));
        await native.Dispatch<bool>(async () =>
        {
            var now = DateTimeOffset.UtcNow;
            var project = FormProjectEditor.Create("Natural column layout", mode, now);
            foreach (var label in new[] { "First", "Second", "Third" })
                project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID,
                    new(Guid.NewGuid(), FormFieldKind.ShortText, label, null, JsonSerializer.SerializeToElement(new { }), true, new()), now);
            project = project with { Pages = [project.Pages[0] with { Layout = new(Columns: 2) }] };
            using var preview = new FormNativePreview(project);
            var registry = new CuiControlRegistry(); preview.Register(registry);
            var window = await CuiSceneHost.CreateWindowAsync(new CuiNativeScene("forms-natural-columns", "Natural layout", "forms",
                preview.CreateDocument(), preview, preview, new Ready()) { ControlRegistry = registry });
            using var host = Assert.IsType<CuiSceneHost>(window.Content); window.Show();
            try
            {
                var columns = Assert.Single(host.GetVisualDescendants().OfType<FormNativePageColumns>());
                var originalInputs = host.GetVisualDescendants().OfType<TextBox>().ToArray();
                Assert.Equal(3, originalInputs.Length);
                for (var question = 0; question < (mode == FormModeKind.Quiz ? 3 : 1); question++)
                {
                    originalInputs[question].Text = "Answer " + question;
                    var original = preview.Response;
                    foreach (var width in new[] { 800d, 360d, 800d })
                    {
                        // Genuine mounted window sizing and maintained layout manager; no direct Panel Measure/Arrange.
                        window.Width = width; window.UpdateLayout();
                        var visible = columns.Children.Where(child => child.IsVisible).ToArray();
                        Assert.Equal(mode == FormModeKind.Quiz ? 1 : 3, visible.Length);
                        Assert.Equal(0, visible[0].Bounds.X); Assert.Equal(0, visible[0].Bounds.Y);
                        if (mode == FormModeKind.Form)
                        {
                            if (width == 800) Assert.True(visible[1].Bounds.X > visible[0].Bounds.X);
                            else Assert.True(visible[1].Bounds.Y > visible[0].Bounds.Y);
                        }
                        else Assert.Equal("field-" + project.Fields[question].FieldID.ToString("N"), visible[0].Name);
                        Assert.Equal(original.ResponseID, preview.Response.ResponseID); Assert.Equal(original.Revision, preview.Response.Revision);
                        var inputs = host.GetVisualDescendants().OfType<TextBox>().ToArray();
                        for (var i = 0; i < originalInputs.Length; i++) Assert.Same(originalInputs[i], inputs[i]);
                    }
                    if (mode == FormModeKind.Quiz && question < 2) Assert.True(preview.Advance().Success);
                }
                if (mode == FormModeKind.Quiz) Assert.True(preview.Submit().Success);
            }
            finally { window.Close(); }
            return true;
        }, default);
    }
    private sealed class Ready : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) =>
            ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "ready", "Ready"));
    }
}
