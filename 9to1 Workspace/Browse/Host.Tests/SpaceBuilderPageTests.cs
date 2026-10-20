using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Desktop.Views.Pages.Spaces;
using HavenOS.Apps.Spaces.Builder;

namespace HavenOS.Browse.Host.Tests;

/// <summary>Independent checks through actual mounted production Builder CUI and
/// file-backed registry. Headless component evidence; no native/design/full Builder claim.</summary>
public sealed class SpaceBuilderPageTests
{
    internal static readonly TimeSpan Bound = TimeSpan.FromSeconds(20);

    [AvaloniaTheory]
    [InlineData("save")]
    [InlineData("discard")]
    [InlineData("cancel")]
    public async Task Mounted_actual_Save_Discard_Cancel_controls_resolve_the_same_canonical_builder_draft(string choice)
    {
        using var files = new Files(); var registry = files.Registry();
        var row = await registry.CreateAsync("Original Builder");
        var binding = new SpaceCapabilityBinding("saved-unavailable-skill", SpaceCapabilityKind.Skill,
            SpaceCapabilityOverride.Disabled, "1.2.3");
        row = await registry.UpdateAsync(row with { Instructions = "Keep saved instructions", CapabilityBindings = [binding] }, row.Revision);
        var page = new SpaceBuilderPage(registry, row.Id, Ready.Instance);
        Window? window = null; Exception? primary = null;
        using var caller = new CancellationTokenSource();
        try
        {
            window = Mount(page); await page.ActivateAsync().WaitAsync(Bound);
            Input(page.Host, "space-builder-name").Text = "Edited through actual Builder CUI";
            Assert.True(page.State.HasChanges);
            var prepare = page.PrepareCloseAsync(caller.Token);
            var decision = await Decision(page);
            Assert.False(prepare.IsCompleted);
            Click(decision, "space-details-close-" + choice); // IDs come from the actual loaded authored decision.
            var lease = await prepare.WaitAsync(Bound);
            Assert.Null(page.OriginalClose);
            Assert.Same(page.Host, page.Content);
            if (choice == "cancel")
            {
                Assert.Null(lease); Assert.True(page.State.HasChanges);
                Assert.Equal("Edited through actual Builder CUI", page.State.Draft.Name);
                Assert.True(page.TryEdit(SpaceBuilderField.Description, "Still editable after actual Cancel"));
            }
            else
            {
                Assert.NotNull(lease); Assert.True(lease.IsCurrent);
                Assert.False(page.TryEdit(SpaceBuilderField.Description, "Must refuse after accepted preparation"));
                Assert.True(await page.CommitCloseAsync(lease).WaitAsync(Bound));
                Assert.True(page.OriginalClose!.IsCompletedSuccessfully);
            }
            var reopened = (await files.Registry().ReadExistingAsync(row.Id))!;
            Assert.Equal(row.Id, reopened.Id);
            Assert.Equal(choice == "save" ? "Edited through actual Builder CUI" : "Original Builder", reopened.Name);
            Assert.Equal("Keep saved instructions", reopened.Instructions);
            Assert.NotNull(reopened.CapabilityBindings);
            Assert.Equal(binding, Assert.Single(reopened.CapabilityBindings));
            Assert.Null(page.State.ActiveRevision); // Saving a draft does not fabricate activation.
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { caller.Cancel(); await Cleanup(page, window, primary); }
    }

    [AvaloniaFact]
    public async Task Accepted_preparation_freezes_inputs_and_old_aborted_token_cannot_clear_a_newer_preparation()
    {
        using var files = new Files(); var registry = files.Registry(); var row = await registry.CreateAsync("Frozen Builder");
        var page = new SpaceBuilderPage(registry, row.Id, Ready.Instance); Window? window = null; Exception? primary = null;
        try
        {
            window = Mount(page); await page.ActivateAsync().WaitAsync(Bound);
            var first = Assert.IsType<SpaceBuilderPage.ClosePreparation>(await page.PrepareCloseAsync().WaitAsync(Bound));
            Assert.False(page.TryEdit(SpaceBuilderField.Name, "Blocked typed mutation"));
            Assert.False(page.TryAttachCapability(new("unavailable-new-reference", SpaceCapabilityKind.Skill, SpaceCapabilityOverride.Inherit)));
            Assert.True(await page.AbortCloseAsync(first).WaitAsync(Bound));
            Assert.True(page.TryEdit(SpaceBuilderField.Name, "Editable after abort"));
            Assert.True(await page.DiscardChangesAsync().WaitAsync(Bound));
            var second = Assert.IsType<SpaceBuilderPage.ClosePreparation>(await page.PrepareCloseAsync().WaitAsync(Bound));
            Assert.NotSame(first, second); Assert.True(second.IsCurrent);
            Assert.False(await page.AbortCloseAsync(first).WaitAsync(Bound));
            Assert.True(second.IsCurrent);
            Assert.False(page.TryEdit(SpaceBuilderField.Description, "Old token must not release the new freeze"));
            Assert.True(await page.AbortCloseAsync(second).WaitAsync(Bound));
            Assert.True(page.TryEdit(SpaceBuilderField.Description, "Editable after current abort"));
            Assert.Null(page.OriginalClose);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { await Cleanup(page, window, primary); }
    }

    [AvaloniaFact]
    public async Task Actual_Back_command_publishes_joinable_origin_and_loader_settlement_without_retiring_itself()
    {
        using var files = new Files(); var row = await files.Registry().CreateAsync("Back target");
        Task? accepted = null; var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = new SpaceBuilderPage(files.Registry(), row.Id, Ready.Instance, original =>
        { accepted = original; entered.TrySetResult(); return true; });
        Window? window = null; Exception? primary = null;
        try
        {
            window = Mount(page); await page.ActivateAsync().WaitAsync(Bound);
            Click(page.Host, "space-builder-back");
            await entered.Task.WaitAsync(Bound);
            await accepted!.WaitAsync(Bound);
            await page.Host.CaptureOriginalActionSettlement(page.Show!).WaitAsync(Bound);
            Assert.Null(page.OriginalClose);
            Assert.True(page.TryEdit(SpaceBuilderField.Description, "The retained page remains editable"));
            Assert.Equal(row.Id, page.State.SpaceId);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { await Cleanup(page, window, primary); }
    }

    internal static Window Mount(Control root)
    { var window = new Window { Content = root, Width = 1100, Height = 1100 }; window.Show(); return window; }

    internal static async Task<CuiSceneHost> Decision(SpaceBuilderPage page)
    {
        using var timeout = new CancellationTokenSource(Bound);
        while (page.Content is not CuiSceneHost host || ReferenceEquals(host, page.Host)
            || host.Availability?.State != CuiSceneAvailabilityState.Ready)
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle).GetTask().WaitAsync(timeout.Token);
        }
        return (CuiSceneHost)page.Content!;
    }

    internal static T Find<T>(CuiSceneHost host, string id) where T : Control
    {
        var window = Assert.IsType<Window>(TopLevel.GetTopLevel(host));
        for (var pass = 0; pass < 3; pass++)
        { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
        var root = Assert.IsAssignableFrom<Control>(host.Content);
        return Assert.Single(root.GetVisualDescendants().Prepend(root).OfType<T>(), control =>
            control.Name == id || AutomationProperties.GetAutomationId(control) == id);
    }
    internal static TextBox Input(CuiSceneHost host, string id) => Find<TextBox>(host, id);
    internal static void Click(CuiSceneHost host, string id)
    { var button = Find<Button>(host, id); Assert.True(button.IsEnabled); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }

    internal static async Task Cleanup(SpaceBuilderPage page, Window? window, Exception? primary)
    {
        List<Exception> failures = [];
        try { await page.CloseAndDrainAsync().WaitAsync(Bound); } catch (Exception error) { failures.Add(error); }
        if (window is not null)
        {
            try { window.Content = null; } catch (Exception error) { failures.Add(error); }
            try { window.Close(); Dispatcher.UIThread.RunJobs(); } catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count == 0) return;
        if (primary is not null) failures.Insert(0, primary);
        throw new AggregateException("Actual Builder check and owner cleanup retained their causes.", failures);
    }
    internal sealed class Ready : ICuiSceneReadiness
    {
        internal static readonly Ready Instance = new();
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) => new(new CuiSceneAvailability(
            CuiSceneAvailabilityState.Ready, "component-fixture", "Explicit headless fixture, not Home/ACL authority."));
    }
    private sealed class Files : IAppPaths, IDisposable
    {
        public Files()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "9to1-independent-builder-" + Guid.NewGuid().ToString("N"));
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(DataDirectory);
            else Directory.CreateDirectory(DataDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "unused.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "unused-browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "unused-attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "unused-logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "unused-legacy");
        public SpaceRegistry Registry() => new(new VersionedAtomicSettingsStore(this));
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }
}
