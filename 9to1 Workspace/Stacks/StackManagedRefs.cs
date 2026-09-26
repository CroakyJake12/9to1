using System.Diagnostics;
using System.Text;

namespace HavenOS.Apps.Stacks;

public sealed record StackGitReference(string Name, string ObjectId);

public sealed record StackManagedReferenceRecovery(
    bool Recovered,
    string ReferenceName,
    string? ObjectId,
    IReadOnlyList<string> AttemptedObjectIds,
    string? FailureCode);

public interface IStackGitReferenceStore
{
    Task<StackGitReference?> GetReferenceAsync(string referenceName, CancellationToken cancellationToken = default);
    Task<bool> ContainsObjectAsync(string objectId, CancellationToken cancellationToken = default);
    Task<bool> CreateReferenceIfMissingAsync(string referenceName, string objectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StackGitReference>> ListBranchesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Enforces the Stack-managed Git ref boundary and recovers only from verified retained Git objects.
/// </summary>
public sealed class StackManagedRefService
{
    public const string DependencyReference = "refs/heads/stack/twigs-and-leaves-dependency";
    public const string DependencyDisplayName = "Twigs and Leaves (DEPENDENCY)";

    private readonly IStackGitReferenceStore _references;

    public StackManagedRefService(IStackGitReferenceStore references)
    {
        _references = references ?? throw new ArgumentNullException(nameof(references));
    }

    public static bool IsManagedReference(string referenceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceName);
        string normalized = referenceName.StartsWith("refs/", StringComparison.Ordinal) ? referenceName : "refs/heads/" + referenceName;
        return normalized.Equals(DependencyReference, StringComparison.Ordinal)
            || normalized.StartsWith("refs/heads/stack/", StringComparison.Ordinal);
    }

    public static string ToUserBranchReference(string branchName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        string reference = branchName.StartsWith("refs/", StringComparison.Ordinal) ? branchName : "refs/heads/" + branchName;
        ValidateReferenceSyntax(reference);
        if (!reference.StartsWith("refs/heads/", StringComparison.Ordinal) || IsManagedReference(reference))
        {
            throw new StackFailureException(StackFailureCode.ManagedRefProtected,
                "Stack-managed refs are internal infrastructure and cannot be created or shown as ordinary project Branches.", reference);
        }

        return reference;
    }

    public static void DemandRawMutationAllowed(string referenceName, StackActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ValidateReferenceSyntax(referenceName);
        if (!IsManagedReference(referenceName)) return;
        if (!actor.Can(StackCapability.AdvancedGitReconciliation))
        {
            throw new StackFailureException(StackFailureCode.ManagedRefProtected,
                "This ref is Stack-managed. Use a Stack semantic operation; raw Git mutation is blocked.", referenceName);
        }

        // Advanced reconciliation is an explicit permission boundary, not permission to bypass lineage checks.
        throw new StackFailureException(StackFailureCode.ApprovalRequired,
            "Managed-ref changes require Stack's advanced reconciliation workflow, which records a preview and audit event.", referenceName, recoverable: true);
    }

    public async Task<IReadOnlyList<StackGitReference>> ListUserBranchesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<StackGitReference> branches = await _references.ListBranchesAsync(cancellationToken).ConfigureAwait(false);
        return branches.Where(static branch => !IsManagedReference(branch.Name)).ToArray();
    }

    public async Task<StackManagedReferenceRecovery> VerifyOrRecoverDependencyReferenceAsync(
        StackManifest manifest,
        StackActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Demand(actor, StackCapability.ManageRemotes, DependencyReference);

        StackGitReference? current = await _references.GetReferenceAsync(DependencyReference, cancellationToken).ConfigureAwait(false);
        if (current is not null)
        {
            if (!await _references.ContainsObjectAsync(current.ObjectId, cancellationToken).ConfigureAwait(false))
            {
                throw new StackFailureException(StackFailureCode.SourceCorrupt,
                    "The managed dependency ref points to a Git object that is not available locally; recovery has not changed the ref.", DependencyReference, recoverable: true);
            }

            return new StackManagedReferenceRecovery(true, DependencyReference, current.ObjectId, [], null);
        }

        string[] candidates = new[] { manifest.ManagedDependencyCommitId }
            .Concat(manifest.RetainedObjectIds)
            .Where(static objectId => !string.IsNullOrWhiteSpace(objectId))
            .Select(static objectId => objectId!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var verified = new List<string>();
        foreach (string objectId in candidates)
        {
            if (!IsObjectIdForStore(objectId)) continue;
            if (await _references.ContainsObjectAsync(objectId, cancellationToken).ConfigureAwait(false)) verified.Add(objectId);
        }

        foreach (string objectId in verified)
        {
            bool created = await _references.CreateReferenceIfMissingAsync(DependencyReference, objectId, cancellationToken).ConfigureAwait(false);
            StackGitReference? after = await _references.GetReferenceAsync(DependencyReference, cancellationToken).ConfigureAwait(false);
            if (after is not null && StringComparer.OrdinalIgnoreCase.Equals(after.ObjectId, objectId))
            {
                manifest.ManagedDependencyCommitId = objectId;
                manifest.RetainedObjectIds = manifest.RetainedObjectIds
                    .Append(objectId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                manifest.Audit.Add(new StackAuditEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, actor.ActorId,
                    "ManagedDependencyReferenceRecovered", manifest.ProjectId, manifest.ProjectId, null, null,
                    created ? "Recovered" : "ConcurrentRecoveryVerified", objectId));
                return new StackManagedReferenceRecovery(true, DependencyReference, objectId, verified, null);
            }

            if (after is not null)
            {
                return new StackManagedReferenceRecovery(true, DependencyReference, after.ObjectId, verified, null);
            }
        }

        return new StackManagedReferenceRecovery(false, DependencyReference, null, verified, StackFailureCode.ManagedMetadataMissing.ToString());
    }

    public static string ToDisplayName(string referenceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceName);
        if (StringComparer.Ordinal.Equals(referenceName, DependencyReference)) return DependencyDisplayName;
        return IsManagedReference(referenceName) ? "Stack-managed infrastructure" : referenceName.Replace("refs/heads/", string.Empty, StringComparison.Ordinal);
    }

    private static void Demand(StackActor? actor, StackCapability capability, string target)
    {
        if (actor is null || !actor.Can(capability))
        {
            throw new StackFailureException(StackFailureCode.PermissionDenied, $"The caller is not permitted to {capability}.", target);
        }
    }

    internal static bool IsObjectIdForStore(string objectId) => objectId.Length is 40 or 64 && objectId.All(Uri.IsHexDigit);

    internal static void ValidateReferenceSyntax(string reference)
    {
        if (reference.StartsWith('-') || reference.Contains('\\') || reference.Contains("..", StringComparison.Ordinal)
            || reference.Contains("@{", StringComparison.Ordinal) || reference.Contains("//", StringComparison.Ordinal)
            || reference.EndsWith('/') || reference.EndsWith('.') || reference.Contains(".lock", StringComparison.OrdinalIgnoreCase)
            || reference.Any(char.IsControl))
        {
            throw new StackFailureException(StackFailureCode.InvalidPath, "The Git reference name is invalid.", reference);
        }

        foreach (string part in reference.Split('/'))
        {
            if (part.Length == 0 || part.StartsWith('.') || part.EndsWith('.') || part.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
            {
                throw new StackFailureException(StackFailureCode.InvalidPath, "The Git reference name is invalid.", reference);
            }
        }
    }
}

/// <summary>
/// Narrow Git CLI reference provider. All arguments are passed directly without a shell; ref and object values are validated.
/// </summary>
public sealed class GitCliStackReferenceStore : IStackGitReferenceStore
{
    private readonly string _gitDirectory;
    private readonly string _gitExecutable;

    public GitCliStackReferenceStore(string gitDirectory, string gitExecutable = "git")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(gitExecutable);
        _gitDirectory = Path.GetFullPath(gitDirectory);
        _gitExecutable = gitExecutable;
    }

    public async Task<StackGitReference?> GetReferenceAsync(string referenceName, CancellationToken cancellationToken = default)
    {
        StackManagedRefService.ValidateReferenceSyntax(referenceName);
        ProcessResult result = await RunAsync(["rev-parse", "--verify", "--end-of-options", referenceName], null, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) return null;
        string objectId = result.StandardOutput.Trim();
        if (!StackManagedRefService.IsObjectIdForStore(objectId))
        {
            throw new StackFailureException(StackFailureCode.SourceCorrupt, "Git returned an invalid object ID for a Stack reference.", referenceName, recoverable: true);
        }

        return new StackGitReference(referenceName, objectId);
    }

    public async Task<bool> ContainsObjectAsync(string objectId, CancellationToken cancellationToken = default)
    {
        if (!StackManagedRefService.IsObjectIdForStore(objectId)) return false;
        ProcessResult result = await RunAsync(["cat-file", "-e", objectId + "^{object}"], null, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    public async Task<bool> CreateReferenceIfMissingAsync(string referenceName, string objectId, CancellationToken cancellationToken = default)
    {
        StackManagedRefService.ValidateReferenceSyntax(referenceName);
        if (!StackManagedRefService.IsObjectIdForStore(objectId) || !await ContainsObjectAsync(objectId, cancellationToken).ConfigureAwait(false))
        {
            throw new StackFailureException(StackFailureCode.SourceCorrupt, "A managed ref cannot point to a missing or malformed Git object.", referenceName, recoverable: true);
        }

        ProcessResult result = await RunAsync(["update-ref", "--stdin"], $"create {referenceName} {objectId}\n", cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    public async Task<IReadOnlyList<StackGitReference>> ListBranchesAsync(CancellationToken cancellationToken = default)
    {
        ProcessResult result = await RunAsync(["for-each-ref", "--format=%(refname)%09%(objectname)", "refs/heads"], null, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new StackFailureException(StackFailureCode.CapabilityUnavailable, "Git branch references could not be enumerated.", _gitDirectory, recoverable: true, retryable: true);
        }

        return result.StandardOutput.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Split('\t', 2))
            .Where(static parts => parts.Length == 2 && StackManagedRefService.IsObjectIdForStore(parts[1]))
            .Select(static parts => new StackGitReference(parts[0], parts[1]))
            .ToArray();
    }

    internal async Task InitializeBareRepositoryAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_gitDirectory)!);
        ProcessStartInfo startInfo = NewStartInfo(null);
        startInfo.ArgumentList.Add("init");
        startInfo.ArgumentList.Add("--bare");
        startInfo.ArgumentList.Add("--initial-branch=main");
        startInfo.ArgumentList.Add(_gitDirectory);
        ProcessResult result = await StartAsync(startInfo, null, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new StackFailureException(StackFailureCode.CapabilityUnavailable,
                "Git could not initialize the local Stack backing repository. No project source was moved into the project folder.", _gitDirectory, recoverable: true, innerException: new InvalidOperationException(result.StandardError));
        }
    }

    private async Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, string? standardInput, CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = NewStartInfo(_gitDirectory);
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        return await StartAsync(startInfo, standardInput, cancellationToken).ConfigureAwait(false);
    }

    private ProcessStartInfo NewStartInfo(string? gitDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _gitExecutable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };
        if (gitDirectory is not null)
        {
            startInfo.ArgumentList.Add("--git-dir");
            startInfo.ArgumentList.Add(gitDirectory);
        }

        return startInfo;
    }

    private static async Task<ProcessResult> StartAsync(ProcessStartInfo startInfo, string? standardInput, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Git process did not start.");
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new StackFailureException(StackFailureCode.CapabilityUnavailable, "Git is not available for this local Stack operation.", startInfo.FileName, recoverable: true, innerException: exception);
        }

        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
