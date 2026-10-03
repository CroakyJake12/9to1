using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Spaces;
using Haven.Infrastructure;
using Haven.UI;
using Haven.UI.Components;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Data.Sqlite;

namespace Haven.Desktop.Tests;

public sealed class SpacesConversationRefreshTests
{
    [AvaloniaFact]
    public async Task Returning_to_same_space_cannot_adopt_older_actual_conversation_read()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        using var fixture = new Fixture(); await fixture.InitializeAsync(token);
        var a = await fixture.Spaces.CreateAsync("Native A", cancellationToken: token);
        var b = await fixture.Spaces.CreateAsync("Native B", cancellationToken: token);
        var now = DateTimeOffset.UtcNow;
        var chat = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Original chat", null, null, false, false, now, now, SpaceId: a.Id);
        await fixture.SeedInSpaceAsync(chat, a, token);
        var proxy = DispatchProxy.Create<IConversationRepository, HeldRead>();
        var held = (HeldRead)(object)proxy; held.Actual = fixture.Repository; held.Token = token;
        using var page = new NativeSpacesPage(fixture.Spaces, conversations: proxy);
        var window = new Window { Width = 1100, Height = 800, Content = page };
        Task? older = null;
        try
        {
            window.Show(); await page.RefreshNowAsync(token);
            Press(page, a.Name); await page.PendingSelection.WaitAsync(token);
            held.HoldNext = true; Press(page, a.Name); older = page.PendingSelection;
            await held.Entered.Task.WaitAsync(token);
            await fixture.Repository.UpsertConversationAsync(chat with { Title = "Current chat", UpdatedAt = now.AddSeconds(1) }, token);
            Press(page, b.Name); await page.PendingSelection.WaitAsync(token);
            Press(page, a.Name); await page.PendingSelection.WaitAsync(token);
            held.Release.TrySetResult(); await older.WaitAsync(token);
            var titles = page.Scene.Root!.DescendantsAndSelf().OfType<Haven.UI.Components.Container>().Single(container => container.Name == "Conversations").DescendantsAndSelf().OfType<Haven.UI.Components.Button>().Select(button => button.Content).ToArray();
            Assert.Contains("Current chat", titles); Assert.DoesNotContain("Original chat", titles);
            Assert.Equal("Current chat", (await fixture.Repository.GetAsync(chat.Id, token))!.Title);
        }
        finally
        {
            held.Release.TrySetResult();
            try { if (older is not null) { try { await older; } catch { } } }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public async Task Deactivated_space_cannot_adopt_pending_actual_conversation_read()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        using var fixture = new Fixture(); await fixture.InitializeAsync(token);
        var space = await fixture.Spaces.CreateAsync("Retired selection", cancellationToken: token);
        var empty = await fixture.Spaces.CreateAsync("Empty before retirement", cancellationToken: token);
        var now = DateTimeOffset.UtcNow;
        var chat = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Retained chat", null, null, false, false, now, now, SpaceId: space.Id);
        await fixture.SeedInSpaceAsync(chat, space, token);
        var proxy = DispatchProxy.Create<IConversationRepository, HeldRead>();
        var held = (HeldRead)(object)proxy; held.Actual = fixture.Repository; held.Token = token;
        using var page = new NativeSpacesPage(fixture.Spaces, conversations: proxy);
        var window = new Window { Width = 1100, Height = 800, Content = page };
        Task? pending = null;
        try
        {
            window.Show(); await page.RefreshNowAsync(token);
            Press(page, empty.Name); await page.PendingSelection.WaitAsync(token);
            held.HoldNext = true; Press(page, space.Name); pending = page.PendingSelection;
            await held.Entered.Task.WaitAsync(token);
            page.Deactivate();
            held.Release.TrySetResult(); await pending.WaitAsync(token);
            Assert.Empty(page.Scene.Root!.DescendantsAndSelf().OfType<Haven.UI.Components.Container>().Single(container => container.Name == "Conversations").DescendantsAndSelf().OfType<Haven.UI.Components.Button>());
            Assert.Equal(chat, await fixture.Repository.GetAsync(chat.Id, token));
        }
        finally
        {
            held.Release.TrySetResult();
            try { if (pending is not null) { try { await pending; } catch { } } }
            finally { window.Close(); }
        }
    }

    [AvaloniaTheory]
    [InlineData("space-aba")]
    [InlineData("newer-click")]
    [InlineData("deactivate")]
    [InlineData("dispose")]
    public async Task Original_native_chat_click_cannot_navigate_after_actual_read_returns_to_retired_selection(string retirement)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        using var fixture = new Fixture(); await fixture.InitializeAsync(token);
        var a = await fixture.Spaces.CreateAsync("Open original A", cancellationToken: token);
        var b = await fixture.Spaces.CreateAsync("Open replacement B", cancellationToken: token);
        var now = DateTimeOffset.UtcNow;
        var first = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Original selected chat", null, null,
            false, false, now, now, SpaceId: a.Id);
        var second = first with { Id = Guid.NewGuid(), Title = "Newer selected chat" };
        await fixture.SeedInSpaceAsync(first, a, token); await fixture.SeedInSpaceAsync(second, a, token);
        var hostCurrent = true;
        var workspace = await OwnedSpacesWorkspace.OpenAsync(fixture.Settings, fixture.Actors, fixture.Receipts,
            fixture.Database, new ConversationLocalStoreAuthority(fixture.Actors, fixture.Receipts), fixture.Repository,
            () => hostCurrent, token);
        var proxy = DispatchProxy.Create<IConversationRepository, HeldConversation>();
        var held = (HeldConversation)(object)proxy; held.Actual = fixture.Repository;
        var navigated = new List<Conversation>();
        using var page = new NativeSpacesPage(workspace.Registry, null, null, conversations: proxy,
            openConversation: conversation => { navigated.Add(conversation); return Task.CompletedTask; },
            requireCurrentAccess: workspace.RequireCurrentAccessAsync);
        var window = new Window { Width = 1100, Height = 800, Content = page }; Task? original = null;
        try
        {
            window.Show(); await page.RefreshNowAsync(token); Press(page, a.Name); await page.PendingSelection.WaitAsync(token);
            var before = SnapshotPhysical(fixture.DataDirectory);
            held.HoldNext = true; Press(page, first.Title); original = page.PendingConversationOpen;
            await held.Entered.Task.WaitAsync(token);
            if (retirement == "space-aba")
            { Press(page, b.Name); await page.PendingSelection.WaitAsync(token); Press(page, a.Name); await page.PendingSelection.WaitAsync(token); }
            else if (retirement == "newer-click")
            { Press(page, second.Title); await page.PendingConversationOpen.WaitAsync(token); Assert.Equal(second, Assert.Single(navigated)); }
            else if (retirement == "deactivate") page.Deactivate();
            else { hostCurrent = false; page.Dispose(); }
            held.Release.TrySetResult(); await original.WaitAsync(token);
            Assert.Equal(retirement == "newer-click" ? 1 : 0, navigated.Count);
            if (retirement != "dispose")
            {
                if (retirement == "deactivate")
                {
                    Press(page, first.Title); await page.PendingConversationOpen.WaitAsync(token); Assert.Empty(navigated);
                    await page.ActivateAsync(token);
                }
                Press(page, first.Title); await page.PendingConversationOpen.WaitAsync(token);
                Assert.Equal(first, navigated[^1]);
            }
            Assert.Equal(first, await fixture.Repository.GetAsync(first.Id, token));
            Assert.Equal(second, await fixture.Repository.GetAsync(second.Id, token));
            Assert.Equal(before.OrderBy(x => x.Key), SnapshotPhysical(fixture.DataDirectory).OrderBy(x => x.Key));
        }
        finally
        {
            held.Release.TrySetResult();
            try { if (original is not null) try { await original; } catch { } try { await page.PendingConversationOpen; } catch { } }
            finally { hostCurrent = false; window.Close(); }
        }
    }

    private static Dictionary<string, string> SnapshotPhysical(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        // SQLite's shared-memory coordination file is an incidental reader lock, not canonical content.
        .Where(path => !path.EndsWith("-shm", StringComparison.Ordinal))
        .ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);

    public class HeldConversation : DispatchProxy
    {
        public IConversationRepository Actual { get; set; } = null!;
        public bool HoldNext { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(method);
            if (method.Name == nameof(IConversationRepository.GetAsync) && HoldNext)
            { HoldNext = false; return ReadAsync((Guid)args![0]!, (CancellationToken)args[1]!); }
            return method.Invoke(Actual, args);
        }
        private async Task<Conversation?> ReadAsync(Guid id, CancellationToken token)
        {
            var actual = await Actual.GetAsync(id, token);
            Entered.TrySetResult(); await Release.Task; return actual;
        }
    }

    private static void Press(NativeSpacesPage page, string title)
    {
        var button = page.Scene.Root!.DescendantsAndSelf().OfType<Haven.UI.Components.Button>().Single(button => button.Content == title);
        Assert.True(button.KeyDown(new(HavenKey.Enter, HavenKeyModifiers.None)));
        Assert.True(button.KeyUp(new(HavenKey.Enter, HavenKeyModifiers.None)));
    }
    public class HeldRead : DispatchProxy
    {
        public IConversationRepository Actual { get; set; } = null!;
        public CancellationToken Token { get; set; }
        public bool HoldNext { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(method);
            if (method.Name == nameof(IConversationRepository.GetBySpaceAsync) && HoldNext)
            {
                HoldNext = false; return ReadAsync((Guid)args![0]!, (int)args[1]!, (CancellationToken)args[2]!);
            }
            return method.Invoke(Actual, args);
        }
        private async Task<IReadOnlyList<Conversation>> ReadAsync(Guid id, int limit, CancellationToken token)
        {
            var actual = await Actual.GetBySpaceAsync(id, limit, token);
            Entered.TrySetResult(); await Release.Task.WaitAsync(Token); return actual;
        }
    }
    private sealed class Fixture : IAppPaths, IDisposable
    {
        public Fixture()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "astra-space-chat-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            Settings = new(this); Database = new(this); Repository = new(Database);
            Home = new(Path.Combine(DataDirectory, "home.json")); Actors = new(Home, new OperatingSystemPrincipalSource());
            Ownership = new(Home, Actors, new HomeLocalStoreEvidenceRegistry([
                new SpacesLocalStoreEvidenceProvider(Settings, Settings), new ConversationLocalStoreEvidenceProvider(Database)]),
                new HomePermissionTrustService(Home, (_, _) => null));
            Receipts = new(Ownership, Actors);
            var authority = new SpaceLocalStoreAuthority(Settings, Actors, Receipts, () => true);
            Spaces = new(Settings, token => authority.CaptureWriteAdmissionForActorAsync(Actor, token));
        }
        public async Task InitializeAsync(CancellationToken token, bool bindConversations = true)
        {
            await new ConversationProductionDatabase(Database).InitializeAsync(token);
            Actor = (await Actors.GetCurrentAsync(token))!;
            var settings = await Settings.GetStoreIdentityAsync(token);
            var sql = await Database.GetStoreIdentityAsync(token);
            await Ownership.BindNewEmptyAsync(Actor, "spaces", settings.StoreId.ToString("D"), token);
            if (bindConversations) await Ownership.BindNewEmptyAsync(Actor, "conversation", sql.StoreId.ToString("D"), token);
        }
        public async Task SeedInSpaceAsync(Conversation value, SpaceDefinition space, CancellationToken token)
        {
            var initial = value with { IsArchived = false, IsTemporary = false };
            var acknowledged = await ((ISpaceConversationWriter)Owner(Repository)).CreateAsync(initial, space, token);
            Assert.Equal(initial, acknowledged);
            // Subsequent ordinary content updates retain the existing canonical membership.
            if (initial != value) await Repository.UpsertConversationAsync(value, token);
        }
        public OwnedSpaceConversations Owner(IConversationSpaceCommitStore repository) =>
            new(Actor, Settings, Database, Receipts, new(Actors, Receipts), repository, () => true);
        public VersionedAtomicSettingsStore Settings { get; }
        public SqliteDatabase Database { get; }
        public ConversationRepository Repository { get; }
        public SpaceRegistry Spaces { get; }
        public FileHomeCoreStateStore Home { get; }
        public HomeLocalProfileIdentity Actors { get; }
        public AuthenticatedResourceActor Actor { get; private set; } = null!;
        public HomeLocalStoreOwnership Ownership { get; }
        public HomeResourceStoreOwnershipAuthority Receipts { get; }
        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "conversation.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(DataDirectory, true); }
    }
}
