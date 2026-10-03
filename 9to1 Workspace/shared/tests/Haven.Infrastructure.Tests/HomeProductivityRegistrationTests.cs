using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class HomeProductivityRegistrationTests
{
    [Fact]
    public void Native_composition_uses_one_shared_engine_and_registered_service()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-productivity-di-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var registrations = new ServiceCollection().AddHavenInfrastructure();
        registrations.AddSingleton<IAppPaths>(new Paths(root));
        registrations.AddSingleton<IHomeCoreStateStore>(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
        using var services = registrations.BuildServiceProvider();
        var engine = services.GetRequiredService<HomeProductivityEngine>();
        Assert.Same(engine, services.GetRequiredService<IHomeProductivityEngine>());
        var service = Assert.Single(services.GetServices<IHomeCoreService>(), value => value.Descriptor.ServiceId == "productivity.engine");
        Assert.Same(engine, Assert.IsType<HomeProductivityEngineService>(service).Engine);
        Assert.Contains("permissions.trust", service.Dependencies);
        Assert.True(engine.GetCompatibility("write", "1", ["text.paragraph"]).Compatible);
        Assert.False(engine.GetCompatibility("canvas", "1", ["drawing.ink"]).Compatible);
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "data.db");
        public string BrowserProfileDirectory => root;
        public string AttachmentsDirectory => root;
        public string LogsDirectory => root;
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
