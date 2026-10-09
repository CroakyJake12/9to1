using System.Security.Cryptography;
using System.Text;
using Haven.Application;
using Haven.Infrastructure;
using Xunit;
namespace Haven.Infrastructure.Tests;

public sealed class LinuxProviderSecretStoreTests
{
    private sealed record Paths(string DataDirectory) : IAppPaths
    {
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
    }
    [Fact]
    public async Task Actual_private_store_roundtrips_without_plaintext_and_reopens_then_deletes()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var credentialLifetime = new CancellationTokenSource();
        var root = Directory.CreateTempSubdirectory("haven-credential-control-").FullName;
        LinuxProviderSecretStore? source = null; Exception? primary = null;
        try
        {
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var token = credentialLifetime.Token;
            var paths = new Paths(root); source = new(paths); const string value = "controlled-not-a-real-OAuth-token";
            Assert.True(source.HasOriginalComposition(paths));
            await source.SetAsync("mcp.controlled", "oauth.tokens", value, token);
            Assert.Equal(value, await source.GetAsync("mcp.controlled", "oauth.tokens", token));
            var stored = Directory.GetFiles(Path.Combine(root, "Credentials"), "*.credential");
            Assert.Single(stored);
            Assert.DoesNotContain(value, Encoding.UTF8.GetString(await File.ReadAllBytesAsync(stored[0], token)));
            source.Dispose(); source = new(paths);
            Assert.Equal(value, await source.GetAsync("mcp.controlled", "oauth.tokens", token));
            await source.DeleteAsync("mcp.controlled", "oauth.tokens", token);
            Assert.Null(await source.GetAsync("mcp.controlled", "oauth.tokens", token));
        }
        catch (Exception cause) { primary = cause; }
        finally { Cleanup(source, root, primary); }
    }
    [Fact]
    public async Task Substituted_namespace_or_modified_ciphertext_is_not_a_credential()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var credentialLifetime = new CancellationTokenSource();
        var root = Directory.CreateTempSubdirectory("haven-credential-control-").FullName;
        LinuxProviderSecretStore? source = null; Exception? primary = null;
        try
        {
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var token = credentialLifetime.Token;
            source = new(new Paths(root));
            await source.SetAsync("first", "name", "controlled", token);
            var original = Directory.GetFiles(Path.Combine(root, "Credentials"), "*.credential").Single();
            var target = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("second\0name"))).ToLowerInvariant() + ".credential";
            File.Copy(original, Path.Combine(root, "Credentials", target));
            File.SetUnixFileMode(Path.Combine(root, "Credentials", target), UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var swapped = await Assert.ThrowsAsync<AggregateException>(() => source.GetAsync("second", "name", token));
            Assert.Contains(swapped.Flatten().InnerExceptions, error => error is CryptographicException);
            var bytes = await File.ReadAllBytesAsync(original, token); bytes[^1] ^= 1; await File.WriteAllBytesAsync(original, bytes, token);
            var modified = await Assert.ThrowsAsync<AggregateException>(() => source.GetAsync("first", "name", token));
            Assert.Contains(modified.Flatten().InnerExceptions, error => error is CryptographicException);
        }
        catch (Exception cause) { primary = cause; }
        finally { Cleanup(source, root, primary); }
    }
    [Fact]
    public void Existing_public_or_symlink_data_root_is_not_adopted_or_chmodded()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Directory.CreateTempSubdirectory("haven-credential-control-").FullName;
        var link = root + "-link"; Exception? primary = null;
        try
        {
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherRead);
            var mode = File.GetUnixFileMode(root);
            Assert.Throws<AggregateException>(() => new LinuxProviderSecretStore(new Paths(root)));
            Assert.Equal(mode, File.GetUnixFileMode(root));
            Directory.CreateSymbolicLink(link, root);
            Assert.Throws<AggregateException>(() => new LinuxProviderSecretStore(new Paths(link)));
        }
        catch (Exception cause) { primary = cause; }
        finally
        {
            var errors = new List<Exception>(); if (primary is not null) errors.Add(primary);
            try { if (Directory.Exists(link)) Directory.Delete(link); } catch (Exception cause) { errors.Add(cause); }
            try { Directory.Delete(root, true); } catch (Exception cause) { errors.Add(cause); }
            if (errors.Count != 0) throw new AggregateException("Actual controlled directories failed.", errors);
        }
    }
    private static void Cleanup(LinuxProviderSecretStore? source, string root, Exception? primary)
    {
        var errors = new List<Exception>(); if (primary is not null) errors.Add(primary);
        try { source?.Dispose(); } catch (Exception cause) { errors.Add(cause); }
        try { Directory.Delete(root, true); } catch (Exception cause) { errors.Add(cause); }
        if (errors.Count != 0) throw new AggregateException("Actual controlled credential originals and cleanup failed.", errors);
    }
}
