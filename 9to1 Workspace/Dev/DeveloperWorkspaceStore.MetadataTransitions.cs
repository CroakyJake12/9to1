using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;

namespace HavenOS.Apps.Dev;

/// <summary>The existing canonical store's original metadata provenance only. This does not
/// rebind a Home READ, update a task capsule, release another owner or relax a native fence.</summary>
public sealed partial class FileDeveloperWorkspaceStore : IDeveloperProjectOriginalMetadataTransitionStore
{
    private readonly object _metadataTransitionGate = new();
    // Weak reference membership avoids retaining every successful autosave indefinitely.
    // A consumer retaining the actual committed value also retains its exact private provenance.
    private readonly ConditionalWeakTable<IDeveloperProjectOriginalMetadataCommitValue, OriginalMetadataTransition> _metadataTransitions = new();
    private sealed record MetadataTransitionBasis(Guid WorkspaceId, string BeforeSha256, string AuthoritySha256);
    private sealed class OriginalMetadataTransition(FileDeveloperWorkspaceStore owner,
        IDeveloperProjectOriginalMetadataCommitValue committed, MetadataTransitionBasis basis,
        string afterSha256, Task actualAtomicWrite) : IDeveloperProjectOriginalMetadataTransition
    {
        internal FileDeveloperWorkspaceStore Owner => owner;
        public IDeveloperProjectOriginalWorkspaceMetadataStore OriginalStore => owner;
        public IDeveloperProjectOriginalMetadataCommitValue OriginalCommittedValue => committed;
        public Guid WorkspaceId => basis.WorkspaceId;
        public string BeforeDocumentSha256 => basis.BeforeSha256;
        public string AfterDocumentSha256 => afterSha256;
        public string WorkspaceAuthoritySha256 => basis.AuthoritySha256;
        public Task OriginalAtomicWriteTask => actualAtomicWrite;
    }

    public IDeveloperProjectOriginalMetadataTransition? GetOriginalMetadataTransition(
        IDeveloperProjectOriginalMetadataCommitValue sameActuallyCommittedValue)
    {
        if (sameActuallyCommittedValue is null) return null;
        lock (_metadataTransitionGate)
            return _metadataTransitions.TryGetValue(sameActuallyCommittedValue, out var actual) &&
                actual.OriginalAtomicWriteTask.IsCompletedSuccessfully ? actual : null;
    }
    public bool IsIssuedOriginalMetadataTransition(IDeveloperProjectOriginalMetadataTransition sameTransition,
        IDeveloperProjectOriginalMetadataCommitValue sameActuallyCommittedValue)
    {
        if (sameActuallyCommittedValue is null) return false;
        lock (_metadataTransitionGate)
            return sameTransition is OriginalMetadataTransition actual && ReferenceEquals(actual.Owner, this) &&
                ReferenceEquals(actual.OriginalCommittedValue, sameActuallyCommittedValue) &&
                _metadataTransitions.TryGetValue(sameActuallyCommittedValue, out var issued) && ReferenceEquals(actual, issued) &&
                actual.OriginalAtomicWriteTask.IsCompletedSuccessfully;
    }

    public bool VerifyOriginalMetadataTransitionDocuments(IDeveloperProjectOriginalMetadataTransition sameTransition,
        IDeveloperProjectOriginalMetadataCommitValue sameActuallyCommittedValue,
        string originalNativeBeforeDocument, string actualNativeAfterDocument)
    {
        if (!IsIssuedOriginalMetadataTransition(sameTransition, sameActuallyCommittedValue) ||
            originalNativeBeforeDocument is null || actualNativeAfterDocument is null) return false;
        byte[] before = [], after = [], beforeAuthority = [], afterAuthority = [];
        try
        {
            if (originalNativeBeforeDocument.Length > 1024 * 1024 || actualNativeAfterDocument.Length > 1024 * 1024) return false;
            before = System.Text.Encoding.UTF8.GetBytes(originalNativeBeforeDocument);
            after = System.Text.Encoding.UTF8.GetBytes(actualNativeAfterDocument);
            if (before.Length > 1024 * 1024 || after.Length > 1024 * 1024 ||
                !StringComparer.Ordinal.Equals(MetadataSha256(before), sameTransition.BeforeDocumentSha256) ||
                !StringComparer.Ordinal.Equals(MetadataSha256(after), sameTransition.AfterDocumentSha256)) return false;
            using var beforeJson = JsonDocument.Parse(before); using var afterJson = JsonDocument.Parse(after);
            var old = DecodeOriginalDocument(beforeJson.RootElement, sameTransition.WorkspaceId);
            var current = DecodeOriginalDocument(afterJson.RootElement, sameTransition.WorkspaceId);
            if (!old.Succeeded || !current.Succeeded) return false;
            beforeAuthority = JsonSerializer.SerializeToUtf8Bytes(old.Value, JsonOptions);
            afterAuthority = JsonSerializer.SerializeToUtf8Bytes(current.Value, JsonOptions);
            return beforeAuthority.AsSpan().SequenceEqual(afterAuthority) &&
                StringComparer.Ordinal.Equals(MetadataSha256(beforeAuthority), sameTransition.WorkspaceAuthoritySha256);
        }
        catch (Exception cause) when (cause is JsonException or ArgumentException) { return false; }
        finally
        {
            CryptographicOperations.ZeroMemory(before); CryptographicOperations.ZeroMemory(after);
            CryptographicOperations.ZeroMemory(beforeAuthority); CryptographicOperations.ZeroMemory(afterAuthority);
        }
    }

    /// <summary>Called beneath the SAME canonical workspace CAS lock, immediately before its
    /// atomic replacement. Capture raw file bytes rather than reserialising an old document:
    /// formatting/casing still contributes to the exact native before-document fingerprint.
    /// A changed document, malformed catalogue or oversized input refuses before replacement.</summary>
    private async Task<MetadataTransitionBasis> CaptureOriginalMetadataTransitionBasisAsync(
        WorkspaceDocument expected, CancellationToken token)
    {
        byte[] observed = [], canonical = [], expectedCanonical = [], authority = [];
        try
        {
            var workspaceId = expected.Workspace.WorkspaceId;
            await using var input = new FileStream(GetWorkspacePath(workspaceId), FileMode.Open, FileAccess.Read,
                FileShare.Read, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var length = input.Length;
            if (length is <= 0 or > 1024 * 1024) throw new InvalidDataException("The original metadata document exceeds its native reader bound.");
            observed = new byte[checked((int)length)];
            await input.ReadExactlyAsync(observed, token).ConfigureAwait(false);
            var sentinel = new byte[1];
            if (await input.ReadAsync(sentinel.AsMemory(), token).ConfigureAwait(false) != 0 || input.Length != length)
                throw new IOException("The original metadata document changed during transition capture.");
            using var parsed = JsonDocument.Parse(observed);
            if (!DecodeOriginalDocument(parsed.RootElement, workspaceId).Succeeded)
                throw new InvalidDataException("Original canonical metadata could not be validated before transition.");
            var actual = parsed.RootElement.Deserialize<WorkspaceDocument>(JsonOptions)
                ?? throw new InvalidDataException("The original canonical metadata envelope is missing.");
            canonical = JsonSerializer.SerializeToUtf8Bytes(actual, JsonOptions);
            expectedCanonical = JsonSerializer.SerializeToUtf8Bytes(expected, JsonOptions);
            if (!canonical.AsSpan().SequenceEqual(expectedCanonical))
                throw new IOException("Canonical metadata changed after its CAS observation; no transition write was dispatched.");
            authority = JsonSerializer.SerializeToUtf8Bytes(actual.Workspace, JsonOptions);
            return new(workspaceId, MetadataSha256(observed), MetadataSha256(authority));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(observed);
            CryptographicOperations.ZeroMemory(canonical);
            CryptographicOperations.ZeroMemory(expectedCanonical);
            CryptographicOperations.ZeroMemory(authority);
        }
    }

    private static string ObserveOriginalMetadataTransitionAfter(MetadataTransitionBasis basis,
        ReadOnlyMemory<byte> exactWriterBytes)
    {
        using var parsed = JsonDocument.Parse(exactWriterBytes);
        var after = parsed.RootElement.Deserialize<WorkspaceDocument>(JsonOptions)
            ?? throw new InvalidDataException("The canonical writer's exact metadata envelope is missing.");
        byte[] authority = [];
        try
        {
            authority = JsonSerializer.SerializeToUtf8Bytes(after.Workspace, JsonOptions);
            if (after.Workspace.WorkspaceId != basis.WorkspaceId ||
                !StringComparer.Ordinal.Equals(MetadataSha256(authority), basis.AuthoritySha256))
                throw new InvalidDataException("A workspace authority change cannot be dispatched as a presentation-only metadata transition.");
            return MetadataSha256(exactWriterBytes.Span);
        }
        finally { CryptographicOperations.ZeroMemory(authority); }
    }

    private void IssueOriginalMetadataTransition(DeveloperDraftRecoverySnapshot sameCommittedValue,
        MetadataTransitionBasis basis, string exactWriterAfterSha256, Task sameAtomicWrite)
    {
        if (!sameAtomicWrite.IsCompletedSuccessfully || sameCommittedValue.ProjectReference.WorkspaceId != basis.WorkspaceId ||
            exactWriterAfterSha256 is not { Length: 64 } || !exactWriterAfterSha256.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Only the actual successful canonical atomic writer can issue metadata provenance.");
        var receipt = new OriginalMetadataTransition(this, sameCommittedValue, basis, exactWriterAfterSha256, sameAtomicWrite);
        lock (_metadataTransitionGate)
        {
            // Reusing a public value never lets an old private receipt qualify as a new
            // write. Membership moves only after this distinct actual writer succeeded.
            _metadataTransitions.Remove(sameCommittedValue);
            _metadataTransitions.Add(sameCommittedValue, receipt);
        }
    }
    private static string MetadataSha256(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
