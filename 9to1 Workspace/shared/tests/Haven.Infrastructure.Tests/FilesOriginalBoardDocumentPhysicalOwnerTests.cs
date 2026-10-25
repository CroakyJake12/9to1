using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using CakeOS.Apps.Boards.Contract;
using Haven.Application;
using Haven.Infrastructure.Native.Linux;
using HavenOS.Home.Core;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Actual canonical rich-board codec, OS profile, directory descriptors
/// and bytes. The configured destination fixture exercises this physical boundary;
/// it is not a Home WRITE approval or a successful Files metadata CAS.</summary>
[SupportedOSPlatform("linux")]
public sealed class FilesOriginalBoardDocumentPhysicalOwnerTests
{
    private sealed class LinuxFactAttribute : FactAttribute
    { public LinuxFactAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Requires actual Linux openat2/statx/create-only publication."; } }

    [LinuxFact]
    public async Task Real_rich_board_bytes_publish_under_new_revision_without_overwriting_the_original()
    {
        await using var rig = await Rig.CreateAsync();
        var materialized = await rig.Write();
        Assert.True(rig.Owner.IsIssuedOriginalBoardDocumentMaterialization(materialized));
        Assert.Same(rig.Destination, materialized.OriginalDestination);
        Assert.Equal(rig.Description.FilesStoreId, materialized.OriginalRecord.FilesStoreId);
        Assert.Equal(rig.Description.FileId, materialized.OriginalRecord.FileId);
        Assert.Equal(rig.Description.ExpectedRevisionId, materialized.OriginalRecord.ExpectedRevisionId);
        Assert.Equal(rig.Description.NewRevisionId, materialized.OriginalRecord.NewRevisionId);
        Assert.Equal(rig.Payload, File.ReadAllBytes(rig.NewPath));
        Assert.Equal(rig.OldBytes, File.ReadAllBytes(rig.OldPath));
        var reopened = HavenBoardDocumentCodec.Deserialize(File.ReadAllBytes(rig.NewPath));
        Assert.Equal(rig.Description.DocumentId, reopened.DocumentId);
        Assert.Equal("Edited rich board", reopened.RichNotes!.Title);
        Assert.True(reopened.RichNotes.Sections[0].Pages[0].Blocks[0].Runs[0].Bold);
        await rig.Owner.RevalidateOriginalBoardDocumentWithinSourceAsync(materialized, Run, rig.Retain, rig.Token);
        rig.Owner.DemandOriginalBoardDocumentCommit(materialized);
        var close = materialized.CloseAndDrainAsync(); Assert.Same(close, materialized.CloseAndDrainAsync()); await close;
        Assert.Equal(rig.Payload, File.ReadAllBytes(rig.NewPath)); // disposal cannot guess a metadata outcome
        Assert.Equal(rig.OldBytes, File.ReadAllBytes(rig.OldPath));
    }

    [LinuxFact]
    public async Task Caller_buffer_mutation_after_admission_cannot_change_the_reviewed_payload()
    {
        await using var rig = await Rig.CreateAsync();
        var original = rig.Payload.ToArray();
        void Retain(Task task) { rig.Retain(task); Array.Fill(rig.Payload, (byte)0); }
        var materialized = await rig.Owner.MaterializeOriginalBoardDocumentWithinSourceAsync(rig.Destination, rig.Payload, Run, Retain, rig.Token);
        Assert.Equal(original, File.ReadAllBytes(rig.NewPath));
        Assert.Equal(Digest(original), materialized.Sha256);
    }

    [LinuxFact]
    public async Task Collision_preserves_existing_final_leaf_and_old_content_without_adoption()
    {
        await using var rig = await Rig.CreateAsync(); rig.ExpectFault = true;
        var foreign = new byte[] { 4, 3, 2, 1 }; File.WriteAllBytes(rig.NewPath, foreign);
        await Assert.ThrowsAnyAsync<IOException>(() => rig.Write());
        Assert.Equal(foreign, File.ReadAllBytes(rig.NewPath));
        Assert.Equal(rig.OldBytes, File.ReadAllBytes(rig.OldPath));
        Assert.NotEmpty(Directory.EnumerateFiles(rig.Anchor, "*.tmp"));
    }

    [LinuxFact]
    public async Task Revoked_source_after_materialization_refuses_metadata_commit_and_retains_new_bytes()
    {
        await using var rig = await Rig.CreateAsync();
        var materialized = await rig.Write();
        rig.ClaimCurrent = false;
        Assert.Throws<UnauthorizedAccessException>(() => rig.Owner.DemandOriginalBoardDocumentCommit(materialized));
        await materialized.DisposeAsync();
        Assert.Equal(rig.Payload, File.ReadAllBytes(rig.NewPath));
        Assert.Equal(rig.OldBytes, File.ReadAllBytes(rig.OldPath));
    }

    [LinuxFact]
    public async Task Revocation_after_actual_staged_write_prevents_publication_and_preserves_recovery_stage()
    {
        await using var rig = await Rig.CreateAsync(); rig.ExpectFault = true;
        rig.BeforeDemand = () =>
        {
            var stage = Directory.EnumerateFiles(rig.Anchor, "*.tmp").SingleOrDefault();
            if (stage is not null && new FileInfo(stage).Length > 0) rig.ClaimCurrent = false;
        };
        await Assert.ThrowsAnyAsync<Exception>(() => rig.Write());
        Assert.False(File.Exists(rig.NewPath));
        Assert.Equal(rig.OldBytes, File.ReadAllBytes(rig.OldPath));
        Assert.NotEmpty(Directory.EnumerateFiles(rig.Anchor, "*.tmp"));
    }

    [LinuxFact]
    public async Task Actual_published_byte_mutation_is_not_a_current_materialization_receipt()
    {
        await using var rig = await Rig.CreateAsync(); rig.ExpectFault = true;
        var materialized = await rig.Write();
        var changed = File.ReadAllBytes(rig.NewPath); changed[^1] ^= 1; File.WriteAllBytes(rig.NewPath, changed);
        Assert.ThrowsAny<IOException>(() => rig.Owner.DemandOriginalBoardDocumentCommit(materialized));
        await Assert.ThrowsAnyAsync<Exception>(() => rig.Owner.RevalidateOriginalBoardDocumentWithinSourceAsync(materialized, Run, rig.Retain, rig.Token));
        Assert.Equal(rig.OldBytes, File.ReadAllBytes(rig.OldPath));
    }

    [LinuxFact]
    public async Task Changed_actor_parent_or_registered_anchor_description_is_refused_before_commit()
    {
        await using var rig = await Rig.CreateAsync();
        var materialized = await rig.Write();
        rig.Description = rig.Description with { ParentFolderId = Guid.NewGuid() };
        Assert.Throws<UnauthorizedAccessException>(() => rig.Owner.DemandOriginalBoardDocumentCommit(materialized));
        Assert.Equal(rig.Payload, File.ReadAllBytes(rig.NewPath));
        Assert.Equal(rig.OldBytes, File.ReadAllBytes(rig.OldPath));
    }

    [LinuxFact]
    public async Task Symlink_registered_anchor_is_not_followed_or_created_from_file_identifiers()
    {
        await using var rig = await Rig.CreateAsync(); rig.ExpectFault = true;
        var linked = Path.Combine(rig.Root, "linked-anchor"); Directory.CreateSymbolicLink(linked, rig.Anchor);
        rig.Description = rig.Description with { DirectoryPath = linked };
        await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() => rig.Write());
        Assert.False(File.Exists(rig.NewPath));
        Assert.Equal(rig.OldBytes, File.ReadAllBytes(rig.OldPath));
    }

    [LinuxFact]
    public async Task Invalid_board_or_document_identity_cannot_publish_new_bytes()
    {
        await using var rig = await Rig.CreateAsync();
        Assert.Throws<InvalidDataException>(() => { _ = rig.Owner.MaterializeOriginalBoardDocumentWithinSourceAsync(
            rig.Destination, new byte[] { 1, 2, 3 }, Run, rig.Retain, rig.Token); });
        Assert.False(File.Exists(rig.NewPath));
        rig.Description = rig.Description with { DocumentId = Guid.NewGuid() }; rig.ExpectFault = true;
        await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() => rig.Write());
        Assert.False(File.Exists(rig.NewPath));
        Assert.Empty(Directory.EnumerateFiles(rig.Anchor, "*.tmp"));
    }

    [LinuxFact]
    public async Task Foreign_materialization_and_issuer_rebinding_never_gain_commit_authority()
    {
        await using var rig = await Rig.CreateAsync();
        var materialized = await rig.Write();
        await using var foreign = new FilesOriginalBoardDocumentPhysicalOwner();
        Assert.False(foreign.IsIssuedOriginalBoardDocumentMaterialization(materialized));
        Assert.Throws<UnauthorizedAccessException>(() => foreign.DemandOriginalBoardDocumentCommit(materialized));
        Assert.Throws<UnauthorizedAccessException>(() => rig.Owner.BindOriginalBoardDocumentWriteSource(new ForeignSource()));
        var close = rig.Owner.CloseAndDrainAsync(); Assert.Same(close, rig.Owner.CloseAndDrainAsync()); await close;
        Assert.Throws<ObjectDisposedException>(() => rig.Owner.DemandOriginalBoardDocumentCommit(materialized));
        Assert.Equal(rig.Payload, File.ReadAllBytes(rig.NewPath));
    }

    [LinuxFact]
    public async Task Original_callbacks_cannot_join_their_own_materialization_owner_close()
    {
        await using var rig = await Rig.CreateAsync();
        var calls = 0;
        void Scope(Action body)
        {
            body(); calls++;
            Assert.Throws<InvalidOperationException>(() => rig.Owner.CloseAndDrainAsync());
        }
        await rig.Owner.MaterializeOriginalBoardDocumentWithinSourceAsync(rig.Destination, rig.Payload, Scope, rig.Retain, rig.Token);
        Assert.True(calls > 0); Assert.Null(rig.Owner.OriginalClose);
    }

    private static void Run(Action body) => body();
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private sealed class Destination : IFilesOriginalBoardDocumentWriteDestination { }
    private sealed class ForeignSource : IFilesOriginalBoardDocumentWriteSource
    {
        public bool IsIssuedOriginalBoardDocumentWriteDestination(IFilesOriginalBoardDocumentWriteDestination destination) => false;
        public FilesOriginalBoardDocumentWriteDescription GetOriginalBoardDocumentWriteDescription(IFilesOriginalBoardDocumentWriteDestination destination)
            => throw new UnauthorizedAccessException("No registered original Files anchor.");
        public void DemandOriginalFilesBoardDocumentCommit(IFilesOriginalBoardDocumentWriteDestination destination)
            => throw new UnauthorizedAccessException("No original Files/Home claim.");
    }
    private sealed class Rig : IFilesOriginalBoardDocumentWriteSource, IAsyncDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("files-board-bytes-").FullName;
        internal string Anchor => Path.Combine(Root, "registered-anchor");
        internal string OldPath => Path.Combine(Anchor, "existing.9to1board");
        internal string NewPath => Path.Combine(Anchor, Description.RelativeContentReference);
        internal FilesOriginalBoardDocumentPhysicalOwner Owner = new();
        internal Destination Destination = new();
        internal FilesOriginalBoardDocumentWriteDescription Description = null!;
        internal byte[] Payload = [], OldBytes = [];
        internal bool ClaimCurrent = true, ExpectFault;
        internal Action? BeforeDemand;
        private readonly List<Task> _retained = [];
        private readonly CancellationTokenSource _deadline = new(TimeSpan.FromSeconds(20));
        internal CancellationToken Token => _deadline.Token;
        internal static async Task<Rig> CreateAsync()
        {
            var rig = new Rig();
            try
            {
                Directory.CreateDirectory(rig.Anchor, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                var profiles = new HomeLocalProfileIdentity(new FileHomeCoreStateStore(Path.Combine(rig.Root, "profile/state.json")), new OperatingSystemPrincipalSource());
                var actor = await profiles.GetCurrentAsync(rig.Token) ?? throw new InvalidOperationException("The actual OS profile is unavailable.");
                var documentId = Guid.NewGuid(); var old = Board(documentId, "Original rich board");
                rig.OldBytes = JsonSerializer.SerializeToUtf8Bytes(old, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                File.WriteAllBytes(rig.OldPath, rig.OldBytes);
                var current = Board(documentId, "Edited rich board");
                rig.Payload = JsonSerializer.SerializeToUtf8Bytes(current, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                var next = Guid.NewGuid();
                rig.Description = new(rig.Anchor, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), next,
                    documentId, ".9to1-board-" + next.ToString("N") + ".content", rig.Payload.Length, Digest(rig.Payload), actor);
                rig.Owner.BindOriginalBoardDocumentWriteSource(rig);
                return rig;
            }
            catch { await rig.DisposeAsync(); throw; }
        }
        private static HavenBoardDocument Board(Guid id, string title)
        {
            var rich = HavenRichNotes.Create(title);
            rich.Sections[0].Pages[0].Blocks[0].PlainText = "Formatting survives canonical reopen";
            rich.Sections[0].Pages[0].Blocks[0].Runs = [new() { Text = "Formatting survives canonical reopen", Bold = true }];
            return new(HavenBoardDocument.FormatIdentity, HavenBoardDocument.CurrentSchemaVersion, id,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, HavenBoardSnapshot.CreateDefault(), rich);
        }
        internal Task<IFilesOriginalBoardDocumentMaterialization> Write() =>
            Owner.MaterializeOriginalBoardDocumentWithinSourceAsync(Destination, Payload, Run, Retain, Token);
        internal void Retain(Task task) { lock (_retained) _retained.Add(task); }
        public bool IsIssuedOriginalBoardDocumentWriteDestination(IFilesOriginalBoardDocumentWriteDestination original) => ReferenceEquals(original, Destination);
        public FilesOriginalBoardDocumentWriteDescription GetOriginalBoardDocumentWriteDescription(IFilesOriginalBoardDocumentWriteDestination original)
            => ReferenceEquals(original, Destination) ? Description : throw new UnauthorizedAccessException("Foreign original file selection.");
        public void DemandOriginalFilesBoardDocumentCommit(IFilesOriginalBoardDocumentWriteDestination original)
        {
            BeforeDemand?.Invoke();
            if (!ReferenceEquals(original, Destination) || !ClaimCurrent) throw new UnauthorizedAccessException("Original Files/Home fixture fence retired.");
        }
        public async ValueTask DisposeAsync()
        {
            var close = Owner.CloseAndDrainAsync();
            if (ExpectFault) await Assert.ThrowsAnyAsync<Exception>(() => close); else await close;
            Task[] originals; lock (_retained) originals = _retained.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
            foreach (var task in originals)
                try { await task; } catch when (ExpectFault) { }
            _deadline.Dispose(); Directory.Delete(Root, recursive: true);
        }
    }
}
