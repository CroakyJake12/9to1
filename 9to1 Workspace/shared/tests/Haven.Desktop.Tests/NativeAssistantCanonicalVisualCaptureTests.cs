#if !ANDROID
using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Diagnostics;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.NativeUI;
using Xunit;

// Uses the SAME maintained genuine Home/Den/SQLite rig and native owner already
// linked for Memory first-use. The fixture's model client rejects every call.
// HAVEN_VISUAL_CAPTURE_DIR activates the existing Skia capture test platform;
// without it this remains a real persisted route/render/close control.
namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [AvaloniaTheory]
    [InlineData("SuperBright")]
    [InlineData("Bright")]
    [InlineData("Dark")]
    [InlineData("SuperDark")]
    public async Task Actual_native_home_saved_configuration_and_canonical_conversation_emit_opt_in_captures(string appearance)
    {
        var application = Assert.IsAssignableFrom<Application>(Application.Current);
        var hadAppearance = application.Resources.ContainsKey("CuiAppearance");
        var originalAppearance = hadAppearance ? application.Resources["CuiAppearance"] : null;
        application.Resources["CuiAppearance"] = appearance;
        try
        {
            const string fictionalName = "Fictional developer — UI fixture";
            AssistantIdentity? identity = null; Guid conversationId = Guid.Empty;
            var evidence = new List<object>(); string? fixture = null;
            await RunAsync(async rig =>
            {
                fixture = rig.Root;
                await WithActualMemorySurface(rig, async (window, surface, controller, _) =>
                {
                    window.Width = 1320; window.Height = 960;
                    await FlushNativeMemoryUi(window);
                    Assert.Empty(controller.Snapshot.Assistants);
                    await ClickCanonicalCaptureControl(window, surface, "assistant-create", () => surface.Bindings.Draft is not null);
                    var name = Assert.Single(window.GetVisualDescendants().OfType<TextBox>(), input => AutomationProperties.GetName(input) == "Assistant name");
                    Assert.True(name.IsEnabled); name.Text = fictionalName;
                    var description = Assert.Single(window.GetVisualDescendants().OfType<TextBox>(), input => AutomationProperties.GetName(input) == "Assistant description");
                    description.Text = "Persisted source fixture for real native rendering; model execution is unavailable.";
                    var instructions = Assert.Single(window.GetVisualDescendants().OfType<TextBox>(), input => AutomationProperties.GetName(input) == "Assistant instructions");
                    instructions.Text = "Use clear explanations and preserve project history. This fixture invokes no model or tools.";
                    await ClickCanonicalCaptureControl(window, surface, "assistant-save", () => controller.Snapshot.SelectedAssistant is not null && surface.Bindings.Draft?.IsDirty == false);
                    identity = controller.Snapshot.SelectedAssistant!.Identity;
                    Assert.Equal(fictionalName, controller.Snapshot.SelectedAssistant.Configuration.Name);
                    Assert.Equal(identity, Assert.Single((await rig.Bridge.ListAsync(Token)).Definitions).Identity);
                    await FlushNativeMemoryUi(window);
                    await CaptureActualCanonicalScene(window, "assistants-configuration", evidence, identity, null,
                        "Saved fictional identity in actual Home/Den state; actual current configuration. No model/tool grant.");
                    await ClickCanonicalCaptureControl(window, surface, "configuration-back", () =>
                        surface.Bindings.TryGetValue("ShowWork", out var shown) && shown is true);
                    await ClickCanonicalCaptureControl(window, surface, "work-home", () =>
                        surface.Bindings.TryGetValue("ShowHome", out var shown) && shown is true);
                    Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == fictionalName);
                    await CaptureActualCanonicalScene(window, "assistants-home", evidence, identity, null,
                        "Actual native home projects the SAME persisted canonical Assistant; no fabricated backend result.");
                    var open = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button =>
                        button.IsEffectivelyVisible && AutomationProperties.GetName(button) == "Open this Assistant");
                    await ClickCanonicalCaptureControl(window, surface, open, () =>
                        controller.Snapshot.SelectedAssistant?.Identity == identity && surface.Bindings.TryGetValue("ShowHome", out var shown) && shown is false);
                    await ClickCanonicalCaptureControl(window, surface, "new-conversation", () =>
                        controller.Snapshot.ConversationBinding is { } created && ReferenceEquals(created, surface.CurrentConversationBinding) &&
                        surface.IsOriginalClosePrepared);
                    var binding = controller.Snapshot.ConversationBinding!; conversationId = binding.Conversation.Id;
                    Assert.Equal(identity, binding.Definition.Identity); Assert.Same(binding, surface.CurrentConversationBinding);
                    Assert.Empty(controller.Snapshot.Conversation!.Messages);
                    await CaptureActualCanonicalScene(window, "assistants-conversation", evidence, identity, conversationId,
                        "Actual canonical empty Chat conversation; model provider is deliberately unavailable. No fabricated messages or success.");
                });
                // The SAME view/controller/management actions have all joined before this
                // process/source reopen; source identity and conversation survive real IO.
                await rig.ReopenAsync();
                await WithActualMemorySurface(rig, async (window, surface, controller, _) =>
                {
                    window.Width = 1320; window.Height = 960;
                    await OpenActualSavedMemoryConversation(surface, identity!, conversationId, window);
                    Assert.Equal(identity, controller.Snapshot.ConversationBinding!.Definition.Identity);
                    Assert.Equal(conversationId, controller.Snapshot.ConversationBinding.Conversation.Id);
                    Assert.Empty(controller.Snapshot.Conversation!.Messages);
                    await CaptureActualCanonicalScene(window, "assistants-conversation-reopened", evidence, identity, conversationId,
                        "SAME actual persisted identity/conversation after source close and reopen; no new model or Task work.");
                });
            }); // The SAME process Memory/WRITE/SQLite/Den/Home sources have independently joined.
            var directory = Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Assert.Equal(8, evidence.Count);
                await File.WriteAllTextAsync(Path.Combine(directory, "assistants-capture-evidence-" + appearance + ".json"), JsonSerializer.Serialize(new
                {
                    status = "Actual headless native source captures; healthy original view/action/process joins completed",
                    fixture, identity, conversationId, appearance, scenes = evidence,
                    limitations = new[] { "Fictional fixture identity", "No model/tool execution", "No installed Windows/Home qualification", "No Android/device qualification" }
                }, new JsonSerializerOptions { WriteIndented = true }), Token);
            }
        }
        finally
        {
            if (hadAppearance) application.Resources["CuiAppearance"] = originalAppearance;
            else application.Resources.Remove("CuiAppearance"); // Only the test's temporary resource.
        }
    }

    private static async Task CaptureActualCanonicalScene(Window window, string name, List<object> evidence,
        AssistantIdentity? identity, Guid? conversationId, string observation)
    {
        var appearance = CakeOS.Cui.Themes.CuiThemeScopeApplier.DetectAppearance().ToString();
        foreach (var (width, height) in new[] { (1320, 960), (430, 860) })
        {
            window.Width = width; window.Height = height;
            await FlushNativeMemoryUi(window);
            await CaptureActualCanonicalFrame(window, name + "-" + appearance + "-" + width + "x" + height,
                evidence, identity, conversationId, observation, width, height, appearance);
        }
        window.Width = 1320; window.Height = 960;
        await FlushNativeMemoryUi(window);
    }

    private static async Task CaptureActualCanonicalFrame(Window window, string name, List<object> evidence,
        AssistantIdentity? identity, Guid? conversationId, string observation, int expectedWidth, int expectedHeight, string appearance)
    {
        window.UpdateLayout();
        // Exercise actual production styles, including their legacy dynamic
        // resources; setting only the host Foreground did not style its labels.
        var heading = window.GetVisualDescendants().OfType<TextBlock>().First(text =>
            text.IsEffectivelyVisible && text.FontSize >= 20 && !string.IsNullOrWhiteSpace(text.Text));
        var headingHost = heading.GetVisualAncestors().OfType<CuiSceneHost>().First();
        var hasScopedText = headingHost.Resources.TryGetResource("CuiTextBrush", headingHost.ActualThemeVariant, out var scopedTextResource);
        // Preserve actual resource/binding evidence before the unchanged style assertion.
        // This observes the real production tree; it never assigns a brush or style.
        var diagnosticDirectory = Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(diagnosticDirectory))
        {
            Directory.CreateDirectory(diagnosticDirectory);
            var logical = new List<Control>();
            for (Control? current = heading; current is not null && logical.Count < 32; current = current.Parent as Control)
                logical.Add(current);
            var diagnostic = new
            {
                name, appearance, headingText = heading.Text, expected = scopedTextResource?.ToString(), hasScopedText,
                sceneLocalText = headingHost.Resources["CuiTextBrush"]?.ToString(),
                mergedResourceProviders = headingHost.Resources.MergedDictionaries.Select(provider => provider.GetType().FullName).ToArray(),
                heading = DescribeCanonicalForeground(heading),
                logicalAncestors = logical.Select(DescribeCanonicalForeground).ToArray(),
                visualAncestors = heading.GetVisualAncestors().OfType<Control>().Take(32)
                    .Select(DescribeCanonicalForeground).ToArray(),
                application = Application.Current?.GetType().FullName,
                applicationStyles = Application.Current?.Styles.Select(style => new
                {
                    type = style.GetType().FullName,
                    identity = style.ToString(),
                    source = (style as Avalonia.Markup.Xaml.Styling.StyleInclude)?.Source?.ToString()
                }).ToArray()
            };
            await File.WriteAllTextAsync(Path.Combine(diagnosticDirectory, name + "-foreground.json"),
                JsonSerializer.Serialize(diagnostic, new JsonSerializerOptions { WriteIndented = true }), Token);
        }
        Assert.True(hasScopedText);
        var scopedText = Assert.IsType<SolidColorBrush>(scopedTextResource).Color;
        Assert.Equal(scopedText, Assert.IsType<SolidColorBrush>(heading.Foreground).Color);
        var button = window.GetVisualDescendants().OfType<Button>().First(control =>
            control.IsEffectivelyVisible && control.IsEnabled);
        var buttonHost = button.GetVisualAncestors().OfType<CuiSceneHost>().First();
        Assert.True(buttonHost.Resources.TryGetResource("CuiButtonForegroundBrush", buttonHost.ActualThemeVariant, out var buttonTextResource));
        Assert.Equal(Assert.IsType<SolidColorBrush>(buttonTextResource).Color,
            Assert.IsType<SolidColorBrush>(button.Foreground).Color);
        Assert.True(buttonHost.Resources.TryGetResource("CuiButtonBrush", buttonHost.ActualThemeVariant, out var buttonBackgroundResource));
        var scopedButton = Assert.IsType<SolidColorBrush>(buttonBackgroundResource).Color;
        var animation = Stopwatch.StartNew();
        while (button.Background is not SolidColorBrush brush || brush.Color != scopedButton)
        {
            // Advance the real headless render timer/animation clock. The
            // primitive's initial null->brush transition is not its final colour.
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
            if (animation.Elapsed >= TimeSpan.FromSeconds(2)) break;
            await Task.Delay(10, Token);
        }
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var directory = Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
            Assert.Equal(expectedWidth, frame.PixelSize.Width); Assert.Equal(expectedHeight, frame.PixelSize.Height);
            var path = Path.Combine(directory, name + ".png"); frame.Save(path);
            Assert.True(new FileInfo(path).Length > 1000);
            evidence.Add(new { name, path, width = frame.PixelSize.Width, height = frame.PixelSize.Height, identity, conversationId, appearance, observation });
        }
        // Preserve the actual frame even when the settled style assertion fails.
        // A timeout remains a real failure; no assertion or source join is waived.
        var actualBackground = Assert.IsType<SolidColorBrush>(button.Background).Color;
        Assert.Equal(scopedButton, actualBackground);
        Assert.True(CakeOS.Cui.Themes.CuiContrast.Ratio(
            Assert.IsType<SolidColorBrush>(button.Foreground).Color, actualBackground) >= 4.5d,
            "The actual production button label must remain readable on its settled background.");
    }
    private static object DescribeCanonicalForeground(Control control)
    {
        var value = control.GetDiagnostic(Avalonia.Controls.Documents.TextElement.ForegroundProperty);
        return new
        {
            type = control.GetType().FullName, control.Name, stableId = CuiRuntimeIdentity.GetStableId(control),
            foreground = DescribeCanonicalResource(value.Value), priority = value.Priority.ToString(),
            value.Diagnostic, value.IsOverriddenCurrentValue, theme = control.ActualThemeVariant.ToString(),
            localStyles = control.Styles.Select(style => style.GetType().FullName + ": " + style).ToArray(),
            resources = new[] { "CuiTextBrush", "HavenTextBrush", "HavenTextPrimaryBrush", "TextControlForeground" }
                .Select(key => new
                {
                    key,
                    found = control.TryFindResource(key, control.ActualThemeVariant, out var resource),
                    value = DescribeCanonicalResource(resource)
                }).ToArray()
        };
    }
    private static string? DescribeCanonicalResource(object? value) => value switch
    {
        null => null,
        SolidColorBrush brush => brush.GetType().FullName + ": " + brush.Color,
        _ => value.GetType().FullName + ": " + value
    };
    private static Task ClickCanonicalCaptureControl(Window window, AssistantsNativeCuiSurface surface, string id, Func<bool> settled) =>
        ClickCanonicalCaptureControl(window, surface, Assert.Single(window.GetVisualDescendants().OfType<Button>(), button =>
            button.IsEffectivelyVisible && (CuiRuntimeIdentity.GetStableId(button) == "id:" + id ||
                CuiRuntimeIdentity.GetStableId(button)?.EndsWith("/id:" + id, StringComparison.Ordinal) == true)), settled);
    private static async Task ClickCanonicalCaptureControl(Window window, AssistantsNativeCuiSurface surface, Button button, Func<bool> settled)
    {
        Assert.True(button.IsEnabled); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(15))
        {
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<CuiSceneHost>(), host => host.LastActionFailure is not null);
            if (settled()) return;
            if (surface.IsRetiring) throw new InvalidOperationException("The retained actual capture surface retired before its action settled.");
            await Task.Delay(10, Token);
        }
        throw new TimeoutException("The actual native capture action did not settle.");
        // The maintained genuine fixture independently joins every same original
        // loader/native/controller/process task after these UI observations.
    }
}
#endif
