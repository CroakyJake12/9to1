using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.AIStudio.Tests;

public sealed partial class StudioNativeHostTests
{
    private static async Task CloseOriginalWindowAsync(StudioNativeWindow window)
    {
        List<Exception> failures = [];
        try { await window.PrepareShutdownAsync(); } catch (Exception error) { StudioOriginalTaskDrain.Add(failures, error); }
        try { window.AllowPreparedClose(); } catch (Exception error) { StudioOriginalTaskDrain.Add(failures, error); }
        if (window.OriginalShutdownTasksCapturedAndSettled)
            try { window.Close(); } catch (Exception error) { StudioOriginalTaskDrain.Add(failures, error); }
        StudioOriginalTaskDrain.Throw(failures);
    }

    [Fact]
    public async Task Ordinary_native_close_cancels_and_settles_the_original_avatar_save_before_the_same_Den_is_disposed()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-native-shutdown-" + Guid.NewGuid().ToString("N"));
        var chosen = Path.Combine(root, "chosen"); Directory.CreateDirectory(chosen);
        HeadlessUnitTestSession? native = null; Exception? primary = null; List<Exception> outerCleanup = [];
        try
        {
            native = HeadlessUnitTestSession.StartNew(typeof(StudioTestApplication));
            await native.Dispatch(async () =>
            {
                var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
                var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
                var actors = new Actors(profiles);
                var permissions = new HomePermissionTrustService(home, (app, action) => null);
                StudioNativeWindow? window = null; StudioDenLifetime? den = null;
                ServiceProvider? provider = null; HomeCoreRuntime? runtime = null;
                Task? originalChild = null; Task? originalShutdown = null; Exception? actionPrimary = null; List<Exception> cleanup = [];
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                bool pause = false, observedOwnedCancellation = false;
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    actors.ObservedCurrent = async ct =>
                    {
                        if (pause)
                        {
                            pause = false; entered.TrySetResult();
                            try { await released.Task.WaitAsync(ct); }
                            catch (OperationCanceledException error) when (error.CancellationToken == ct && ct.IsCancellationRequested)
                            { observedOwnedCancellation = true; throw; }
                        }
                        return await profiles.GetCurrentAsync(ct);
                    };
                    den = new StudioDenLifetime(actors, ct => window?.RetireSelectedContextAsync(ct) ?? Task.CompletedTask);
                    var ownership = new HomeLocalStoreOwnership(home, profiles, new HomeLocalStoreEvidenceRegistry([den]), permissions);
                    var receipts = new HomeResourceStoreOwnershipAuthority(ownership, profiles);
                    runtime = new HomeCoreRuntime([new HomeCoreStateService(home), new HomePermissionsCoreService(permissions, profiles)]);
                    var services = new ServiceCollection();
                    services.AddSingleton(runtime); services.AddSingleton(profiles); services.AddSingleton<IAuthenticatedResourceActorSource>(actors);
                    services.AddSingleton(permissions); services.AddSingleton(den); services.AddSingleton(ownership);
                    services.AddSingleton<IResourceStoreOwnershipReceiptAuthority>(receipts);
                    services.AddSingleton(SelectedFolder(chosen, Path.Combine(root, "unused.gif")));
                    services.AddTransient<ICanonicalAgentBuilderAdapter>(p => new DenCanonicalAgentBuilderAdapter(
                        ct => p.GetRequiredService<StudioDenLifetime>().OpenBoundSessionAsync(p.GetRequiredService<IResourceStoreOwnershipReceiptAuthority>(), ct)));
                    provider = services.BuildServiceProvider(); window = new StudioNativeWindow(provider); window.Show();
                    await window.Initialization.WaitAsync(deadline.Token);
                    FindButton(window, "Create a Den in an empty folder…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var originalCreateDen = window.WhenActionsIdleAsync(); await originalCreateDen.WaitAsync(deadline.Token);
                    var session = await den.OpenBoundSessionAsync(receipts, deadline.Token);
                    All(window).OfType<TextBox>().Single(item => Avalonia.Automation.AutomationProperties.GetName(item) == "New Agent name").Text = "Shutdown Agent";
                    FindButton(window, "Create Agent").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var originalCreateAgent = window.WhenActionsIdleAsync(); await originalCreateAgent.WaitAsync(deadline.Token);
                    var before = Assert.Single(await session.Den.ListAsync<AgentDefinitionRecord>("personal", deadline.Token));
                    All(window).OfType<TextBox>().Single(item => Avalonia.Automation.AutomationProperties.GetName(item) == "Static fallback asset reference").Text = "unresolved-static";
                    All(window).OfType<TextBox>().Single(item => Avalonia.Automation.AutomationProperties.GetName(item) == "Accessible avatar name").Text = "Shutdown preview";
                    FindButton(window, "Apply avatar identity").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var originalDraft = window.WhenAvatarActionsIdleAsync(); await originalDraft.WaitAsync(deadline.Token);
                    pause = true;
                    FindButton(window, "Save to Agent").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    originalChild = window.WhenAvatarActionsIdleAsync();
                    await entered.Task.WaitAsync(deadline.Token); Assert.False(originalChild.IsCompleted);
                    window.Close(); Assert.True(window.IsVisible);
                    originalShutdown = window.PrepareShutdownAsync();
                    Assert.Same(originalShutdown, window.PrepareShutdownAsync());
                    await originalShutdown.WaitAsync(deadline.Token);
                    Assert.True(originalChild.IsCompleted); await originalChild;
                    Assert.True(observedOwnedCancellation); Assert.True(window.OriginalShutdownTasksCapturedAndSettled);
                    // The same original Store remains live until its actual provider is disposed below.
                    var after = await session.Den.GetAsync<AgentDefinitionRecord>("personal", before.Id, deadline.Token);
                    Assert.NotNull(after); Assert.Equal(before.Id, after.Id); Assert.Equal(before.Revision, after.Revision); Assert.Null(after.Presentation);
                    Assert.False(await session.Den.AccessPolicy.IsAllowedAsync(session.Actor.ActorId, "personal", before.Id, DenPermission.Execute, deadline.Token));
                }
                catch (Exception error) { actionPrimary = error; }
                finally
                {
                    // A failed assertion/timeout must release the SAME controlled read, then cancel/drain the original tasks.
                    released.TrySetResult();
                    if (window is not null) try { await CloseOriginalWindowAsync(window); } catch (Exception error) { cleanup.Add(error); }
                    if (originalChild is not null) try { await originalChild; } catch (Exception error) { cleanup.Add(error); }
                    if (originalShutdown is not null) try { await originalShutdown; } catch (Exception error) { cleanup.Add(error); }
                    if (provider is not null) try { await provider.DisposeAsync(); } catch (Exception error) { cleanup.Add(error); }
                    if (den is not null) try { await den.DisposeAsync(); } catch (Exception error) { cleanup.Add(error); }
                    if (runtime is not null) try { await runtime.DisposeAsync(); } catch (Exception error) { cleanup.Add(error); }
                }
                ThrowOriginalAndCleanup(actionPrimary, cleanup);
                return true;
            }, default);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            if (native is not null) try { await native.DisposeAsync(); } catch (Exception error) { outerCleanup.Add(error); }
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception error) { outerCleanup.Add(error); }
        }
        ThrowOriginalAndCleanup(primary, outerCleanup);
    }
}
