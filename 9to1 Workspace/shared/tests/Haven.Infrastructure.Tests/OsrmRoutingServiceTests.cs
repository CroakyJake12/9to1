using System.Net;
using System.Text.Json;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class OsrmRoutingServiceTests
{
    [Fact]
    public async Task Public_service_uses_valid_full_overview_and_does_not_mislabel_driving_as_walking()
    {
        var handler = new Handler(Reply([[0, 0], [1, 1]]));
        var service = new OsrmRoutingService(new Factory(handler));
        Assert.Null(await service.GetRouteAsync(new(0, 0), new(1, 1), MapTravelProfile.Walking, default));
        Assert.Null(await service.GetRouteAsync(new(0, 0), new(1, 1), MapTravelProfile.Cycling, default));
        Assert.Equal(0, handler.Calls);
        var route = await service.GetRouteAsync(new(0, 0), new(1, 1), MapTravelProfile.Driving, default);
        Assert.NotNull(route);
        Assert.Equal(new GeoPoint(1, 1), route.Points[^1]);
        Assert.Contains("overview=full", handler.LastUri!.Query);
        Assert.Contains("/route/v1/driving/", handler.LastUri.AbsolutePath);
        Assert.Single(service.SupportedProfiles);
    }

    [Fact]
    public async Task Explicit_separate_provider_support_is_required_and_keeps_its_profile()
    {
        var handler = new Handler(Reply([[0, 0], [1, 1]]));
        var service = new OsrmRoutingService(new Factory(handler), new(new Uri("https://walking.example.test"), [MapTravelProfile.Walking]));
        Assert.Null(await service.GetRouteAsync(new(0, 0), new(1, 1), MapTravelProfile.Driving, default));
        var route = await service.GetRouteAsync(new(0, 0), new(1, 1), MapTravelProfile.Walking, default);
        Assert.NotNull(route);
        Assert.Contains("/route/v1/walking/", handler.LastUri!.AbsolutePath);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Too_many_geometry_points_and_invalid_coordinates_are_rejected_instead_of_truncated()
    {
        var points = Enumerable.Range(0, 2001).Select(index => new double[] { index / 10000d, 0 }).ToArray();
        var handler = new Handler(Reply(points));
        var service = new OsrmRoutingService(new Factory(handler));
        Assert.Null(await service.GetRouteAsync(new(0, 0), new(1, 1), MapTravelProfile.Driving, default));
        handler.Body = Reply([[0, 0], [1, 91]]);
        Assert.Null(await service.GetRouteAsync(new(0, 0), new(1, 1), MapTravelProfile.Driving, default));
        var calls = handler.Calls;
        Assert.Null(await service.GetRouteAsync(new(double.NaN, 0), new(1, 1), MapTravelProfile.Driving, default));
        Assert.Equal(calls, handler.Calls);
    }

    [Fact]
    public async Task Oversized_payload_and_unsafe_endpoint_configuration_fail_closed()
    {
        var handler = new Handler(new string(' ', 2 * 1024 * 1024 + 1));
        var service = new OsrmRoutingService(new Factory(handler));
        Assert.Null(await service.GetRouteAsync(new(0, 0), new(1, 1), MapTravelProfile.Driving, default));
        Assert.Throws<ArgumentException>(() => new OsrmRoutingService(new Factory(handler),
            new(new Uri("https://user:secret@routing.example.test"), [MapTravelProfile.Driving])));
    }

    private static string Reply(double[][] points) => JsonSerializer.Serialize(new
    { code = "Ok", routes = new[] { new { distance = 1500d, duration = 120d, geometry = new { coordinates = points } } } });
    private sealed class Handler(string body) : HttpMessageHandler
    {
        public string Body { get; set; } = body;
        public int Calls { get; private set; }
        public Uri? LastUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body) });
        }
    }
    private sealed class Factory(Handler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, disposeHandler: false); }
}
