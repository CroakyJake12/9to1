using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Development;
using HavenOS.Apps.Dev;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class CanonicalCompatibleTaskContextCompositionTests
{
    [AvaloniaFact]
    public async Task Build_session_records_Recent_only_after_the_same_real_manual_workbench_opens()
    {
        await Run(async rig =>
        {
            var token = TestContext.Current.CancellationToken;
            var landing = BuildLanding(rig);
            var settings = rig.Provider.GetRequiredService<IVersionedSettingsStore>();
            using var lifetime = new CancellationTokenSource();
            var session = CreateStartSession(rig, landing, lifetime, settings);
            try
            {
                await session.InitializeAsync(token);
                var view = Assert.IsType<Border>(session.OriginalNativeView);
                var page = Assert.IsType<DeveloperSavedProjectsPage>(view.Child);
                var preferences = new BuildProjectStartPreferences(settings, rig.Actor.ProfileId,
                    cancellation => landing.Borrowed.RevalidateOriginalAppContentAsync(rig.Actor.ProfileId, cancellation));
                Assert.Empty((await preferences.ReadAsync(token)).List.Projects);
                var reference = JsonSerializer.Deserialize<DeveloperProjectReference>(rig.ProjectReference)!;
                await page.DispatchAsync("dev.references.open-task", StartManualSelection(page), token);
                var workbench = Assert.IsType<DeveloperProjectWorkbenchPage>(view.Child);
                Assert.True(session.IsPublicationCurrent);
                Assert.NotSame(page, workbench);
                Assert.True(workbench.TryGetValue("Status", out var openedStatus));
                Assert.Equal("Existing project opened. Source reads use its current Home permissions.", openedStatus);
                var opened = Assert.Single((await preferences.ReadAsync(token)).List.Projects);
                Assert.Equal(BuildProjectStartIdentity.ForManual(reference), opened.Identity);
                Assert.NotNull(opened.LastOpenedAt);
                Assert.False(page.IsActionAvailable("dev.references.open-task"));
                Assert.True(await session.TryPrepareToCloseAsync(token));
            }
            finally { await session.CloseAndDrainAsync(); }
            Assert.True(session.CanRetireOriginalView);
            Assert.True(landing.Borrowed.Closed);
        }, includeOriginalManualOwners: true);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recent_store_failure_keeps_the_open_workbench_reports_only_while_current_and_never_reopens(bool closeDuringCommit)
    {
        await Run(async rig =>
        {
            var token = TestContext.Current.CancellationToken;
            var landing = BuildLanding(rig);
            var settings = new StartSessionSettings(rig.Provider.GetRequiredService<IVersionedSettingsStore>())
                { FailCommit = true, HoldCommit = closeDuringCommit };
            using var lifetime = new CancellationTokenSource();
            var session = CreateStartSession(rig, landing, lifetime, settings);
            Task? close = null;
            Task? opening = null;
            try
            {
                await session.InitializeAsync(token);
                var view = Assert.IsType<Border>(session.OriginalNativeView);
                var page = Assert.IsType<DeveloperSavedProjectsPage>(view.Child);
                opening = page.DispatchAsync("dev.references.open-task", StartManualSelection(page), token).AsTask();
                await Task.WhenAny(settings.CommitEntered.Task, opening).WaitAsync(token);
                if (!settings.CommitEntered.Task.IsCompleted) await opening;
                Assert.True(settings.CommitEntered.Task.IsCompleted);
                var workbench = Assert.IsType<DeveloperProjectWorkbenchPage>(view.Child);
                Assert.True(session.IsPublicationCurrent);
                if (closeDuringCommit)
                {
                    close = session.CloseAndDrainAsync();
                    Assert.False(close.IsCompleted);
                    settings.CommitRelease.TrySetResult();
                }
                var failure = await Record.ExceptionAsync(() => opening);
                Assert.NotNull(failure);
                Assert.Contains(Flatten(failure!), error => error is InvalidOperationException
                    { Message: "The project opened, but its Recent entry could not be saved.", InnerException: IOException original }
                    && ReferenceEquals(original, settings.Failure));
                Assert.DoesNotContain(Flatten(failure!), error => error is ObjectDisposedException);
                if (closeDuringCommit)
                    Assert.False(workbench.TryGetValue("Status", out _)); // Retired native bindings redact their former page.
                else
                {
                    Assert.True(session.IsPublicationCurrent);
                    Assert.True(workbench.TryGetValue("Status", out var status));
                    Assert.Contains("The project opened, but its Recent entry could not be saved.", Assert.IsType<string>(status));
                }
                Assert.Equal(1, settings.CommitCalls);
                Assert.False(page.IsActionAvailable("dev.references.open-task"));
                close ??= session.CloseAndDrainAsync();
                var closeFailure = await Record.ExceptionAsync(() => close);
                Assert.NotNull(closeFailure);
                Assert.Contains(Flatten(closeFailure!), error => error is InvalidOperationException
                    { Message: "The project opened, but its Recent entry could not be saved.", InnerException: IOException original }
                    && ReferenceEquals(original, settings.Failure));
                Assert.DoesNotContain(Flatten(closeFailure!), error => error is ObjectDisposedException);
                Assert.Equal(1, settings.CommitCalls);
                Assert.Same(workbench, view.Child);
                Assert.True(landing.Borrowed.Closed);
            }
            finally
            {
                settings.CommitRelease.TrySetResult();
                if (opening is not null) await Record.ExceptionAsync(() => opening);
                await Record.ExceptionAsync(() => close ?? session.CloseAndDrainAsync());
            }
        }, includeOriginalManualOwners: true);
    }

    [AvaloniaFact]
    public async Task Build_close_joins_the_original_setup_preferences_read_and_retains_its_late_failure()
    {
        await Run(async rig =>
        {
            var token = TestContext.Current.CancellationToken;
            var landing = BuildLanding(rig);
            var settings = new StartSessionSettings(rig.Provider.GetRequiredService<IVersionedSettingsStore>())
                { HoldRead = true };
            using var lifetime = new CancellationTokenSource();
            var session = CreateStartSession(rig, landing, lifetime, settings);
            var initialization = session.InitializeAsync(token);
            Task? close = null;
            try
            {
                await Task.WhenAny(settings.ReadEntered.Task, initialization).WaitAsync(token);
                if (!settings.ReadEntered.Task.IsCompleted) await initialization;
                Assert.True(settings.ReadEntered.Task.IsCompleted, "The actual setup preferences read must be acquired during activation.");
                close = session.CloseAndDrainAsync();
                Assert.False(close.IsCompleted);
                settings.ReadRelease.TrySetResult();
                var failure = await Record.ExceptionAsync(() => initialization);
                var closeFailure = await Record.ExceptionAsync(() => close);
                Assert.NotNull(failure); Assert.NotNull(closeFailure);
                Assert.Contains(Flatten(failure!), error => ReferenceEquals(error, settings.Failure));
                Assert.Contains(Flatten(closeFailure!), error => ReferenceEquals(error, settings.Failure));
                Assert.False(session.IsPublicationCurrent);
                Assert.True(landing.Borrowed.Closed);
                Assert.Equal(0, settings.CommitCalls);
            }
            finally
            {
                settings.ReadRelease.TrySetResult();
                await Record.ExceptionAsync(() => initialization);
                await Record.ExceptionAsync(() => close ?? session.CloseAndDrainAsync());
            }
        });
    }

    private static BuildBrowseAppContentSession CreateStartSession(Rig rig, BuildLandingState landing,
        CancellationTokenSource lifetime, IVersionedSettingsStore settings)
    {
        var request = LandingRequest(rig);
        var session = new BuildBrowseAppContentSession(request, BuildBrowseAppContentAddress.Parse(request.Address),
            rig.Provider.GetRequiredService<IDeveloperWorkspaceStore>(),
            rig.Provider.GetRequiredService<DeveloperTaskWorkspaceService>(),
            rig.Provider.GetRequiredService<TaskExecutionCoordinator>(),
            rig.Provider.GetRequiredService<DeveloperProjectWorkbenchPageFactory>(),
            landing.Spaces, landing.Catalog, lifetime, null, startSettings: settings);
        session.AttachBorrowed(new StartSessionBorrowedResources(landing.Borrowed));
        return session;
    }

    private sealed class StartSessionBorrowedResources(BuildTestBorrowedResources actual) : IBuildBorrowedResources
    {
        public ICuiSceneReadiness BorrowOriginalHomeReadiness() => actual.BorrowOriginalHomeReadiness();
        public void DemandOriginalHomeJoin() => actual.DemandOriginalHomeJoin();
        public Task CloseAndDrainAsync() => actual.CloseAndDrainAsync();
        public Task AdmitOriginalShellInitializationAsync(CancellationToken token) => actual.AdmitOriginalShellInitializationAsync(token);
        public void CheckOriginalAppContentProfile(string profile) => actual.CheckOriginalAppContentProfile(profile);
        public Task RevalidateOriginalAppContentAsync(string profile, CancellationToken token)
        {
            Dispatcher.UIThread.VerifyAccess(); // The production borrowed route has this same requirement.
            return actual.RevalidateOriginalAppContentAsync(profile, token);
        }
        public Task? OriginalAdmission => actual.OriginalAdmission;
    }

    private static string StartManualSelection(DeveloperSavedProjectsPage page)
    {
        Assert.True(page.TryGetValue("Projects", out var value));
        var row = Assert.Single(Assert.IsType<object[]>(value));
        Assert.True(((ICuiRepeatItemBindingContext)page).TryGetItemValue(row, "SelectionKey", out var key));
        return Assert.IsType<string>(key);
    }

    private sealed class StartSessionSettings(IVersionedSettingsStore actual)
        : IVersionedSettingsStore, IVersionedSettingsGuardedCompareExchange
    {
        internal bool FailCommit, HoldRead, HoldCommit;
        internal int CommitCalls;
        internal IOException Failure { get; } = new("Original Build preferences storage failed.");
        internal TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReadRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CommitEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CommitRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private IVersionedSettingsGuardedCompareExchange Guarded => Assert.IsAssignableFrom<IVersionedSettingsGuardedCompareExchange>(actual);
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class => actual.GetAsync<T>(key, token);
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => actual.SetAsync(key, value, token);
        public Task RemoveAsync(string key, CancellationToken token) => actual.RemoveAsync(key, token);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => actual.ImportAsync(manifest, token);
        public async Task<SettingsExportManifest> ExportAsync(CancellationToken token)
        {
            var value = await actual.ExportAsync(token);
            if (!HoldRead) return value;
            ReadEntered.TrySetResult();
            await ReadRelease.Task; // Deliberately ignores cancellation: close must join this original.
            throw Failure;
        }
        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expected,
            string? replacement, CancellationToken token) => Guarded.CompareExchangeAsync(key, expected, replacement, token);
        public Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected,
            string? replacement, IReadOnlyDictionary<string, string?> guards, CancellationToken token) =>
            Guarded.CompareExchangeGuardedAsync(key, expected, replacement, guards, token);
        public async Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected,
            string? replacement, IReadOnlyDictionary<string, string?> guards, ISettingsCommitAdmission admission, CancellationToken token)
        {
            CommitCalls++;
            CommitEntered.TrySetResult();
            if (HoldCommit) await CommitRelease.Task; // Same ownership check when the original write ignores cancellation.
            if (FailCommit) throw Failure;
            return await Guarded.CompareExchangeGuardedAsync(key, expected, replacement, guards, admission, token);
        }
    }
}
