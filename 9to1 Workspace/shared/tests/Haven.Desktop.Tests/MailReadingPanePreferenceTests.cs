using MailPage = Haven.Desktop.Views.Pages.Mail.MailPage;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Desktop.Views.Pages.Mail;

namespace Haven.Desktop.Tests;

public sealed class MailReadingPanePreferenceTests
{
    [AvaloniaFact]
    public async Task Actual_native_reading_choice_persists_in_real_settings_and_restores_in_fresh_page_without_mail_provider_IO()
    {
        var paths = new Paths(); Directory.CreateDirectory(paths.DataDirectory);
        try
        {
            var store = new VersionedAtomicSettingsStore(paths);
            await store.SetAsync("unrelated.preference", new OtherPreference("keep"), CancellationToken.None);
            using (var first = Page(store))
            {
                await first.PendingReadingPreference;
                first.FindControl<ComboBox>("ReadingPlacementPicker")!.SelectedIndex = 1;
                await first.PendingReadingPreference;
            }
            var fresh = new VersionedAtomicSettingsStore(paths);
            using var reopened = Page(fresh);
            await reopened.PendingReadingPreference;
            Assert.Equal(MailReadingPanePlacement.Bottom, reopened.ReadingPanePlacement);
            Assert.Equal(1, reopened.FindControl<ComboBox>("ReadingPlacementPicker")!.SelectedIndex);
            reopened.ApplyResponsiveLayout(1280);
            Assert.Equal(1, Grid.GetRow(reopened.FindControl<Border>("ReadingPanel")!));
            Assert.Equal("keep", (await fresh.GetAsync<OtherPreference>("unrelated.preference", CancellationToken.None))!.Value);
        }
        finally { Directory.Delete(paths.DataDirectory, true); }
    }

    [AvaloniaFact]
    public async Task Actual_saved_preference_read_held_after_IO_cannot_replace_later_native_user_choice_or_persisted_choice()
    {
        var paths = new Paths(); Directory.CreateDirectory(paths.DataDirectory);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            var store = new VersionedAtomicSettingsStore(paths);
            await store.SetAsync(MailPage.ReadingPreferenceKey, new MailReadingPanePreference(1, MailReadingPanePlacement.Bottom), deadline.Token);
            var port = DispatchProxy.Create<IVersionedSettingsStore, HeldRead>();
            var timing = (HeldRead)port; timing.Actual = store; timing.Lifetime = deadline.Token;
            using var page = Page(port);
            var oldRead = page.PendingReadingPreference;
            try
            {
                await timing.Entered.Task.WaitAsync(deadline.Token);
                page.FindControl<ComboBox>("ReadingPlacementPicker")!.SelectedIndex = 2;
                await page.PendingReadingPreference.WaitAsync(deadline.Token);
            }
            finally { timing.Release.TrySetResult(); }
            await oldRead.WaitAsync(deadline.Token);
            Assert.Equal(MailReadingPanePlacement.Off, page.ReadingPanePlacement);
            Assert.Equal(2, page.FindControl<ComboBox>("ReadingPlacementPicker")!.SelectedIndex);
            var actual = await new VersionedAtomicSettingsStore(paths).GetAsync<MailReadingPanePreference>(MailPage.ReadingPreferenceKey, deadline.Token);
            Assert.Equal(MailReadingPanePlacement.Off, actual!.Placement);
        }
        finally { Directory.Delete(paths.DataDirectory, true); }
    }

    [AvaloniaFact]
    public async Task Actual_retained_native_picker_cannot_write_after_disposal_while_original_issued_write_is_retained()
    {
        var paths = new Paths(); Directory.CreateDirectory(paths.DataDirectory);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var port = DispatchProxy.Create<IVersionedSettingsStore, HeldWrite>();
        var timing = (HeldWrite)port;
        timing.Actual = new VersionedAtomicSettingsStore(paths); timing.Lifetime = deadline.Token;
        using var page = Page(port);
        var window = new Window { Content = page, Width = 1280, Height = 720 };
        Task? originalWrite = null;
        try
        {
            window.Show(); await page.PendingReadingPreference.WaitAsync(deadline.Token);
            var picker = page.FindControl<ComboBox>("ReadingPlacementPicker")!;
            picker.SelectedIndex = 1;
            originalWrite = page.PendingReadingPreference;
            await timing.Entered.Task.WaitAsync(deadline.Token);
            // The actual settings write has committed; only its return is held.
            Assert.Equal(MailReadingPanePlacement.Bottom,
                (await new VersionedAtomicSettingsStore(paths).GetAsync<MailReadingPanePreference>(MailPage.ReadingPreferenceKey, deadline.Token))!.Placement);
            var files = Directory.GetFiles(paths.DataDirectory, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, File.ReadAllBytes);
            page.Dispose();
            picker.SelectedIndex = 2;
            Assert.Equal(MailReadingPanePlacement.Bottom, page.ReadingPanePlacement);
            Assert.Same(originalWrite, page.PendingReadingPreference);
            Assert.Equal(1, timing.Writes);
            timing.Release.TrySetResult();
            await originalWrite.WaitAsync(deadline.Token);
            Assert.Equal(1, timing.Writes);
            Assert.Equal(files.Keys.Order(), Directory.GetFiles(paths.DataDirectory, "*", SearchOption.AllDirectories).Order());
            foreach (var file in files) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
            Assert.Equal(MailReadingPanePlacement.Bottom,
                (await new VersionedAtomicSettingsStore(paths).GetAsync<MailReadingPanePreference>(MailPage.ReadingPreferenceKey, deadline.Token))!.Placement);
        }
        finally
        {
            timing.Release.TrySetResult();
            try { if (originalWrite is not null) await originalWrite.WaitAsync(deadline.Token); } catch { }
            window.Close(); page.Dispose(); Directory.Delete(paths.DataDirectory, true);
        }
    }

    public class HeldWrite : DispatchProxy
    {
        public IVersionedSettingsStore Actual { get; set; } = null!;
        public CancellationToken Lifetime { get; set; }
        public int Writes { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(method);
            if (method.Name == nameof(IVersionedSettingsStore.SetAsync) && method.GetGenericArguments().Single() == typeof(MailReadingPanePreference))
            {
                Writes++;
                return RetainReturnAsync((Task)method.Invoke(Actual, args)!);
            }
            return method.Invoke(Actual, args);
        }
        private async Task RetainReturnAsync(Task actual)
        {
            await actual; Entered.TrySetResult(); await Release.Task.WaitAsync(Lifetime);
        }
    }

    private static MailPage Page(IVersionedSettingsStore store) => new(
        DispatchProxy.Create<IMailService, MailReadingPaneChoiceTests.NoConnectedAccount>(),
        DispatchProxy.Create<IProviderModelClient, MailReadingPaneChoiceTests.UnusedModel>(), store);
    public class HeldRead : DispatchProxy
    {
        public IVersionedSettingsStore Actual { get; set; } = null!;
        public CancellationToken Lifetime { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(method);
            if (method.Name == nameof(IVersionedSettingsStore.GetAsync) && method.GetGenericArguments().Single() == typeof(MailReadingPanePreference))
                return ReadAsync((string)args![0]!, (CancellationToken)args[1]!);
            return method.Invoke(Actual, args);
        }
        private async Task<MailReadingPanePreference?> ReadAsync(string key, CancellationToken token)
        {
            var actual = await Actual.GetAsync<MailReadingPanePreference>(key, token);
            Entered.TrySetResult(); await Release.Task.WaitAsync(Lifetime); return actual;
        }
    }
    public sealed record OtherPreference(string Value);
    private sealed class Paths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "astra-mail-reading-pref-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "app.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
    }
}
