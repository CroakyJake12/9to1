using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Infrastructure.Tests;

// Configuration components only. The controlled capture/outcome source refuses every
// issuance and productive operation; it is not a Files producer or approval witness.
public sealed class LocalDomainDeveloperCompositionTests
{
    [Fact]
    public async Task Local_DOMAIN_uses_same_real_Home_owners_and_lazy_Files_sources_without_Windows_tuple_or_IO()
    {
        if (!OperatingSystem.IsLinux()) return;
        var directory = Directory.CreateTempSubdirectory("local-home-developer-").FullName;
        HomeLocalDomainComposition? home = null; ServiceProvider? provider = null;
        HomeDeveloperProjectReadAdmissionSource? read = null; HomeDeveloperProjectSetupPermissionSource? setup = null;
        HomeDeveloperProjectSetupJournal? journal = null; Exception? primary = null; var errors = new List<Exception>();
        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var path = Path.Combine(directory, "state.json"); var store = new FileHomeCoreStateStore(path);
            var readPolicy = new HomeDeveloperProjectReadActionPolicySource(); var setupPolicy = new HomeDeveloperProjectSetupActionPolicySource();
            var resolver = new HomeDeveloperProjectReadResourceResolver(() => read ?? throw new InvalidOperationException("Original READ unresolved"));
            home = new(store, new OperatingSystemPrincipalSource(), originalResourceResolvers: [resolver], originalActionPolicies: [readPolicy, setupPolicy]);
            var services = new ServiceCollection(); AddDomain(services, home);
            var denied = new UnavailableCaptureAndOutcome(); var selections = 0; var scopes = 0;
            services.AddHavenOwnedDeveloperSourceReads(home, _ => { selections++; throw new NotSupportedException("No Files source is issued by this component control"); }, resolver, readPolicy);
            services.AddHavenOwnedDeveloperSetups(home, _ => { scopes++; throw new NotSupportedException("No Files destination is issued by this component control"); }, _ => denied, _ => denied, setupPolicy);
            provider = services.BuildServiceProvider();
            read = provider.GetRequiredService<HomeDeveloperProjectReadAdmissionSource>();
            setup = provider.GetRequiredService<HomeDeveloperProjectSetupPermissionSource>();
            journal = provider.GetRequiredService<HomeDeveloperProjectSetupJournal>();
            Assert.Same(read, provider.GetRequiredService<IDeveloperProjectOriginalReadAdmissionSource>());
            Assert.Same(read, provider.GetRequiredService<IDeveloperProjectOriginalReadRetirementSource>());
            Assert.Same(read, provider.GetRequiredService<IDeveloperProjectOriginalReadAdmissionJoinGuard>());
            Assert.Same(setup, provider.GetRequiredService<IDeveloperProjectOriginalSetupPermissionSource>());
            Assert.Same(journal, provider.GetRequiredService<IDeveloperProjectOriginalSetupCompletionSource>());
            Assert.Same(resolver, provider.GetRequiredService<HomeDeveloperProjectReadResourceResolver>());
            Assert.Same(readPolicy, provider.GetRequiredService<HomeDeveloperProjectReadActionPolicySource>());
            Assert.Same(setupPolicy, provider.GetRequiredService<HomeDeveloperProjectSetupActionPolicySource>());
            Assert.Equal(0, selections); Assert.Equal(0, scopes); Assert.Equal(0, denied.ProductiveCalls); Assert.False(File.Exists(path));
            Assert.Throws<InvalidOperationException>(() => services.AddHavenOwnedDeveloperSourceReads(home, _ => throw new InvalidOperationException(), resolver, readPolicy));
            Assert.Throws<InvalidOperationException>(() => services.AddHavenOwnedDeveloperSetups(home, _ => throw new InvalidOperationException(), _ => denied, _ => denied, setupPolicy));
        }
        catch (Exception cause) { primary = cause; }
        finally
        {
            var closes = new List<Task>();
            void Acquire(Func<Task> factory) { try { closes.Add(factory()); } catch (Exception cause) { errors.Add(cause); } }
            if (setup is not null) Acquire(setup.CloseAndDrainOriginalSetupsAsync);
            if (read is not null) Acquire(read.CloseAndDrainOriginalReadsAsync);
            if (journal is not null) Acquire(journal.CloseAndDrainAsync);
            foreach (var close in closes) try { await close; } catch (Exception cause) { errors.Add(close.Exception ?? cause); }
            closes.Clear();
            if (provider is not null) Acquire(() => provider.DisposeAsync().AsTask());
            foreach (var close in closes) try { await close; } catch (Exception cause) { errors.Add(close.Exception ?? cause); }
            closes.Clear();
            if (home is not null) Acquire(home.CloseAndDrainAsync);
            foreach (var close in closes) try { await close; } catch (Exception cause) { errors.Add(close.Exception ?? cause); }
            try { Directory.Delete(directory, true); } catch (Exception cause) { errors.Add(cause); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual local composition/independent cleanup failed.", primary is null ? errors : new[] { primary }.Concat(errors));
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }

    [Fact]
    public async Task Missing_original_domain_alias_refuses_before_any_lazy_source_factory()
    {
        if (!OperatingSystem.IsLinux()) return;
        var directory = Directory.CreateTempSubdirectory("local-home-developer-missing-").FullName;
        HomeLocalDomainComposition? home = null; Exception? primary = null; var errors = new List<Exception>();
        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            home = new(new FileHomeCoreStateStore(Path.Combine(directory, "state.json")), new OperatingSystemPrincipalSource());
            var calls = 0; var services = new ServiceCollection();
            Assert.Throws<InvalidOperationException>(() => services.AddHavenOwnedDeveloperSourceReads(home,
                _ => { calls++; throw new NotSupportedException(); }, new(() => throw new NotSupportedException()), new()));
            Assert.Equal(0, calls); Assert.Empty(services);
        }
        catch (Exception cause) { primary = cause; }
        finally
        {
            Task? close = null;
            try { if (home is not null) close = home.CloseAndDrainAsync(); } catch (Exception cause) { errors.Add(cause); }
            if (close is not null) try { await close; } catch (Exception cause) { errors.Add(close.Exception ?? cause); }
            try { Directory.Delete(directory, true); } catch (Exception cause) { errors.Add(cause); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual local refusal/independent cleanup failed.", primary is null ? errors : new[] { primary }.Concat(errors));
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private static void AddDomain(IServiceCollection services, HomeLocalDomainComposition home)
    {
        services.AddSingleton(home); services.AddSingleton(home.StateStore); services.AddSingleton(home.Profiles);
        services.AddSingleton(home.Permissions); services.AddSingleton(home.Resources); services.AddSingleton(home.Broker);
    }
    private sealed class UnavailableCaptureAndOutcome : IDeveloperProjectOriginalCaptureAuthority, IDeveloperProjectOriginalSetupStepOutcomeSource
    {
        internal int ProductiveCalls;
        public bool IsIssuedOriginal(IDeveloperProjectOriginalSourceCapture capture) { ProductiveCalls++; return false; }
        public Task RevalidateOriginalAsync(IDeveloperProjectOriginalSourceCapture capture, AuthenticatedResourceActor actor, CancellationToken token)
        { ProductiveCalls++; return Task.FromException(new UnauthorizedAccessException("No private source capture in configuration control")); }
        public void DemandExternalOriginalCaptureJoin() { }
        public bool IsIssuedOriginalStepOutcome(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture,
            DeveloperProjectSetupStep step, Task originalTask, object? originalResult) { ProductiveCalls++; return false; }
        public Task ValidateOriginalStepOutcomeAsync(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture,
            DeveloperProjectSetupStep step, Task originalTask, object? originalResult, CancellationToken token)
        { ProductiveCalls++; return Task.FromException(new UnauthorizedAccessException("No physical outcome in configuration control")); }
        public DeveloperProjectOriginalStepOutcomeObservation GetOriginalStepOutcomeObservation(DeveloperProjectSetupIntent intent,
            IDeveloperProjectOriginalSourceCapture capture, DeveloperProjectSetupStep step, Task originalTask, object? originalResult)
        { ProductiveCalls++; throw new UnauthorizedAccessException("No observation in configuration control"); }
        public void DemandExternalOriginalSetupStepOutcomeJoin() { }
    }
}
