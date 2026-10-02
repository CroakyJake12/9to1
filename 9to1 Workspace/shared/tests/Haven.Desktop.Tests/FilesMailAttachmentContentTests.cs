using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using HavenOS.Mail;
using HavenOS.Mail.Providers;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace Haven.Desktop.Tests;

public sealed class FilesMailAttachmentContentTests
{
    [Fact]
    public async Task Genuine_original_Files_attachment_returns_exact_detached_bytes_and_denies_foreign_draft_and_issuer_without_writes()
    {
        using var f = await Fixture.CreateAsync(); var ct = TestContext.Current.CancellationToken;
        using var source = await FilesMailAttachmentContentSource.CaptureAsync(f.Workspace, f.Owner, f.Resources, f.Draft, () => true, ct);
        var home = await File.ReadAllBytesAsync(f.HomeFile, ct); var drive = await File.ReadAllBytesAsync(f.DriveFile, ct);
        var content = await source.ReadAsync(f.Draft.AccountId, f.Draft.DraftId, f.File.Value, ct);
        Assert.Equal(f.Bytes, content.Bytes.ToArray()); Assert.Equal("attachment.txt", content.FileName); Assert.Equal("text/plain", content.MimeType);
        var detached = content.Bytes.ToArray(); detached[0] ^= 1;
        Assert.Equal(f.Bytes, (await source.ReadAsync(f.Draft.AccountId, f.Draft.DraftId, f.File.Value, ct)).Bytes.ToArray());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await source.ReadAsync(Guid.NewGuid(), f.Draft.DraftId, f.File.Value, ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await source.ReadAsync(f.Draft.AccountId, Guid.NewGuid(), f.File.Value, ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await source.ReadAsync(f.Draft.AccountId, f.Draft.DraftId, Guid.NewGuid(), ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => FilesMailAttachmentContentSource.CaptureAsync(f.Workspace,
            new FilesArtifactResourceResolver(f.Authority), f.Resources, f.Draft, () => true, ct));
        Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile, ct)); Assert.Equal(drive, await File.ReadAllBytesAsync(f.DriveFile, ct));
    }

    [Fact]
    public async Task Actual_materialized_byte_tamper_is_not_returned_and_does_not_rewrite_canonical_metadata()
    {
        using var f = await Fixture.CreateAsync(); var ct = TestContext.Current.CancellationToken;
        using var source = await FilesMailAttachmentContentSource.CaptureAsync(f.Workspace, f.Owner, f.Resources, f.Draft, () => true, ct);
        var home = await File.ReadAllBytesAsync(f.HomeFile, ct); var drive = await File.ReadAllBytesAsync(f.DriveFile, ct);
        var wrong = f.Bytes.ToArray(); wrong[0] ^= 1; await File.WriteAllBytesAsync(f.LocalFile, wrong, ct);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await source.ReadAsync(f.Draft.AccountId, f.Draft.DraftId, f.File.Value, ct));
        Assert.Equal(wrong, await File.ReadAllBytesAsync(f.LocalFile, ct));
        Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile, ct)); Assert.Equal(drive, await File.ReadAllBytesAsync(f.DriveFile, ct));
    }

    [Fact]
    public async Task Actual_store_UUID_replacement_cannot_disclose_original_attachment_and_observed_retirement_cannot_resurrect()
    {
        using var f = await Fixture.CreateAsync(); var ct = TestContext.Current.CancellationToken;
        using var source = await FilesMailAttachmentContentSource.CaptureAsync(f.Workspace, f.Owner, f.Resources, f.Draft, () => true, ct);
        var home = await File.ReadAllBytesAsync(f.HomeFile, ct); var original = await File.ReadAllBytesAsync(f.DriveFile, ct);
        var state = JsonNode.Parse(original)!.AsObject(); state["state"]!["storeId"] = Guid.NewGuid().ToString("D");
        await File.WriteAllTextAsync(f.DriveFile, state.ToJsonString(), ct);
        var replacement = await File.ReadAllBytesAsync(f.DriveFile, ct);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await source.ReadAsync(f.Draft.AccountId, f.Draft.DraftId, f.File.Value, ct));
        Assert.Equal(replacement, await File.ReadAllBytesAsync(f.DriveFile, ct)); Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile, ct));
        await File.WriteAllBytesAsync(f.DriveFile, original, ct);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await source.ReadAsync(f.Draft.AccountId, f.Draft.DraftId, f.File.Value, ct));
    }

    [Fact]
    public async Task Disposed_original_attachment_source_cannot_be_reused_for_submission_byte_reads()
    {
        using var f = await Fixture.CreateAsync(); var ct = TestContext.Current.CancellationToken;
        using var source = await FilesMailAttachmentContentSource.CaptureAsync(f.Workspace, f.Owner, f.Resources, f.Draft, () => true, ct);
        var home = await File.ReadAllBytesAsync(f.HomeFile, ct); var drive = await File.ReadAllBytesAsync(f.DriveFile, ct);
        source.Dispose();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await source.ReadAsync(f.Draft.AccountId, f.Draft.DraftId, f.File.Value, ct));
        Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile, ct)); Assert.Equal(drive, await File.ReadAllBytesAsync(f.DriveFile, ct));
    }

    [Fact]
    public async Task Actual_original_Files_attachment_is_delivered_as_exact_MIME_bytes_by_real_TLS_SMTP_and_IMAP()
    {
        using var f = await Fixture.CreateAsync();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(90)); var ct = deadline.Token;
        static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value : throw new InvalidOperationException("Real attachment transport fixture missing: " + name);
        var host = Required("HAVEN_MAIL_FIXTURE_HOST");
        Assert.True(host is "localhost" or "127.0.0.1" or "::1", "The real transport fixture must be isolated on loopback.");
        var address = Required("HAVEN_MAIL_FIXTURE_ADDRESS"); var password = Required("HAVEN_MAIL_FIXTURE_PASSWORD");
        var imapPort = int.Parse(Required("HAVEN_MAIL_FIXTURE_IMAP_TLS_PORT"), System.Globalization.CultureInfo.InvariantCulture);
        var smtpPort = int.Parse(Required("HAVEN_MAIL_FIXTURE_SMTP_TLS_PORT"), System.Globalization.CultureInfo.InvariantCulture);
        var identity = new MailFromIdentity(Guid.NewGuid(), address, "Canonical attachment fixture", null, null, null);
        var account = new MailAccount(f.Draft.AccountId, MailProviderKind.ImapSmtp, "isolated-attachments", address, "Fixture",
            MailCapability.Attachments, "fixture-local-mail", new(new(host, imapPort, MailTransportSecurity.Tls), new(host, smtpPort, MailTransportSecurity.Tls), address, "fixture-local-mail"),
            new(true, true, 1, false, [], TimeSpan.FromSeconds(1)), new(TimeSpan.Zero, true), [identity], MailSyncState.Never, null, null, null, true);
        var draft = f.Draft with { FromIdentityId = identity.Id, To = [new(address)], Subject = "Canonical attachment " + Guid.NewGuid().ToString("N") };
        using var source = await FilesMailAttachmentContentSource.CaptureAsync(f.Workspace, f.Owner, f.Resources, draft, () => true, ct);
        var beforeHome = await File.ReadAllBytesAsync(f.HomeFile, ct); var beforeDrive = await File.ReadAllBytesAsync(f.DriveFile, ct);
        var provider = new MailKitImapSmtpProvider(new TransportCredentials(password), source);
        Assert.True(provider.Capabilities.HasFlag(MailCapability.Attachments));
        var accepted = await provider.SendAsync(account, draft, ct); Assert.False(string.IsNullOrWhiteSpace(accepted.ProviderMessageId));
        using var receiver = new ImapClient();
        try
        {
            await receiver.ConnectAsync(host, imapPort, SecureSocketOptions.SslOnConnect, ct);
            await receiver.AuthenticateAsync(address, password, ct);
            await receiver.Inbox.OpenAsync(FolderAccess.ReadOnly, ct);
            MimeMessage? received = null;
            while (received is null)
            {
                var ids = await receiver.Inbox.SearchAsync(SearchQuery.SubjectContains(draft.Subject), ct);
                foreach (var id in ids)
                {
                    var candidate = await receiver.Inbox.GetMessageAsync(id, ct);
                    if (candidate.Subject == draft.Subject) { received = candidate; break; }
                }
                if (received is null) await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
            }
            var part = Assert.IsAssignableFrom<MimePart>(Assert.Single(received.Attachments));
            Assert.Equal("attachment.txt", part.FileName); Assert.Equal("text/plain", part.ContentType.MimeType);
            using var actual = new MemoryStream(); await part.Content.DecodeToAsync(actual, ct);
            Assert.Equal(f.Bytes, actual.ToArray()); Assert.Contains(draft.PlainBody, received.TextBody);
            Assert.Equal(address, Assert.Single(received.From.Mailboxes).Address);
            Assert.Equal(beforeHome, await File.ReadAllBytesAsync(f.HomeFile, ct)); Assert.Equal(beforeDrive, await File.ReadAllBytesAsync(f.DriveFile, ct));
        }
        finally
        {
            if (receiver.IsConnected) try { await receiver.DisconnectAsync(true, CancellationToken.None); } catch { }
        }
    }

    private sealed class TransportCredentials(string password) : IMailCredentialResolver
    {
        public ValueTask<MailProviderCredential> ResolveAsync(string reference, CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); if (reference != "fixture-local-mail") throw new UnauthorizedAccessException(); return ValueTask.FromResult(new MailProviderCredential(password, false)); }
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "astra-real-mail-files-" + Guid.NewGuid().ToString("N"));
        public string HomeFile => Path.Combine(Root, "home.json");
        public string DriveFile => Path.Combine(Workspace.Configuration.RootDirectory, ".9to1-files", "drive.json");
        public string LocalFile => Path.Combine(Workspace.Configuration.RootDirectory, "attachment.txt");
        public byte[] Bytes { get; } = Encoding.UTF8.GetBytes("Canonical Files attachment — exact original bytes.\n");
        public NativeFilesWorkspace Workspace { get; private set; } = null!;
        public NativeFilesWorkspaceAuthority Authority { get; private set; } = null!;
        public FilesArtifactResourceResolver Owner { get; private set; } = null!;
        public ResourceAuthorizationService Resources { get; private set; } = null!;
        public HostedItemId File { get; } = HostedItemId.New();
        public MailDraft Draft { get; private set; } = null!;
        public static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture(); Directory.CreateDirectory(f.Root);
            try
            {
                var ct = TestContext.Current.CancellationToken;
                var home = new FileHomeCoreStateStore(f.HomeFile); var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
                var files = new NativeFilesWorkspaceService(home, profiles);
                var ownership = new HomeLocalStoreOwnership(home, profiles, new HomeLocalStoreEvidenceRegistry([files]), new HomePermissionTrustService(home, (_, _) => null));
                var chosen = Path.Combine(f.Root, "chosen"); Directory.CreateDirectory(chosen);
                var configured = await files.ConfigureNewAsync(chosen, ownership, ct);
                f.Authority = new(files, profiles, new HomeResourceStoreOwnershipAuthority(ownership, profiles));
                f.Workspace = await f.Authority.GetCurrentAsync(configured.Configuration.StoreId, ct) ?? throw new InvalidOperationException("Actual original Files workspace required.");
                f.Owner = new(f.Authority); f.Resources = new(profiles, [f.Owner]);
                await System.IO.File.WriteAllBytesAsync(f.LocalFile, f.Bytes, ct);
                var revision = new FilesRevisionId(Guid.NewGuid()); var hash = "sha256:" + Convert.ToHexString(SHA256.HashData(f.Bytes)).ToLowerInvariant();
                var committed = await f.Workspace.Provider.CommitUploadedContentAsync(new(f.File, null, "attachment.txt", "text/plain", revision, null,
                    "local-profile:" + f.Workspace.Actor.ProfileId, DateTimeOffset.UtcNow, f.Bytes.Length, hash, "attachment.txt"), ct);
                Assert.True(committed.IsSuccess);
                await f.Workspace.Materializations.RegisterValidatedAsync(f.LocalFile, new(f.File, revision, hash, f.Bytes.Length, DateTimeOffset.UtcNow), SyncAvailability.AvailableOffline, ct);
                f.Draft = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [new("fixture@example.test")], [], [], "Attachment fixture", null,
                    "body", [f.File.Value], null, 1, DateTimeOffset.UtcNow, null, false);
                return f;
            }
            catch { f.Dispose(); throw; }
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
