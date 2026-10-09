using System.Reflection;
using Haven.Desktop.Services;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class NativeDevelopmentStartupCallTests
{
    [Fact]
    public async Task Missing_original_Home_graph_keeps_ordinary_descriptors_and_does_not_construct_native_owners()
    {
        var app = new App();
        var services = new ServiceCollection();
        services.AddSingleton(new object());
        var original = services.ToArray();
        try
        {
            Configure(app, services);
            Assert.Equal(original, services.ToArray());
            Assert.DoesNotContain(services, item => item.ServiceType == typeof(FilesNativeBrowserService));
            Assert.DoesNotContain(services, item => item.ServiceType == typeof(DeveloperTaskWorkspaceService));
            Assert.DoesNotContain(services, item => item.ServiceType == typeof(DeveloperProjectWorkbenchPageFactory));
        }
        finally { await Close(app); }
    }

    [Fact]
    public async Task Partial_original_Home_descriptors_are_unavailable_without_factory_activation_or_parallel_owner()
    {
        var app = new App();
        var services = new ServiceCollection();
        var activated = 0;
        // A descriptor is no Home authority. This negative never invokes its factory.
        services.AddSingleton<IHomeCoreStateStore>(_ =>
        {
            activated++;
            throw new InvalidOperationException("The missing original Home tuple must not be resolved.");
        });
        var original = services.ToArray();
        try
        {
            Configure(app, services);
            Assert.Equal(0, activated);
            Assert.Equal(original, services.ToArray());
            Assert.DoesNotContain(services, item => item.ServiceType == typeof(FilesNativeBrowserService));
            Assert.DoesNotContain(services, item => item.ServiceType == typeof(DeveloperTaskWorkspaceService));
            Assert.DoesNotContain(services, item => item.ServiceType == typeof(DeveloperProjectWorkbenchPageFactory));
        }
        finally { await Close(app); }
    }

    private static void Configure(App app, IServiceCollection services) =>
        typeof(App).GetMethod("ConfigureOriginalNativeDevelopmentRegistrations", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [services]);

    private static Task Close(App app) => (Task)typeof(App)
        .GetMethod("JoinOriginalAppProducersAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null)!;
}
