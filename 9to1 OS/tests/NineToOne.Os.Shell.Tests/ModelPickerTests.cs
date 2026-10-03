using System.Text.Json;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CakeOS.Cui.Runtime;
using CakeOS.Cui;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using NineToOne.Os.Shell;

namespace NineToOne.Os.Shell.Tests;

[Collection("Native CUI")]
public sealed class ModelPickerTests
{
    [Fact]
    public async Task ApprovalFrontdoorOnlyNavigatesAndRetriesExactBoundEdit()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var provider = new Provider(); string? shown = null;
            using var model = new HomeModelPickerBindings(provider, (id, _) => { shown = id; return Task.CompletedTask; });
            await model.OpenAsync();
            await model.DispatchAsync("Preview", null);
            Assert.True(model.TryGetValue("CanPreview", out var preview)); Assert.Equal(false, preview);
            await model.DispatchAsync("Add", provider.Model);
            await model.DispatchAsync("Category.Chat", null);
            Assert.Equal(1, provider.Reads); // unsaved edits survive category navigation
            await model.DispatchAsync("Save", null);
            Assert.Null(provider.Edits[0].ApprovalRequestId);
            await model.DispatchAsync("HomePermissions", null);
            Assert.Equal("pending-exact", shown);
            Assert.Equal(2, provider.Edits.Count);
            Assert.Equal("pending-exact", provider.Edits[1].ApprovalRequestId);
            Assert.Equal(provider.Edits[0].ExpectedRevision, provider.Edits[1].ExpectedRevision);
            Assert.Equal(JsonSerializer.Serialize(provider.Edits[0].Route), JsonSerializer.Serialize(provider.Edits[1].Route));
            Assert.Null(provider.Edits[1].Route.Candidates.Single().ArtifactRevision);
            Assert.True(model.TryGetValue("CanSave", out var unsaved)); Assert.Equal(true, unsaved); // close is never approval
            return true;
        }, CancellationToken.None));
    }

    [Fact]
    public async Task MissingFrontdoorCannotDecideApprovalOrDiscardDraft()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var provider = new Provider(); using var model = new HomeModelPickerBindings(provider);
            await model.OpenAsync(); await model.DispatchAsync("Add", provider.Model); await model.DispatchAsync("Save", null);
            Assert.True(model.TryGetValue("CanApprove", out var enabled)); Assert.Equal(false, enabled);
            await model.DispatchAsync("HomePermissions", null); Assert.Single(provider.Edits);
            await model.DispatchAsync("Discard", null); Assert.Equal(2, provider.Reads);
            Assert.True(model.TryGetValue("CanSave", out var dirty)); Assert.Equal(false, dirty);
            return true;
        }, CancellationToken.None));
    }
    [Fact]
    public async Task AuthoredCatalogueActionKeepsOriginalCanonicalModelAndTwoWayQuery()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var provider = new Provider(); using var model = new HomeModelPickerBindings(provider); await model.OpenAsync();
            using var stream = typeof(HomeModelPickerBindings).Assembly.GetManifestResourceStream("HavenOS.Home.NativeUI.Resources.Cui.Models.cui");
            using var reader = new StreamReader(stream!);
            var actions = new Recorder(); using var loader = new CuiControlLoader();
            loader.SetBindingContext(model); loader.SetActionDispatcher(actions);
            var (root, diagnostics) = loader.LoadMarkup(reader.ReadToEnd()); Assert.NotNull(root);
            Assert.DoesNotContain(diagnostics, d => d.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
            loader.WireBindings(root!);
            var add = Controls(root!).OfType<Button>().Single(b => Equals(b.Content, "Add to route"));
            Assert.True(add.IsEnabled); add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Add", actions.Command); Assert.Same(provider.Model, actions.Parameter);
            var query = Controls(root!).OfType<TextBox>().Single(t => t.Name == "model-query");
            query.Text = "local text"; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(model.TryGetValue("Query", out var value)); Assert.Equal("local text", value);
            Assert.False(Controls(root!).OfType<Button>().Single(b => Equals(b.Content, "Preview eligible selection")).IsEnabled);
            return true;
        }, CancellationToken.None));
    }
    private static IEnumerable<Control> Controls(Control root)
    {
        yield return root;
        IEnumerable<Control> children = root switch { Panel p => p.Children, Decorator d when d.Child is not null => [d.Child], ContentControl c when c.Content is Control child => [child], _ => [] };
        foreach (var child in children) foreach (var descendant in Controls(child)) yield return descendant;
    }
    private sealed class Recorder : ICuiActionDispatcher
    {
        public string? Command; public object? Parameter;
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken ct = default)
        { Command = command; Parameter = parameter; return ValueTask.CompletedTask; }
    }
    private sealed class Provider : IHomeModelPickerFeatureProvider
    {
        public int Reads; public List<HomeModelRouteEdit> Edits { get; } = [];
        public HomeModelPickerCatalogueEntry Model { get; } = new("provider", "model", null, "Model", "Provider", true, new HashSet<string> { "Text" }, null, null, null, null);
        private HomeModelPickerSnapshot Snapshot(string category) => new(0, "User", category,
            [new("personal-" + category, 0, "User", category, null, null, [], JsonSerializer.SerializeToElement(new { }), "profile")]);
        public Task<HomeCoreOperationResult<HomeModelCataloguePage>> GetCatalogueAsync(string? query = null, CancellationToken cancellationToken = default) => Task.FromResult(new HomeCoreOperationResult<HomeModelCataloguePage>(true, "Ready", "Ready", new([Model], 0, 1, false, 1)));
        public Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> GetSnapshotAsync(string scope, string category, CancellationToken cancellationToken = default)
        { Reads++; Assert.Equal("User", scope); return Task.FromResult(new HomeCoreOperationResult<HomeModelPickerSnapshot>(true, "Ready", "Ready", Snapshot(category))); }
        public Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> UpdateRouteAsync(HomeModelRouteEdit edit, CancellationToken cancellationToken = default)
        { Edits.Add(edit); return Task.FromResult(new HomeCoreOperationResult<HomeModelPickerSnapshot>(false, "ApprovalRequired", "Home approval required", Snapshot(edit.Route.Category) with { PendingApprovalRequestId = "pending-exact" })); }
        public Task<HomeCoreOperationResult<HomeModelRoutePreview>> PreviewResolutionAsync(HomeModelRoutePreviewRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unsaved drafts must not be previewed.");
    }
}

[CollectionDefinition("Native CUI", DisableParallelization = true)]
public sealed class NativeCuiCollection { }
