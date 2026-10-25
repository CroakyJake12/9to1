using System.Net;
using System.Text;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.Infrastructure;

namespace Haven.Desktop.Tests;

/// <summary>Exercises the actual settings commands with an in-memory HTTP peer, never a model or network.</summary>
public sealed class StrataLoopbackProviderSettingsTests
{
    [Fact]
    public async Task RefreshOffersTheTypedLocalConnectionWithoutReadingItsSecretsOrGenerating()
    {
        await using var fixture = new Fixture();
        await fixture.ViewModel.RefreshCommand.ExecuteAsync();
        Assert.Contains(fixture.ViewModel.Items, item => item.Id == "strata-local");
        Assert.DoesNotContain("strata-local", fixture.Secrets.Reads);
        Assert.Empty(fixture.Methods);
    }

    [Fact]
    public async Task KeylessConnectIsPassiveAndOnlyExplicitTestStartsTheCapabilityProbe()
    {
        await using var fixture = new Fixture();
        var item = fixture.Item("strata-local");
        await fixture.ViewModel.ConnectCommand.ExecuteAsync(item);
        var saved = Assert.Single(fixture.Configurations.Saved);
        Assert.True(saved.IsLocal);
        Assert.False(saved.AllowCloudFallback);
        Assert.True(saved.IsEnabled);
        Assert.True(item.IsConnected);
        Assert.DoesNotContain("strata-local", fixture.Secrets.Reads);
        Assert.DoesNotContain("strata-local", fixture.Secrets.Writes);
        Assert.DoesNotContain("POST", fixture.Methods);

        await fixture.ViewModel.TestCommand.ExecuteAsync(item);
        Assert.Single(fixture.Methods, method => method == "POST");
        // The fixture intentionally returns no tool call. A successful connection must not
        // turn that failed explicit capability probe into a claim that tools are supported.
        Assert.False(item.IsHealthy);
    }

    [Fact]
    public async Task UnsafeLocalEndpointFailsBeforeSavingConfigurationOrSendingHttp()
    {
        await using var fixture = new Fixture();
        var item = fixture.Item("strata-local");
        item.Endpoint = "https://remote.example/v1/";
        await fixture.ViewModel.ConnectCommand.ExecuteAsync(item);
        Assert.Empty(fixture.Configurations.Saved);
        Assert.Empty(fixture.Methods);
        Assert.DoesNotContain("strata-local", fixture.Secrets.Reads);
        Assert.DoesNotContain("strata-local", fixture.Secrets.Writes);
        Assert.False(item.IsConnected);
    }

    [Fact]
    public async Task ExistingRemoteProviderStillRequiresAndStoresAKeyAndRemainsRemote()
    {
        await using var fixture = new Fixture();
        var item = fixture.Item("openai");
        await fixture.ViewModel.ConnectCommand.ExecuteAsync(item);
        Assert.Empty(fixture.Configurations.Saved);
        Assert.Empty(fixture.Methods);
        Assert.False(item.IsConnected);
        item.ApiKey = "fixture-key";
        await fixture.ViewModel.ConnectCommand.ExecuteAsync(item);
        var saved = Assert.Single(fixture.Configurations.Saved);
        Assert.False(saved.IsLocal);
        Assert.Contains("openai", fixture.Secrets.Writes);
        Assert.Equal("", item.ApiKey);
        Assert.True(item.IsConnected);
    }

    private sealed class Fixture : IHttpClientFactory, IAsyncDisposable
    {
        private readonly Handler _handler;
        public ConfigurationStore Configurations { get; } = new();
        public SecretStore Secrets { get; } = new();
        public List<string> Methods { get; } = [];
        public ProviderConnectionsViewModel ViewModel { get; }
        public Fixture()
        {
            _handler = new Handler(this);
            IModelProvider[] providers = [new StrataLoopbackModelProvider(this, Configurations, Secrets, new ProviderUsageCaptureBuffer()),
                new OpenAiModelProvider(this, Configurations, Secrets, new ProviderUsageCaptureBuffer())];
            ViewModel = new(new ModelProviderRegistry(providers), Configurations, Secrets, new EmptyCalendarRegistry(), new EmptyPlanner());
        }
        public ProviderConnectionItemViewModel Item(string id) => Assert.Single(ViewModel.Items, item => item.Id == id);
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
        public async ValueTask DisposeAsync()
        {
            await ViewModel.RefreshCommand.DisposeAsync();
            await ViewModel.ConnectCommand.DisposeAsync();
            await ViewModel.TestCommand.DisposeAsync();
            await ViewModel.DisconnectCommand.DisposeAsync();
            _handler.Dispose();
        }
        private sealed class Handler(Fixture fixture) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                fixture.Methods.Add(request.Method.Method);
                var json = request.Method == HttpMethod.Post ? "{\"choices\":[]}" : request.RequestUri?.AbsolutePath == "/v1/status"
                    ? "{\"service\":\"strata\",\"started\":1792872000,\"model\":\"actual-model\",\"loaded\":true,\"activity\":{\"in_flight\":0}}"
                    : "{\"data\":[{\"id\":\"actual-model\",\"meta\":{\"n_ctx\":32768}}]}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(json, Encoding.UTF8, "application/json") });
            }
        }
    }

    private sealed class ConfigurationStore : IProviderConfigurationStore
    {
        private readonly Dictionary<string, ProviderConfiguration> _items = new()
        {
            ["strata-local"] = new("strata-local", ModelProviderKind.OpenAICompatible, "Strata local", "http://127.0.0.1:8081/v1/", false, true, false, new Dictionary<string, string>(), DateTimeOffset.UtcNow),
            ["openai"] = new("openai", ModelProviderKind.OpenAI, "OpenAI", "https://provider.example/v1/", false, false, false, new Dictionary<string, string>(), DateTimeOffset.UtcNow)
        };
        public List<ProviderConfiguration> Saved { get; } = [];
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ProviderConfiguration>>(_items.Values.ToArray());
        public Task<ProviderConfiguration?> GetAsync(string id, CancellationToken token) => Task.FromResult<ProviderConfiguration?>(_items.GetValueOrDefault(id));
        public Task UpsertAsync(ProviderConfiguration configuration, CancellationToken token)
        { _items[configuration.Id] = configuration; Saved.Add(configuration); return Task.CompletedTask; }
        public Task DeleteAsync(string id, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class SecretStore : IProviderSecretStore
    {
        private readonly Dictionary<string, string> _values = [];
        public List<string> Reads { get; } = [];
        public List<string> Writes { get; } = [];
        public Task<string?> GetAsync(string providerId, string name, CancellationToken token)
        { Reads.Add(providerId); return Task.FromResult<string?>(_values.GetValueOrDefault(providerId)); }
        public Task SetAsync(string providerId, string name, string value, CancellationToken token)
        { Writes.Add(providerId); _values[providerId] = value; return Task.CompletedTask; }
        public Task DeleteAsync(string providerId, string name, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class EmptyCalendarRegistry : ICalendarSyncProviderRegistry
    {
        public IReadOnlyList<ICalendarSyncProvider> Providers => [];
        public ICalendarSyncProvider Get(CalendarProviderKind kind) => throw new NotSupportedException();
    }

    // Settings refresh may observe calendar accounts; any unrelated planner effect fails immediately.
    private sealed class EmptyPlanner : IPlannerRepository
    {
        public Task EnsureDefaultsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlannerCollection>> GetCollectionsAsync(bool includeArchived, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertCollectionAsync(PlannerCollection collection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ArchiveCollectionAsync(Guid id, bool archived, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PlannerTask?> GetTaskAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlannerTask>> GetTasksAsync(PlannerTaskQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertTaskAsync(PlannerTask task, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteTaskAsync(Guid id, DateTimeOffset completedAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteTaskAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlannerTaskCompletion>> GetCompletionHistoryAsync(Guid taskId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlannerCalendar>> GetCalendarsAsync(bool visibleOnly, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertCalendarAsync(PlannerCalendar calendar, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlannerEvent>> GetEventsAsync(DateTimeOffset rangeStart, DateTimeOffset rangeEnd, Guid? calendarId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PlannerEvent?> GetEventAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertEventAsync(PlannerEvent plannerEvent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteEventAsync(Guid id, DateTimeOffset deletedAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CalendarAccount>> GetCalendarAccountsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CalendarAccount>>([]);
        public Task UpsertCalendarAccountAsync(CalendarAccount account, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CalendarConflict>> GetUnresolvedConflictsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ResolveConflictAsync(Guid id, CalendarConflictResolution resolution, DateTimeOffset resolvedAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlannerReminder>> GetDueRemindersAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkReminderDeliveredAsync(PlannerReminder reminder, DateTimeOffset deliveredAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ApplyProposalAsync(PlannerChangeProposal proposal, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
