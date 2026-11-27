using System.ComponentModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Desktop.Services;
using HavenOS.Home;
using HavenOS.Home.Core;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class HomeModelPickerConversationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private const string Model = "qwen3.8-flash-next-alora-multimodal-20261126";

    [Fact]
    public async Task Picker_search_retains_selected_identity_across_roles_settings_and_None_without_route_mutation()
    {
        var source = new ConversationSource();
        var routes = new Routes();
        var controller = NewController(routes, source);
        await controller.OpenAsync("User", "chat", Token);
        var bindings = new HomeModelPickerConversationBindings(controller.Surface, () => true);
        var notifications = 0;
        ((INotifyPropertyChanged)bindings).PropertyChanged += (_, _) => notifications++;
        Assert.True(bindings.TrySetValue("CompactSpecialistQuery", "beta"));
        var row = Assert.Single(Value<IReadOnlyList<HomeModelPickerSpecialistRow>>(bindings, "CompactSpecialists"));
        Assert.Equal(source.Beta.Id.ToString("D"), Value<string>(row, "Identity"));
        Assert.True(bindings.TryGetItemValue(row, "Selection", out var choice));
        Assert.True((await controller.ExecuteAsync(new(HomeModelPickerAction.ChooseCompactSpecialist,
            SpecialistSelection: Assert.IsType<HomeModelPickerSpecialistSelection>(choice)), Token)).Succeeded);
        Assert.Equal(source.Beta.Name, Value<string>(bindings, "CompactParticipantName"));

        Assert.True(bindings.TrySetValue("CompactSpecialistQuery", "unmatched"));
        Assert.Empty(Value<IReadOnlyList<HomeModelPickerSpecialistRow>>(bindings, "CompactSpecialists"));
        Assert.Equal(source.Beta.Name, Value<string>(bindings, "CompactParticipantName"));
        controller.Surface.SetSettingsOpen(true); controller.Surface.SetSettingsOpen(false);
        Assert.Equal("unmatched", Value<string>(bindings, "CompactSpecialistQuery"));
        await controller.ExecuteAsync(new(HomeModelPickerAction.SelectCompactRole, Category: "vision"), Token);
        Assert.False(Value<bool>(bindings, "CanChooseCompactSpecialist"));
        Assert.Contains("unavailable", Value<string>(bindings, "CompactParticipantName"), StringComparison.OrdinalIgnoreCase);
        await controller.ExecuteAsync(new(HomeModelPickerAction.SelectCompactRole, Category: "chat"), Token);
        Assert.Equal(source.Beta.Name, Value<string>(bindings, "CompactParticipantName"));
        Assert.Equal("unmatched", Value<string>(bindings, "CompactSpecialistQuery"));
        var none = Value<HomeModelPickerSpecialistSelection>(bindings, "CompactSpecialistNoneSelection");
        Assert.True((await controller.ExecuteAsync(new(HomeModelPickerAction.ChooseCompactSpecialist, SpecialistSelection: none), Token)).Succeeded);
        Assert.Equal("None", Value<string>(bindings, "CompactParticipantName"));
        Assert.Equal(2, source.Selections); Assert.Equal(0, routes.Writes); Assert.True(notifications > 0);
        Assert.False(bindings.TrySetValue("CompactContextCapacity", "262144"));
    }

    [Fact]
    public async Task Configured_context_uses_exact_Chat_model_and_preserves_524288_without_preferences_or_pending_route_substitution()
    {
        var source = new ConversationSource(); var routes = new Routes();
        var controller = NewController(routes, source);
        await controller.OpenAsync("User", "chat", Token);
        Assert.Equal($"Configured context: {524288:N0} tokens.", Value<string>(controller.Surface, "CompactConversationContext"));
        Assert.Contains("pinned", Value<string>(controller.Surface, "CompactContextChangeMessage"), StringComparison.Ordinal);
        source.Snapshot = source.Snapshot with { SelectedModelName = "another-provider:" + Model };
        await controller.ExecuteAsync(new(HomeModelPickerAction.RefreshConversation), Token);
        Assert.DoesNotContain($"{524288:N0}", Value<string>(controller.Surface, "CompactConversationContext"), StringComparison.Ordinal);
        Assert.Contains("no current context observation", Value<string>(controller.Surface, "CompactConversationContext"), StringComparison.Ordinal);
        Assert.Equal(0, routes.Writes);
    }

    [Fact]
    public async Task Refresh_keeps_a_removed_selected_Specialist_inspectable_without_defaulting_to_another_definition()
    {
        var source = new ConversationSource();
        source.Snapshot = source.Snapshot with { SelectedSpecialistId = source.Beta.Id,
            SelectedSpecialistName = source.Beta.Name, SelectedSpecialistVersion = source.Beta.Version };
        var controller = NewController(new Routes(), source);
        await controller.OpenAsync("User", "chat", Token);
        source.Snapshot = source.Snapshot with { Specialists = [source.Alpha] };
        await controller.ExecuteAsync(new(HomeModelPickerAction.RefreshConversation), Token);
        Assert.Equal(source.Beta.Name, Value<string>(controller.Surface, "CompactParticipantName"));
        Assert.Contains("no longer available", Value<string>(controller.Surface, "CompactParticipantMessage"), StringComparison.Ordinal);
        Assert.Equal(source.Beta.Id, Value<HomeModelPickerSpecialistSelection>(controller.Surface, "CompactSpecialistNoneSelection").ExpectedSpecialistId);
        Assert.Equal(0, source.Selections);
    }

    [Fact]
    public async Task Retired_picker_cannot_publish_a_delayed_conversation_read_or_write_its_search()
    {
        var source = new ConversationSource(); var controller = NewController(new Routes(), source);
        var current = true; controller.PublicationGate = () => current;
        await controller.OpenAsync("User", "chat", Token);
        source.Held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var reading = controller.ExecuteAsync(new(HomeModelPickerAction.RefreshConversation), Token);
        await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        current = false;
        source.Snapshot = source.Snapshot with { SelectedSpecialistId = source.Beta.Id, SelectedSpecialistName = source.Beta.Name };
        source.Held.SetResult(new(true, "Observed", "Changed source", source.Snapshot));
        await reading;
        Assert.Equal("None", Value<string>(controller.Surface, "CompactParticipantName"));
        var bindings = new HomeModelPickerConversationBindings(controller.Surface, () => current);
        Assert.False(bindings.TrySetValue("CompactSpecialistQuery", "foreign late input"));
    }

    [AvaloniaTheory]
    [InlineData("Glow", 400)]
    [InlineData("Glow", 720)]
    [InlineData("Glow", 1440)]
    [InlineData("Professional", 400)]
    [InlineData("Professional", 720)]
    [InlineData("Professional", 1440)]
    public async Task Authored_picker_keeps_actual_context_search_and_Specialist_controls_inside_viewport(string themeName, int width)
    {
        var source = new ConversationSource(); var controller = new HomeModelPickerCuiController(
            new HomeModelPickerRouteEditor(new Routes()), conversationSource: source);
        using var theme = new CuiThemePreferenceContext(new PickerTheme(themeName));
        using var loader = new CuiControlLoader(new CuiControlRegistry { ScaleAuthoredInterfaceMetrics = true });
        loader.SetThemePreferenceContext(theme);
        loader.SetBindingContext(new HomeModelPickerConversationBindings(controller.Surface, () => true));
        loader.SetActionDispatcher(new PickerDispatcher(controller));
        var result = loader.TryLoad(controller.Surface.Document);
        Assert.DoesNotContain(result.Diagnostics, item => item.Severity == CuiDiagnosticSeverity.Error);
        var root = Assert.IsAssignableFrom<Control>(result.Root);
        var window = new Window { Width = width, Height = 900, Content = new ScrollViewer
            { Content = root, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } };
        try
        {
            loader.WireBindings(root);
            await controller.OpenAsync("User", "chat", Token);
            window.Show(); await PumpAsync(window);
            Assert.True(ButtonAt(root, "compact-model-main-role").TryFindResource("CuiWorkspaceBrush", out var workspaceBrush));
            window.Background = Assert.IsAssignableFrom<IBrush>(workspaceBrush);
            Assert.True(ButtonAt(root, "compact-model-main-role").TryFindResource("CuiEffectiveThemeName", out var actualTheme));
            Assert.Equal(themeName, actualTheme);
            ButtonAt(root, "compact-model-context").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), Token); await PumpAsync(window);
            Assert.Contains($"{524288:N0}", Find<TextBlock>(root, "compact-model-configured-context").Text, StringComparison.Ordinal);
            ButtonAt(root, "compact-model-participant").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), Token); await PumpAsync(window);
            var search = Find<TextBox>(root, "compact-model-specialist-search");
            search.Text = "beta"; await PumpAsync(window);
            Assert.Equal("beta", Value<string>(controller.Surface, "CompactSpecialistQuery"));
            var beta = Assert.Single(root.GetVisualDescendants().OfType<Button>(),
                item => AutomationProperties.GetName(item) == source.Beta.Name);
            beta.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), Token); await PumpAsync(window);
            Assert.Equal(source.Beta.Name, Find<TextBlock>(root, "compact-model-participant-selection").Text);
            foreach (var control in new Control[] { search, beta, ButtonAt(root, "compact-model-specialist-none"),
                Find<TextBlock>(root, "compact-model-configured-context") })
            {
                var point = control.TranslatePoint(default, window)!.Value;
                Assert.True(control.Bounds.Width > 0); Assert.True(control.Bounds.Height > 0);
                Assert.InRange(point.X, 0, width); Assert.True(point.X + control.Bounds.Width <= width + 1);
                Assert.InRange(point.Y, 0, window.Height); Assert.True(point.Y + control.Bounds.Height <= window.Height + 1);
            }
            if (Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR") is { Length: > 0 } captures)
            {
                Directory.CreateDirectory(captures);
                using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                frame.Save(Path.Combine(captures, $"model-picker-{themeName.ToLowerInvariant()}-participant-context-{width}x900.png"));
            }
            ButtonAt(root, "compact-model-specialist-none").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10), Token);
            Assert.Equal(2, source.Selections);
        }
        finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); window.Content = null; window.Close(); }
    }

    private static HomeModelPickerCuiController NewController(Routes routes, ConversationSource source) =>
        new(new HomeModelPickerRouteEditor(routes), new(new CuiRichParser().Parse("<Cui><Page/></Cui>")), conversationSource: source);
    private static T Value<T>(ICuiBindingContext bindings, string path)
    { Assert.True(bindings.TryGetValue(path, out var value)); return Assert.IsAssignableFrom<T>(value); }
    private static T Find<T>(Control root, string id) where T : Control => Assert.Single(root.GetVisualDescendants().OfType<T>(),
        item => item.Name == id || AutomationProperties.GetAutomationId(item) == id);
    private static Button ButtonAt(Control root, string id) => Find<Button>(root, id);
    private static async Task PumpAsync(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        { Dispatcher.UIThread.RunJobs(); await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render); }
    }
    private sealed class PickerTheme(string name) : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; } = new(name, 1, "picker-test", true,
            CuiAccessibilitySettings.Default, CuiAppearance.Dark);
        public event EventHandler? Changed { add { } remove { } }
    }
    private sealed class PickerDispatcher(HomeModelPickerCuiController controller) : ICuiActionDispatcher
    {
        public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            var request = command switch
            {
                "ToggleCompactSection" => new HomeModelPickerActionRequest(HomeModelPickerAction.ToggleCompactSection, FamilyKey: (string?)parameter),
                "ChooseCompactSpecialist" => new(HomeModelPickerAction.ChooseCompactSpecialist,
                    SpecialistSelection: Assert.IsType<HomeModelPickerSpecialistSelection>(parameter)),
                _ => throw new InvalidOperationException("Unexpected picker test action: " + command)
            };
            Assert.True((await controller.ExecuteAsync(request, cancellationToken)).Succeeded);
        }
    }
    private sealed class ConversationSource : IHomeModelPickerConversationSource
    {
        internal HomeModelPickerSpecialist Alpha { get; } = new(Guid.NewGuid(), "Alpha specialist", "First canonical test definition", "alpha-v1");
        internal HomeModelPickerSpecialist Beta { get; } = new(Guid.NewGuid(), "Beta specialist", "Second canonical test definition", "beta-v1");
        internal HomeModelPickerConversationSnapshot Snapshot;
        internal int Selections;
        internal TaskCompletionSource<HomeCoreOperationResult<HomeModelPickerConversationSnapshot>>? Held;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ConversationSource() => Snapshot = new(Guid.NewGuid(), 7, "dulche-local:" + Model, null, null, null, false, [Alpha, Beta]);
        public Task<HomeCoreOperationResult<HomeModelPickerConversationSnapshot>> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (Held is not null) { Entered.TrySetResult(); return Held.Task; }
            return Task.FromResult(new HomeCoreOperationResult<HomeModelPickerConversationSnapshot>(true, "Observed", "Current test Chat", Snapshot));
        }
        public Task<HomeCoreOperationResult<HomeModelPickerConversationSnapshot>> SelectSpecialistAsync(
            HomeModelPickerSpecialistSelection selection, CancellationToken cancellationToken = default)
        {
            Assert.Equal(Snapshot.ConversationId, selection.ConversationId);
            Assert.Equal(Snapshot.SelectedSpecialistId, selection.ExpectedSpecialistId);
            var selected = Snapshot.Specialists.SingleOrDefault(item => item.Id == selection.SpecialistId);
            Snapshot = Snapshot with { SelectedSpecialistId = selected?.Id, SelectedSpecialistName = selected?.Name,
                SelectedSpecialistVersion = selected?.Version };
            Selections++;
            return ReadAsync(cancellationToken);
        }
    }
    private sealed class Routes : IHomeModelPickerFeatureProvider
    {
        internal int Writes;
        private static HomeModelRouteContract Route => new("chat.active", 1, "User", "chat", null, null,
            [new("dulche-local", Model, null, true, 0)], JsonSerializer.SerializeToElement(new { }));
        public Task<HomeCoreOperationResult<HomeModelCataloguePage>> GetCatalogueAsync(string? query = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new HomeCoreOperationResult<HomeModelCataloguePage>(true, "Ready", "Test provider metadata",
                new([new("dulche-local", Model, null, "ALORA verified model", "Dulche", true, new HashSet<string> { "Text", "Vision" },
                    524288, "unloaded", "local", null)], 0, 20, false, 1)));
        public Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> GetSnapshotAsync(string scope, string category, CancellationToken cancellationToken = default) =>
            Task.FromResult(new HomeCoreOperationResult<HomeModelPickerSnapshot>(true, "Ready", "Test route", new(1, scope, category, [Route])));
        public Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> UpdateRouteAsync(HomeModelRouteEdit edit, CancellationToken cancellationToken = default)
        { Writes++; throw new InvalidOperationException("Participant choices cannot mutate a route."); }
        public Task<HomeCoreOperationResult<HomeModelRoutePreview>> PreviewResolutionAsync(HomeModelRoutePreviewRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
