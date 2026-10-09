using System.Text.Json;
using Haven.Application;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace Haven.HomeWriteClassification.Tests;

public sealed class HomeWriteClassificationTests
{
    private const string Ownership = "home.local-store-ownership";
    private const string Configuration = "files.native-workspace";

    [Theory]
    [InlineData(Ownership, HomeCoreErrorCode.HomeStateConflict)]
    [InlineData(Ownership, HomeCoreErrorCode.PermissionDenied)]
    [InlineData(Ownership, HomeCoreErrorCode.HomeServiceUnavailable)]
    [InlineData(Configuration, HomeCoreErrorCode.HomeStateConflict)]
    [InlineData(Configuration, HomeCoreErrorCode.PermissionDenied)]
    [InlineData(Configuration, HomeCoreErrorCode.HomeServiceUnavailable)]
    public async Task Exact_owner_failure_survives_both_native_setup_write_boundaries(string target, HomeCoreErrorCode code)
    {
        var fixture = await Fixture.CreateAsync(target, Fault.Result);
        var failure = new HomeCoreFailure(code, "Original owning Home failure", "exact-owner-target", code != HomeCoreErrorCode.PermissionDenied,
            "Original owning Home recovery guidance");
        fixture.Store.InjectedResult = HomeStateWriteResult.Failed(failure);

        var exception = await Record.ExceptionAsync(fixture.ConfigureAsync);

        Assert.NotNull(exception);
        AssertClassification(exception!, code);
        Assert.Same(failure, exception!.Data["HomeCoreFailure"]);
        Assert.Contains(failure.Message, exception.Message);
        Assert.Contains(failure.RecoveryAction!, exception.Message);
        Assert.Equal(code == HomeCoreErrorCode.HomeStateConflict,
            exception.Message.Contains("conflict", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("profile changed", exception.Message, StringComparison.OrdinalIgnoreCase);
        await fixture.AssertFailedPublicationPreservedAsync();
    }

    [Theory]
    [InlineData(Ownership)]
    [InlineData(Configuration)]
    public async Task Unsuccessful_result_without_failure_details_is_reported_as_malformed(string target)
    {
        var fixture = await Fixture.CreateAsync(target, Fault.Result);
        fixture.Store.InjectedResult = new HomeStateWriteResult(null, null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(fixture.ConfigureAsync);

        Assert.Contains("without failure details", exception.Message);
        Assert.DoesNotContain("conflict", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(exception.Data.Contains("HomeCoreFailure"));
        await fixture.AssertFailedPublicationPreservedAsync();
    }

    [Theory]
    [InlineData(Ownership)]
    [InlineData(Configuration)]
    public async Task Actual_Windows_reader_obstruction_retains_permission_failure_and_original_Home_bytes(string target)
    {
        // A real read handle without delete sharing obstructs the production atomic Move.
        // This test does not substitute an I/O algorithm or synthesize its result.
        Assert.True(OperatingSystem.IsWindows(), "This control requires the actual Windows file-sharing rules.");
        var fixture = await Fixture.CreateAsync(target, Fault.WindowsReader);

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(fixture.ConfigureAsync);

        var failure = Assert.IsType<HomeCoreFailure>(exception.Data["HomeCoreFailure"]);
        Assert.Same(fixture.Store.ObservedResult!.Failure, failure);
        Assert.Equal(HomeCoreErrorCode.PermissionDenied, failure.Code);
        Assert.False(failure.Retryable);
        Assert.Equal("9to1.Home.State", failure.Target);
        Assert.Equal("Home does not have permission to persist local state.", failure.Message);
        Assert.Contains(failure.Message, exception.Message);
        Assert.Contains(failure.RecoveryAction!, exception.Message);
        Assert.DoesNotContain("conflict", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("profile changed", exception.Message, StringComparison.OrdinalIgnoreCase);
        await fixture.AssertFailedPublicationPreservedAsync();
        // The obstruction is released. Ordinary owner-controlled persistence can still progress.
        var actor = (await fixture.Profiles.GetCurrentAsync(default))!;
        var retryProbe = new HomeCoreStateRecord("classification.recovery-observation", "classification.recovery-observation", 1,
            HomeDataScope.DeviceLocal, HomeRecordAuthority.LocalCanonical, 1, JsonSerializer.SerializeToElement(new { observed = true }));
        Assert.True((await fixture.Store.Inner.WriteGuardedAsync(retryProbe, 0, actor, fixture.Profiles)).IsSuccess);
    }

    [Theory]
    [InlineData(Ownership)]
    [InlineData(Configuration)]
    public async Task Actual_Windows_exclusive_holder_retains_IO_failure_and_original_Home_bytes(string target)
    {
        Assert.True(OperatingSystem.IsWindows(), "This control requires the actual Windows file-sharing rules.");
        var fixture = await Fixture.CreateAsync(target, Fault.WindowsExclusiveReader);

        var exception = await Assert.ThrowsAsync<HomeCoreStateUnavailableException>(fixture.ConfigureAsync);

        var failure = Assert.IsType<HomeCoreFailure>(exception.Data["HomeCoreFailure"]);
        Assert.Same(fixture.Store.ObservedResult!.Failure, failure);
        Assert.Equal(HomeCoreErrorCode.HomeServiceUnavailable, exception.Code);
        Assert.Equal(HomeCoreErrorCode.HomeServiceUnavailable, failure.Code);
        Assert.True(failure.Retryable);
        Assert.Equal("9to1.Home.State", failure.Target);
        Assert.Contains("IOException", failure.Message);
        Assert.Contains(failure.Message, exception.Message);
        Assert.Contains(failure.RecoveryAction!, exception.Message);
        Assert.DoesNotContain("conflict", exception.Message, StringComparison.OrdinalIgnoreCase);
        await fixture.AssertFailedPublicationPreservedAsync();
    }

    [Theory]
    [InlineData(Ownership)]
    [InlineData(Configuration)]
    public async Task Actual_owner_CAS_conflict_preserves_the_first_committed_record(string target)
    {
        var fixture = await Fixture.CreateAsync(target, Fault.RealConflict);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(fixture.ConfigureAsync);

        var failure = Assert.IsType<HomeCoreFailure>(exception.Data["HomeCoreFailure"]);
        Assert.Same(fixture.Store.ObservedResult!.Failure, failure);
        Assert.Equal(HomeCoreErrorCode.HomeStateConflict, failure.Code);
        Assert.Contains("expected 0, observed 1", failure.Message);
        Assert.Equal(fixture.Store.LastRecord!.RecordId, failure.Target);
        Assert.True(failure.Retryable);
        Assert.Contains(failure.Message, exception.Message);
        Assert.Contains(failure.RecoveryAction!, exception.Message);
        Assert.Equal(fixture.Store.BeforeFailedWrite, await File.ReadAllBytesAsync(fixture.HomePath));
        var state = (await fixture.Store.Inner.ReadAsync()).State!;
        var winner = Assert.Single(state.Records, record => record.RecordId == fixture.Store.LastRecord.RecordId);
        Assert.Equal(1, winner.Revision);
        Assert.Equal(fixture.Store.LastRecord.Payload.GetRawText(), winner.Payload.GetRawText());
        await fixture.AssertOriginalGuardAsync();
    }

    [Fact]
    public async Task Actual_success_keeps_OS_profile_ownership_and_all_native_app_folders()
    {
        var fixture = await Fixture.CreateAsync(null, Fault.None);

        var workspace = await fixture.Files.ConfigureNewAsync(fixture.Chosen, fixture.Ownership, default);

        var actor = (await fixture.Profiles.GetCurrentAsync(default))!;
        Assert.Equal(actor, workspace.Actor);
        Assert.Null(actor.AccountId);
        Assert.Null(actor.OrganisationId);
        Assert.Equal(fixture.Chosen, workspace.Configuration.RootDirectory);
        Assert.Equal(actor.ProfileId, workspace.Configuration.ProfileId);
        Assert.Equal(8, workspace.Configuration.AppFolders.Count);
        AssertConfiguration(workspace.Configuration, await fixture.Files.GetConfigurationAsync());
        var binding = await fixture.Ownership.GetVerifiedAsync("files", workspace.Configuration.StoreId.ToString("D"));
        Assert.NotNull(binding);
        Assert.Equal(actor.ProfileId, binding!.ProfileId);
        foreach (var app in workspace.Configuration.AppFolders.Keys)
            Assert.True((await workspace.Directories.ResolveProfileAsync(Guid.Parse(actor.ProfileId), app, default)).IsSuccess);
        Assert.Equal(0, fixture.Store.TargetWrites);
    }

    [Fact]
    public async Task Existing_configuration_is_preserved_when_native_setup_is_requested_again()
    {
        var fixture = await Fixture.CreateAsync(null, Fault.None);
        var workspace = await fixture.Files.ConfigureNewAsync(fixture.Chosen, fixture.Ownership, default);
        var beforeHome = await File.ReadAllBytesAsync(fixture.HomePath);
        var drive = Path.Combine(fixture.Chosen, ".9to1-files", "drive.json");
        var beforeDrive = await File.ReadAllBytesAsync(drive);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(fixture.ConfigureAsync);

        Assert.Contains("already configured", exception.Message);
        Assert.Equal(beforeHome, await File.ReadAllBytesAsync(fixture.HomePath));
        Assert.Equal(beforeDrive, await File.ReadAllBytesAsync(drive));
        AssertConfiguration(workspace.Configuration, await fixture.Files.GetConfigurationAsync());
    }

    [Fact]
    public async Task Existing_directory_contents_require_explicit_import_and_are_never_adopted()
    {
        var fixture = await Fixture.CreateAsync(null, Fault.None);
        var original = Path.Combine(fixture.Chosen, "existing-content.txt");
        await File.WriteAllTextAsync(original, "Preserve this original content.");
        var before = await File.ReadAllBytesAsync(fixture.HomePath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(fixture.ConfigureAsync);

        Assert.Contains("explicit import", exception.Message);
        Assert.Equal("Preserve this original content.", await File.ReadAllTextAsync(original));
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.HomePath));
        Assert.False(Directory.Exists(Path.Combine(fixture.Chosen, ".9to1-files")));
        Assert.Null(await fixture.Files.GetConfigurationAsync());
    }

    private static void AssertClassification(Exception exception, HomeCoreErrorCode code)
    {
        if (code == HomeCoreErrorCode.HomeStateConflict) Assert.IsType<InvalidOperationException>(exception);
        else if (code == HomeCoreErrorCode.PermissionDenied) Assert.IsType<UnauthorizedAccessException>(exception);
        else Assert.Equal(code, Assert.IsType<HomeCoreStateUnavailableException>(exception).Code);
    }

    private static void AssertConfiguration(NativeFilesWorkspaceConfiguration expected, NativeFilesWorkspaceConfiguration? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.ProfileId, actual!.ProfileId);
        Assert.Equal(expected.StoreId, actual.StoreId);
        Assert.Equal(expected.LocationId, actual.LocationId);
        Assert.Equal(expected.RootDirectory, actual.RootDirectory);
        Assert.Equal(expected.AppFolders.OrderBy(pair => pair.Key), actual.AppFolders.OrderBy(pair => pair.Key));
    }

    private enum Fault { None, Result, WindowsReader, WindowsExclusiveReader, RealConflict }

    private sealed class Fixture
    {
        public string HomePath { get; }
        public string Chosen { get; }
        public InterceptingStateStore Store { get; }
        public HomeLocalProfileIdentity Profiles { get; }
        public NativeFilesWorkspaceService Files { get; }
        public HomeLocalStoreOwnership Ownership { get; }
        private readonly string? _target;

        private Fixture(string? target, Fault fault)
        {
            // Every state is fresh, owned and under this task's isolated artifacts directory.
            // Retain it for independent inspection; never alter a user's Home data or trust state.
            var root = Path.Combine(AppContext.BaseDirectory, "classification-state", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            HomePath = Path.Combine(root, "home.json");
            Chosen = Path.Combine(root, "chosen");
            Directory.CreateDirectory(Chosen);
            Store = new(new FileHomeCoreStateStore(HomePath), HomePath, target, fault);
            Profiles = new(Store, new OperatingSystemPrincipalSource());
            Files = new(Store, Profiles);
            Ownership = new(Store, Profiles, new HomeLocalStoreEvidenceRegistry([Files]),
                new HomePermissionTrustService(Store, (_, _) => null));
            _target = target;
        }

        public static async Task<Fixture> CreateAsync(string? target, Fault fault)
        {
            var fixture = new Fixture(target, fault);
            Assert.NotNull(await fixture.Profiles.GetCurrentAsync(default));
            Assert.Equal(0, fixture.Store.TargetWrites);
            return fixture;
        }

        public async Task ConfigureAsync() => await Files.ConfigureNewAsync(Chosen, Ownership, default);

        public async Task AssertOriginalGuardAsync()
        {
            Assert.Equal(1, Store.TargetWrites);
            Assert.Equal(_target, Store.LastRecord!.RecordType);
            Assert.Equal(0, Store.LastExpectedRevision);
            Assert.Same(Profiles, Store.LastGuard);
            Assert.Equal(await Profiles.GetCurrentAsync(default), Store.LastActor);
            Assert.True(Store.AdmissionGuardPassed);
            Assert.True(Store.PublicationGuardPassed);
        }

        public async Task AssertFailedPublicationPreservedAsync()
        {
            await AssertOriginalGuardAsync();
            Assert.Equal(Store.BeforeFailedWrite, await File.ReadAllBytesAsync(HomePath));
            var state = (await Store.Inner.ReadAsync()).State!;
            Assert.DoesNotContain(state.Records, record => record.RecordType == _target);
            Assert.NotNull(await Profiles.GetCurrentAsync(default));
            Assert.Null(await Files.GetConfigurationAsync());
            Assert.True(File.Exists(Path.Combine(Chosen, ".9to1-files", "drive.json")));
            var driveBytes = await File.ReadAllBytesAsync(Path.Combine(Chosen, ".9to1-files", "drive.json"));
            using var document = JsonDocument.Parse(driveBytes);
            var durable = document.RootElement.GetProperty("state");
            var storeId = durable.EnumerateObject().Single(property => property.Name.Equals("storeId", StringComparison.OrdinalIgnoreCase)).Value.GetGuid();
            Assert.Null(await Files.ReadAsync(storeId.ToString("D"), default)); // Failed setup released only its in-memory construction entry.
            Assert.Equal(driveBytes, await File.ReadAllBytesAsync(Path.Combine(Chosen, ".9to1-files", "drive.json")));
        }
    }

    /// <summary>Only the selected result boundary is injected. All storage, profile, Files evidence,
    /// successful writes and commit-guard checks execute the real production implementations.</summary>
    private sealed class InterceptingStateStore(FileHomeCoreStateStore inner, string path, string? target, Fault fault) : IHomeCoreStateStore
    {
        public FileHomeCoreStateStore Inner { get; } = inner;
        public HomeStateWriteResult? InjectedResult { get; set; }
        public HomeStateWriteResult? ObservedResult { get; private set; }
        public HomeCoreStateRecord? LastRecord { get; private set; }
        public long LastExpectedRevision { get; private set; }
        public AuthenticatedResourceActor? LastActor { get; private set; }
        public IHomeStateCommitActorGuard? LastGuard { get; private set; }
        public bool AdmissionGuardPassed { get; private set; }
        public bool PublicationGuardPassed { get; private set; }
        public int TargetWrites { get; private set; }
        public byte[]? BeforeFailedWrite { get; private set; }

        public Task<HomeStateReadResult> ReadAsync(CancellationToken cancellationToken = default) => Inner.ReadAsync(cancellationToken);
        public Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expectedRecordRevision, CancellationToken cancellationToken = default) =>
            Inner.WriteAsync(record, expectedRecordRevision, cancellationToken);

        public async Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long expectedRecordRevision,
            AuthenticatedResourceActor expectedActor, IHomeStateCommitActorGuard guard, CancellationToken cancellationToken = default)
        {
            if (record.RecordType != target) return await Inner.WriteGuardedAsync(record, expectedRecordRevision, expectedActor, guard, cancellationToken);
            TargetWrites++;
            LastRecord = record;
            LastExpectedRevision = expectedRecordRevision;
            LastActor = expectedActor;
            LastGuard = guard;
            var current = (await Inner.ReadAsync(cancellationToken)).State!;
            AdmissionGuardPassed = await guard.CheckAsync(current, expectedActor, HomeStateCommitPhase.Admission, cancellationToken);
            PublicationGuardPassed = await guard.CheckAsync(current, expectedActor, HomeStateCommitPhase.Publication, cancellationToken);
            Assert.True(AdmissionGuardPassed);
            Assert.True(PublicationGuardPassed);
            if (fault == Fault.RealConflict)
                Assert.True((await Inner.WriteGuardedAsync(record, expectedRecordRevision, expectedActor, guard, cancellationToken)).IsSuccess);
            BeforeFailedWrite = await File.ReadAllBytesAsync(path, cancellationToken);
            if (fault == Fault.Result) return InjectedResult ?? throw new InvalidOperationException("The control did not supply its boundary result.");
            if (fault is Fault.WindowsReader or Fault.WindowsExclusiveReader)
            {
                using var obstruction = new FileStream(path, FileMode.Open, FileAccess.Read,
                    fault == Fault.WindowsReader ? FileShare.Read : FileShare.None);
                ObservedResult = await Inner.WriteGuardedAsync(record, expectedRecordRevision, expectedActor, guard, cancellationToken);
            }
            else ObservedResult = await Inner.WriteGuardedAsync(record, expectedRecordRevision, expectedActor, guard, cancellationToken);
            Assert.False(ObservedResult.IsSuccess);
            return ObservedResult;
        }
    }
}
