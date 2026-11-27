using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.Spaces;

namespace HavenOS.Browse.Host.Tests;

/// <summary>Drives the real Home command queue while its session serial is held. The held
/// serial makes admission versus execution deterministic without replacing the route owner.</summary>
public sealed class SpacesQueuedNavigationSupersessionTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [AvaloniaFact]
    public Task Newer_selection_settles_older_queue_without_navigation_and_allows_close() => RunAsync(async owner =>
    {
        var olderTarget = await owner.Rig.Spaces.CreateAsync("Superseded target");
        var newerTarget = await owner.Rig.Spaces.CreateAsync("Current target");
        var publishedSpaces = new ConcurrentQueue<Guid?>();
        var statuses = new ConcurrentQueue<string>();
        // Session state also publishes repository-load notifications from a worker.
        // Observe the real UI mount's Content change instead: this records every actual
        // mounted page synchronously and never reads an Avalonia control off its owner thread.
        var mount = Assert.IsType<ContentControl>(owner.Page.SpaceWorkspaceMount);
        mount.PropertyChanged += (_, change) =>
        {
            if (change.Property == ContentControl.ContentProperty)
                publishedSpaces.Enqueue((change.NewValue as NewChatPage)?.CurrentConversation.SpaceId);
        };
        owner.Page.Controller.StateChanged += state => statuses.Enqueue(state.StatusMessage);
        var serial = CommandSerial(owner.Session);
        Task older, newer;
        await serial.WaitAsync(Bound);
        try
        {
            older = await QueueAsync(owner, olderTarget.Id);
            owner.Chat.SetDraft("Newer draft typed while navigation waits");
            newer = await QueueAsync(owner, newerTarget.Id);
            Assert.NotSame(older, newer);
            Assert.False(older.IsCompleted);
            Assert.False(newer.IsCompleted);
        }
        finally { serial.Release(); }

        await Task.WhenAll(older, newer).WaitAsync(Bound);
        Assert.True(older.IsCompletedSuccessfully); // Settled refusal, not a successful navigation.
        var refused = Assert.IsType<SpacesBrowseAppContentSession.SupersededSpaceNavigation>(
            owner.Session.LastSupersededSpaceNavigation);
        Assert.Equal(olderTarget.Id, refused.RequestedSpaceId);
        Assert.Equal(newerTarget.Id, refused.CurrentSpaceId);
        Assert.DoesNotContain(olderTarget.Id, publishedSpaces);
        Assert.Contains(newerTarget.Id, publishedSpaces);
        Assert.DoesNotContain(OwnedChats(owner.Session), chat => chat.CurrentConversation.SpaceId == olderTarget.Id);
        var current = Assert.IsType<NewChatPage>(owner.Page.SpaceWorkspaceContent);
        Assert.Equal(newerTarget.Id, current.CurrentConversation.SpaceId);
        Assert.Equal(newerTarget.Id, owner.Page.Controller.State.Selection!.Id);
        Assert.Equal("Newer draft typed while navigation waits", owner.Chat.ComposerInput.Text);
        Assert.DoesNotContain(statuses, status => status.Contains("Select it again.", StringComparison.Ordinal));
        await AssertPreparedCloseSucceeds(owner);
    });

    [AvaloniaFact]
    public Task Same_target_refresh_reports_retry_preserves_draft_and_allows_close() => RunAsync(async owner =>
    {
        var originalAddress = owner.Session.Address;
        var modelReads = owner.Rig.ModelReads;
        var serial = CommandSerial(owner.Session);
        Task queued;
        await serial.WaitAsync(Bound);
        try
        {
            queued = await QueueAsync(owner, SpaceRegistry.ChatSpaceId);
            var admittedSelection = owner.Page.Controller.State.Selection;
            await owner.Page.Controller.LoadAsync().WaitAsync(Bound);
            Assert.NotSame(admittedSelection, owner.Page.Controller.State.Selection);
            Assert.Equal(SpaceRegistry.ChatSpaceId, owner.Page.Controller.State.Selection!.Id);
            owner.Chat.SetDraft("Keep this exact newer draft after refresh");
            Assert.False(queued.IsCompleted);
        }
        finally { serial.Release(); }

        await queued.WaitAsync(Bound);
        var refused = Assert.IsType<SpacesBrowseAppContentSession.SupersededSpaceNavigation>(
            owner.Session.LastSupersededSpaceNavigation);
        Assert.Equal(SpaceRegistry.ChatSpaceId, refused.RequestedSpaceId);
        Assert.Equal(refused.AdmittedRouteEpoch, refused.CurrentRouteEpoch);
        Assert.Same(owner.Chat, owner.Page.SpaceWorkspaceContent);
        Assert.Equal(originalAddress, owner.Session.Address);
        Assert.Equal(modelReads, owner.Rig.ModelReads);
        Assert.Single(OwnedChats(owner.Session));
        Assert.Equal("Keep this exact newer draft after refresh", owner.Chat.ComposerInput.Text);
        Assert.Contains("Select it again.", owner.Page.Controller.State.StatusMessage);
        await AssertPreparedCloseSucceeds(owner);
    });

    [AvaloniaFact]
    public Task Actual_navigation_failure_remains_retained_and_vetoes_close() => RunAsync(async owner =>
    {
        var target = await owner.Rig.Spaces.CreateAsync("Archived after admission");
        owner.Chat.SetDraft("Keep draft when real navigation fails");
        var serial = CommandSerial(owner.Session);
        Task queued;
        await serial.WaitAsync(Bound);
        try
        {
            queued = await QueueAsync(owner, target.Id);
            var admittedSelection = owner.Page.Controller.State.Selection;
            // The queue entry itself is still current. The canonical repository now rejects
            // its destination inside ExecuteChatOnUiAsync, after the supersession fence.
            await owner.Rig.Spaces.SetArchivedAsync(target.Id, true).WaitAsync(Bound);
            Assert.Same(admittedSelection, owner.Page.Controller.State.Selection);
            Assert.False(queued.IsCompleted);
        }
        finally { serial.Release(); }

        var originalFailure = await Assert.ThrowsAsync<SpacesWorkspaceActionRefusal>(() => queued.WaitAsync(Bound));
        owner.ExpectedCloseFailure = originalFailure;
        Assert.Contains("archived", originalFailure.Message);
        Assert.Null(owner.Session.LastSupersededSpaceNavigation);
        Assert.True(queued.IsFaulted);
        Assert.Equal("Keep draft when real navigation fails", owner.Chat.ComposerInput.Text);
        Assert.Null(owner.Page.SpaceWorkspaceContent); // Existing unavailable presentation is retained.
        var preparationFailure = await Assert.ThrowsAnyAsync<Exception>(
            () => owner.Session.PrepareCloseAsync().WaitAsync(Bound));
        Assert.True(Contains(preparationFailure, originalFailure));
        Assert.False(owner.Session.CanRetireOriginalView);
    });

    private static async Task<Task> QueueAsync(Owner owner, Guid spaceId)
    {
        var prior = owner.Session.OriginalQueuedNavigation;
        await owner.Page.DispatchAsync("spaces.select", spaceId).AsTask().WaitAsync(Bound);
        var queued = owner.Session.OriginalQueuedNavigation;
        Assert.NotNull(queued);
        Assert.NotSame(prior, queued);
        return queued;
    }

    private static async Task AssertPreparedCloseSucceeds(Owner owner)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle).GetTask().WaitAsync(Bound);
        var preparation = await owner.Session.PrepareCloseAsync().WaitAsync(Bound);
        Assert.NotNull(preparation);
        Assert.True(preparation.IsCurrent);
        await preparation.BeginClose().WaitAsync(Bound);
        Assert.True(owner.Session.CanRetireOriginalView);
        Assert.Null(owner.Root.Content);
    }

    private static SemaphoreSlim CommandSerial(SpacesBrowseAppContentSession session) =>
        (SemaphoreSlim)typeof(SpacesBrowseAppContentSession)
            .GetField("_commandSerial", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;

    private static IReadOnlyList<NewChatPage> OwnedChats(SpacesBrowseAppContentSession session) =>
        (List<NewChatPage>)typeof(SpacesBrowseAppContentSession)
            .GetField("_ownedChats", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;

    private sealed record Owner(SpacesConversationRouteTests.Rig Rig, SpacesBrowseAppContentSession Session,
        ContentControl Root, SpacesHomePage Page, NewChatPage Chat)
    {
        internal Exception? ExpectedCloseFailure;
    }

    private static async Task RunAsync(Func<Owner, Task> body)
    {
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true, allowConversationWrites: true);
        SpacesBrowseAppContentSession? session = null;
        Window? window = null;
        Owner? owner = null;
        var failures = new List<Exception>();
        try
        {
            await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
            var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
            Assert.NotNull(actor);
            var saved = rig.Row! with { SpaceId = SpaceRegistry.ChatSpaceId };
            rig.Row = saved;
            session = rig.Session($"9to1://spaces/selected/{SpaceRegistry.ChatSpaceId:D}/chat/{saved.Id:D}", profileId: actor.ProfileId);
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var page = Assert.IsType<SpacesHomePage>(root.Content);
            var chat = Assert.IsType<NewChatPage>(page.SpaceWorkspaceContent);
            window = new Window { Content = root, Width = 1100, Height = 900 };
            window.Show();
            owner = new(rig, session, root, page, chat);
            await body(owner);
        }
        catch (Exception cause) { failures.Add(cause); }
        try
        {
            if (session is not null)
            {
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle).GetTask().WaitAsync(Bound);
                if (owner?.ExpectedCloseFailure is { } expected)
                {
                    var closeFailure = await Assert.ThrowsAnyAsync<Exception>(() => session.CloseAndDrainAsync().WaitAsync(Bound));
                    Assert.True(Contains(closeFailure, expected));
                    Assert.False(session.CanRetireOriginalView);
                }
                else await session.CloseAndDrainAsync().WaitAsync(Bound);
            }
        }
        catch (Exception cause) { failures.Add(cause); }
        if (window is not null)
        {
            try { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
            catch (Exception cause) { failures.Add(cause); }
        }
        try { await rig.Home!.CloseAndDrainAsync().WaitAsync(Bound); }
        catch (Exception cause) { failures.Add(cause); }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("The check and owner cleanup retained their actual causes.", failures);
    }

    private static bool Contains(Exception actual, Exception expected) => ReferenceEquals(actual, expected) ||
        actual is AggregateException group && group.InnerExceptions.Any(inner => Contains(inner, expected)) ||
        actual.InnerException is { } next && Contains(next, expected);
}
