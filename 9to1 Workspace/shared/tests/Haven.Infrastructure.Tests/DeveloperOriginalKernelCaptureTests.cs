using System.Security.Cryptography;
using Haven.Application;
using Haven.Infrastructure;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Genuine maintained Linux kernel/filesystem captures with synthetic read issuers.
/// These controls prove only the physical boundary, never Home approval or completed import.</summary>
public sealed partial class DeveloperOriginalKernelCaptureTests
{
    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Actual_existing_source_manifest_preserves_root_hashes_and_held_capture_identity()
    {
        var rig = new Rig();
        try
        {
            Directory.CreateDirectory(Path.Combine(rig.Project, "src"));
            File.WriteAllText(Path.Combine(rig.Project, "src", "code.cs"), "genuine selected source");
            var root = await rig.Select();
            var capture = await rig.Source.CaptureOriginalAsync(root, rig.Selections.Logical, rig.Reads.Admission, CancellationToken.None);
            Assert.Equal(rig.Project, capture.OriginalExistingProjectRoot);
            Assert.Equal("src", Assert.Single(capture.OriginalFolderPaths));
            var file = Assert.Single(capture.OriginalFiles);
            Assert.Equal("src/code.cs", file.RelativePath);
            var bytes = File.ReadAllBytes(Path.Combine(rig.Project, "src", "code.cs"));
            Assert.Equal(bytes.Length, file.SizeBytes);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), file.ContentSha256);
            Assert.True(rig.Source.IsIssuedOriginalCapture(capture, root, rig.Selections.Logical));
            await rig.Source.RevalidateOriginalCaptureAsync(capture, CancellationToken.None);
            Assert.True(rig.Reads.Validations > 0);
            Assert.True(rig.Reads.ReadStarts > 0);
            var close = rig.Source.CloseAndDrainOriginalCapturesAsync();
            Assert.Same(close, rig.Source.CloseAndDrainOriginalCapturesAsync());
            await close;
            Assert.False(rig.Source.IsIssuedOriginalCapture(capture, root, rig.Selections.Logical));
            Assert.False(rig.Source.IsIssuedOriginalSelection(root));
        }
        finally { await rig.DisposeAsync(); }
    }

    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Missing_original_read_admission_refuses_before_any_manifest_read_or_enumeration()
    {
        var rig = new Rig(); var refusal = new UnauthorizedAccessException("genuine source has no original read admission");
        try
        {
            File.WriteAllText(Path.Combine(rig.Project, "secret.cs"), "not authorized for this read cohort");
            var root = await rig.Select(); rig.Reads.Refusal = refusal;
            var actual = rig.Source.CaptureOriginalAsync(root, rig.Selections.Logical, rig.Reads.Admission, CancellationToken.None);
            var error = await Assert.ThrowsAnyAsync<Exception>(() => actual);
            Assert.True(Contains(error, refusal));
            Assert.Equal(0, rig.Reads.ReadStarts);
            var close = rig.Source.CloseAndDrainOriginalCapturesAsync();
            var cleanup = await Assert.ThrowsAnyAsync<Exception>(() => close);
            Assert.True(Contains(cleanup, refusal));
            Assert.False(rig.Source.IsIssuedOriginalSelection(root));
            Assert.Same(close, rig.Source.CloseAndDrainOriginalCapturesAsync());
        }
        finally { await rig.DisposeAsync(); }
    }

    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Actual_source_change_refuses_old_capture_instead_of_publishing_a_new_manifest()
    {
        var rig = new Rig();
        try
        {
            var path = Path.Combine(rig.Project, "code.cs"); File.WriteAllText(path, "first captured bytes");
            var root = await rig.Select();
            var capture = await rig.Source.CaptureOriginalAsync(root, rig.Selections.Logical, rig.Reads.Admission, CancellationToken.None);
            File.WriteAllText(path, "changed bytes with a different size");
            await Assert.ThrowsAnyAsync<Exception>(() => rig.Source.RevalidateOriginalCaptureAsync(capture, CancellationToken.None));
            await Assert.ThrowsAnyAsync<Exception>(() => rig.Source.CaptureOriginalAsync(root, rig.Selections.Logical, rig.Reads.Admission, CancellationToken.None));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("first captured bytes"))).ToLowerInvariant(), Assert.Single(capture.OriginalFiles).ContentSha256);
            await Assert.ThrowsAnyAsync<Exception>(() => rig.Source.CloseAndDrainOriginalCapturesAsync());
        }
        finally { await rig.DisposeAsync(); }
    }

    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Restored_source_callback_cannot_join_the_encompassing_capture_original()
    {
        var rig = new Rig(); var oldContext = ExecutionContext.Capture(); Exception? observed = null;
        try
        {
            File.WriteAllText(Path.Combine(rig.Project, "code.cs"), "genuine source"); var root = await rig.Select();
            rig.Reads.BeforeValidation = () => ExecutionContext.Run(oldContext!, _ =>
            {
                try { rig.Source.CloseAndDrainOriginalCapturesAsync().GetAwaiter().GetResult(); }
                catch (Exception error) { observed = error; }
            }, null);
            var capture = await rig.Source.CaptureOriginalAsync(root, rig.Selections.Logical, rig.Reads.Admission, CancellationToken.None);
            Assert.IsType<InvalidOperationException>(observed);
            Assert.True(rig.Source.IsIssuedOriginalCapture(capture, root, rig.Selections.Logical));
            rig.Reads.BeforeValidation = null;
            await rig.Source.CloseAndDrainOriginalCapturesAsync();
        }
        finally { rig.Reads.BeforeValidation = null; await rig.DisposeAsync(); }
    }

    private static bool Contains(Exception actual, Exception target) => ReferenceEquals(actual, target) ||
        actual is AggregateException compound && compound.InnerExceptions.Any(error => Contains(error, target));
    private sealed class Rig : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "dev-kernel-capture-" + Guid.NewGuid().ToString("N"));
        public string Project => Path.Combine(Root, "existing-project");
        public SelectionSource Selections = new(); public ReadSource Reads = new();
        public IDeveloperProjectOriginalPhysicalCaptureSource Source;
        public Rig()
        { Directory.CreateDirectory(Project); Source = new WorkspaceToolService().CreateOriginalDeveloperCaptureSource(Reads, () => Selections); }
        public async Task<IDeveloperProjectOriginalPhysicalSelection> Select()
        { var actual = await Source.OpenOriginalSelectionAsync(Root, Project, CancellationToken.None); Selections.Root = actual; return actual; }
        public async ValueTask DisposeAsync()
        {
            try { await Source.CloseAndDrainOriginalCapturesAsync(); } catch { }
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
    private sealed class Selection : IDeveloperProjectOriginalReadSelection { }
    private sealed class SelectionSource : IDeveloperProjectOriginalPhysicalReadSelectionSource
    {
        public Selection Logical = new(); public IDeveloperProjectOriginalPhysicalSelection? Root;
        public bool IsIssuedOriginal(IDeveloperProjectOriginalReadSelection value) => ReferenceEquals(value, Logical);
        public bool IsIssuedOriginalPhysicalBinding(IDeveloperProjectOriginalReadSelection value, IDeveloperProjectOriginalPhysicalSelection actual) => IsIssuedOriginal(value) && ReferenceEquals(actual, Root);
        public Task RevalidateOriginalAsync(IDeveloperProjectOriginalReadSelection value, AuthenticatedResourceActor actor, CancellationToken token) => throw new NotSupportedException("Synthetic physical scope; no actor grant.");
        public IReadOnlyList<ResourceScope> GetOriginalReadScopes(IDeveloperProjectOriginalReadSelection value) => throw new NotSupportedException("Synthetic physical scope; no resource grant.");
        public void DemandExternalOriginalReadSelectionJoin() { }
    }
    private sealed class ReadSource : IDeveloperProjectOriginalReadAdmissionSource, IDeveloperProjectOriginalReadAdmissionJoinGuard
    {
        public int Validations, ReadStarts; public Action? BeforeValidation; public Exception? Refusal;
        public IDeveloperProjectOriginalReadAdmission Admission;
        public ReadSource() => Admission = new Read(this);
        public Task<IDeveloperProjectOriginalReadAdmission> AcquireOriginalAsync(IDeveloperProjectOriginalReadSelection value, CancellationToken token) => throw new NotSupportedException("No Home approval is simulated.");
        public Task ValidateOriginalAsync(IDeveloperProjectOriginalReadSelection value, IDeveloperProjectOriginalReadAdmission actual, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Validations++; Assert.Same(Admission, actual); BeforeValidation?.Invoke();
            return Refusal is null ? Task.CompletedTask : Task.FromException(Refusal);
        }
        public void DemandExternalOriginalReadAdmissionJoin() { }
        private sealed class Read(ReadSource owner) : IDeveloperProjectOriginalReadAdmission
        {
            public Task RevalidateOriginalAsync(CancellationToken token) => Task.CompletedTask;
            public T RunOriginalRead<T>(Func<T> finiteOriginalReadStart, CancellationToken token)
            { token.ThrowIfCancellationRequested(); owner.ReadStarts++; return finiteOriginalReadStart(); }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
