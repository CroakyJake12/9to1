using Haven.Application;
using Haven.Desktop.Services;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class OriginalNativeColdProjectCompositionTests
{
    [Fact]
    public Task Current_project_execution_uses_one_private_bridge_consent_and_all_aliases_without_starting_a_review()
    {
        FilesDeveloperOriginalCurrentProjectExecutionBridge? capturedBridge = null;
        var resolver = new FilesDeveloperOriginalCurrentProjectExecutionResolver(() => capturedBridge
            ?? throw new InvalidOperationException("The actual bridge is not captured before domain use."));
        var policy = new HomeDeveloperWorkspaceExecutionActionPolicySource();
        return WithGraph("1", async (services, registration, paths, captureProject) =>
        {
            services.AddHavenOwnedNativeColdProjectResources(registration.OriginalHome);
            var dev = services.Single(row => row.ServiceType == typeof(DeveloperTaskWorkspaceService));
            services.AddHavenOwnedCurrentProjectExecution(registration.OriginalHome, resolver, policy);
            Assert.Same(dev, services.Single(row => row.ServiceType == typeof(DeveloperTaskWorkspaceService)));
            Assert.Throws<InvalidOperationException>(() => services.AddHavenOwnedCurrentProjectExecution(registration.OriginalHome, resolver, policy));
            ServiceProvider? provider = null; HomeDeveloperWorkspaceExecutionConsentSource? consent = null;
            Exception? primary = null; var errors = new List<Exception>();
            try
            {
                provider = services.BuildServiceProvider();
                var source = provider.GetRequiredService<HomeColdProjectReadReconciliation>(); captureProject(source);
                capturedBridge = provider.GetRequiredService<FilesDeveloperOriginalCurrentProjectExecutionBridge>();
                consent = provider.GetRequiredService<HomeDeveloperWorkspaceExecutionConsentSource>();
                Assert.Same(source, provider.GetRequiredService<IDeveloperOriginalProjectCommandReadSource>());
                Assert.Same(capturedBridge, provider.GetRequiredService<IDeveloperWorkspaceTrustService>());
                Assert.Same(capturedBridge, provider.GetRequiredService<IDeveloperWorkspaceOriginalProjectExecutionTrustService>());
                Assert.Same(capturedBridge, provider.GetRequiredService<IDeveloperWorkspaceOriginalProjectExecutionBindingSource>());
                Assert.Same(capturedBridge, provider.GetRequiredService<IDeveloperWorkspaceOriginalExecutionBindingSource>());
                Assert.Same(capturedBridge, provider.GetRequiredService<IDeveloperWorkspaceOriginalExecutionScopedBindingSource>());
                Assert.Same(capturedBridge, provider.GetRequiredService<IDeveloperWorkspaceOriginalExecutionCommitBindingSource>());
                Assert.Same(capturedBridge, provider.GetRequiredService<IDeveloperWorkspaceOriginalExecutionPinCustodySource>());
                Assert.Same(consent, provider.GetRequiredService<IWorkspaceOriginalProcessStartConsentSource>());
                Assert.Same(consent, provider.GetRequiredService<Func<IWorkspaceOriginalProcessStartConsentSource>>()());
                Assert.Same(resolver, provider.GetRequiredService<FilesDeveloperOriginalCurrentProjectExecutionResolver>());
                Assert.Same(policy, provider.GetRequiredService<HomeDeveloperWorkspaceExecutionActionPolicySource>());
                Assert.True(capturedBridge.IsBoundToOriginalToolOwner(provider.GetRequiredService<ITaskRunToolActionOwner>()));
                Assert.False(await capturedBridge.IsTrustedAsync(Guid.NewGuid(), CancellationToken.None));
                Assert.False(File.Exists(paths.DatabasePath));
                Assert.False(File.Exists(Path.Combine(paths.DataDirectory, "Home", "state.json")));
            }
            catch (Exception cause) { primary = cause; }
            // Independently close the empty borrowers before provider/global Home cleanup.
            foreach (var acquire in new Func<Task>[]
            {
                () => consent?.CloseAndDrainOriginalExecutionsAsync() ?? Task.CompletedTask,
                () => capturedBridge?.CloseAndDrainOriginalExecutionAsync() ?? Task.CompletedTask
            })
            {
                Task? actual = null;
                try { actual = acquire(); } catch (Exception cause) { errors.Add(cause); }
                if (actual is not null) try { await actual; } catch (Exception cause) { errors.Add((Exception?)actual.Exception ?? cause); }
            }
            Task? providerClose = null;
            try { if (provider is not null) providerClose = provider.DisposeAsync().AsTask(); } catch (Exception cause) { errors.Add(cause); }
            if (providerClose is not null) try { await providerClose; } catch (Exception cause) { errors.Add((Exception?)providerClose.Exception ?? cause); }
            Throw(primary, errors);
        }, [resolver], [policy]);
    }

    [Fact]
    public Task Missing_current_project_source_refuses_execution_registration_before_any_issuer_alias()
        => WithGraph("1", (services, registration, paths, _) =>
        {
            var resolver = new FilesDeveloperOriginalCurrentProjectExecutionResolver(() => throw new InvalidOperationException("No source must be resolved by registration."));
            var policy = new HomeDeveloperWorkspaceExecutionActionPolicySource();
            Assert.Throws<InvalidOperationException>(() => services.AddHavenOwnedCurrentProjectExecution(registration.OriginalHome, resolver, policy));
            Assert.DoesNotContain(services, row => row.ServiceType == typeof(FilesDeveloperOriginalCurrentProjectExecutionBridge));
            Assert.DoesNotContain(services, row => row.ServiceType == typeof(HomeDeveloperWorkspaceExecutionConsentSource));
            Assert.False(File.Exists(paths.DatabasePath)); return Task.CompletedTask;
        });
}
