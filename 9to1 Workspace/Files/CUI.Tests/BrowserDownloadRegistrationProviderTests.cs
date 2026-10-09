using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HavenOS.Files.CUI.Tests;

// Actual persisted Files component controls. The denial/order-only authority
// callback is not a Home WRITE receipt or native/browser platform qualification.
internal static class BrowserDownloadRegistrationProviderTests
{
    public static async Task RunAllAsync()
    {
        await OrdinaryUploadsRetainTheirLegacyEnvelopeAndRevisionReplay();
        await BrowserMappingAndUploadReopenTogetherWithoutAnotherCopy();
        await CreateOnlyIdentityCollisionRefusesBeforeOriginalContentCallback();
        await ActualCopyFaultKeepsOpaqueAndIndependentCausesThroughOriginalDrain();
    }
    private static async Task OrdinaryUploadsRetainTheirLegacyEnvelopeAndRevisionReplay() => await Fixture(async rig =>
    {
        var content = rig.Content("ordinary.txt");
        var actual = await rig.Provider.CommitUploadedContentAsync(content, [rig.Parent]);
        Check.True(actual.IsSuccess);
        var replay = await rig.Provider.CommitUploadedContentAsync(content, [rig.Parent]);
        Check.True(replay.IsSuccess); Check.Equal(actual.Value, replay.Value);
        var observed = await rig.Provider.GetCurrentArtifactContentForOriginalStoreAsync(rig.StoreId, content.FileId, content.RevisionId);
        Check.True(observed.IsSuccess); Check.Equal(content.ProviderContentReference, observed.Value?.ProviderContentReference);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(rig.StatePath));
        Check.False(document.RootElement.GetProperty("state").TryGetProperty("browserDownloads", out _));
        Check.Equal(1, document.RootElement.GetProperty("state").GetProperty("uploadedContents").GetArrayLength());
    });
    private static async Task BrowserMappingAndUploadReopenTogetherWithoutAnotherCopy() => await Fixture(async rig =>
    {
        var content = rig.Content("registered.txt"); var registration = rig.Registration(content); var copies = 0;
        var raw = new List<Task>();
        var actual = await rig.Provider.CommitOriginalBrowserDownloadAsync(registration, rig.StoreRevision, rig.Parent, rig.Guard,
            _ => { copies++; return Task.CompletedTask; }, body => body(), task => raw.Add(task), CancellationToken.None);
        Check.True(actual.IsSuccess); Check.Equal(1, copies);
        await rig.Provider.CloseOriginalBrowserDownloadsAndDrainAsync(); foreach (var task in raw) await task;
        var reopened = new DurableDriveProvider(rig.StatePath, rig.Location, rig.Actor);
        rig.OtherProviders.Add(reopened);
        var saved = await reopened.ReadOriginalBrowserDownloadRegistrationAsync(rig.StoreId, registration.DownloadId, registration.ActionId,
            body => body(), _ => { }, CancellationToken.None);
        Check.Equal(registration, saved);
        var second = await reopened.CommitOriginalBrowserDownloadAsync(registration, rig.StoreRevision, rig.Parent, rig.Guard,
            _ => { copies++; return Task.CompletedTask; }, body => body(), _ => { }, CancellationToken.None);
        Check.True(second.IsSuccess); Check.Equal(actual.Value, second.Value); Check.Equal(1, copies);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(rig.StatePath));
        var state = document.RootElement.GetProperty("state");
        Check.Equal(1, state.GetProperty("browserDownloads").GetArrayLength());
        Check.Equal(1, state.GetProperty("uploadedContents").GetArrayLength());
        Check.Equal(2, state.GetProperty("items").GetArrayLength());
    });
    private static async Task CreateOnlyIdentityCollisionRefusesBeforeOriginalContentCallback() => await Fixture(async rig =>
    {
        var original = rig.Content("existing.txt");
        Check.True((await rig.Provider.CommitUploadedContentAsync(original, [rig.Parent])).IsSuccess);
        var evidence = await rig.Provider.GetStoreEvidenceAsync(rig.StoreId);
        var proposed = rig.Content("different.txt") with { FileId = original.FileId };
        var registration = rig.Registration(proposed); var copies = 0; var before = await File.ReadAllTextAsync(rig.StatePath);
        var refused = await rig.Provider.CommitOriginalBrowserDownloadAsync(registration, evidence.Revision, rig.Parent, rig.Guard,
            _ => { copies++; return Task.CompletedTask; }, body => body(), _ => { }, CancellationToken.None);
        Check.False(refused.IsSuccess); Check.Equal(0, copies);
        Check.Equal(before, await File.ReadAllTextAsync(rig.StatePath));
        Check.Equal(original.RevisionId, (await rig.Provider.GetForOriginalStoreAsync(rig.StoreId, original.FileId)).Value?.CurrentRevisionId);
        Check.True(await rig.Provider.ReadOriginalBrowserDownloadRegistrationAsync(rig.StoreId, registration.DownloadId,
            registration.ActionId, body => body(), _ => { }, CancellationToken.None) is null);
    });
    private static async Task ActualCopyFaultKeepsOpaqueAndIndependentCausesThroughOriginalDrain() => await Fixture(async rig =>
    {
        var empty = new AggregateException("Foreign direct empty original copy cause.");
        var io = new IOException("Independent actual copy sibling.");
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); failed.SetException([empty, io]);
        var accepted = new List<Task>(); var before = await File.ReadAllTextAsync(rig.StatePath);
        var actual = rig.Provider.CommitOriginalBrowserDownloadAsync(rig.Registration(rig.Content("failed.txt")), rig.StoreRevision,
            rig.Parent, rig.Guard, _ => failed.Task, body => body(), task => accepted.Add(task), CancellationToken.None);
        var actualFailure = await Failure(actual); Check.True(accepted.Any(task => ReferenceEquals(task, failed.Task)));
        var close = rig.Provider.CloseOriginalBrowserDownloadsAndDrainAsync(); var closeFailure = await Failure(close);
        Check.True(Contains(actualFailure, empty)); Check.True(Contains(actualFailure, io));
        Check.True(Contains(closeFailure, empty)); Check.True(Contains(closeFailure, io));
        Check.True(ReferenceEquals(close, rig.Provider.OriginalBrowserDownloadsClose));
        Check.Equal(before, await File.ReadAllTextAsync(rig.StatePath));
        Check.True(Leaves(closeFailure).All(cause => ReferenceEquals(cause, empty) || ReferenceEquals(cause, io)));
        rig.ExpectedFailedClose = close;
    });
    private sealed class Rig(string directory)
    {
        internal readonly string DirectoryPath = directory;
        internal readonly string StatePath = Path.Combine(directory, "files.json");
        internal readonly Guid Profile = Guid.NewGuid();
        internal readonly FilesLocationId Location = new(Guid.NewGuid());
        internal string Actor => "local-profile:" + Profile.ToString("D");
        internal DurableDriveProvider Provider = null!;
        internal Guid StoreId;
        internal string StoreRevision = null!;
        internal FilesItemRevisionPrecondition Parent = null!;
        internal readonly List<DurableDriveProvider> OtherProviders = [];
        internal Task? ExpectedFailedClose;
        internal FilesCommitAuthorityGuard Guard => new(Actor, _ => ValueTask.FromResult(true));
        internal FilesUploadedContent Content(string name)
        {
            var bytes = Encoding.UTF8.GetBytes("actual component content"); var revision = new FilesRevisionId(Guid.NewGuid());
            return new(new(Guid.NewGuid()), Parent.ItemId, name, "text/plain", revision, null, Actor, DateTimeOffset.UtcNow,
                bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), ".9to1-browser-" + revision.Value.ToString("N") + ".content");
        }
        internal FilesBrowserDownloadRegistration Registration(FilesUploadedContent content) => new(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), StoreId, Actor, Profile.ToString("D"), content);
        internal async Task Initialize()
        {
            Provider = new(StatePath, Location, Actor); var now = DateTimeOffset.UtcNow; var folder = HostedItemId.New();
            var operation = new FilesOperation(new(Guid.NewGuid()), Actor, folder, null, null, "CreateFolder", null, null,
                FilesOperationState.Pending, now, now, null, null);
            Check.True((await Provider.MutateAsync(operation, "Registered folder", CancellationToken.None)).IsSuccess);
            var evidence = await Provider.GetStoreEvidenceAsync(); StoreId = evidence.StoreId; StoreRevision = evidence.Revision;
            var row = (await Provider.GetForOriginalStoreAsync(StoreId, folder)).Value!;
            Parent = new(folder, row.CurrentRevisionId);
        }
    }
    private static async Task Fixture(Func<Rig, Task> body)
    {
        var directory = Path.Combine(Path.GetTempPath(), "files-browser-registration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); var rig = new Rig(directory); Exception? primary = null; var errors = new List<Exception>();
        try { await rig.Initialize(); await body(rig); } catch (Exception error) { primary = error; }
        foreach (var provider in new[] { rig.Provider }.Concat(rig.OtherProviders).OfType<DurableDriveProvider>())
        {
            Task? close = null;
            try { close = provider.CloseOriginalBrowserDownloadsAndDrainAsync(); await close; }
            catch (Exception error)
            {
                // Only the exact already independently joined cached failure may
                // be observed again here; another cleanup source remains unknown.
                if (!ReferenceEquals(close, rig.ExpectedFailedClose) || primary is not null) errors.Add(close?.Exception ?? error);
            }
        }
        if (primary is not null) errors.Insert(0, primary);
        if (errors.Count != 0) throw new AggregateException("Actual fixture sources retained; no cleanup after failure.", errors);
        if (rig.ExpectedFailedClose is null) Directory.Delete(directory, recursive: true);
    }
    private static async Task<Exception> Failure(Task actual)
    { try { await actual; } catch (Exception error) { return actual.Exception ?? error; } throw new InvalidOperationException("Expected actual original failure."); }
    private static IEnumerable<Exception> Leaves(Exception actual) => actual is AggregateException { InnerExceptions.Count: > 0 } group
        ? group.InnerExceptions.SelectMany(Leaves) : [actual];
    private static bool Contains(Exception group, Exception cause) => ReferenceEquals(group, cause) ||
        group is AggregateException aggregate && aggregate.InnerExceptions.Any(inner => Contains(inner, cause));
}
