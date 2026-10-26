using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class HuggingFaceModelServiceTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Token = "hf_FixtureToken12345";
    private static readonly byte[] Gguf = [71, 71, 85, 70, 3, 0, 0, 0, 1, 2, 3, 4];
    private static readonly HuggingFaceModelFile File = new("weights/model-Q4_K_M.gguf", Gguf.Length, Convert.ToHexStringLower(SHA256.HashData(Gguf)));
    private static readonly HuggingFaceModelDetails Details = new("owner/model-GGUF", Commit, "apache-2.0", false, [File]);

    [Fact]
    public async Task Public_search_never_reads_or_sends_stored_token_and_encodes_query()
    {
        using var fixture = new Fixture();
        fixture.Secrets.FailReads = true;
        fixture.Handler.Send = (request, _) =>
        {
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("huggingface.co", request.RequestUri!.Host);
            Assert.Contains("limit=40", request.RequestUri.Query);
            Assert.Contains("search=a%26private%3Dtrue", request.RequestUri.Query);
            return Reply("[{\"id\":\"owner/model\",\"downloads\":12,\"likes\":3,\"gated\":\"auto\"}]");
        };
        var result = await fixture.Service.SearchAsync("profile-a", "a&private=true", CancellationToken.None);
        Assert.Equal(new HuggingFaceModelSummary("owner/model", 12, 3, true), Assert.Single(result));
    }

    [Fact]
    public async Task Manual_CDN_redirect_strips_auth_and_import_receives_only_pinned_public_provenance()
    {
        using var fixture = new Fixture();
        await fixture.Service.SaveTokenAsync("profile-a", Token, CancellationToken.None);
        var hosts = new List<string>();
        fixture.Handler.Send = (request, _) =>
        {
            hosts.Add(request.RequestUri!.Host);
            if (request.RequestUri.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal)) return Reply(Metadata());
            if (request.RequestUri.Host == "huggingface.co")
            {
                Assert.Equal(Token, request.Headers.Authorization!.Parameter);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://cas-bridge.xethub.hf.co/object?signed=fixture") } });
            }
            Assert.Null(request.Headers.Authorization);
            return Bytes(Gguf);
        };
        var result = await fixture.Service.InstallAsync("profile-a", Details, File, null, CancellationToken.None);
        Assert.False(result.RuntimeObserved);
        Assert.Equal(1, fixture.Importer.Count);
        Assert.Contains(Commit, fixture.Importer.Last!.Source);
        Assert.DoesNotContain(Token, JsonSerializer.Serialize(fixture.Importer.Last));
        Assert.DoesNotContain("signed", fixture.Importer.Last.Source);
        Assert.Equal(["huggingface.co", "huggingface.co", "cas-bridge.xethub.hf.co"], hosts);
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(fixture.Root, "HuggingFaceStaging")));
    }

    [Theory]
    [InlineData("http://cdn-lfs.huggingface.co/file")]
    [InlineData("https://huggingface.co.evil.invalid/file?token=secret")]
    [InlineData("https://evil.invalid/file")]
    [InlineData("https://huggingface.co:444/file")]
    [InlineData("https://user@huggingface.co/file")]
    [InlineData("file:///tmp/model.gguf")]
    public async Task Unapproved_redirect_is_refused_before_a_second_request(string location)
    {
        using var fixture = new Fixture(); var calls = 0;
        fixture.Handler.Send = (_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri(location) } });
        };
        var error = await Assert.ThrowsAsync<HuggingFaceModelException>(() => fixture.Service.GetDetailsAsync("profile-a", "owner/model", CancellationToken.None));
        Assert.Equal(HuggingFaceModelError.DownloadRejected, error.Code);
        Assert.Equal(1, calls);
        Assert.DoesNotContain(location, error.ToString());
    }

    [Theory]
    [InlineData(401, HuggingFaceModelError.AuthenticationRequired)]
    [InlineData(403, HuggingFaceModelError.AccessDenied)]
    public async Task Gated_errors_are_actionable_and_do_not_echo_remote_bodies(int status, HuggingFaceModelError expected)
    {
        using var fixture = new Fixture();
        fixture.Handler.Send = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(Token) });
        var error = await Assert.ThrowsAsync<HuggingFaceModelException>(() => fixture.Service.GetDetailsAsync("profile-a", "owner/model", CancellationToken.None));
        Assert.Equal(expected, error.Code); Assert.DoesNotContain(Token, error.ToString()); Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("truncated")]
    [InlineData("oversized")]
    [InlineData("magic")]
    [InlineData("version")]
    public async Task Invalid_download_never_reaches_import_and_private_staging_is_removed(string fault)
    {
        using var fixture = new Fixture(); var bytes = Gguf.ToArray(); var pin = File;
        if (fault == "hash") bytes[^1] ^= 1;
        if (fault == "truncated") bytes = bytes[..^1];
        if (fault == "oversized") bytes = [.. bytes, 1];
        if (fault is "magic" or "version")
        {
            bytes[fault == "magic" ? 0 : 4] = 9;
            pin = pin with { Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) };
        }
        fixture.Handler.Send = (request, _) => request.RequestUri!.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal)
            ? Reply(Metadata(pin)) : Bytes(bytes);
        var error = await Assert.ThrowsAsync<HuggingFaceModelException>(() => fixture.Service.InstallAsync("profile-a", Details, pin, null, CancellationToken.None));
        Assert.Equal(HuggingFaceModelError.IntegrityFailure, error.Code); Assert.Equal(0, fixture.Importer.Count);
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(fixture.Root, "HuggingFaceStaging")));
    }

    [Fact]
    public async Task Cancellation_removes_staging_and_never_invokes_import()
    {
        using var fixture = new Fixture(); using var cancel = new CancellationTokenSource();
        var progress = new ImmediateProgress(p => { if (p.Phase == HuggingFaceInstallPhase.Downloading) cancel.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.InstallAsync("profile-a", Details, File, progress, cancel.Token));
        Assert.Equal(0, fixture.Importer.Count);
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(fixture.Root, "HuggingFaceStaging")));
    }

    [Fact]
    public async Task Pin_substitution_and_traversal_are_rejected()
    {
        using var fixture = new Fixture();
        var pin = File with { Sha256 = new string('b', 64) };
        var error = await Assert.ThrowsAsync<HuggingFaceModelException>(() => fixture.Service.InstallAsync("profile-a", Details, pin, null, CancellationToken.None));
        Assert.Equal(HuggingFaceModelError.InvalidMetadata, error.Code);
        Assert.Throws<HuggingFaceModelException>(() => { _ = fixture.Service.InstallAsync("profile-a", Details, File with { Path = "../model.gguf" }, null, CancellationToken.None); });
        Assert.Equal(0, fixture.Importer.Count);
    }

    [Fact]
    public async Task Credential_identity_is_profile_scoped_and_failures_have_no_plaintext_fallback()
    {
        using var fixture = new Fixture();
        await fixture.Service.SaveTokenAsync("profile-a", Token, CancellationToken.None);
        Assert.True(await fixture.Service.HasTokenAsync("profile-a", CancellationToken.None));
        Assert.False(await fixture.Service.HasTokenAsync("profile-b", CancellationToken.None));
        Assert.DoesNotContain("profile-a", Assert.Single(fixture.Secrets.Values).Key);
        await fixture.Service.RemoveTokenAsync("profile-a", CancellationToken.None);
        Assert.False(await fixture.Service.HasTokenAsync("profile-a", CancellationToken.None));
        fixture.Secrets.FailWrites = true;
        var error = await Assert.ThrowsAsync<HuggingFaceModelException>(() => fixture.Service.SaveTokenAsync("profile-a", Token, CancellationToken.None));
        Assert.Equal(HuggingFaceModelError.SecureStorageUnavailable, error.Code);
        Assert.DoesNotContain(Token, error.ToString()); Assert.Empty(Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories));
        Assert.Throws<HuggingFaceModelException>(() => HuggingFaceModelService.Create(fixture.Secrets, new TestPaths(fixture.Root)));
    }

    [Fact]
    public async Task Actual_Linux_secure_store_keeps_HF_tokens_encrypted_across_reopen()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new Fixture(); var paths = new TestPaths(fixture.Root);
        using (var store = new LinuxProviderSecretStore(paths))
        using (var service = new HuggingFaceModelService(fixture.Http, store, paths, fixture.Importer))
        {
            await service.SaveTokenAsync("profile-a", Token, CancellationToken.None);
            Assert.True(await service.HasTokenAsync("profile-a", CancellationToken.None));
            Assert.False(await service.HasTokenAsync("profile-b", CancellationToken.None));
        }
        foreach (var path in Directory.GetFiles(Path.Combine(fixture.Root, "Credentials")))
            Assert.DoesNotContain(Token, Encoding.UTF8.GetString(await System.IO.File.ReadAllBytesAsync(path)));
        using var reopened = new LinuxProviderSecretStore(paths);
        using var browser = new HuggingFaceModelService(fixture.Http, reopened, paths, fixture.Importer);
        Assert.True(await browser.HasTokenAsync("profile-a", CancellationToken.None));
        await browser.RemoveTokenAsync("profile-a", CancellationToken.None);
        Assert.False(await browser.HasTokenAsync("profile-a", CancellationToken.None));
    }

    private static string Metadata(HuggingFaceModelFile? file = null)
    {
        var selected = file ?? File;
        return JsonSerializer.Serialize(new { id = Details.RepositoryId, sha = Commit, gated = false,
            cardData = new { license = "apache-2.0" }, siblings = new[] { new { rfilename = selected.Path, lfs = new { size = selected.Size, sha256 = selected.Sha256 } } } });
    }
    private static Task<HttpResponseMessage> Reply(string value) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(value) });
    private static Task<HttpResponseMessage> Bytes(byte[] value) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(value) });
    private sealed class Handler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Send { get; set; } = (request, _) =>
            request.RequestUri!.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal) ? Reply(Metadata()) : Bytes(Gguf);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Send(request, cancellationToken);
    }
    private sealed class Secrets : IProviderSecretStore
    {
        public Dictionary<string, string> Values { get; } = new();
        public bool FailReads { get; set; }
        public bool FailWrites { get; set; }
        public Task SetAsync(string providerId, string secretName, string secret, CancellationToken cancellationToken)
        { if (FailWrites) throw new IOException(secret); Values[providerId + ":" + secretName] = secret; return Task.CompletedTask; }
        public Task<string?> GetAsync(string providerId, string secretName, CancellationToken cancellationToken)
        { if (FailReads) throw new IOException(Token); return Task.FromResult(Values.GetValueOrDefault(providerId + ":" + secretName)); }
        public Task DeleteAsync(string providerId, string secretName, CancellationToken cancellationToken)
        { Values.Remove(providerId + ":" + secretName); return Task.CompletedTask; }
    }
    private sealed class Importer : IHuggingFaceModelImporter
    {
        public int Count { get; private set; }
        public HuggingFaceModelImport? Last { get; private set; }
        public Task ImportAsync(HuggingFaceModelImport request, CancellationToken cancellationToken)
        { Count++; Last = request; return Task.CompletedTask; }
    }
    private sealed class ImmediateProgress(Action<HuggingFaceInstallProgress> report) : IProgress<HuggingFaceInstallProgress>
    { public void Report(HuggingFaceInstallProgress value) => report(value); }
    private sealed record TestPaths(string DataDirectory) : IAppPaths
    {
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "Browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("haven-hf-tests-").FullName;
        public Handler Handler { get; } = new();
        public Secrets Secrets { get; } = new();
        public Importer Importer { get; } = new();
        public HttpClient Http { get; }
        public HuggingFaceModelService Service { get; }
        public Fixture()
        {
            if (!OperatingSystem.IsWindows()) System.IO.File.SetUnixFileMode(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Http = new(Handler); Service = new(Http, Secrets, new TestPaths(Root), Importer);
        }
        public void Dispose() { Service.Dispose(); Http.Dispose(); Directory.Delete(Root, recursive: true); }
    }
}
