using System.Runtime.ExceptionServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Chat;
using Haven.UI;
using Haven.UI.Components;
using HavenOS.Home.Core;
using HavenButton = Haven.UI.Components.Button;
using HavenContainer = Haven.UI.Components.Container;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Tests;

/// <summary>Actual retained message/tool templates, native input and approved theme owners.
/// Controlled tool observations qualify presentation, not external execution or artifact readiness.</summary>
public sealed class ChatActivityDisclosureTests
{
    private readonly CancellationToken _token = TestContext.Current.CancellationToken;

    [AvaloniaFact]
    public Task Completed_receipts_collapse_without_replacing_the_response_or_generated_owner() =>
        InSceneAsync(async rig =>
        {
            var activity = Activity("Read source", "The selected source was read.");
            var reply = Reply([activity]);
            rig.Scene.SyncMessages([reply]);
            var message = Message(rig.Scene, reply.Id);
            var tool = Tool(rig.Scene, activity.Id);
            var body = message.GetComponent<Markdown>("Body");
            var generated = new HavenContainer { Name = "ActualOwnedGeneratedView" };
            generated.Add(new HavenText { Content = "Retained generated view" });
            rig.Scene.SetGeneratedContent(reply.Id, [generated]);
            var semanticActions = 0;
            rig.Scene.MessageActionRequested += (_, _) => semanticActions++;
            rig.Scene.SendRequested += (_, _) => semanticActions++;
            rig.Scene.StopRequested += (_, _) => semanticActions++;
            Assert.Equal(HavenVisibility.Collapsed, tool.GetValue(HavenProperties.Visibility));
            Assert.Equal("1 tool result", message.GetComponent<HavenText>("ActivityOutcome").Content);

            Press(message.GetComponent<HavenButton>("ActivityToggle"));
            await PumpAsync(rig.Window);
            Assert.Equal(HavenVisibility.Visible, tool.GetValue(HavenProperties.Visibility));
            Assert.Same(message, Message(rig.Scene, reply.Id));
            Assert.Same(body, message.GetComponent<Markdown>("Body"));
            Assert.Same(generated, Assert.Single(message.GetComponent<HavenContainer>("GeneratedContent").Children));
            Assert.Same(tool, Tool(rig.Scene, activity.Id));
            Press(message.GetComponent<HavenButton>("ActivityToggle"));
            Assert.Equal(HavenVisibility.Collapsed, tool.GetValue(HavenProperties.Visibility));
            Assert.Equal(0, semanticActions);
        });

    [AvaloniaFact]
    public Task Each_response_keeps_its_own_disclosure_across_updates_and_transcript_reordering() =>
        InSceneAsync(async rig =>
        {
            var firstActivity = Activity("First response", "First exact observation.");
            var secondActivity = Activity("Second response", "Second exact observation.");
            var first = Reply([firstActivity]); var second = Reply([secondActivity]);
            rig.Scene.SyncMessages([first, second]);
            var firstItem = Message(rig.Scene, first.Id); var secondItem = Message(rig.Scene, second.Id);
            var firstTool = Tool(rig.Scene, firstActivity.Id);
            Press(firstItem.GetComponent<HavenButton>("ActivityToggle"));
            rig.Scene.SyncMessages([second, first]);
            var later = Activity("Later result", "A later observation for the same first response.");
            rig.Scene.UpdateMessage(first with { IsStreaming = true, Content = "More text", ToolActivities = [firstActivity, later] });
            await PumpAsync(rig.Window);

            Assert.Same(firstItem, Message(rig.Scene, first.Id));
            Assert.Same(secondItem, Message(rig.Scene, second.Id));
            Assert.Same(firstTool, Tool(rig.Scene, firstActivity.Id));
            Assert.Equal(HavenVisibility.Visible, firstTool.GetValue(HavenProperties.Visibility));
            Assert.Equal(HavenVisibility.Visible, Tool(rig.Scene, later.Id).GetValue(HavenProperties.Visibility));
            Assert.Equal(HavenVisibility.Collapsed, Tool(rig.Scene, secondActivity.Id).GetValue(HavenProperties.Visibility));
            Assert.Equal(new[] { second.Id.ToString("N"), "tool-" + secondActivity.Id.ToString("N"),
                first.Id.ToString("N"), "tool-" + firstActivity.Id.ToString("N"), "tool-" + later.Id.ToString("N") },
                rig.Scene.Messages.Items.Select(item => item.InstanceID));
        });

    [AvaloniaFact]
    public Task A_later_failure_stays_visible_in_the_summary_without_overriding_an_explicit_collapse() =>
        InSceneAsync(async rig =>
        {
            var completed = Activity("Inspect source", "Source inspected.");
            var reply = Reply([completed]) with { IsStreaming = true };
            rig.Scene.SyncMessages([reply]);
            var item = Message(rig.Scene, reply.Id);
            Assert.Equal(HavenVisibility.Visible, Tool(rig.Scene, completed.Id).GetValue(HavenProperties.Visibility));
            Press(item.GetComponent<HavenButton>("ActivityToggle"));
            var failed = Activity("Run check", "The check returned an error.", succeeded: false);
            rig.Scene.UpdateMessage(reply with { IsStreaming = false, ToolActivities = [completed, failed] });
            await PumpAsync(rig.Window);

            Assert.Equal(HavenVisibility.Collapsed, Tool(rig.Scene, failed.Id).GetValue(HavenProperties.Visibility));
            Assert.Equal("Failed", Tool(rig.Scene, failed.Id).GetComponent<HavenText>("Status").Content);
            Assert.Equal("2 tool results · 1 failed", item.GetComponent<HavenText>("ActivityOutcome").Content);
            Assert.Contains("1 failed", item.GetComponent<HavenButton>("ActivityToggle").Accessibility.AccessibleName);
            Assert.Equal(HavenVisibility.Visible, item.GetComponent<HavenContainer>("ActivitySummary").GetValue(HavenProperties.Visibility));
        });

    [AvaloniaFact]
    public Task Observed_streaming_and_deferred_results_do_not_claim_a_completed_follow_up() =>
        InSceneAsync(async rig =>
        {
            var activity = Activity("Read input", "The input was read.");
            var reply = Reply([activity]) with { IsStreaming = true };
            rig.Scene.SyncMessages([reply]);
            Assert.Equal(HavenVisibility.Visible, Tool(rig.Scene, activity.Id).GetValue(HavenProperties.Visibility));
            rig.Scene.UpdateMessage(reply with { IsStreaming = false });
            Assert.Equal(HavenVisibility.Collapsed, Tool(rig.Scene, activity.Id).GetValue(HavenProperties.Visibility));

            var deferred = Activity("Owned continuation", "A continuation was registered for the failed result.", succeeded: false)
                with { HasDeferredInvocations = true };
            rig.Scene.UpdateMessage(reply with { IsStreaming = false, ToolActivities = [deferred] });
            await PumpAsync(rig.Window);
            Assert.Equal(HavenVisibility.Visible, Tool(rig.Scene, deferred.Id).GetValue(HavenProperties.Visibility));
            Assert.Equal("Failed · follow-up registered", Tool(rig.Scene, deferred.Id).GetComponent<HavenText>("Status").Content);
            Assert.Equal("1 tool result · 1 failed · 1 follow-up registered", Message(rig.Scene, reply.Id).GetComponent<HavenText>("ActivityOutcome").Content);
            Assert.DoesNotContain(rig.Scene.Messages.Items, item => item.InstanceID == "tool-" + activity.Id.ToString("N"));
        });

    [AvaloniaFact]
    public Task Removed_response_controls_cannot_change_a_replacement_response_or_a_retired_scene() =>
        InSceneAsync(async rig =>
        {
            var activity = Activity("Old result", "Retained source.");
            var reply = Reply([activity]);
            rig.Scene.SyncMessages([reply]);
            var old = Message(rig.Scene, reply.Id).GetComponent<HavenButton>("ActivityToggle");
            Press(old);
            rig.Scene.SyncMessages([]);
            rig.Scene.SyncMessages([reply]);
            var replacement = Message(rig.Scene, reply.Id).GetComponent<HavenButton>("ActivityToggle");
            Assert.NotSame(old, replacement);
            Press(old);
            Assert.Equal(HavenVisibility.Collapsed, Tool(rig.Scene, activity.Id).GetValue(HavenProperties.Visibility));
            Press(replacement);
            Assert.Equal(HavenVisibility.Visible, Tool(rig.Scene, activity.Id).GetValue(HavenProperties.Visibility));
            rig.Scene.Dispose();
            Press(replacement);
            Assert.Equal(HavenVisibility.Visible, Tool(rig.Scene, activity.Id).GetValue(HavenProperties.Visibility));
            await PumpAsync(rig.Window);
        });

    [AvaloniaTheory]
    [InlineData("Glow", 720, 650)]
    [InlineData("Glow", 1440, 900)]
    [InlineData("Professional", 720, 650)]
    [InlineData("Professional", 1440, 900)]
    public Task Authored_activity_is_keyboard_reachable_and_bounded_in_both_approved_themes(string theme, int width, int height) =>
        InSceneAsync(async rig =>
        {
            var completed = Activity("Inspect selected source", "Read the selected source and recorded its current state.");
            var failed = Activity("Run the selected check", "The check returned an error. Its original result is retained.", succeeded: false);
            var reply = Reply([completed, failed]);
            rig.Scene.SyncMessages([new(Guid.NewGuid(), MessageRole.User, "Check the source and explain the result.", "", false, ""), reply]);
            rig.Scene.Input.Text = "Keep this draft while I inspect the results.";
            await PumpAsync(rig.Window);
            var item = Message(rig.Scene, reply.Id);
            var toggle = item.GetComponent<HavenButton>("ActivityToggle");
            Assert.True(rig.Host.FocusElement(toggle));
            rig.Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            rig.Window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            await PumpAsync(rig.Window);
            Assert.Equal(HavenVisibility.Collapsed, Tool(rig.Scene, failed.Id).GetValue(HavenProperties.Visibility));
            Assert.True(toggle.Bounds.Width > 0 && toggle.Bounds.Height >= 36);
            Assert.InRange(toggle.Bounds.X, 0, width);
            Assert.True(toggle.Bounds.Right <= width + .5);
            Assert.Contains("1 failed", item.GetComponent<HavenText>("ActivityOutcome").Content);
            Assert.Equal("Worked for 12 seconds", item.GetComponent<HavenText>("Thinking").Content);
            Capture(rig.Window, $"spaces-activity-collapsed-{theme}-{width}x{height}.png");

            Assert.True(rig.Host.FocusElement(toggle));
            rig.Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            rig.Window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            await PumpAsync(rig.Window);
            rig.Scene.ScrollToEnd(); await PumpAsync(rig.Window);
            var lastRow = Tool(rig.Scene, failed.Id).GetComponent<HavenContainer>("ToolRow");
            var viewport = rig.Scene.ConversationViewport.Bounds;
            Assert.Equal(HavenVisibility.Visible, Tool(rig.Scene, failed.Id).GetValue(HavenProperties.Visibility));
            Assert.True(lastRow.Bounds.Width > 0 && lastRow.Bounds.Height > 0);
            Assert.InRange(lastRow.Bounds.Y, viewport.Y, viewport.Bottom);
            Assert.True(lastRow.Bounds.Bottom <= viewport.Bottom + .5);
            Assert.InRange(rig.Scene.ConversationViewport.MaxScrollX, 0, .5);
            Assert.True(rig.Scene.ComposerHost.Bounds.Bottom <= rig.Host.SurfaceMetrics.Viewport.Height + .5);
            Assert.Equal("Keep this draft while I inspect the results.", rig.Scene.Input.Text);
            Assert.Equal(CuiThemeCatalog.Parse(theme), CuiSurfacePaletteCatalog.ActiveTheme);
            Assert.Equal((byte)255, Assert.IsAssignableFrom<ISolidColorBrush>(rig.Window.Background).Color.A);
            Capture(rig.Window, $"spaces-activity-expanded-{theme}-{width}x{height}.png");
        }, theme, width, height);

    private static ToolActivity Activity(string title, string detail, bool succeeded = true) =>
        new(Guid.NewGuid(), title, detail, succeeded, TimeSpan.FromMilliseconds(850), DateTimeOffset.UnixEpoch);
    private static ChatSceneMessage Reply(IReadOnlyList<ToolActivity> activities) =>
        new(Guid.NewGuid(), MessageRole.Assistant, "The observed results are recorded below.", "Assistant", false,
            ChatProgressText.Format(false, 12, "Provider detail remains in the source owner."), activities, "Observed model");
    private static DynamicUIItem Message(ChatHavenScene scene, Guid id) => scene.Messages.GetItem(id.ToString("N"));
    private static DynamicUIItem Tool(ChatHavenScene scene, Guid id) => scene.Messages.GetItem("tool-" + id.ToString("N"));
    private static void Press(HavenButton button)
    {
        Assert.True(button.KeyDown(new(HavenKey.Space, HavenKeyModifiers.None)));
        Assert.True(button.KeyUp(new(HavenKey.Space, HavenKeyModifiers.None)));
    }
    private sealed record SceneCase(ChatHavenScene Scene, HavenSceneControl Host, Window Window);

    private async Task InSceneAsync(Func<SceneCase, Task> body, string theme = "Professional", int width = 1000, int height = 720)
    {
        var previousTheme = CuiSurfacePaletteCatalog.ActiveTheme;
        var previousAppearance = CuiThemeScopeApplier.DetectAppearance();
        var previousOverride = CuiSurfacePaletteCatalog.OverrideAccent;
        var previousAccent = CuiSurfacePaletteCatalog.AccentOverride;
        var previousVariant = Avalonia.Application.Current?.RequestedThemeVariant;
        var directory = Directory.CreateTempSubdirectory("spaces-activity-theme-").FullName;
        var failures = new List<Exception>();
        UserPreferencesService? preferences = null;
        NativeHomeThemePreferences? themeOwner = null;
        ChatHavenScene? scene = null;
        Window? window = null;
        IDisposable? backgroundBinding = null;
        try
        {
            preferences = new UserPreferencesService(new ThemePaths(directory));
            var file = Path.Combine(directory, "native-home.json");
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new
            { havenUiThemeName = theme, havenUiAppearance = (int)HavenUiAppearance.Dark }), _token);
            themeOwner = new NativeHomeThemePreferences(file, new(HomeDataScope.DeviceLocal, "activity-capture"),
                UserPreferencesService.ProjectLegacyHomeTheme, _ => ValueTask.FromResult(true),
                action => Dispatcher.UIThread.Post(action), preferences.ApplyHomeThemePresentation);
            var resolver = new ChatGenUiNativeControlResolver();
            scene = new ChatHavenScene(resolver);
            var host = new HavenSceneControl(new HavenDesktopImageResolver(), resolver) { Root = scene.Root };
            window = new Window { Width = width, Height = height, Content = host };
            backgroundBinding = window.Bind(Window.BackgroundProperty, window.GetResourceObservable("CuiBackgroundBrush"));
            scene.SetConversationHeader(new object(), "Activity for this conversation", "Observed model");
            await themeOwner.RefreshAsync(_token);
            window.Show();
            await Task.WhenAll(scene.HeaderInitialization, scene.ComposerInitialization);
            await PumpAsync(window);
            Assert.Equal(theme, themeOwner.Current.Name);
            Assert.False(themeOwner.Context.Current.UsedFallback);
            await body(new(scene, host, window));
        }
        catch (Exception error) { failures.Add(error); }
        finally
        {
            if (scene is not null)
            {
                try { scene.Dispose(); } catch (Exception error) { failures.Add(error); }
                static Task Admit(Func<Task> close)
                { try { return close(); } catch (Exception error) { return Task.FromException(error); } }
                var close = Task.WhenAll(Admit(scene.JoinHeaderCloseAndReleaseAsync),
                    Admit(scene.JoinComposerCloseAndReleaseAsync), Admit(scene.JoinRecoveryCloseAndReleaseAsync));
                try { await close; }
                catch (Exception error)
                {
                    if (close.Exception is { } aggregate) failures.AddRange(aggregate.InnerExceptions);
                    else failures.Add(error);
                }
            }
            try { if (window is not null) { window.Content = null; window.Close(); } backgroundBinding?.Dispose(); }
            catch (Exception error) { failures.Add(error); }
            try { if (themeOwner is not null) await themeOwner.DisposeAsync(); }
            catch (Exception error) { failures.Add(error); }
            try
            {
                preferences?.ApplyAccentOverride(previousOverride,
                    previousAccent is { } accent ? CuiAccentCatalog.Name(accent) : null, save: false);
                preferences?.ApplyThemeChoice(CuiThemeCatalog.Name(previousTheme), save: false);
                preferences?.ApplyAppearance((HavenUiAppearance)previousAppearance, save: false);
                CuiSurfacePaletteCatalog.OverrideAccent = previousOverride;
                CuiSurfacePaletteCatalog.AccentOverride = previousAccent;
                CuiThemeScopeApplier.ApplyGlobalTheme(previousTheme, appearanceOverride: previousAppearance);
                if (Avalonia.Application.Current is { } application && previousVariant is not null)
                    application.RequestedThemeVariant = previousVariant;
            }
            catch (Exception error) { failures.Add(error); }
            try { Directory.Delete(directory, true); } catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Activity test and original scene cleanup failures are retained.", failures);
    }

    private static async Task PumpAsync(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
        }
    }
    private static void Capture(Window window, string filename)
    {
        if (Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, filename));
    }
    private sealed class ThemePaths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "unused.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
