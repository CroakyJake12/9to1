using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure.Native.Linux;

/// <summary>Explicit administrative export of reviewable deployment files from the
/// SAME settled signed activation. Never writes a systemd unit location, registers,
/// enables, starts, repairs or recovers a service. Exported observations grant no role.</summary>
public sealed class LinuxHomeRootServiceUnitArtifactOwner(LinuxHomeBootstrapActivationOwner originalActivation)
{
    public sealed record Request(LinuxHomeBootstrapActivationOwner.Request OriginalActivation,
        string ExistingControlDirectory, string ExistingPrivateArtifactParent, Guid ArtifactId);
    public sealed record Result(Guid ArtifactId, Guid BootstrapOperationId, string ArtifactDirectory,
        string UnitSha256, string ProvenanceSha256);
    public sealed class ExportUnconfirmedException(string path, Exception cause)
        : IOException("The original unit artifact may be incomplete; preserve it for review rather than overwrite or install it.", cause)
    { public string ObservedArtifactDirectory { get; } = path; }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public async Task<Result> ExportAsync(Request request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(originalActivation);
        if (request.ArtifactId == Guid.Empty) throw new ArgumentException("A distinct original artifact identity is required.");
        var original = request.OriginalActivation;
        var input = original.SigningInputs;
        original = original with { ExistingActivationParent = RequireAbsolute(original.ExistingActivationParent), PlanPath = RequireAbsolute(original.PlanPath),
            SigningInputs = input with { PublicCertificatePath = RequireAbsolute(input.PublicCertificatePath), SignedCataloguePath = RequireAbsolute(input.SignedCataloguePath),
                SelectedPackageIds = Array.AsReadOnly(input.SelectedPackageIds.ToArray()) } };
        var output = RequireArtifactParent(request.ExistingPrivateArtifactParent);
        var control = RequireAbsolute(request.ExistingControlDirectory);
        string? observed = null; Result? result = null;
        try
        {
            await originalActivation.WithinSettledCodeAsync(original, async (code, caller) => {
                var root = code.Packages.Single(value => value.AppId == "root");
                if (!root.ActivatedFiles.Any(value => value.RelativePath == "home-root-launcher"))
                    throw new UnauthorizedAccessException("The same signed Root activation must include its actual native launcher cohort member.");
                using var executable = code.OpenOriginalFile(root, root.EntrypointRelativePath);
                using var launcher = code.OpenOriginalFile(root, "home-root-launcher");
                if ((LinuxOriginalDownloadPaths.ReadIdentity(executable).Mode & 0x1ff) != 0x1ed ||
                    (LinuxOriginalDownloadPaths.ReadIdentity(launcher).Mode & 0x1ff) != 0x1ed)
                    throw new UnauthorizedAccessException("The original signed Root executable and native helper must retain executable mode0755.");
                LinuxHomePublisherEnrollmentOwner.DemandProtectedMachineDirectory(control);
                using var controlPin = LinuxOriginalDownloadPaths.OpenAbsoluteDirectory(control, mutable: false);
                if ((LinuxOriginalDownloadPaths.ReadIdentity(controlPin).Mode & 1) == 0)
                    throw new UnauthorizedAccessException("The explicit original control directory must already permit local-user traversal.");
                LinuxHomePublisherEnrollmentOwner.DemandProtectedMachineDirectory(output);
                using var parent = LinuxOriginalDownloadPaths.OpenAbsoluteDirectory(output, mutable: true);
                LinuxOriginalDownloadPaths.DemandOwner(parent, 0, privateDirectory: true);
                var arguments = new List<string> { Path.Combine(root.InstallationRoot, root.EntrypointRelativePath), "--home-root-service",
                    "--machine-state", code.OriginalMachineStatePath, "--publisher-certificate", original.SigningInputs.PublicCertificatePath,
                    "--publisher-sha256", original.SigningInputs.ExplicitCertificateSha256, "--signed-catalogue", original.SigningInputs.SignedCataloguePath,
                    "--catalogue-sha256", original.SigningInputs.ExplicitSignedCatalogueSha256, "--issuer-key-id", original.SigningInputs.IssuerKeyId,
                    "--abi", original.SigningInputs.Abi, "--operation-id", original.SigningInputs.OperationId.ToString("D"),
                    "--activation-parent", original.ExistingActivationParent, "--stage-leaf", original.StageLeaf,
                    "--plan", original.PlanPath, "--control-directory", control };
                foreach (var selected in original.SigningInputs.SelectedPackageIds.Order(StringComparer.Ordinal)) { arguments.Add("--select"); arguments.Add(selected); }
                var unit = Encoding.UTF8.GetBytes(RenderUnit(arguments, root.InstallationRoot));
                var provenance = JsonSerializer.SerializeToUtf8Bytes(new {
                    schemaVersion = 1, artifactId = request.ArtifactId, bootstrapOperationId = original.SigningInputs.OperationId,
                    machineStatePath = code.OriginalMachineStatePath, planSha256 = code.OriginalPlanSha256,
                    publisherCertificateSha256 = original.SigningInputs.ExplicitCertificateSha256,
                    signedCatalogueSha256 = original.SigningInputs.ExplicitSignedCatalogueSha256,
                    rootActivationSha256 = Digest(JsonSerializer.SerializeToUtf8Bytes(root)), unitSha256 = Digest(unit),
                    unitName = LinuxHomeRegisteredRootServiceContext.ServiceUnit, serviceInstalled = false, serviceStarted = false, runtimeReady = false
                }, Json);
                if (unit.Length > 128 * 1024 || provenance.Length > 128 * 1024) throw new InvalidDataException("The original unit artifact exceeds its bounded size.");
                await code.DemandPinnedCurrentAsync(caller).ConfigureAwait(false);
                var leaf = "root-unit-" + request.ArtifactId.ToString("N");
                observed = Path.Combine(output, leaf); // preserve a possible mkdir/postguard outcome
                LinuxOriginalDownloadRoot created;
                try { created = LinuxOriginalDownloadPaths.CreatePrivateChildRoot(parent, output, leaf, 0, caller); }
                catch (Exception collision) when (LinuxOriginalDownloadPaths.IsNameCollision(collision)) { observed = null; throw; }
                using var bundle = created;
                using var directory = bundle.OpenDirectory(observed, mutable: true);
                using var unitFile = await WriteOriginal(directory, observed, LinuxHomeRegisteredRootServiceContext.ServiceUnit, unit, caller).ConfigureAwait(false);
                using var provenanceFile = await WriteOriginal(directory, observed, "provenance.json", provenance, caller).ConfigureAwait(false);
                bundle.DemandCurrent(); LinuxOriginalDownloadPaths.DemandPath(controlPin, control, directory: true);
                LinuxOriginalDownloadPaths.Flush(directory); LinuxOriginalDownloadPaths.Flush(parent);
                await code.DemandPinnedCurrentAsync(caller).ConfigureAwait(false);
                await unitFile.DemandCurrentAsync(caller).ConfigureAwait(false);
                await provenanceFile.DemandCurrentAsync(caller).ConfigureAwait(false);
                bundle.DemandCurrent();
                result = new(request.ArtifactId, original.SigningInputs.OperationId, observed, Digest(unit), Digest(provenance));
            }, token).ConfigureAwait(false);
            return result ?? throw new InvalidOperationException("No original settled unit artifact was issued.");
        }
        catch (Exception cause) when (observed is not null) { throw new ExportUnconfirmedException(observed, cause); }
    }
    private sealed class OriginalWrittenFile(SafeFileHandle held, string path, byte[] expected, LinuxOriginalDownloadIdentity identity) : IDisposable
    {
        internal async Task DemandCurrentAsync(CancellationToken token)
        {
            LinuxOriginalDownloadPaths.DemandPath(held, path, directory: false);
            LinuxOriginalDownloadPaths.DemandPrivateStage(held, 0);
            if (!identity.SameReadVersion(LinuxOriginalDownloadPaths.ReadIdentity(held))) throw new IOException("The original unit artifact inode/read version changed.");
            var read = new byte[expected.Length]; var offset = 0;
            while (offset != read.Length) { var count = await RandomAccess.ReadAsync(held, read.AsMemory(offset), offset, token).ConfigureAwait(false);
                if (count == 0) throw new EndOfStreamException("The immutable original unit artifact became incomplete."); offset += count; }
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(read), SHA256.HashData(expected)) ||
                !identity.SameReadVersion(LinuxOriginalDownloadPaths.ReadIdentity(held))) throw new IOException("The immutable original unit artifact hash/read version changed.");
        }
        public void Dispose() => held.Dispose();
    }
    private static async Task<OriginalWrittenFile> WriteOriginal(SafeFileHandle directory, string path, string leaf, byte[] bytes, CancellationToken token)
    {
        var stageLeaf = ".export-" + Guid.NewGuid().ToString("N");
        var file = LinuxOriginalDownloadPaths.CreateStage(directory, stageLeaf);
        try
        {
            await RandomAccess.WriteAsync(file, bytes, 0, token).ConfigureAwait(false); LinuxOriginalDownloadPaths.Flush(file);
            LinuxOriginalDownloadPaths.DemandPrivateStage(file, 0);
            var expected = LinuxOriginalDownloadPaths.ReadIdentity(file);
            if (expected.Size != (ulong)bytes.Length) throw new IOException("The original unit stage size differs from its complete bytes.");
            var published = LinuxOriginalDownloadPaths.PublishCreateOnly(file, directory, path, stageLeaf, leaf, expected);
            var retained = new OriginalWrittenFile(file, Path.Combine(path, leaf), bytes, published);
            await retained.DemandCurrentAsync(token).ConfigureAwait(false);
            return retained;
        }
        catch { file.Dispose(); throw; }
    }
    internal static string RenderUnit(IReadOnlyList<string> actualArguments, string protectedWorkingDirectory)
    {
        if (actualArguments.Count is 0 or > 300) throw new ArgumentException("Bounded source-issued Root arguments are required.");
        return "# Reviewable 9to1 Root artifact. Not installed or started by export.\n[Unit]\nDescription=9to1 Root\nAfter=systemd-logind.service\nRequires=systemd-logind.service\n\n[Service]\nType=simple\nUser=root\nGroup=root\nUMask=0077\nRestart=no\nKillMode=control-group\nWorkingDirectory=" + QuoteSystemdArgument(protectedWorkingDirectory) +
            "\nExecStart=:" + string.Join(" ", actualArguments.Select(QuoteSystemdArgument)) + "\n";
    }
    // ExecStart's ':' prefix disables environment substitution. Percent escaping
    // still prevents systemd specifiers in both argv and WorkingDirectory.
    internal static string QuoteSystemdArgument(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 4096 || value.Any(char.IsControl)) throw new ArgumentException("A bounded literal systemd argument is required.");
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("%", "%%", StringComparison.Ordinal) + "\"";
    }
    private static string RequireAbsolute(string value)
    {
        _ = QuoteSystemdArgument(value);
        return Path.IsPathFullyQualified(value) ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(value)) : throw new ArgumentException("Explicit absolute original paths are required.");
    }
    internal static string RequireArtifactParent(string value)
    {
        var path = RequireAbsolute(value);
        foreach (var prohibited in new[] { "/etc/systemd", "/run/systemd", "/usr/lib/systemd", "/usr/local/lib/systemd", "/lib/systemd" })
            if (path == prohibited || path.StartsWith(prohibited + "/", StringComparison.Ordinal))
                throw new UnauthorizedAccessException("Export artifacts into a separate private review directory, never a systemd location.");
        return path;
    }
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
