using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Desktop;

namespace Haven.Desktop.Tests;

public sealed class MainWindowOriginalPreCloseTests
{
    [AvaloniaFact]
    public async Task Resource_retirement_is_not_a_native_Closed_callback_witness()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var first = window.CloseAndDrainAsync();
            Assert.Same(first, window.CloseAndDrainAsync());
            await first;
            Assert.True(window.IsVisible);
            Assert.False(window.OriginalNativeClosedCallback.IsCompleted);
            window.Close();
            await window.OriginalNativeClosedCallback;
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Existing_native_close_veto_preserves_window_and_admits_no_delivery()
    {
        var window = new MainWindow();
        EventHandler<WindowClosingEventArgs> veto = (_, args) => args.Cancel = true;
        window.Closing += veto;
        window.Show();
        try
        {
            window.Close();
            Assert.True(window.IsVisible);
            Assert.Null(window.OriginalWindowCloseDelivery);
            Assert.False(window.OriginalNativeClosedCallback.IsCompleted);
            window.Closing -= veto;
            window.Close();
            var original = Assert.IsAssignableFrom<Task>(window.OriginalWindowCloseDelivery);
            await original;
            Assert.True(window.OriginalNativeClosedCallback.IsCompletedSuccessfully);
            Assert.False(window.IsVisible);
            Assert.Same(original, window.OriginalWindowCloseDelivery);
            await window.CloseAndDrainAsync();
        }
        finally { window.Closing -= veto; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Ordinary_window_close_joins_its_actual_delivery_and_emits_Closed_once()
    {
        var window = new MainWindow();
        var closed = 0;
        window.Closed += (_, _) => closed++;
        window.Show();
        try
        {
            window.Close();
            var delivery = Assert.IsAssignableFrom<Task>(window.OriginalWindowCloseDelivery);
            var entire = window.CloseAndDrainAsync();
            Assert.Same(entire, window.CloseAndDrainAsync());
            await entire;
            Assert.True(delivery.IsCompletedSuccessfully);
            Assert.True(window.OriginalNativeClosedCallback.IsCompletedSuccessfully);
            Assert.Equal(1, closed);
            Assert.False(window.IsVisible);
            window.Close();
            Assert.Equal(1, closed);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Restored_context_native_Closing_callback_cannot_join_its_original_window()
    {
        var window = new MainWindow();
        var externalContext = ExecutionContext.Capture()!;
        var refusals = 0;
        window.Closing += (_, _) => ExecutionContext.Run(externalContext, _ =>
        {
            Assert.Throws<InvalidOperationException>(() =>
            { window.CloseAndDrainAsync().GetAwaiter().GetResult(); });
            refusals++;
        }, null);
        window.Show();
        try
        {
            window.Close();
            await Assert.IsAssignableFrom<Task>(window.OriginalWindowCloseDelivery);
            Assert.Equal(2, refusals); // Initial request and actual resumed native close.
            Assert.True(window.OriginalNativeClosedCallback.IsCompletedSuccessfully);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Actual_Closed_callback_is_unsettled_until_its_synchronous_observers_return()
    {
        var window = new MainWindow();
        var observed = false;
        var externalContext = ExecutionContext.Capture()!;
        window.Closed += (_, _) =>
        {
            Assert.False(window.OriginalNativeClosedCallback.IsCompleted);
            ExecutionContext.Run(externalContext, _ =>
            {
                Assert.Throws<InvalidOperationException>(() =>
                { window.CloseAndDrainAsync().GetAwaiter().GetResult(); });
            }, null);
            observed = true;
        };
        window.Show();
        try
        {
            window.Close();
            await Assert.IsAssignableFrom<Task>(window.OriginalWindowCloseDelivery);
            Assert.True(observed);
            Assert.True(window.OriginalNativeClosedCallback.IsCompletedSuccessfully);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Veto_after_resource_retirement_still_refuses_a_native_closure_witness()
    {
        var window = new MainWindow();
        EventHandler<WindowClosingEventArgs> veto = (_, args) => args.Cancel = true;
        window.Show();
        try
        {
            await window.CloseAndDrainAsync();
            window.Closing += veto;
            window.Close();
            Assert.True(window.IsVisible);
            Assert.False(window.OriginalNativeClosedCallback.IsCompleted);
            window.Closing -= veto;
            window.Close();
            await window.OriginalNativeClosedCallback;
            Assert.False(window.IsVisible);
        }
        finally { window.Closing -= veto; window.Close(); }
    }
}
