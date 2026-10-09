using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

// All descriptors, classes, and policies here are SCRIPTED inputs. The envelope
// satisfies the codec's shape only. These tests exercise pure dependency planning,
// never protected publisher verification, installed approval, root effects, or DI.
public sealed class HomePackageOriginalDependencyLifecycleTests
{
    [Fact]
    public void Required_closure_reuses_exact_compatible_shared_core_once()
    {
        var owner = new ScriptedOwner();
        var policy = new ScriptedPolicy();
        var home = Selection(owner, "fixture.home");
        var shared = Selection(owner, "fixture.core");
        var dependency = Selection(owner, "fixture.dependency", new HomePackageDependency("fixture.core", "1.0.0", "2.0.0"));
        var app = Selection(owner, "fixture.app", new("fixture.core", "1.0.0", "2.0.0"),
            new("fixture.dependency", "1.0.0", "2.0.0"));
        policy.Classify(home, HomePackageComponentClass.MandatorySharedCore);
        policy.Classify(shared, HomePackageComponentClass.MandatorySharedCore);
        policy.Classify(dependency, HomePackageComponentClass.AppRequiredDependency);
        policy.Classify(app, HomePackageComponentClass.OptionalApp);
        var lifecycle = new HomePackageOriginalDependencyLifecycle(owner, policy, new[] { app, dependency, home, shared });
        var registry = Registry(Installed(home), Installed(shared));
        var result = lifecycle.Plan(Request(app, HomePackageAction.Install), registry, false);
        Assert.Null(result.Error);
        var plan = Assert.IsType<HomePackageOriginalLifecyclePlan>(result.Plan);
        Assert.Equal(new[] { "fixture.core", "fixture.home", "fixture.dependency", "fixture.app" },
            plan.Steps.Select(step => step.OriginalSelection.Descriptor.PackageId));
        Assert.Equal(new[] { true, true, false, false }, plan.Steps.Select(step => step.ReuseCompatibleInstallation));
        Assert.Equal(new[] { "fixture.dependency", "fixture.app" }, plan.AffectedPackageIds);
        Assert.Equal(registry.Revision, plan.OriginalRegistryRevision);
        Assert.Equal(0, owner.EffectCalls);
    }

    [Theory]
    [InlineData("missing", "DependencyRequired")]
    [InlineData("unsupported", "DependencyRequired")]
    [InlineData("conflict", "PackageDependencyConflict")]
    [InlineData("cycle", "PackageDependencyConflict")]
    public void Missing_ambiguous_conflicting_or_cyclic_dependency_never_becomes_a_plan(string kind, string code)
    {
        var owner = new ScriptedOwner();
        var policy = new ScriptedPolicy();
        var home = Selection(owner, "fixture.home");
        var requirement = kind == "unsupported" ? new HomePackageDependency("fixture.dependency", "unsupported", null)
            : new HomePackageDependency("fixture.dependency", kind == "conflict" ? "2.0.0" : "1.0.0", null);
        var app = Selection(owner, "fixture.app", requirement);
        var dependency = kind == "cycle" ? Selection(owner, "fixture.dependency", new HomePackageDependency("fixture.app"))
            : Selection(owner, "fixture.dependency");
        policy.Classify(home, HomePackageComponentClass.MandatorySharedCore);
        policy.Classify(app, HomePackageComponentClass.OptionalApp);
        policy.Classify(dependency, HomePackageComponentClass.AppRequiredDependency);
        var selections = kind == "missing" ? new[] { home, app } : new[] { home, app, dependency };
        var lifecycle = new HomePackageOriginalDependencyLifecycle(owner, policy, selections);
        var result = lifecycle.Plan(Request(app, HomePackageAction.Install), Registry(Installed(home)), false);
        Assert.Null(result.Plan);
        Assert.Equal(code, Assert.IsType<HomeAppsError>(result.Error).Code);
        Assert.Equal(0, owner.EffectCalls);
    }

    [Fact]
    public void Optional_feature_cancel_leaves_the_same_request_and_canonical_work_then_explicit_retry_plans()
    {
        var owner = new ScriptedOwner();
        var policy = new ScriptedPolicy();
        var home = Selection(owner, "fixture.home");
        var app = Selection(owner, "fixture.app");
        var feature = Selection(owner, "fixture.feature", new HomePackageDependency("fixture.app"));
        policy.Classify(home, HomePackageComponentClass.MandatorySharedCore);
        policy.Classify(app, HomePackageComponentClass.OptionalApp);
        policy.Classify(feature, HomePackageComponentClass.OptionalFeature);
        var lifecycle = new HomePackageOriginalDependencyLifecycle(owner, policy, new[] { home, app, feature });
        var registry = Registry(Installed(home), Installed(app));
        var request = Request(feature, HomePackageAction.Install);
        var originalBytes = JsonSerializer.SerializeToUtf8Bytes(registry);
        var declined = lifecycle.Plan(request, registry, false);
        Assert.Null(declined.Plan);
        Assert.Equal("DependencyRequired", Assert.IsType<HomeAppsError>(declined.Error).Code);
        Assert.Equal(originalBytes, JsonSerializer.SerializeToUtf8Bytes(registry));
        var accepted = Assert.IsType<HomePackageOriginalLifecyclePlan>(lifecycle.Plan(request, registry, true).Plan);
        Assert.Equal(request, accepted.OriginalRequest);
        Assert.True(accepted.RequiresOptionalFeatureConsent);
        Assert.Equal("fixture.feature", Assert.Single(accepted.AffectedPackageIds));
        Assert.Equal(0, owner.EffectCalls);
    }

    [Fact]
    public void Shared_core_and_in_use_dependency_removal_refuse_then_app_removal_retains_other_packages()
    {
        var owner = new ScriptedOwner();
        var policy = new ScriptedPolicy();
        var home = Selection(owner, "fixture.home");
        var dependency = Selection(owner, "fixture.dependency");
        var app = Selection(owner, "fixture.app", new HomePackageDependency("fixture.dependency"));
        policy.Classify(home, HomePackageComponentClass.MandatorySharedCore);
        policy.Classify(dependency, HomePackageComponentClass.AppRequiredDependency);
        policy.Classify(app, HomePackageComponentClass.OptionalApp);
        var lifecycle = new HomePackageOriginalDependencyLifecycle(owner, policy, new[] { home, dependency, app });
        var registry = Registry(Installed(home), Installed(dependency), Installed(app));
        Assert.Equal("PackageDependencyConflict", lifecycle.Plan(Request(home, HomePackageAction.Uninstall), registry, false).Error!.Code);
        Assert.Equal("PackageDependencyConflict", lifecycle.Plan(Request(dependency, HomePackageAction.Uninstall), registry, false).Error!.Code);
        var plan = Assert.IsType<HomePackageOriginalLifecyclePlan>(lifecycle.Plan(Request(app, HomePackageAction.Uninstall), registry, false).Plan);
        Assert.Equal("fixture.app", Assert.Single(plan.AffectedPackageIds));
        Assert.Equal(new[] { "fixture.dependency", "fixture.home" }, plan.RetainedSharedPackageIds);
        Assert.Equal(3, registry.Packages.Count);
        Assert.Equal(0, owner.EffectCalls);
    }

    [Fact]
    public void Explicit_core_update_is_not_lost_when_it_is_also_a_mandatory_graph_root()
    {
        var owner = new ScriptedOwner();
        var policy = new ScriptedPolicy();
        var home = Selection(owner, "fixture.home");
        policy.Classify(home, HomePackageComponentClass.MandatorySharedCore);
        var lifecycle = new HomePackageOriginalDependencyLifecycle(owner, policy, new[] { home });
        var plan = Assert.IsType<HomePackageOriginalLifecyclePlan>(
            lifecycle.Plan(Request(home, HomePackageAction.Update), Registry(Installed(home)), false).Plan);
        var step = Assert.Single(plan.Steps);
        Assert.Equal(HomePackageAction.Update, step.Action);
        Assert.False(step.ReuseCompatibleInstallation);
        Assert.Equal("1.0.0", step.OriginalKnownGoodVersion);
        Assert.Equal(0, owner.EffectCalls);
    }

    [Fact]
    public void Unresolved_original_effect_requires_audit_and_never_plans_a_replacement()
    {
        var owner = new ScriptedOwner();
        var policy = new ScriptedPolicy();
        var home = Selection(owner, "fixture.home");
        var app = Selection(owner, "fixture.app");
        policy.Classify(home, HomePackageComponentClass.MandatorySharedCore);
        policy.Classify(app, HomePackageComponentClass.OptionalApp);
        var lifecycle = new HomePackageOriginalDependencyLifecycle(owner, policy, new[] { home, app });
        var pending = new HomePackageJournalEntry("old-key", "old-operation", "fixture.app", "Install",
            HomePackageJournalState.OutcomeUnknown, DateTimeOffset.UnixEpoch, null, "OutcomeUnknown",
            false, false, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        var registry = Registry(Installed(home)) with { RecentOperations = new[] { pending } };
        var result = lifecycle.Plan(Request(app, HomePackageAction.Install), registry, false);
        Assert.Null(result.Plan);
        Assert.Equal("PackageDependencyConflict", result.Error!.Code);
        Assert.Equal(pending, Assert.Single(registry.RecentOperations));
        Assert.Equal(0, owner.EffectCalls);
    }

    [Fact]
    public void Missing_root_adapter_changed_classification_and_foreign_selection_refuse()
    {
        var owner = new ScriptedOwner();
        var other = new ScriptedOwner();
        var policy = new ScriptedPolicy();
        var home = Selection(owner, "fixture.home");
        var app = Selection(owner, "fixture.app");
        policy.Classify(home, HomePackageComponentClass.MandatorySharedCore);
        policy.Classify(app, HomePackageComponentClass.OptionalApp);
        var lifecycle = new HomePackageOriginalDependencyLifecycle(owner, policy, new[] { home, app });
        policy.AdaptersAvailable = false;
        Assert.Equal("DependencyRequired", lifecycle.Plan(Request(app, HomePackageAction.Install), Registry(Installed(home)), false).Error!.Code);
        policy.AdaptersAvailable = true;
        policy.Classify(app, HomePackageComponentClass.AppRequiredDependency);
        Assert.Equal("PackageSelectionChanged", lifecycle.Plan(Request(app, HomePackageAction.Install), Registry(Installed(home)), false).Error!.Code);
        var foreign = Selection(other, "fixture.foreign");
        policy.Classify(foreign, HomePackageComponentClass.OptionalApp);
        Assert.Throws<InvalidDataException>(() => new HomePackageOriginalDependencyLifecycle(owner, policy, new[] { home, foreign }));
        Assert.Equal(0, owner.EffectCalls);
        Assert.Equal(0, other.EffectCalls);
    }

    [Fact]
    public void Optional_feature_cannot_be_silently_installed_through_an_app_dependency()
    {
        var owner = new ScriptedOwner();
        var policy = new ScriptedPolicy();
        var home = Selection(owner, "fixture.home");
        var feature = Selection(owner, "fixture.feature");
        var app = Selection(owner, "fixture.app", new HomePackageDependency("fixture.feature"));
        policy.Classify(home, HomePackageComponentClass.MandatorySharedCore);
        policy.Classify(feature, HomePackageComponentClass.OptionalFeature);
        policy.Classify(app, HomePackageComponentClass.OptionalApp);
        var lifecycle = new HomePackageOriginalDependencyLifecycle(owner, policy, new[] { home, feature, app });
        var refused = lifecycle.Plan(Request(app, HomePackageAction.Install), Registry(Installed(home)), false);
        Assert.Null(refused.Plan);
        Assert.Equal("DependencyRequired", refused.Error!.Code);
        Assert.Equal("fixture.feature", refused.Error.Target);
        Assert.Equal(0, owner.EffectCalls);
    }

    [Fact]
    public void Registered_policy_detaches_exact_selection_classes_and_supported_actions()
    {
        var owner = new ScriptedOwner();
        var home = DeclaredSelection(owner, "fixture.home", HomePackageComponentClass.MandatorySharedCore);
        var app = DeclaredSelection(owner, "fixture.app", HomePackageComponentClass.OptionalApp);
        var homeActions = new HashSet<HomePackageAction> { HomePackageAction.Install, HomePackageAction.Update };
        var appActions = new HashSet<HomePackageAction> { HomePackageAction.Install, HomePackageAction.Uninstall };
        var rows = new List<HomePackageOriginalLifecycleRegistration>
        {
            new(home, HomePackageComponentClass.MandatorySharedCore, homeActions),
            new(app, HomePackageComponentClass.OptionalApp, appActions)
        };
        var policy = new HomePackageOriginalLifecyclePolicy(owner, "fixture.home", "script-platform", "script-abi", rows);
        rows.Clear(); homeActions.Clear(); appActions.Add(HomePackageAction.Rollback);
        Assert.Equal(HomePackageComponentClass.MandatorySharedCore, policy.ClassifyOriginal(home));
        Assert.Equal(HomePackageComponentClass.OptionalApp, policy.ClassifyOriginal(app));
        Assert.True(policy.SupportsOriginalAction(home, HomePackageAction.Install));
        Assert.True(policy.SupportsOriginalAction(app, HomePackageAction.Uninstall));
        Assert.False(policy.SupportsOriginalAction(app, HomePackageAction.Rollback));
        Assert.Null(policy.ClassifyOriginal(DeclaredSelection(owner, "fixture.app", HomePackageComponentClass.OptionalApp)));
        Assert.Equal(0, owner.EffectCalls);
    }

    [Fact]
    public void Registered_policy_refuses_foreign_selections_duplicate_identity_and_mandatory_removal()
    {
        var owner = new ScriptedOwner();
        var other = new ScriptedOwner();
        var home = DeclaredSelection(owner, "fixture.home", HomePackageComponentClass.MandatorySharedCore);
        var original = new HomePackageOriginalLifecycleRegistration(home,
            HomePackageComponentClass.MandatorySharedCore, new HashSet<HomePackageAction> { HomePackageAction.Install });
        Assert.Throws<InvalidDataException>(() => new HomePackageOriginalLifecyclePolicy(owner,
            "fixture.home", "script-platform", "script-abi", new[]
            {
                original, new HomePackageOriginalLifecycleRegistration(DeclaredSelection(other, "fixture.app", HomePackageComponentClass.OptionalApp),
                    HomePackageComponentClass.OptionalApp, new HashSet<HomePackageAction>())
            }));
        Assert.Throws<InvalidDataException>(() => new HomePackageOriginalLifecyclePolicy(owner,
            "fixture.home", "script-platform", "script-abi", new[]
            {
                original, new HomePackageOriginalLifecycleRegistration(DeclaredSelection(owner, "fixture.home", HomePackageComponentClass.MandatorySharedCore),
                    HomePackageComponentClass.MandatorySharedCore, new HashSet<HomePackageAction>())
            }));
        Assert.Throws<InvalidDataException>(() => new HomePackageOriginalLifecyclePolicy(owner,
            "fixture.home", "script-platform", "script-abi", new[]
            {
                original with { SupportedActions = new HashSet<HomePackageAction> { HomePackageAction.Uninstall } }
            }));
        Assert.Equal(0, owner.EffectCalls);
        Assert.Equal(0, other.EffectCalls);
    }

    [Theory]
    [InlineData("1.0.0-alpha", "1.0.0-alpha", "1.0.0-alpha.1", true)]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha", "1.0.0-alpha.1", false)]
    [InlineData("1.0.0-alpha.beta", "1.0.0-alpha.1", "1.0.0-beta", true)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.1", "1.0.0-beta.11", true)]
    [InlineData("1.0.0-rc.1", "1.0.0-beta.11", "1.0.0", true)]
    [InlineData("1.0.0", "1.0.0-rc.1", "1.0.1", true)]
    [InlineData("1.0.0+original.001", "1.0.0+other", "1.0.1", true)]
    [InlineData("1.0.0-0", "1.0.0-0", "1.0.0-a", true)]
    [InlineData("999999999999999999999999999999.0.0", "999999999999999999999999999998.0.0", null, true)]
    [InlineData("2.0.0", null, "2.0.0", false)]
    public void Original_semver_precedence_and_inclusive_exclusive_ranges_are_explicit(
        string actual, string? minimum, string? maximum, bool expected)
    {
        var owner = new ScriptedOwner();
        var home = DeclaredSelection(owner, "fixture.home", HomePackageComponentClass.MandatorySharedCore);
        var policy = new HomePackageOriginalLifecyclePolicy(owner, "fixture.home", "script-platform", "script-abi",
            new[] { new HomePackageOriginalLifecycleRegistration(home, HomePackageComponentClass.MandatorySharedCore,
                new HashSet<HomePackageAction> { HomePackageAction.Install }) });
        Assert.True(policy.TrySatisfyOriginalVersion(actual, new("fixture.dependency", minimum, maximum), out var satisfies));
        Assert.Equal(expected, satisfies);
        Assert.Equal(0, owner.EffectCalls);
    }

    [Theory]
    [InlineData("1.0", null, null)]
    [InlineData("v1.0.0", null, null)]
    [InlineData("1.0.0.0", null, null)]
    [InlineData("01.0.0", null, null)]
    [InlineData("1.0.0-01", null, null)]
    [InlineData("1.0.0+", null, null)]
    [InlineData("1.0.0-alpha..beta", null, null)]
    [InlineData("1.0.0+build+other", null, null)]
    [InlineData("1.0.0", "1.*", null)]
    [InlineData("1.0.0", "2.0.0", "2.0.0")]
    public void Unsupported_or_ambiguous_version_syntax_refuses_without_coercion(
        string actual, string? minimum, string? maximum)
    {
        var owner = new ScriptedOwner();
        var home = DeclaredSelection(owner, "fixture.home", HomePackageComponentClass.MandatorySharedCore);
        var policy = new HomePackageOriginalLifecyclePolicy(owner, "fixture.home", "script-platform", "script-abi",
            new[] { new HomePackageOriginalLifecycleRegistration(home, HomePackageComponentClass.MandatorySharedCore,
                new HashSet<HomePackageAction> { HomePackageAction.Install }) });
        Assert.False(policy.TrySatisfyOriginalVersion(actual, new("fixture.dependency", minimum, maximum), out var satisfies));
        Assert.False(satisfies);
        Assert.Equal(0, owner.EffectCalls);
    }

    [Fact]
    public void Signed_component_declaration_must_match_the_exact_registered_class()
    {
        var owner = new ScriptedOwner();
        var unknown = Selection(owner, "fixture.home");
        Assert.Throws<InvalidDataException>(() => new HomePackageOriginalLifecyclePolicy(owner,
            "fixture.home", "script-platform", "script-abi", new[]
            {
                new HomePackageOriginalLifecycleRegistration(unknown, HomePackageComponentClass.MandatorySharedCore,
                    new HashSet<HomePackageAction> { HomePackageAction.Install })
            }));
        var home = DeclaredSelection(owner, "fixture.home", HomePackageComponentClass.MandatorySharedCore);
        var feature = DeclaredSelection(owner, "fixture.feature", HomePackageComponentClass.OptionalFeature);
        Assert.Throws<InvalidDataException>(() => new HomePackageOriginalLifecyclePolicy(owner,
            "fixture.home", "script-platform", "script-abi", new[]
            {
                new HomePackageOriginalLifecycleRegistration(home, HomePackageComponentClass.MandatorySharedCore,
                    new HashSet<HomePackageAction> { HomePackageAction.Install }),
                new HomePackageOriginalLifecycleRegistration(feature, HomePackageComponentClass.OptionalApp,
                    new HashSet<HomePackageAction> { HomePackageAction.Install })
            }));
        Assert.Equal(0, owner.EffectCalls);
    }

    private static HomePackageArtifactSelection DeclaredSelection(ScriptedOwner owner, string id,
        HomePackageComponentClass classification)
    {
        // Explicitly SCRIPTED envelope and evidence; this only checks declaration
        // binding and never establishes publisher, payload or administrative trust.
        var descriptor = new HomePackageArtifactDescriptor(1, id, id, "1.0.0", "script-channel",
            HomePackageOriginalLifecyclePolicy.DeclaredComponentClass(classification),
            "script-platform", "script-abi", Array.Empty<HomePackageDependency>(),
            Array.Empty<string>(), new string('1', 64), new string('2', 64), 1);
        var payload = JsonSerializer.SerializeToUtf8Bytes(descriptor, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var envelope = JsonSerializer.SerializeToUtf8Bytes(new {
            schemaVersion = 1, issuerKeyId = "scripted.test-only",
            payload = Convert.ToBase64String(payload), signature = Convert.ToBase64String(new byte[384])
        });
        return HomePackageArtifactSelection.Capture(owner,
            new(envelope, payload, "script-catalogue-revision", new object()),
            new(id, HomePackageAction.Install, "script-declared-capture", "1.0.0", "script-channel", "script-catalogue-revision"));
    }

    private static HomePackageActionRequest Request(HomePackageArtifactSelection selection, HomePackageAction action) =>
        new(selection.Descriptor.PackageId, action, "script-" + selection.Descriptor.PackageId + "-" + action,
            selection.Descriptor.Version, selection.Descriptor.Channel, selection.CatalogueRevision);

    private static HomePackageDatabaseSnapshot Registry(params HomePackageDatabaseEntry[] entries) =>
        HomePackageDatabaseSnapshot.Empty with { Revision = 11, Packages = entries };

    private static HomePackageDatabaseEntry Installed(HomePackageArtifactSelection selection) =>
        new(selection.Descriptor.PackageId, selection.Descriptor.AppId, selection.Descriptor.PackageId,
            null, selection.Descriptor.Version, selection.Descriptor.Version, selection.Descriptor.Channel,
            HomePackageInstallState.Installed, HomePackageCompatibility.Compatible, null,
            selection.Descriptor.Dependencies, new[] { new HomePackageIntegrityEvidence(
                selection.Descriptor.Version, selection.Descriptor.PayloadSha256, "SCRIPTED_ONLY_NO_VERIFICATION",
                HomePackageIntegrityState.Verified, DateTimeOffset.UnixEpoch) },
            selection.Descriptor.Version, new[] { selection.Descriptor.Version }, true, 7);

    private static HomePackageArtifactSelection Selection(ScriptedOwner owner, string id,
        params HomePackageDependency[] dependencies)
    {
        var descriptor = new HomePackageArtifactDescriptor(1, id, id, "1.0.0", "script-channel",
            "scripted-observation-only", "script-platform", "script-abi", dependencies,
            Array.Empty<string>(), new string('1', 64), new string('2', 64), 1);
        var payload = JsonSerializer.SerializeToUtf8Bytes(descriptor, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var envelope = JsonSerializer.SerializeToUtf8Bytes(new {
            schemaVersion = 1, issuerKeyId = "scripted.test-only",
            payload = Convert.ToBase64String(payload), signature = Convert.ToBase64String(new byte[384])
        });
        return HomePackageArtifactSelection.Capture(owner,
            new(envelope, payload, "script-catalogue-revision", new object()),
            new(id, HomePackageAction.Install, "script-capture", "1.0.0", "script-channel", "script-catalogue-revision"));
    }

    private sealed class ScriptedPolicy : IHomePackageOriginalLifecyclePolicy
    {
        private readonly Dictionary<HomePackageArtifactSelection, HomePackageComponentClass> _classes = new();
        public string OriginalHomePackageId => "fixture.home";
        public string Platform => "script-platform";
        public string Abi => "script-abi";
        internal bool AdaptersAvailable = true;
        internal void Classify(HomePackageArtifactSelection selection, HomePackageComponentClass value) => _classes[selection] = value;
        public HomePackageComponentClass? ClassifyOriginal(HomePackageArtifactSelection selection) =>
            _classes.TryGetValue(selection, out var value) ? value : null;
        public bool SupportsOriginalAction(HomePackageArtifactSelection selection, HomePackageAction action) => AdaptersAvailable;
        public bool TrySatisfyOriginalVersion(string actualVersion, HomePackageDependency requiredRange, out bool satisfies)
        {
            satisfies = false;
            if (!Version.TryParse(actualVersion, out var actual) ||
                requiredRange.MinimumVersion is not null && !Version.TryParse(requiredRange.MinimumVersion, out _) ||
                requiredRange.MaximumVersionExclusive is not null && !Version.TryParse(requiredRange.MaximumVersionExclusive, out _))
                return false;
            satisfies = (requiredRange.MinimumVersion is null || actual >= Version.Parse(requiredRange.MinimumVersion)) &&
                (requiredRange.MaximumVersionExclusive is null || actual < Version.Parse(requiredRange.MaximumVersionExclusive));
            return true;
        }
    }

    private sealed class ScriptedOwner : IHomePackageOriginalPlatformOwner
    {
        internal int EffectCalls;
        public IHomeCoreStateStore DeviceStore => throw new InvalidOperationException("Pure planner never selects a store.");
        public HomePackageDatabase Database => throw new InvalidOperationException("Pure planner never creates a registry.");
        public ValueTask<HomePackageOriginalArtifactMaterial?> ResolveOriginalArtifactAsync(
            HomePackageActionRequest request, HomePackageDatabaseSnapshot registry, CancellationToken token) =>
            throw new InvalidOperationException("All SCRIPTED selections are captured synchronously.");
        public HomePackageOriginalActionBinding? ResolveOriginalAction(HomePackageAction action, HomePackageArtifactDescriptor descriptor) =>
            throw new InvalidOperationException("Planning is not approval.");
        public Task DemandOriginalArtifactAndChannelAsync(HomePackageArtifactSelection selection,
            AuthenticatedResourceActor actor, HomeNativeInstalledPeer caller, string session, CancellationToken token) =>
            throw new InvalidOperationException("Planning is not root admission.");
        public Task<HomePackageActionResult> ExecuteAndCommitOriginalAsync(
            HomePackageOriginalInvocation invocation, CancellationToken token)
        {
            EffectCalls++;
            throw new InvalidOperationException("The dependency planner must never dispatch an effect.");
        }
    }
}
