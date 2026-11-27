using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
using Xunit;
using NativeButton = Avalonia.Controls.Button;
using NativeText = Avalonia.Controls.TextBlock;

namespace Haven.Desktop.Tests;

public sealed class ChatConversationPresentationTests
{
    [AvaloniaTheory]
    [InlineData("Glow", 1440, 900)]
    [InlineData("Glow", 720, 650)]
    [InlineData("Glow", 400, 650)]
    [InlineData("Professional", 1440, 900)]
    [InlineData("Professional", 720, 650)]
    [InlineData("Professional", 400, 650)]
    public async Task Retained_conversation_has_aligned_transcript_composer_and_bounded_user_bubble(string themeName, int width, int height)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var previousTheme = CuiSurfacePaletteCatalog.ActiveTheme;
        var previousAppearance = CuiThemeScopeApplier.DetectAppearance();
        var previousOverride = CuiSurfacePaletteCatalog.OverrideAccent;
        var previousAccent = CuiSurfacePaletteCatalog.AccentOverride;
        var previousVariant = Avalonia.Application.Current?.RequestedThemeVariant;
        var directory = Directory.CreateTempSubdirectory("spaces-conversation-theme-").FullName;
        UserPreferencesService? preferences = null;
        NativeHomeThemePreferences? themeOwner = null;
        try
        {
            preferences = new UserPreferencesService(new ThemePaths(directory));
            var preferenceFile = Path.Combine(directory, "native-home.json");
            await File.WriteAllTextAsync(preferenceFile, JsonSerializer.Serialize(new
            {
                havenUiThemeName = themeName,
                havenUiAppearance = (int)HavenUiAppearance.Dark
            }), cancellationToken);
            themeOwner = new NativeHomeThemePreferences(preferenceFile, new(HomeDataScope.DeviceLocal, "conversation-capture"),
                UserPreferencesService.ProjectLegacyHomeTheme, _ => ValueTask.FromResult(true),
                action => Dispatcher.UIThread.Post(action), preferences.ApplyHomeThemePresentation);
            var resolver = new ChatGenUiNativeControlResolver();
            using var scene = new ChatHavenScene(resolver);
            var host = new HavenSceneControl(new HavenDesktopImageResolver(), resolver) { Root = scene.Root };
            var window = new Window { Width = width, Height = height, Content = host };
            using var backgroundBinding = window.Bind(Window.BackgroundProperty, window.GetResourceObservable("CuiBackgroundBrush"));
            var context = new object();
            scene.BindHeaderActions(value => ReferenceEquals(context, value), _ => ValueTask.CompletedTask);
            scene.SetConversationHeader(context, "Revision plan from my conversation", "Observed local model");
            scene.SetTemporaryControl(true, false);
            var user = new ChatSceneMessage(Guid.NewGuid(), MessageRole.User,
                "Help me plan a manageable revision week. Keep Wednesday evening free.", "", false, "");
            var assistant = new ChatSceneMessage(Guid.NewGuid(), MessageRole.Assistant,
                "## A manageable revision week\n\nStart with one focused session, then review what you learned.",
                "Revision specialist", false, "Worked for 4 seconds", ModelName: "Observed reply model");
            scene.SyncMessages([user, assistant]);
            try
            {
                await themeOwner.RefreshAsync(cancellationToken);
                window.Show();
                await Task.WhenAll(scene.HeaderInitialization, scene.ComposerInitialization);
                await PumpAsync(window);
                Assert.Equal(themeName, themeOwner.Current.Name);
                Assert.False(themeOwner.Context.Current.UsedFallback);
                Assert.Equal(CuiAppearance.Dark, themeOwner.Current.Appearance);
                Assert.Equal(CuiThemeCatalog.Parse(themeName), CuiSurfacePaletteCatalog.ActiveTheme);
                var title = Find<NativeText>(scene.HeaderView, "chat-conversation-title");
                var nativeInput = Assert.Single(scene.ComposerView.GetVisualDescendants().OfType<TextBox>(),
                    item => AutomationProperties.GetName(item) == "Message");
                Assert.True(title.TryFindResource("CuiEffectiveThemeName", out var headerTheme));
                Assert.Equal(themeName, headerTheme);
                Assert.True(nativeInput.TryFindResource("CuiEffectiveThemeName", out var composerTheme));
                Assert.Equal(themeName, composerTheme);
                var background = Assert.IsAssignableFrom<ISolidColorBrush>(window.Background).Color;
                var composerText = Assert.IsAssignableFrom<ISolidColorBrush>(nativeInput.Foreground).Color;
                var titleText = Assert.IsAssignableFrom<ISolidColorBrush>(title.Foreground).Color;
                var transcriptText = HavenAvaloniaThemeResolver.EffectColor(HavenAvaloniaThemeResolver.Resolve("TextPrimary", host));
                var transcriptSurface = HavenAvaloniaThemeResolver.EffectColor(HavenAvaloniaThemeResolver.Resolve("Surface", host));
                Assert.Equal((byte)255, background.A);
                Assert.True(RelativeLuminance(background) < .2, $"{themeName} Dark must apply the actual dark window palette: {background}.");
                Assert.True(ContrastRatio(composerText, background) >= 4.5, $"{themeName} composer {composerText} must remain readable over {background}.");
                Assert.True(ContrastRatio(titleText, background) >= 4.5, $"{themeName} title {titleText} must remain readable over {background}.");
                Assert.True(ContrastRatio(transcriptText, transcriptSurface) >= 4.5,
                    $"{themeName} retained transcript {transcriptText} must remain readable over {transcriptSurface}.");
                var userBubble = scene.Messages.GetItem(user.Id.ToString("N")).GetComponent<Container>("MessageBody");
                var assistantBody = scene.Messages.GetItem(assistant.Id.ToString("N")).GetComponent<Container>("MessageBody");
                var footer = scene.Root.DescendantsAndSelf().OfType<Container>().Single(item => item.Name == "Footer");
                Assert.True(scene.ConversationViewport.Bounds.Height > 100);
                Assert.Equal("Surface", scene.ConversationBody.GetValue(HavenProperties.Background));
                Assert.InRange(Math.Abs(scene.Messages.Bounds.X - footer.Bounds.X), 0, 1);
                Assert.InRange(Math.Abs(scene.Messages.Bounds.Width - footer.Bounds.Width), 0, 1);
                Assert.True(userBubble.Bounds.X > assistantBody.Bounds.X + 20);
                Assert.InRange(Math.Abs(userBubble.Bounds.Right - scene.Messages.Bounds.Right), 0, 1);
                Assert.True(userBubble.Bounds.Width <= scene.Messages.Bounds.Width * .87);
                Assert.True(scene.ComposerHost.Bounds.Bottom <= host.SurfaceMetrics.Viewport.Height + .5);
                Assert.True(scene.ComposerHost.Bounds.Width > 100);
                Assert.InRange(scene.ConversationViewport.MaxScrollX, 0, .5);
                Assert.Equal("Revision plan from my conversation", Find<NativeText>(scene.HeaderView, "chat-conversation-title").Text);
                var temporaryLabel = Find<NativeText>(scene.HeaderView, "chat-conversation-temporary-label");
                Assert.Equal("Temporary", temporaryLabel.Text);
                Assert.True(temporaryLabel.IsEffectivelyVisible);
                Assert.True(temporaryLabel.Bounds.Width > 0 && temporaryLabel.Bounds.Height > 0,
                    "Temporary must have a visible measured label beside the native switch.");
                foreach (var id in new[] { "chat-conversation-temporary-label", "chat-conversation-temporary", "chat-conversation-work", "chat-conversation-summary" })
                {
                    var control = Find<Control>(scene.HeaderView, id);
                    var origin = control.TranslatePoint(new Point(0, 0), window)!.Value;
                    Assert.True(origin.X >= 0 && origin.X + control.Bounds.Width <= width + .5, id + " must fit the viewport.");
                    Assert.True(origin.Y >= 0 && origin.Y + control.Bounds.Height <= height + .5, id + " must remain reachable.");
                }
                await AssertRecoveryUsesOnlyObservedSpaceAsync(scene, host, window, footer, cancellationToken);
                Capture(window, $"spaces-conversation-{themeName.ToLowerInvariant()}-{width}x{height}.png");
            }
            finally
            {
                try { await CloseSceneAsync(scene, window); }
                finally { window.Content = null; window.Close(); }
            }
        }
        finally
        {
            // Global presentation must be restored even when an actual scene/header
            // close or the Home theme owner's own original close reports a failure.
            try { if (themeOwner is not null) await themeOwner.DisposeAsync(); }
            finally
            {
                try
                {
                    preferences?.ApplyAccentOverride(previousOverride,
                        previousAccent is { } accent ? CuiAccentCatalog.Name(accent) : null, save: false);
                    preferences?.ApplyThemeChoice(CuiThemeCatalog.Name(previousTheme), save: false);
                    preferences?.ApplyAppearance((HavenUiAppearance)previousAppearance, save: false);
                }
                finally
                {
                    try
                    {
                        CuiSurfacePaletteCatalog.OverrideAccent = previousOverride;
                        CuiSurfacePaletteCatalog.AccentOverride = previousAccent;
                        CuiThemeScopeApplier.ApplyGlobalTheme(previousTheme, appearanceOverride: previousAppearance);
                        if (Avalonia.Application.Current is { } application && previousVariant is not null)
                            application.RequestedThemeVariant = previousVariant;
                    }
                    finally { Directory.Delete(directory, true); }
                }
            }
        }
    }

    [AvaloniaFact]
    public async Task Native_scroll_up_survives_streaming_and_latest_uses_the_same_overflow_owner()
    {
        var resolver = new ChatGenUiNativeControlResolver();
        using var scene = new ChatHavenScene(resolver);
        var host = new HavenSceneControl(new HavenDesktopImageResolver(), resolver) { Root = scene.Root };
        var window = new Window { Width = 1000, Height = 700, Content = host };
        var context = new object();
        scene.SetConversationHeader(context, "Long source conversation", "Model");
        var messages = Enumerable.Range(0, 24).Select(index => new ChatSceneMessage(Guid.NewGuid(),
            index % 2 == 0 ? MessageRole.User : MessageRole.Assistant,
            $"Message {index + 1}\n\n" + string.Join("\n\n", Enumerable.Repeat("A retained paragraph from the same conversation.", 3)),
            "Assistant", index == 23, "")).ToArray();
        scene.SyncMessages(messages);
        try
        {
            window.Show();
            await Task.WhenAll(scene.HeaderInitialization, scene.ComposerInitialization);
            await PumpAsync(window);
            Assert.True(scene.ConversationViewport.MaxScrollY > 500);
            scene.ScrollToEnd();
            await PumpAsync(window);
            Assert.True(scene.IsFollowingLatest);
            var router = new HavenInputRouter(scene.Root);
            var viewport = scene.ConversationViewport.Bounds;
            Assert.True(router.Scroll(new HavenPoint(viewport.X + 5, viewport.Y + viewport.Height / 2), 0, -250));
            await PumpAsync(window);
            var readingPosition = scene.ConversationViewport.ScrollY;
            Assert.False(scene.IsFollowingLatest);
            var lastItem = scene.Messages.GetItem(messages[^1].Id.ToString("N"));
            scene.UpdateMessage(messages[^1] with { Content = messages[^1].Content + "\n\nA new streamed paragraph." });
            await PumpAsync(window);
            Assert.Same(lastItem, scene.Messages.GetItem(messages[^1].Id.ToString("N")));
            Assert.Equal(readingPosition, scene.ConversationViewport.ScrollY, 3);
            var latest = Find<NativeButton>(scene.HeaderView, "chat-conversation-latest");
            Assert.True(latest.IsVisible);
            latest.RaiseEvent(new RoutedEventArgs(NativeButton.ClickEvent));
            await scene.HeaderView.CaptureOriginalActionSettlement();
            await PumpAsync(window);
            Assert.Equal(scene.ConversationViewport.MaxScrollY, scene.ConversationViewport.ScrollY, 3);
            Assert.True(scene.IsFollowingLatest);
        }
        finally { await CloseSceneAsync(scene, window); }
    }

    [AvaloniaFact]
    public async Task One_long_native_reply_keeps_wrapped_height_and_its_final_text_and_actions_reachable()
    {
        var resolver = new ChatGenUiNativeControlResolver();
        using var scene = new ChatHavenScene(resolver);
        var host = new HavenSceneControl(new HavenDesktopImageResolver(), resolver) { Root = scene.Root };
        var window = new Window { Width = 720, Height = 650, Content = host };
        scene.SetConversationHeader(new object(), "Long reply from this conversation", "Observed model");
        const string lastWords = "The final paragraph remains reachable.";
        // One Markdown paragraph forces a single native text leaf to exceed the viewport;
        // many short rows alone would not detect a leaf-measure height clamp.
        var content = string.Join(" ", Enumerable.Repeat(
            "This is a long retained response with ordinary wrapped text that must remain selectable and readable.", 120)) + " " + lastWords;
        var message = new ChatSceneMessage(Guid.NewGuid(), MessageRole.Assistant, content, "Assistant", false, "");
        scene.SyncMessages([message]);
        try
        {
            window.Show();
            await Task.WhenAll(scene.HeaderInitialization, scene.ComposerInitialization);
            await PumpAsync(window);
            scene.Input.Text = "Keep this draft while reading the complete reply.";
            // The native composer measures asynchronously after text changes. Observe
            // that actual footer size before choosing the transcript's current maximum.
            await PumpAsync(window);
            var item = scene.Messages.GetItem(message.Id.ToString("N"));
            var markdown = item.GetComponent<Markdown>("Body");
            var paragraph = Assert.Single(markdown.Children.OfType<Haven.UI.Components.Text>());
            var action = item.GetComponent<Haven.UI.Components.Button>("MessageMenu");
            Assert.EndsWith(lastWords, paragraph.Content);
            Assert.True(paragraph.Bounds.Height > scene.ConversationViewport.Bounds.Height * 2,
                "A long wrapped paragraph must retain its intrinsic height.");
            Assert.True(scene.ConversationViewport.MaxScrollY > scene.ConversationViewport.Bounds.Height);
            Assert.InRange(scene.ConversationViewport.MaxScrollX, 0, .5);

            scene.ScrollToEnd();
            await PumpAsync(window);

            var viewport = scene.ConversationViewport.Bounds;
            Assert.InRange(paragraph.Bounds.Bottom, viewport.Y + 1, viewport.Bottom + .5);
            Assert.InRange(action.Bounds.Y, viewport.Y, viewport.Bottom);
            Assert.True(action.Bounds.Bottom <= viewport.Bottom + .5,
                $"Action {action.Bounds}; viewport {viewport}; scroll {scene.ConversationViewport.ScrollY}/{scene.ConversationViewport.MaxScrollY}; " +
                $"extent {scene.ConversationViewport.ExtentSize}; composer {scene.ComposerHost.Bounds}.");
            Assert.True(scene.ComposerHost.Bounds.Bottom <= host.SurfaceMetrics.Viewport.Height + .5);
            Assert.Equal("Keep this draft while reading the complete reply.", scene.Input.Text);
            Assert.Same(item, scene.Messages.GetItem(message.Id.ToString("N")));
            Capture(window, "spaces-conversation-long-reply-bottom.png");
        }
        finally { await CloseSceneAsync(scene, window); }
    }

    [AvaloniaFact]
    public async Task Header_keeps_observed_temporary_state_and_close_joins_the_exact_admitted_action()
    {
        var context = new object();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bindings = new ChatConversationHeaderBindings
        {
            IsCurrentContext = value => ReferenceEquals(value, context),
            ToggleTemporary = _ => { entered.TrySetResult(); return new ValueTask(release.Task); }
        };
        bindings.UpdateConversation(context, "Temporary draft", "Model");
        bindings.SetTemporary(true, false);
        var view = new ChatConversationHeaderCuiView(bindings);
        var window = new Window { Width = 800, Height = 300, Content = view };
        try
        {
            window.Show(); await view.InitializeAsync(); await PumpAsync(window);
            var toggle = Find<ToggleSwitch>(view, "chat-conversation-temporary");
            toggle.IsChecked = true; // The same native pre-click change made by pointer/keyboard activation.
            Assert.False(toggle.IsChecked);
            toggle.RaiseEvent(new RoutedEventArgs(NativeButton.ClickEvent));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var settlement = view.CaptureOriginalActionSettlement();
            Assert.False(settlement.IsCompleted);
            Assert.False(toggle.IsChecked); // An accepted switch/save has not yet been observed.
            var close = view.CloseAsync();
            Assert.Same(close, view.CloseAsync());
            Assert.False(close.IsCompleted);
            release.SetResult();
            await settlement.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await close.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(bindings.Temporary);
        }
        finally
        {
            release.TrySetResult();
            await view.CloseAsync();
            window.Content = null; window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Genuine_header_action_failure_remains_in_the_original_close_receipt()
    {
        var failure = new IOException("Actual local save failed.");
        var bindings = new ChatConversationHeaderBindings { ToggleTemporary = _ => ValueTask.FromException(failure) };
        bindings.SetTemporary(true, true);
        var view = new ChatConversationHeaderCuiView(bindings);
        var window = new Window { Width = 800, Height = 300, Content = view };
        try
        {
            window.Show(); await view.InitializeAsync(); await PumpAsync(window);
            Find<ToggleSwitch>(view, "chat-conversation-temporary").RaiseEvent(new RoutedEventArgs(NativeButton.ClickEvent));
            var cause = await Assert.ThrowsAnyAsync<Exception>(() => view.CaptureOriginalActionSettlement());
            Assert.True(ContainsCause(cause, failure));
            var close = view.CloseAsync();
            var closeCause = await Assert.ThrowsAnyAsync<Exception>(() => close);
            Assert.True(ContainsCause(closeCause, failure));
            Assert.Same(close, view.CloseAsync());
            Assert.True(bindings.Temporary);
        }
        finally { window.Content = null; window.Close(); view.Dispose(); }
    }

    [Fact]
    public async Task Superseded_header_target_cannot_toggle_the_new_conversation()
    {
        var calls = 0;
        var first = new object(); var second = new object();
        var bindings = new ChatConversationHeaderBindings { ToggleTemporary = _ => { calls++; return ValueTask.CompletedTask; } };
        bindings.UpdateConversation(first, "First conversation", "Model A");
        bindings.SetTemporary(true, false);
        Assert.True(bindings.TryGetValue("Header.Target", out var issued));
        bindings.UpdateConversation(second, "Second conversation", "Model B");
        await bindings.DispatchAsync("chat.header.temporary", issued, TestContext.Current.CancellationToken);
        Assert.Equal(0, calls);
        Assert.True(bindings.TryGetValue("Header.Title", out var title));
        Assert.Equal("Second conversation", title);
        Assert.False(bindings.Temporary);
    }

    [AvaloniaFact]
    public async Task Work_and_summary_project_existing_records_without_mutating_draft_or_creating_work()
    {
        var resolver = new ChatGenUiNativeControlResolver();
        using var scene = new ChatHavenScene(resolver);
        var host = new HavenSceneControl(new HavenDesktopImageResolver(), resolver) { Root = scene.Root };
        var window = new Window { Width = 900, Height = 700, Content = host };
        scene.SetConversationHeader(new object(), "Existing conversation", "Selected model");
        var activity = new ToolActivity(Guid.NewGuid(), "Read source", "Read the already authorised document.", true,
            TimeSpan.FromMilliseconds(900), DateTimeOffset.UtcNow);
        scene.SyncMessages([new ChatSceneMessage(Guid.NewGuid(), MessageRole.Assistant, "A source-backed answer", "Assistant", false, "", [activity])]);
        var conversationId = Guid.NewGuid();
        scene.SetConversationSummaries([
            new(Guid.NewGuid(), conversationId, ContextEntryKind.CompactSummary, "Saved summary", "The actual retained summary.", "", DateTimeOffset.UtcNow),
            new(Guid.NewGuid(), conversationId, ContextEntryKind.Registered, "Source context", "This is not a summary.", "", DateTimeOffset.UtcNow)
        ]);
        try
        {
            window.Show(); await Task.WhenAll(scene.HeaderInitialization, scene.ComposerInitialization); await PumpAsync(window);
            scene.Input.Text = "A draft that must survive panel browsing.";
            var input = scene.Input;
            foreach (var id in new[] { "chat-conversation-work", "chat-conversation-summary" })
            {
                Find<NativeButton>(scene.HeaderView, id).RaiseEvent(new RoutedEventArgs(NativeButton.ClickEvent));
                await scene.HeaderView.CaptureOriginalActionSettlement(); await PumpAsync(window);
                Assert.Same(input, scene.Input);
                Assert.Equal("A draft that must survive panel browsing.", scene.Input.Text);
                var visibleText = scene.HeaderView.GetVisualDescendants().OfType<NativeText>()
                    .Where(item => item.IsEffectivelyVisible).Select(item => item.Text ?? "").ToArray();
                Assert.Contains(id == "chat-conversation-work" ? "Read source" : "The actual retained summary.", visibleText);
            }
            var text = scene.HeaderView.GetVisualDescendants().OfType<NativeText>().Select(item => item.Text ?? "").ToArray();
            Assert.Contains("The actual retained summary.", text);
            Assert.DoesNotContain("This is not a summary.", text);
            Assert.Single(scene.Messages.Items, item => item.TemplateName == "ChatAssistantMessage");
        }
        finally { await CloseSceneAsync(scene, window); }
    }

    private static double ContrastRatio(Color foreground, Color background)
    {
        var first = RelativeLuminance(foreground); var second = RelativeLuminance(background);
        return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
    }

    private static double RelativeLuminance(Color colour)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        return .2126 * Linear(colour.R) + .7152 * Linear(colour.G) + .0722 * Linear(colour.B);
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

    internal static T Find<T>(Control owner, string id) where T : Control => owner.GetVisualDescendants().OfType<T>()
        .Single(item => item.Name == id || AutomationProperties.GetAutomationId(item) == id);

    internal static async Task PumpAsync(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
        }
    }

    private static async Task AssertRecoveryUsesOnlyObservedSpaceAsync(ChatHavenScene scene, HavenSceneControl host,
        Window window, Container footer, CancellationToken cancellationToken)
    {
        var recovery = scene.RecoveryView;
        var input = scene.Input;
        var draft = input.Text;
        await recovery.InitializeAsync(cancellationToken);
        await PumpAsync(window);
        var idleFooterHeight = footer.Bounds.Height;
        Assert.False(scene.RecoveryHost.IsIncluded);
        Assert.Equal(HavenSize.Zero, scene.RecoveryHost.DesiredSize);
        Assert.False(recovery.IsVisible);
        Assert.True(host.TryGetNativeControl(scene.RecoveryHost, out var retained));
        Assert.Same(recovery, retained);

        const string observedStatus = "The selected local model is unavailable.";
        var requests = 0;
        EventHandler manage = (_, _) => requests++;
        scene.ManageLocalModelsRequested += manage;
        try
        {
            scene.SetStatus(observedStatus);
            scene.SetManageLocalModelsVisible(true);
            await PumpAsync(window);
            Assert.True(scene.RecoveryHost.IsIncluded);
            Assert.True(recovery.IsEffectivelyVisible);
            Assert.True(footer.Bounds.Height >= idleFooterHeight + 31.5);
            Assert.Equal(HavenVisibility.Collapsed, scene.Status.GetValue(HavenProperties.Visibility));
            var button = Find<NativeButton>(recovery, "recovery-manage-button");
            Assert.True(button.IsEffectivelyVisible);
            Assert.True(button.IsEffectivelyEnabled);
            var origin = button.TranslatePoint(default, window);
            Assert.NotNull(origin);
            Assert.True(origin.Value.X >= 0 && origin.Value.X + button.Bounds.Width <= window.ClientSize.Width + .5);
            Assert.True(origin.Value.Y >= 0 && origin.Value.Y + button.Bounds.Height <= window.ClientSize.Height + .5);
            button.RaiseEvent(new RoutedEventArgs(NativeButton.ClickEvent));
            await recovery.CaptureOriginalActionSettlement();
            Assert.Equal(1, requests);

            scene.SetManageLocalModelsVisible(false);
            await PumpAsync(window);
            Assert.False(scene.RecoveryHost.IsIncluded);
            Assert.Equal(HavenSize.Zero, scene.RecoveryHost.DesiredSize);
            Assert.False(recovery.IsVisible);
            Assert.Equal(observedStatus, scene.Status.Content);
            Assert.Equal(HavenVisibility.Visible, scene.Status.GetValue(HavenProperties.Visibility));
            scene.SetStatus(null);
            await PumpAsync(window);
            Assert.InRange(Math.Abs(footer.Bounds.Height - idleFooterHeight), 0, .5);
            Assert.Same(recovery, scene.RecoveryView);
            Assert.True(host.TryGetNativeControl(scene.RecoveryHost, out retained));
            Assert.Same(recovery, retained);
            Assert.Same(input, scene.Input);
            Assert.Equal(draft, scene.Input.Text);
        }
        finally { scene.ManageLocalModelsRequested -= manage; }
    }

    private static async Task CloseSceneAsync(ChatHavenScene scene, Window window)
    {
        scene.Dispose();
        try { await Task.WhenAll(scene.JoinHeaderCloseAndReleaseAsync(), scene.JoinComposerCloseAndReleaseAsync(), scene.JoinRecoveryCloseAndReleaseAsync()); }
        finally { window.Content = null; window.Close(); }
    }

    private static bool ContainsCause(Exception actual, Exception expected) => ReferenceEquals(actual, expected) ||
        actual is AggregateException aggregate && aggregate.InnerExceptions.Any(item => ContainsCause(item, expected)) ||
        actual.InnerException is { } inner && ContainsCause(inner, expected);

    private static void Capture(Window window, string filename)
    {
        if (Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, filename));
    }
}

public sealed partial class ChatCloudPermissionCallerTests
{
    [AvaloniaFact]
    public async Task Spaces_conversation_header_tracks_actual_title_model_and_temporary_owner()
    {
        var token = TestContext.Current.CancellationToken;
        var timeout = TimeSpan.FromSeconds(15);
        await using var rig = new Rig(temporary: false);
        var model = rig.Provider.Model.Model with { Name = rig.Provider.Model.Key, Capabilities = new HashSet<ToolCapability> { ToolCapability.Text } };
        rig.Client.AvailableModels = [model];
        var directory = Path.Combine(Path.GetTempPath(), "spaces-header-owner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var page = CreateNativeComposerPage(rig, directory, new NativeComposerDraftStore(), enableFreshChatTemporaryLifecycle: true);
        var window = new Window { Width = 1000, Height = 760, Content = page };
        try
        {
            await page.OriginalInitialization.WaitAsync(timeout, token);
            await page.RefreshModelsAsync(); page.SelectModel(model);
            await page.StartFreshConversationAsync();
            var source = page.CurrentConversation with { Title = "Title from the actual conversation" };
            await page.LoadConversationAsync(source);
            window.Show(); await ChatConversationPresentationTests.PumpAsync(window);
            var header = Assert.Single(page.GetVisualDescendants().OfType<ChatConversationHeaderCuiView>());
            Assert.Equal(source.Title, ChatConversationPresentationTests.Find<NativeText>(header, "chat-conversation-title").Text);
            Assert.Equal("Model: " + page.SelectedModelName, ChatConversationPresentationTests.Find<NativeText>(header, "chat-conversation-model").Text);
            await page.LoadConversationAsync(source with { Title = "Updated by the canonical conversation owner" });
            await ChatConversationPresentationTests.PumpAsync(window);
            Assert.Same(header, Assert.Single(page.GetVisualDescendants().OfType<ChatConversationHeaderCuiView>()));
            Assert.Equal("Updated by the canonical conversation owner",
                ChatConversationPresentationTests.Find<NativeText>(header, "chat-conversation-title").Text);
            page.SetDraft("Keep this exact current draft.");
            var id = page.ConversationId;
            var input = page.ComposerInput;
            ChatConversationPresentationTests.Find<ToggleSwitch>(header, "chat-conversation-temporary")
                .RaiseEvent(new RoutedEventArgs(NativeButton.ClickEvent));
            await header.CaptureOriginalActionSettlement().WaitAsync(timeout, token);
            Assert.True(page.IsTemporary);
            Assert.Equal(id, page.ConversationId);
            Assert.Same(input, page.ComposerInput);
            Assert.Equal("Keep this exact current draft.", page.ComposerInput.Text);
            Assert.True(ChatConversationPresentationTests.Find<ToggleSwitch>(header, "chat-conversation-temporary").IsChecked);
        }
        finally
        {
            await page.CloseAndDrainAsync().WaitAsync(timeout, token);
            window.Content = null; window.Close(); Directory.Delete(directory, true);
        }
    }
}
