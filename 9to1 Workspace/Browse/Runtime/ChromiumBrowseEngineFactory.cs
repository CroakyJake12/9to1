using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HavenOS.Apps.Browse.Runtime;

/// <summary>Approved executable identity; source acquisition alone cannot satisfy this runtime contract.</summary>
public sealed record ChromiumRuntimeOptions(string ExecutablePath, string SourceCommit, string ExecutableSha256,
    bool Headless = false, TimeSpan? StartupTimeout = null);

public sealed record ChromiumRuntimeIdentity(string Product, string SourceCommit, string ExecutableSha256,
    int ProcessId, bool Headless)
{
    public string ReportedRevision { get; init; } = string.Empty;
    public string RevisionEvidence { get; init; } = string.Empty;
}

/// <summary>
/// Runs the real Chromium donor and creates CDP-backed tabs in isolated engine profiles.
/// This factory does not claim to supply Gecko, native CUI embedding, or full donor-feature parity.
/// </summary>
public sealed class ChromiumBrowseEngineFactory(ChromiumRuntimeOptions options) : IBrowseEngineHostFactory, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Entry> _profiles = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private bool _disposed;
    public bool IsSupported => (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) &&
        Path.IsPathFullyQualified(options.ExecutablePath) && File.Exists(options.ExecutablePath);
    public string UnsupportedReason => "A verified Chromium executable is required; Firefox/Gecko is a separate runtime.";
    public bool IsSupportedFor(BrowseEngineKind engine) => engine == BrowseEngineKind.Chromium && IsSupported;
    public string GetUnsupportedReason(BrowseEngineKind engine) => engine == BrowseEngineKind.Gecko
        ? "Firefox/Gecko remains the default, but its native runtime is not installed. Select Chromium explicitly; no silent fallback is performed."
        : UnsupportedReason;

    public async Task<IBrowseEngineTab> CreateAsync(BrowseEngineTabRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSupportedFor(request.Engine)) throw new PlatformNotSupportedException(GetUnsupportedReason(request.Engine));
        ChromiumEngineTab.ValidateAddress(request.InitialAddress);
        if (!Path.IsPathFullyQualified(request.ProfileDirectory)) throw new ArgumentException("Profile path must be absolute.", nameof(request));
        var profile = Path.GetFullPath(request.ProfileDirectory);
        Entry entry;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_profiles.TryGetValue(profile, out entry!))
            {
                entry = new Entry(await ChromiumProcess.StartAsync(options, profile, cancellationToken).ConfigureAwait(false));
                _profiles.Add(profile, entry);
            }
            entry.Users++;
        }
        finally { _gate.Release(); }
        try
        {
            return await ChromiumEngineTab.CreateAsync(entry.Process, request, () => ReleaseAsync(profile, entry), cancellationToken).ConfigureAwait(false);
        }
        catch { await ReleaseAsync(profile, entry).ConfigureAwait(false); throw; }
    }

    private async ValueTask ReleaseAsync(string profile, Entry entry)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_profiles.TryGetValue(profile, out var current) || !ReferenceEquals(current, entry)) return;
            if (--entry.Users != 0) return;
            _profiles.Remove(profile);
            await entry.Process.DisposeAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var entry in _profiles.Values) await entry.Process.DisposeAsync().ConfigureAwait(false);
            _profiles.Clear();
        }
        finally { _gate.Release(); }
    }
    private sealed class Entry(ChromiumProcess process) { public ChromiumProcess Process { get; } = process; public int Users; }
}

internal sealed class ChromiumProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly FileStream _profileLease;
    private readonly string _portFile;
    private int _disposed;
    public ChromiumProtocolConnection Connection { get; private set; } = null!;
    public ChromiumRuntimeIdentity Identity { get; private set; } = null!;
    private ChromiumProcess(Process process, FileStream profileLease, string portFile)
    { _process = process; _profileLease = profileLease; _portFile = portFile; }

    public static async Task<ChromiumProcess> StartAsync(ChromiumRuntimeOptions options, string profile, CancellationToken cancellationToken)
    {
        if (!Regex.IsMatch(options.SourceCommit, "^[a-f0-9]{40}$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(options.ExecutableSha256, "^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("The approved runtime requires an exact source commit and executable SHA-256.", nameof(options));
        await using (var binary = File.OpenRead(options.ExecutablePath))
        {
            var digest = Convert.ToHexString(await SHA256.HashDataAsync(binary, cancellationToken).ConfigureAwait(false));
            if (!digest.Equals(options.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Chromium executable hash differs from the approved binary.");
        }
        Directory.CreateDirectory(profile);
        if (!File.Exists(Path.Combine(profile, ".browse-runtime.lock")) && Directory.EnumerateFileSystemEntries(profile).Any())
            throw new IOException("Refusing to adopt a non-empty profile not owned by Browse's Chromium runtime.");
        var lease = new FileStream(Path.Combine(profile, ".browse-runtime.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var portFile = Path.Combine(profile, "DevToolsActivePort");
        var info = new ProcessStartInfo(options.ExecutablePath) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in new[] { "--remote-debugging-address=127.0.0.1", "--remote-debugging-port=0", "--user-data-dir=" + profile,
            "--no-first-run", "--no-default-browser-check", "--disable-background-networking", "--disable-component-update" }) info.ArgumentList.Add(argument);
        if (options.Headless) info.ArgumentList.Add("--headless=new");
        info.ArgumentList.Add("about:blank");
        var process = new Process { StartInfo = info };
        var runtime = new ChromiumProcess(process, lease, portFile);
        var errors = new System.Text.StringBuilder();
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (errors) { if (errors.Length < 8192) errors.AppendLine(e.Data); } };
        process.OutputDataReceived += (_, _) => { };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.StartupTimeout ?? TimeSpan.FromSeconds(30));
        try
        {
            // The application lease is held before discarding a stale endpoint. Native Chromium also enforces its own profile lock.
            if (File.Exists(portFile)) File.Delete(portFile);
            if (!process.Start()) throw new IOException("Chromium process did not start.");
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
            string[] lines;
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (process.HasExited) { lock (errors) throw new IOException("Chromium exited during startup: " + errors); }
                try { lines = await File.ReadAllLinesAsync(portFile, timeout.Token).ConfigureAwait(false); }
                catch (IOException) { lines = []; }
                if (lines.Length >= 2 && int.TryParse(lines[0], out var port) && port is > 0 and <= 65535 &&
                    Regex.IsMatch(lines[1], "^/devtools/browser/[A-Za-z0-9-]+$", RegexOptions.CultureInvariant))
                {
                    runtime.Connection = await ChromiumProtocolConnection.ConnectAsync(new Uri($"ws://127.0.0.1:{port}{lines[1]}"), timeout.Token).ConfigureAwait(false);
                    break;
                }
                await Task.Delay(50, timeout.Token).ConfigureAwait(false);
            }
            var version = await runtime.Connection.CallAsync("Browser.getVersion", cancellationToken: timeout.Token).ConfigureAwait(false);
            var revision = version.GetProperty("revision").GetString()?.TrimStart('@');
            var evidence = ChromiumRuntimeProvenance.VerifyRevision(options.SourceCommit, options.ExecutableSha256, revision, OperatingSystem.IsLinux());
            runtime.Identity = new(version.GetProperty("product").GetString()!, options.SourceCommit, options.ExecutableSha256.ToLowerInvariant(), process.Id, options.Headless)
            {
                ReportedRevision = revision ?? string.Empty,
                RevisionEvidence = evidence
            };
            // Downloads are denied until Browse's approved download broker is wired. No unmanaged writes are implied as supported.
            await runtime.Connection.CallAsync("Browser.setDownloadBehavior", new { behavior = "deny" }, cancellationToken: timeout.Token).ConfigureAwait(false);
            await runtime.Connection.CallAsync("Target.setDiscoverTargets", new { discover = true }, cancellationToken: timeout.Token).ConfigureAwait(false);
            return runtime;
        }
        catch { await runtime.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            if (Connection is not null)
            {
                using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try { await Connection.CallAsync("Browser.close", cancellationToken: closeTimeout.Token).ConfigureAwait(false); }
                catch (Exception exception) when (exception is not OutOfMemoryException) { }
                await Connection.DisposeAsync().ConfigureAwait(false);
            }
            try
            {
                if (!_process.HasExited)
                {
                    using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try { await _process.WaitForExitAsync(exitTimeout.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { _process.Kill(entireProcessTree: true); await _process.WaitForExitAsync().ConfigureAwait(false); }
                }
            }
            catch (InvalidOperationException) { }
        }
        finally
        {
            _process.Dispose();
            try { if (File.Exists(_portFile)) File.Delete(_portFile); }
            finally { _profileLease.Dispose(); }
        }
    }
}
