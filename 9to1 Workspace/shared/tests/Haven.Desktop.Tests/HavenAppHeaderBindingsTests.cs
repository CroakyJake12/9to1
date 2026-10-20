#if !ANDROID
using Haven.Application.Haven;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class HavenAppHeaderBindingsTests
{
    private sealed class FakeHostContext(bool embedded, bool model = true, bool notifications = true, bool profile = true)
        : IHavenHostContext
    {
        public bool IsEmbedded => embedded;
        public bool ModelSupported => model;
        public bool NotificationsSupported => notifications;
        public bool ProfileSupported => profile;
        public IHavenSharedControlGateway? SharedControls => null;
    }

    private sealed record Probe
    {
        public List<string> Opened { get; } = [];
        public List<string> Searched { get; } = [];
        public Task<HavenSharedOpenResult> OpenAsync(string kind, CancellationToken token) { Opened.Add(kind); return Task.FromResult(HavenSharedOpenResult.Succeeded()); }
        public Task SearchAsync(string query, CancellationToken token) { Searched.Add(query); return Task.CompletedTask; }
    }

    [Fact]
    public void Embedded_header_exposes_search_only()
    {
        var probe = new Probe();
        var bindings = new HavenAppHeaderBindings(new FakeHostContext(embedded: true), probe.OpenAsync, probe.SearchAsync);
        Assert.True(bindings.IsEmbedded);
        Assert.True(bindings.ShowSearch);
        Assert.False(bindings.ShowModel);
        Assert.False(bindings.ShowNotifications);
        Assert.False(bindings.ShowProfile);
        Assert.True(bindings.TryGetValue("ShowModel", out var model) && (bool)model! == false);
    }

    [Fact]
    public void Standalone_header_exposes_all_four_through_real_gateway()
    {
        var probe = new Probe();
        var bindings = new HavenAppHeaderBindings(new FakeHostContext(embedded: false), probe.OpenAsync, probe.SearchAsync);
        Assert.False(bindings.IsEmbedded);
        Assert.True(bindings.ShowSearch);
        Assert.True(bindings.ShowModel);
        Assert.True(bindings.ShowNotifications);
        Assert.True(bindings.ShowProfile);
    }

    [Fact]
    public void Standalone_header_respects_actual_support_flags()
    {
        var probe = new Probe();
        var bindings = new HavenAppHeaderBindings(new FakeHostContext(embedded: false, model: false, notifications: true, profile: false),
            probe.OpenAsync, probe.SearchAsync);
        Assert.False(bindings.ShowModel);
        Assert.True(bindings.ShowNotifications);
        Assert.False(bindings.ShowProfile);
    }

    [Fact]
    public async Task Dispatch_routes_shared_controls_to_the_same_gateway()
    {
        var probe = new Probe();
        var bindings = new HavenAppHeaderBindings(new FakeHostContext(embedded: false), probe.OpenAsync, probe.SearchAsync);
        await bindings.DispatchAsync("9to1.Haven.OpenModel", null);
        await bindings.DispatchAsync("9to1.Haven.OpenNotifications", null);
        await bindings.DispatchAsync("9to1.Haven.OpenProfile", null);
        Assert.Equal(["9to1.Haven.OpenModel", "9to1.Haven.OpenNotifications", "9to1.Haven.OpenProfile"], probe.Opened);
    }

    [Fact]
    public async Task Dispatch_routes_search_to_the_app_owned_search()
    {
        var probe = new Probe();
        var bindings = new HavenAppHeaderBindings(new FakeHostContext(embedded: true), probe.OpenAsync, probe.SearchAsync);
        bindings.SearchText = "geometry dash";
        await bindings.DispatchAsync("9to1.Haven.Search", null);
        Assert.Equal(["geometry dash"], probe.Searched);
    }
}
#endif
