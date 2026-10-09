using Avalonia.Headless.XUnit;
using Haven.Desktop.Events;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Pages.Go;
using Haven.Desktop.Views.Shell;

namespace Haven.Desktop.Tests;

/// <summary>Real maintained Go callback and pure shell-cohort component control.
/// This is not a MainView/DI/factory, rendered frame or whole shutdown receipt.</summary>
public sealed class MainViewOriginalLateChildTests
{
    [AvaloniaFact]
    public async Task Late_actual_child_callback_cannot_join_parent_through_a_sealed_only_cohort()
    {
        var previous = ExecutionContext.Capture()!;
        var child = new GoPage(new HavenEventBus());
        Task? actualClose = null;
        Exception? refusal = null;
        var callbacks = 0;
        child.Disposed += (_, _) => ExecutionContext.Run(previous, _ =>
        {
            callbacks++;
            try { MainView.DemandOriginalShellChildJoins([], [child]); }
            catch (Exception error) { refusal = error; }
        }, null);
        try
        {
            child.RequestRetirement();
            actualClose = child.CloseAndDrainAsync();
            await actualClose.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(1, callbacks);
            Assert.IsType<InvalidOperationException>(refusal);
            Assert.Same(actualClose, child.CloseAndDrainAsync());
            // After the SAME real child callback terminal, pure external join is permitted.
            MainView.DemandOriginalShellChildJoins([], [child]);
        }
        finally
        {
            child.RequestRetirement();
            await (actualClose ?? child.CloseAndDrainAsync());
        }
    }
    [AvaloniaFact]
    public async Task Late_actual_tab_cancellation_callback_is_in_the_whole_join_preflight()
    {
        var previous = ExecutionContext.Capture()!;
        using var bus = new HavenEventBus();
        WorkspaceTabViewModel? tab = null;
        CancellationTokenRegistration registration = default;
        Task? actualClose = null;
        Exception? refusal = null;
        Exception? primary = null;
        var callbacks = 0;
        var page = new GoPage(bus);
        try
        {
            tab = new WorkspaceTabViewModel("actual-late", "Late", page, true, HavenSurface.Go);
            registration = tab.LifetimeToken.Register(() => ExecutionContext.Run(previous, _ =>
            {
                callbacks++;
                try { MainView.DemandOriginalShellTabJoins([], [], tab); }
                catch (Exception error) { refusal = error; }
            }, null));
            actualClose = tab.CloseAndDrainAsync();
            await actualClose.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(1, callbacks);
            Assert.IsType<InvalidOperationException>(refusal);
            Assert.Same(actualClose, tab.CloseAndDrainAsync());
            MainView.DemandOriginalShellTabJoins([], [tab], tab);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            var failures = new List<Exception>();
            try { registration.Dispose(); } catch (Exception error) { failures.Add(error); }
            if (tab is not null)
                try { await (actualClose ?? tab.CloseAndDrainAsync()); }
                catch (Exception error) { failures.Add(error); }
            // The earliest returned Go product remains independently owned even when
            // actual tab construction/registration/close fails before admitting it.
            try { await page.CloseAndDrainAsync(); } catch (Exception error) { failures.Add(error); }
            if (failures.Count != 0)
            {
                if (primary is not null) failures.Insert(0, primary);
                throw new AggregateException("Actual acquired tab/page fixture cleanup.", failures);
            }
        }
    }
}
