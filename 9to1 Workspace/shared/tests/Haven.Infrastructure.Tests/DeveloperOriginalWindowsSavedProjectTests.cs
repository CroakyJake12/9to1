using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;
using static Haven.Infrastructure.Tests.DeveloperOriginalWindowsTestHost;

namespace Haven.Infrastructure.Tests;

public sealed class DeveloperOriginalWindowsSavedProjectTests
{
    [WindowsNativeFact]
    public async Task Independent_current_read_pins_real_metadata_registration_root_without_old_setup_ack()
    {
        await using var rig = new Rig(); var capture = await rig.Capture(CancellationToken.None); var intent = rig.Intent(capture);
        Directory.CreateDirectory(rig.Store.OriginalWorkspaceMetadataDirectory);
        var metadata = Path.Combine(rig.Store.OriginalWorkspaceMetadataDirectory, intent.WorkspaceId.ToString("N") + ".json"); File.WriteAllBytes(metadata, Document(intent));
        var registration = Path.Combine(rig.Root, "current-registration.json");
        const string selected = "{\"originalSelectedPhysicalProject\":true}";
        var state = "{\"bindings\":[" + selected + "]}"; File.WriteAllText(registration, "{\"schemaVersion\":1,\"state\":" + state + "}");
        var issuer = new CurrentSelections(new Descriptor(rig, intent, registration, state, selected)); var reads = new CurrentReads(issuer, rig.Reads.Admission);
        var native = new WorkspaceToolService().CreateOriginalCurrentProjectNativeSource(() => issuer, reads, rig.Store); var retained = new List<Task>();
        try
        {
            var driver = native.CaptureOriginalWithinSourceAsync(issuer.Selection, rig.Reads.Admission, callback => callback(), retained.Add, CancellationToken.None);
            var read = await driver; Assert.True(native.IsIssuedOriginalRead(issuer.Selection, driver, read));
            Assert.False(native.IsIssuedOriginalRead(issuer.Selection, Task.CompletedTask, read));
            Assert.Equal(System.Text.Encoding.UTF8.GetString(Document(intent)), read.OriginalWorkspaceDocument);
            Assert.Equal(64, read.OriginalRegisteredRootFingerprint.Length); read.DemandOriginalExecutionBinding();
            Assert.ThrowsAny<IOException>(() => File.WriteAllText(metadata, "foreign metadata"));
            Assert.ThrowsAny<IOException>(() => File.WriteAllText(registration, "foreign registration"));
            await native.ValidateOriginalReadWithinSourceAsync(issuer.Selection, driver, read, callback => callback(), retained.Add, CancellationToken.None);
            var close = read.DisposeAsync().AsTask(); await close;
            Assert.True(native.IsClosedOriginalRead(issuer.Selection, driver, read, close));
            Assert.ThrowsAny<Exception>(() => read.DemandOriginalExecutionBinding());
            File.WriteAllText(metadata, "foreign metadata after exact original close");
            await Assert.ThrowsAnyAsync<Exception>(() => native.CaptureOriginalWithinSourceAsync(issuer.Selection, rig.Reads.Admission, callback => callback(), retained.Add, CancellationToken.None));
        }
        finally { try { await native.CloseAndDrainOriginalAsync(); } catch { } }
    }
    [WindowsNativeFact]
    public async Task Missing_current_original_read_issuer_refuses_before_native_document_capture()
    {
        await using var rig = new Rig(); var capture = await rig.Capture(CancellationToken.None); var intent = rig.Intent(capture);
        var issuer = new CurrentSelections(new Descriptor(rig, intent, Path.Combine(rig.Root, "absent-registration.json"), "{\"bindings\":[]}", "{}"));
        var reads = new CurrentReads(issuer, rig.Reads.Admission) { Issued = false };
        var native = new WorkspaceToolService().CreateOriginalCurrentProjectNativeSource(() => issuer, reads, rig.Store);
        try
        {
            var task = native.CaptureOriginalWithinSourceAsync(issuer.Selection, rig.Reads.Admission, callback => callback(), _ => { }, CancellationToken.None);
            var error = await Assert.ThrowsAnyAsync<Exception>(() => task);
            Assert.Contains(Causes(error), cause => cause is UnauthorizedAccessException);
            Assert.False(Directory.Exists(rig.Store.OriginalWorkspaceMetadataDirectory));
        }
        finally { await Assert.ThrowsAnyAsync<Exception>(() => native.CloseAndDrainOriginalAsync()); }
    }
    [WindowsNativeFact]
    public async Task Saved_execution_pin_retains_exact_acknowledged_documents_and_expires_after_actual_close()
    {
        await using var rig = new Rig(); var original = await Prepare(rig); var entry = await original.Permission.EnterOriginalStepAsync(original.Step, CancellationToken.None);
        var write = original.Preparation.CreateOriginalMetadataAsync(Document(original.Intent), entry, callback => callback(), _ => { }, CancellationToken.None);
        var observation = await write; await entry.DisposeAsync(); await original.Preparation.CloseAndDrainAsync();
        var registration = Path.Combine(rig.Root, "saved-registration.json"); const string state = "{\"bindings\":[]}";
        File.WriteAllText(registration, "{\"schemaVersion\":1,\"state\":" + state + "}");
        var binding = rig.SavedBindings.Actual = new Binding(original.Intent.WorkspaceId, original.Intent.ProjectId, original.Intent.RootId, rig.Project, original.Intent.OriginalActor);
        rig.SavedBindings.Descriptor = new Evidence(original.Preparation, write, observation, rig.Root, registration, state);
        var source = (IDeveloperWorkspaceOriginalExecutionCommitBindingSource)rig.Source;
        var pin = await source.AcquireOriginalExecutionPinAsync(binding, CancellationToken.None);
        Assert.True(source.IsIssuedOriginalExecutionPin(binding, pin)); pin.DemandOriginalExecutionBinding();
        Assert.ThrowsAny<IOException>(() => File.WriteAllText(registration, "foreign saved binding"));
        var metadata = Path.Combine(rig.Store.OriginalWorkspaceMetadataDirectory, binding.WorkspaceId.ToString("N") + ".json");
        Assert.ThrowsAny<IOException>(() => File.WriteAllText(metadata, "foreign workspace"));
        await pin.DisposeAsync(); Assert.False(source.IsIssuedOriginalExecutionPin(binding, pin));
        Assert.ThrowsAny<Exception>(() => pin.DemandOriginalExecutionBinding());
        File.WriteAllText(metadata, "actual replacement after pin close");
        await Assert.ThrowsAnyAsync<Exception>(() => source.AcquireOriginalExecutionPinAsync(binding, CancellationToken.None));
    }
    private sealed class Selection : IDeveloperOriginalCurrentProjectSelection { }
    private sealed class Descriptor(Rig rig, DeveloperProjectSetupIntent intent, string registration, string state, string selected)
        : IDeveloperOriginalCurrentProjectDescriptor
    {
        public IDeveloperProjectOriginalWorkspaceMetadataStore OriginalStore => rig.Store;
        public Guid WorkspaceId => intent.WorkspaceId; public Guid ProjectId => intent.ProjectId; public Guid RootId => intent.RootId;
        public long WorkspaceRevision => 1; public long ProjectRevision => 1; public string? RepositoryBindingId => null;
        public string ExactProjectReferenceJson => "{}";
        public Conversation OriginalConversation => null!; public ContainerDefinition OriginalContainer => null!;
        public AuthenticatedResourceActor OriginalActor => intent.OriginalActor;
        public string ConfiguredFilesRoot => rig.Root; public string RegisteredProjectRoot => rig.Project;
        public string OriginalRegistrationStatePath => registration; public string OriginalRegistrationStateJson => state;
        public string OriginalSelectedRegistrationJson => selected;
        public string? ExpectedWorkspaceDocumentSha256 => null; public string? ExpectedRegisteredRootFingerprint => null;
    }
    private sealed class CurrentSelections(Descriptor descriptor) : IDeveloperOriginalCurrentProjectSelectionSource
    {
        internal readonly Selection Selection = new();
        public bool IsIssuedOriginal(IDeveloperProjectOriginalReadSelection value) => ReferenceEquals(value, Selection);
        public Task RevalidateOriginalAsync(IDeveloperProjectOriginalReadSelection value, AuthenticatedResourceActor actor, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (!IsIssuedOriginal(value) || actor != descriptor.OriginalActor) throw new UnauthorizedAccessException(); return Task.CompletedTask; }
        public IReadOnlyList<ResourceScope> GetOriginalReadScopes(IDeveloperProjectOriginalReadSelection value) => throw new NotSupportedException("This native fixture grants no Home resource scope.");
        public void DemandExternalOriginalReadSelectionJoin() { }
        public void DemandExternalOriginalCurrentProjectJoin() { }
        public Task<IDeveloperOriginalCurrentProjectSelection> SelectOriginalWithinSourceAsync(Conversation conversation, ContainerDefinition container, string reference,
            string? expectedDocument, string? expectedRoot, Action<Action> scope, Action<Task> retain, CancellationToken token) => throw new NotSupportedException("No installed Files selection is simulated.");
        public IDeveloperOriginalCurrentProjectDescriptor GetOriginalDescriptor(IDeveloperOriginalCurrentProjectSelection value) => IsIssuedOriginal(value) ? descriptor : throw new UnauthorizedAccessException();
        public bool IsIssuedOriginalDescriptor(IDeveloperOriginalCurrentProjectSelection value, IDeveloperOriginalCurrentProjectDescriptor actual)
            => IsIssuedOriginal(value) && ReferenceEquals(actual, descriptor);
        public Task RevalidateOriginalWithinSourceAsync(IDeveloperOriginalCurrentProjectSelection value, AuthenticatedResourceActor actor,
            Action<Action> scope, Action<Task> retain, CancellationToken token)
        { Task? task = null; scope(() => { task = RevalidateOriginalAsync(value, actor, token); retain(task); }); return task!; }
    }
    private sealed class CurrentReads(CurrentSelections issuer, IDeveloperProjectOriginalReadAdmission admission) : IDeveloperOriginalCurrentProjectReadAdmissionSource
    {
        internal bool Issued = true;
        public bool IsIssuedOriginalCurrentProjectRead(IDeveloperOriginalCurrentProjectSelection selection, IDeveloperProjectOriginalReadAdmission actual)
            => Issued && issuer.IsIssuedOriginal(selection) && ReferenceEquals(actual, admission);
        public Task ValidateOriginalCurrentProjectReadWithinSourceAsync(IDeveloperOriginalCurrentProjectSelection selection, IDeveloperProjectOriginalReadAdmission actual,
            Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            scope(() => { token.ThrowIfCancellationRequested(); if (!IsIssuedOriginalCurrentProjectRead(selection, actual)) throw new UnauthorizedAccessException(); retain(Task.CompletedTask); });
            return Task.CompletedTask;
        }
    }
}
