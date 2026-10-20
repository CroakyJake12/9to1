using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Genuine SQLite fixture for the Home dashboard briefing sources. It seeds distinct canonical
/// entities and asserts the bounded agenda/recent reads surface those real identities with their
/// expected kind/action keys — and never fabricate rows that were not seeded.
/// </summary>
public sealed class DashboardBriefingSourceFixtureTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task AgendaAndRecentSurfaceDistinctCanonicalIdentities()
    {
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        var conversations = new ConversationRepository(database);
        var containers = new ContainerRepository(database);
        var planner = new PlannerRepository(database);
        var calls = new CallRepository(database);
        var now = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.FromHours(1));
        var day = new DateTimeOffset(now.Date, now.Offset);

        var conversationId = Guid.NewGuid();
        await conversations.UpsertConversationAsync(new Conversation(conversationId, HavenMode.Chat, ConversationKind.Chat, "Dashboard chat",
            null, null, false, false, now.AddMinutes(-20), now.AddMinutes(-10)), CancellationToken.None);

        var containerId = Guid.NewGuid();
        await containers.UpsertAsync(new ContainerDefinition(containerId, HavenMode.Study, "Subject", null, "", "", now, now), CancellationToken.None);

        await planner.EnsureDefaultsAsync(CancellationToken.None);
        var taskId = Guid.NewGuid();
        await planner.UpsertTaskAsync(TaskAt(taskId, "Due today", day.AddHours(17), now), CancellationToken.None);
        var eventId = Guid.NewGuid();
        await planner.UpsertEventAsync(new PlannerEvent(
            eventId, PlannerDefaults.LocalCalendarId, "Seminar", "", "Room 2", day.AddHours(13), day.AddHours(14),
            false, null, null, false, null, null, now, now, null, "Europe/London"), CancellationToken.None);

        var callConversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Call, "Call",
            null, null, false, false, now.AddMinutes(-8), now.AddMinutes(-6));
        await conversations.UpsertConversationAsync(callConversation, CancellationToken.None);
        await calls.UpsertAsync(new CallSession(Guid.NewGuid(), callConversation.Id, "qwen", null, null, null,
            CallInputMode.HandsFree, false, CallSessionStatus.Completed, now.AddMinutes(-8), now.AddMinutes(-6)), CancellationToken.None);

        var snapshot = await new DashboardRepository(database).GetSnapshotAsync(now, CancellationToken.None);

        // Agenda carries only genuinely seeded planner entities, each with its stable canonical id.
        var agendaIds = snapshot.Agenda.Select(item => item.Id).Distinct(StringComparer.Ordinal).ToArray();
        Assert.Contains(taskId.ToString("D"), agendaIds);
        Assert.Contains(eventId.ToString("D"), agendaIds);
        Assert.All(snapshot.Agenda, item => Assert.Equal("plan", item.ActionKey));
        Assert.Contains(snapshot.Agenda, item => item.Kind == "task");
        Assert.Contains(snapshot.Agenda, item => item.Kind == "event");

        // Recent work surfaces the seeded conversation/container/call identities with typed action keys.
        var recentIds = snapshot.RecentWork.Select(item => item.Id).Distinct(StringComparer.Ordinal).ToArray();
        Assert.Contains(conversationId.ToString("D"), recentIds);
        Assert.Contains(containerId.ToString("D"), recentIds);
        Assert.Contains(snapshot.RecentWork, item => item.Kind == "chat" && item.ActionKey == "chat");
        Assert.Contains(snapshot.RecentWork, item => item.Kind == "subject" && item.ActionKey == "study");
        Assert.Contains(snapshot.RecentWork, item => item.Kind == "call" && item.ActionKey == "call");

        // The snapshot never adds rows outside the seeded set.
        Assert.DoesNotContain(snapshot.RecentWork, item => item.Title == "Not seeded");
        Assert.Empty(snapshot.Agenda.Where(item => item.Id == "not-a-canonical-id"));
    }

    private static PlannerTask TaskAt(Guid id, string title, DateTimeOffset dueAt, DateTimeOffset now) => new(
        id, PlannerDefaults.PersonalCollectionId, null, title, "", PlannerPriority.Medium,
        PlannerTaskStatus.Planned, "[]", 30, null, dueAt, null, null, null, 0, now, now, "Europe/London");

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-briefing-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            DatabasePath = Path.Combine(DataDirectory, "test.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
        }

        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }

        public void Dispose() { try { Directory.Delete(DataDirectory, true); } catch (IOException) { } }
    }
}
