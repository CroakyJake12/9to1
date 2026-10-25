using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Haven.Desktop.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Apps.Dev;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using HomePermissionRequest = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest;

namespace Haven.Desktop.Tests;

/// <summary>
/// Genuine Linux Home/Files/manual-publication controls. The maintained DI graph supplies the
/// actual Home broker, Files issuer, capture kernel, final-fence validator and atomic writer.
/// Each source READ and CREATE/EDIT WRITE receives a separate real Home decision. Fixture file IO
/// seeds pre-existing inputs or observes external interference; successful publication never uses
/// a test source, admission, fence or replacement writer. These are dependency tests, not Build/Sites
/// UI acceptance. Windows and deterministic interruption during anonymous staging remain separate
/// allocation requirements: no such pause/cleanup-fault seam is exposed by these production APIs.
/// </summary>
public sealed class FilesDeveloperOriginalSourcePublicationTests
{
    private const string Target = "site.project.json";
    private const string Reviewed = "{\"source\":\"reviewed\",\"revision\":1}\n";

    [LinuxPublicationFact]
    public Task Accepted_create_uses_distinct_absent_write_and_cold_captured_read_preserves_exact_bytes() => Run(async rig =>
    {
        var source = await rig.OpenReadAsync();
        Assert.DoesNotContain(source.Capture.OriginalFiles, item => item.RelativePath == Target);
        var (write, request) = await rig.CreateAdmissionAsync(source, Reviewed);
        Assert.True(write.RequiresAbsentTarget);
        Assert.Equal(HomeDeveloperProjectEditAdmissionSource.CreateAction, request.Scope.ActionName);
        Assert.NotNull(request.Impact.ResourceBinding);
        var scope = Assert.Single(request.Impact.ResourceBinding!.Scopes);
        Assert.Equal(ResourceAccess.Write, scope.Access);
        Assert.Equal("dev.project.file", scope.Kind);
        Assert.StartsWith("dev-file-create:", scope.Id);
        Assert.Equal("absent", scope.Revision);
        Assert.NotEqual(Hash(""), scope.Revision);
        Assert.Equal(Reviewed, write.ReviewedAfterText);
        Assert.Equal(Hash(Reviewed), write.ReviewedAfterSha256);
        Assert.Equal(Encoding.UTF8.GetByteCount(Reviewed), write.ReviewedAfterUtf8Length);
        var genuineFence = rig.RetainFence(rig.Edits.MintOriginalCreateFence(write, Target, Reviewed));
        rig.AssertGenuineManualComposition(genuineFence);
        Assert.Equal(Hash(Reviewed), await rig.Keep(rig.Kernel.ApplyOriginalCapturedFileCreateAsync(
            source.Capture, source.Read, write, Target, Reviewed, rig.Token)));
        Assert.Equal(Encoding.UTF8.GetBytes(Reviewed), await File.ReadAllBytesAsync(rig.TargetPath, rig.Token));
        await rig.Keep(write.DisposeAsync().AsTask());
        await rig.Keep(source.Read.DisposeAsync().AsTask());

        var reopened = await rig.OpenReadAsync();
        var file = Assert.Single(reopened.Capture.OriginalFiles, item => item.RelativePath == Target);
        Assert.Equal(Hash(Reviewed), file.ContentSha256);
        Assert.Equal(Reviewed, await rig.Keep(rig.Kernel.ReadOriginalCapturedFileTextAsync(
            reopened.Capture, reopened.Read, Target, rig.Token)));
        var observed = await rig.Keep(rig.Home.Permissions.ReadRequestObservationAsync(request.RequestId, rig.Token));
        Assert.NotNull(observed);
        Assert.Equal(HomePermissionRequestState.Succeeded, observed!.State);
        Assert.Equal(2, rig.Decisions.Count(item => item.Scope.ActionName == HomeDeveloperProjectReadAdmissionSource.ReadAction));
        Assert.Single(rig.Decisions, item => item.Scope.ActionName == HomeDeveloperProjectEditAdmissionSource.CreateAction);
    });

    [LinuxPublicationFact]
    public Task Existing_file_edit_retains_original_before_hash_and_real_write_pipeline() => Run(async rig =>
    {
        const string before = "{\"source\":\"existing\"}\n";
        var source = await rig.OpenReadAsync();
        var operation = rig.Keep(rig.Edits.AcquireOriginalEditAsync(source.Selection, source.Capture,
            Target, Hash(before), Reviewed, rig.Token));
        var request = await rig.DecideNextAsync(operation, HomeDeveloperProjectEditAdmissionSource.EditAction, HomeApprovalChoice.Accept);
        var write = await operation;
        Assert.False(write.RequiresAbsentTarget);
        Assert.Equal(Hash(before), Assert.Single(request.Impact.ResourceBinding!.Scopes).Revision);
        Assert.Equal(Hash(Reviewed), await rig.Keep(rig.Kernel.ApplyOriginalCapturedFileEditAsync(
            source.Capture, source.Read, write, Target, Hash(before), Reviewed, rig.Token)));
        Assert.Equal(Reviewed, await File.ReadAllTextAsync(rig.TargetPath, rig.Token));
        Assert.DoesNotContain(rig.Decisions, item => item.Scope.ActionName == HomeDeveloperProjectEditAdmissionSource.CreateAction);
    }, "{\"source\":\"existing\"}\n");

    [LinuxPublicationFact]
    public Task Same_edit_operation_cannot_adopt_a_different_reviewed_before_revision() => Run(async rig =>
    {
        const string before = "the exact existing source\n";
        var source = await rig.OpenReadAsync();
        var operation = rig.Keep(rig.Edits.AcquireOriginalEditAsync(source.Selection, source.Capture,
            Target, Hash(before), Reviewed, rig.Token));
        await rig.DecideNextAsync(operation, HomeDeveloperProjectEditAdmissionSource.EditAction, HomeApprovalChoice.Accept);
        await operation;
        await rig.RefusesAsync(() => rig.Edits.AcquireOriginalEditAsync(source.Selection, source.Capture,
            Target, Hash("different reviewed BEFORE"), Reviewed, rig.Token));
        Assert.Equal(before, await File.ReadAllTextAsync(rig.TargetPath, rig.Token));
        Assert.Single(rig.Decisions, item => item.Scope.ActionName == HomeDeveloperProjectEditAdmissionSource.EditAction);
    }, "the exact existing source\n");

    [LinuxPublicationFact]
    public Task Read_only_and_declined_create_do_not_publish_or_report_success() => Run(async rig =>
    {
        var source = await rig.OpenReadAsync();
        await rig.RefusesAsync(() => rig.Kernel.ApplyOriginalCapturedFileCreateAsync(
            source.Capture, source.Read, null!, Target, Reviewed, rig.Token));
        Assert.False(File.Exists(rig.TargetPath));
        var operation = rig.Keep(rig.Edits.AcquireOriginalCreateAsync(source.Selection, source.Capture, Target, Reviewed, rig.Token));
        var request = await rig.DecideNextAsync(operation, HomeDeveloperProjectEditAdmissionSource.CreateAction, HomeApprovalChoice.Decline);
        await rig.RefusesAsync(() => operation);
        Assert.False(File.Exists(rig.TargetPath));
        var observed = await rig.Keep(rig.Home.Permissions.ReadRequestObservationAsync(request.RequestId, rig.Token));
        Assert.NotNull(observed);
        Assert.Equal(HomePermissionRequestState.Denied, observed!.State);
        Assert.NotEqual(HomePermissionRequestState.Succeeded, observed.State);
    });

    [LinuxPublicationFact]
    public Task Existing_zero_byte_target_is_not_an_absent_target_or_a_create_grant() => Run(async rig =>
    {
        var source = await rig.OpenReadAsync();
        var existing = Assert.Single(source.Capture.OriginalFiles, item => item.RelativePath == Target);
        Assert.Equal(0, existing.SizeBytes);
        Assert.Equal(Hash(""), existing.ContentSha256);
        await rig.RefusesAsync(() => rig.Edits.AcquireOriginalCreateAsync(
            source.Selection, source.Capture, Target, Reviewed, rig.Token));
        Assert.Empty(await File.ReadAllBytesAsync(rig.TargetPath, rig.Token));
        Assert.DoesNotContain(rig.Decisions, item => item.Scope.ActionName == HomeDeveloperProjectEditAdmissionSource.CreateAction);
    }, "");

    [LinuxPublicationFact]
    public Task Create_cannot_create_a_missing_parent_directory() => Run(async rig =>
    {
        var source = await rig.OpenReadAsync();
        await rig.RefusesAsync(() => rig.Edits.AcquireOriginalCreateAsync(source.Selection, source.Capture,
            "not-captured/site.project.json", Reviewed, rig.Token), unsupportedCapability: true);
        Assert.False(Directory.Exists(Path.Combine(rig.ProjectRoot, "not-captured")));
        Assert.False(File.Exists(rig.TargetPath));
        Assert.DoesNotContain(rig.Decisions, item => item.Scope.ActionName == HomeDeveloperProjectEditAdmissionSource.CreateAction);
    });

    [LinuxPublicationFact]
    public Task Existing_captured_nested_parent_is_explicitly_unsupported_and_never_adopted() => Run(async rig =>
    {
        // This dependency intentionally supports direct project-root leaves only. An existing
        // captured folder is not a grant to adopt whichever inode later occupies its pathname.
        var nested = Directory.CreateDirectory(Path.Combine(rig.ProjectRoot, "nested")).FullName;
        var nestedSeed = Path.Combine(nested, "existing.txt");
        const string original = "existing nested source remains untouched\n";
        await File.WriteAllTextAsync(nestedSeed, original, rig.Token);
        var source = await rig.OpenReadAsync();
        Assert.Contains("nested", source.Capture.OriginalFolderPaths);
        Assert.Contains(source.Capture.OriginalFiles, item => item.RelativePath == "nested/existing.txt");
        await rig.RefusesAsync(() => rig.Edits.AcquireOriginalCreateAsync(source.Selection, source.Capture,
            "nested/" + Target, Reviewed, rig.Token), unsupportedCapability: true);
        Assert.False(File.Exists(Path.Combine(nested, Target)));
        Assert.Equal(original, await File.ReadAllTextAsync(nestedSeed, rig.Token));
        Assert.False(File.Exists(rig.TargetPath));
        Assert.DoesNotContain(rig.Decisions, item => item.Scope.ActionName == HomeDeveloperProjectEditAdmissionSource.CreateAction);
    });

    [LinuxPublicationFact]
    public Task Closed_real_write_admission_cannot_publish_later() => Run(async rig =>
    {
        var source = await rig.OpenReadAsync();
        var (write, request) = await rig.CreateAdmissionAsync(source, Reviewed);
        await rig.Keep(write.DisposeAsync().AsTask());
        await rig.AssertUnpublishedRequestAsync(request);
        await rig.RefusesAsync(() => rig.Kernel.ApplyOriginalCapturedFileCreateAsync(
            source.Capture, source.Read, write, Target, Reviewed, rig.Token));
        Assert.False(File.Exists(rig.TargetPath));
    });

    [LinuxPublicationFact]
    public Task Target_appearing_after_review_is_preserved_and_create_refuses() => Run(async rig =>
    {
        // This is a deterministic after-review/before-apply race, not a during-staging claim.
        var source = await rig.OpenReadAsync();
        var (write, _) = await rig.CreateAdmissionAsync(source, Reviewed);
        const string sentinel = "another writer owns this target\n";
        await File.WriteAllTextAsync(rig.TargetPath, sentinel, rig.Token);
        await rig.RefusesAsync(() => rig.Kernel.ApplyOriginalCapturedFileCreateAsync(
            source.Capture, source.Read, write, Target, Reviewed, rig.Token));
        Assert.Equal(sentinel, await File.ReadAllTextAsync(rig.TargetPath, rig.Token));
        Assert.Equal("existing independent source\n", await File.ReadAllTextAsync(rig.SeedPath, rig.Token));
    });

    [LinuxPublicationFact]
    public Task Reviewed_create_cannot_change_payload_or_target() => Run(async rig =>
    {
        var source = await rig.OpenReadAsync();
        var (write, request) = await rig.CreateAdmissionAsync(source, Reviewed);
        await rig.RefusesAsync(() => rig.Kernel.ApplyOriginalCapturedFileCreateAsync(
            source.Capture, source.Read, write, Target, Reviewed + "unreviewed", rig.Token));
        await rig.RefusesAsync(() => rig.Kernel.ApplyOriginalCapturedFileCreateAsync(
            source.Capture, source.Read, write, "different.json", Reviewed, rig.Token));
        Assert.False(File.Exists(rig.TargetPath));
        Assert.False(File.Exists(Path.Combine(rig.ProjectRoot, "different.json")));
        await rig.CloseUnpublishedWriteAsync(write, request);
    });

    [LinuxPublicationFact]
    public Task Another_real_Home_owner_cannot_validate_or_mint_from_foreign_create_admission() => Run(async first =>
    {
        await Run(async second =>
        {
            var source = await first.OpenReadAsync();
            var other = await second.OpenReadAsync();
            var (foreign, _) = await second.CreateAdmissionAsync(other, Reviewed);
            var foreignFence = second.RetainFence(second.Edits.MintOriginalCreateFence(foreign, Target, Reviewed));
            second.AssertGenuineManualComposition(foreignFence);
            Assert.False(first.FenceAuthority.IsIssuedOriginal(foreignFence));
            var writer = Assert.IsType<WorkspaceToolService>(first.Graph.GetRequiredService<IWorkspaceToolService>());
            var foreignFenceError = Assert.Throws<UnauthorizedAccessException>(() => writer.AcquireOriginalInvocation(foreignFence));
            first.Expect(foreignFenceError);
            await first.RefusesAsync(() => first.Edits.ValidateOriginalCreateAsync(
                source.Selection, source.Capture, Target, foreign, first.Token));
            var error = Assert.Throws<UnauthorizedAccessException>(() => first.Edits.MintOriginalCreateFence(foreign, Target, Reviewed));
            first.Expect(error);
            await first.RefusesAsync(() => first.Kernel.ApplyOriginalCapturedFileCreateAsync(
                source.Capture, source.Read, foreign, Target, Reviewed, first.Token));
            Assert.False(File.Exists(first.TargetPath));
            Assert.False(File.Exists(second.TargetPath));
        });
    });

    [LinuxPublicationFact]
    public Task Actual_caller_block_after_accept_refuses_the_uncompleted_create_claim() => Run(async rig =>
    {
        var source = await rig.OpenReadAsync();
        var (write, request) = await rig.CreateAdmissionAsync(source, Reviewed);
        Assert.True((await rig.Keep(rig.Home.Permissions.BlockCallerAsync(request.Caller.CallerId, rig.Token))).Succeeded);
        Assert.False(await rig.Keep(rig.Home.Permissions.IsExecutionCurrentAsync(request.RequestId, rig.Token)));
        await rig.RefusesAsync(() => rig.Kernel.ApplyOriginalCapturedFileCreateAsync(
            source.Capture, source.Read, write, Target, Reviewed, rig.Token));
        Assert.False(File.Exists(rig.TargetPath));
    });

    [LinuxPublicationFact]
    public Task Actual_native_root_replacement_after_accept_refuses_without_touching_replacement() => Run(async rig =>
    {
        var source = await rig.OpenReadAsync();
        var (write, _) = await rig.CreateAdmissionAsync(source, Reviewed);
        var heldRoot = rig.ProjectRoot + "-held-original";
        Directory.Move(rig.ProjectRoot, heldRoot);
        Directory.CreateDirectory(rig.ProjectRoot);
        const string sentinel = "replacement root belongs to another writer";
        await File.WriteAllTextAsync(rig.SeedPath, sentinel, rig.Token);
        try
        {
            await rig.RefusesAsync(() => rig.Kernel.ApplyOriginalCapturedFileCreateAsync(
                source.Capture, source.Read, write, Target, Reviewed, rig.Token));
            Assert.False(File.Exists(rig.TargetPath));
            Assert.Equal(sentinel, await File.ReadAllTextAsync(rig.SeedPath, rig.Token));
            Assert.False(File.Exists(Path.Combine(heldRoot, Target)));
        }
        catch { rig.PreserveStorage = true; throw; }
        finally
        {
            if (!rig.PreserveStorage)
            {
                Directory.Delete(rig.ProjectRoot, true);
                Directory.Move(heldRoot, rig.ProjectRoot);
            }
        }
    });

    [LinuxPublicationFact]
    public Task Existing_edit_refuses_changed_native_before_bytes_and_preserves_external_change() => Run(async rig =>
    {
        const string before = "reviewed BEFORE\n";
        var source = await rig.OpenReadAsync();
        var operation = rig.Keep(rig.Edits.AcquireOriginalEditAsync(source.Selection, source.Capture,
            Target, Hash(before), Reviewed, rig.Token));
        await rig.DecideNextAsync(operation, HomeDeveloperProjectEditAdmissionSource.EditAction, HomeApprovalChoice.Accept);
        var write = await operation;
        const string changed = "external revision after review\n";
        await File.WriteAllTextAsync(rig.TargetPath, changed, rig.Token);
        await rig.RefusesAsync(() => rig.Kernel.ApplyOriginalCapturedFileEditAsync(
            source.Capture, source.Read, write, Target, Hash(before), Reviewed, rig.Token));
        Assert.Equal(changed, await File.ReadAllTextAsync(rig.TargetPath, rig.Token));
    }, "reviewed BEFORE\n");

    [LinuxPublicationFact]
    public Task Registered_saved_project_create_edit_and_cold_reopen_keep_exact_canonical_source() => Run(async rig =>
    {
        var metadata = await File.ReadAllBytesAsync(rig.SavedMetadataPath, rig.Token);
        var registry = await File.ReadAllBytesAsync(rig.SavedRegistryPath, rig.Token);
        var source = await rig.OpenRegisteredReadAsync();
        rig.AssertRegisteredDescriptor(source);
        var (create, createRequest) = await rig.CreateAdmissionAsync(source, Reviewed);
        rig.AssertRegisteredWriteScope(createRequest);
        Assert.Equal(Hash(Reviewed), await rig.Keep(rig.Kernel.ApplyOriginalCapturedFileCreateAsync(
            source.Capture, source.Read, create, Target, Reviewed, rig.Token)));
        await rig.Keep(create.DisposeAsync().AsTask());
        await rig.Keep(source.Read.DisposeAsync().AsTask());

        const string edited = "{\"source\":\"same canonical saved project\",\"revision\":2}\n";
        var reopened = await rig.OpenRegisteredReadAsync();
        rig.AssertRegisteredDescriptor(reopened);
        Assert.Equal(Reviewed, await rig.Keep(rig.Kernel.ReadOriginalCapturedFileTextAsync(
            reopened.Capture, reopened.Read, Target, rig.Token)));
        var editing = rig.Keep(rig.Edits.AcquireOriginalEditAsync(reopened.Selection, reopened.Capture,
            Target, Hash(Reviewed), edited, rig.Token));
        var editRequest = await rig.DecideNextAsync(editing, HomeDeveloperProjectEditAdmissionSource.EditAction, HomeApprovalChoice.Accept);
        var edit = await editing;
        rig.AssertRegisteredWriteScope(editRequest);
        Assert.Equal(Hash(edited), await rig.Keep(rig.Kernel.ApplyOriginalCapturedFileEditAsync(
            reopened.Capture, reopened.Read, edit, Target, Hash(Reviewed), edited, rig.Token)));
        await rig.Keep(edit.DisposeAsync().AsTask());
        await rig.Keep(reopened.Read.DisposeAsync().AsTask());

        var cold = await rig.OpenRegisteredReadAsync();
        rig.AssertRegisteredDescriptor(cold);
        Assert.Equal(edited, await rig.Keep(rig.Kernel.ReadOriginalCapturedFileTextAsync(cold.Capture, cold.Read, Target, rig.Token)));
        Assert.Equal(metadata, await File.ReadAllBytesAsync(rig.SavedMetadataPath, rig.Token));
        Assert.Equal(registry, await File.ReadAllBytesAsync(rig.SavedRegistryPath, rig.Token));
        var listed = await rig.Keep(rig.SavedStore.ListAsync(rig.Token));
        Assert.True(listed.Succeeded);
        Assert.Equal(rig.Saved.Reference.WorkspaceId, Assert.Single(listed.Value!).WorkspaceId);
        Assert.False(File.Exists(Path.Combine(rig.ProjectRoot, ".9to1-sites-index.json")));
        Assert.Equal("existing independent source\n", await File.ReadAllTextAsync(rig.SeedPath, rig.Token));
    }, canonicalSavedProject: true);

    [LinuxPublicationFact]
    public Task Registered_saved_source_requires_a_real_unique_Files_registration() => Run(async rig =>
    {
        var metadata = await File.ReadAllBytesAsync(rig.SavedMetadataPath, rig.Token);
        await rig.RefusesAsync(() => rig.OpenRegisteredReadAsync());
        Assert.Empty(rig.Decisions);
        Assert.False(File.Exists(rig.TargetPath));
        Assert.Equal(metadata, await File.ReadAllBytesAsync(rig.SavedMetadataPath, rig.Token));
    }, canonicalSavedProject: true, registerSavedProject: false);

    [LinuxPublicationFact]
    public Task Registered_saved_source_refuses_duplicate_actual_persisted_registration_rows() => Run(async rig =>
    {
        // Copy one actual emitted row to simulate corrupt persisted registry state. No synthetic
        // owner, grant or native read is installed, and the real registry must reject ambiguity.
        var envelope = JsonNode.Parse(await File.ReadAllBytesAsync(rig.SavedRegistryPath, rig.Token))!.AsObject();
        var bindings = envelope["state"]!["bindings"]!.AsArray();
        var actual = Assert.Single(bindings, node => node?["owningAppId"]?.GetValue<string>() ==
            "dev.project." + rig.Saved.Reference.ProjectId.ToString("N"));
        bindings.Add(actual!.DeepClone());
        var corrupt = Encoding.UTF8.GetBytes(envelope.ToJsonString());
        await File.WriteAllBytesAsync(rig.SavedRegistryPath, corrupt, rig.Token);
        await rig.RefusesAsync(() => rig.OpenRegisteredReadAsync());
        Assert.Empty(rig.Decisions);
        Assert.False(File.Exists(rig.TargetPath));
        Assert.Equal(corrupt, await File.ReadAllBytesAsync(rig.SavedRegistryPath, rig.Token));
    }, canonicalSavedProject: true);

    [LinuxPublicationFact]
    public Task Registered_saved_source_never_falls_back_when_its_configured_saved_store_is_absent() => Run(async rig =>
    {
        // This genuine project/registration exists, but this issuer retained its original three-arg
        // path-only composition. The explicit registered API must refuse its unavailable owner.
        await rig.RefusesAsync(() => rig.OpenRegisteredReadAsync(), unsupportedCapability: true);
        Assert.Empty(rig.Decisions);
        Assert.False(File.Exists(rig.TargetPath));
    }, canonicalSavedProject: true, bindSavedStore: false);

    [LinuxPublicationFact]
    public Task Registered_saved_source_refuses_a_foreign_actual_store_workspace() => Run(async rig =>
    {
        // The other real store emits valid metadata with the same registered project/root. Its
        // distinct workspace does not exist in the configured canonical store and grants nothing.
        var foreign = await rig.CreateForeignSavedReferenceAsync();
        await rig.RefusesAsync(() => rig.OpenRegisteredReadAsync(foreign));
        Assert.False(File.Exists(rig.TargetPath));
        Assert.DoesNotContain(rig.Decisions, request => request.Scope.ActionName == HomeDeveloperProjectEditAdmissionSource.CreateAction);
    }, canonicalSavedProject: true);

    [LinuxPublicationFact]
    public Task Registered_saved_source_refuses_unregistered_project_identity() => Run(async rig =>
    {
        var otherId = Guid.NewGuid();
        var proposed = rig.Saved with
        {
            Reference = rig.Saved.Reference with { ProjectId = otherId },
            Project = rig.Saved.Project with { ProjectId = otherId }
        };
        await rig.RefusesAsync(() => rig.OpenRegisteredReadAsync(proposed));
        Assert.False(File.Exists(rig.TargetPath));
        Assert.Empty(rig.Decisions);
    }, canonicalSavedProject: true);

    [LinuxPublicationFact]
    public Task Registered_saved_source_refuses_an_existing_but_different_root() => Run(async rig =>
    {
        var wrongRoot = Directory.CreateDirectory(rig.ProjectRoot + "-other").FullName;
        var sentinel = Path.Combine(wrongRoot, "existing.txt");
        await File.WriteAllTextAsync(sentinel, "another root remains unchanged", rig.Token);
        var proposed = rig.Saved with { Root = rig.Saved.Root with { Location = wrongRoot } };
        await rig.RefusesAsync(() => rig.OpenRegisteredReadAsync(proposed));
        Assert.False(File.Exists(Path.Combine(wrongRoot, Target)));
        Assert.Equal("another root remains unchanged", await File.ReadAllTextAsync(sentinel, rig.Token));
        Assert.False(File.Exists(rig.TargetPath));
    }, canonicalSavedProject: true);

    [LinuxPublicationFact]
    public Task Registered_saved_source_refuses_changed_repository_identity_with_the_same_binding_and_root() => Run(async rig =>
    {
        var repository = Assert.IsType<DeveloperSourceControlBinding>(rig.Saved.Repository);
        var changed = repository with { CanonicalRepositoryId = "another-canonical-repository" };
        var proposed = rig.Saved with
        {
            Repository = changed,
            Workspace = rig.Saved.Workspace with { SourceControlBindings = [changed] }
        };
        await rig.RefusesAsync(() => rig.OpenRegisteredReadAsync(proposed));
        Assert.False(File.Exists(rig.TargetPath));
        Assert.Single(rig.Decisions, request => request.Scope.ActionName == HomeDeveloperProjectReadAdmissionSource.ReadAction);
        Assert.DoesNotContain(rig.Decisions, request => request.Scope.ActionName == HomeDeveloperProjectEditAdmissionSource.CreateAction);
    }, canonicalSavedProject: true);

    [LinuxPublicationFact]
    public Task Coherent_public_saved_project_snapshot_cannot_override_real_metadata_after_read_accept() => Run(async rig =>
    {
        var changed = rig.Saved.Project with { Name = "coherent caller snapshot but not the saved project" };
        var proposed = rig.Saved with { Project = changed, Workspace = rig.Saved.Workspace with { Projects = [changed] } };
        await rig.RefusesAsync(() => rig.OpenRegisteredReadAsync(proposed));
        Assert.Single(rig.Decisions, request => request.Scope.ActionName == HomeDeveloperProjectReadAdmissionSource.ReadAction);
        Assert.DoesNotContain(rig.Decisions, request => request.Scope.ActionName == HomeDeveloperProjectEditAdmissionSource.CreateAction);
        Assert.False(File.Exists(rig.TargetPath));
        var actual = await rig.Keep(rig.SavedStore.GetAsync(rig.Saved.Reference.WorkspaceId, rig.Token));
        Assert.True(actual.Succeeded);
        Assert.Equal(rig.Saved.Project.Name, Assert.Single(actual.Value!.Projects).Name);
    }, canonicalSavedProject: true);

    [LinuxPublicationFact]
    public Task Registered_saved_metadata_is_not_parsed_before_the_actual_home_read_decision() => Run(async rig =>
    {
        var original = await File.ReadAllBytesAsync(rig.SavedMetadataPath, rig.Token);
        const string invalid = "this is not a workspace JSON document";
        await File.WriteAllTextAsync(rig.SavedMetadataPath, invalid, rig.Token);
        try
        {
            // The configured directory and real registration are known, while metadata content is
            // deliberately invalid. Selection and the pending prompt must precede native parsing.
            var selection = await rig.Keep(rig.Selections.SelectOriginalRegisteredProjectAsync(
                rig.FilesStoreId, rig.Saved, rig.Token));
            var acquiring = rig.Keep(rig.Reads.AcquireOriginalAsync(selection, rig.Token));
            var pending = await rig.ObservePendingAsync(acquiring, HomeDeveloperProjectReadAdmissionSource.ReadAction);
            Assert.False(acquiring.IsCompleted);
            Assert.Equal(invalid, await File.ReadAllTextAsync(rig.SavedMetadataPath, rig.Token));
            Assert.True((await rig.Keep(rig.Home.Permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept,
                cancellationToken: rig.Token))).Succeeded);
            rig.Decisions.Add(pending);
            await rig.RefusesInvalidSavedMetadataAsync(async () =>
            {
                var read = await acquiring;
                await rig.Keep(rig.Selections.CaptureOriginalAsync(selection, read, rig.Token));
            });
            Assert.Single(rig.Decisions, request => request.Scope.ActionName == HomeDeveloperProjectReadAdmissionSource.ReadAction);
            Assert.False(File.Exists(rig.TargetPath));
            Assert.DoesNotContain(rig.Decisions, request => request.Scope.ActionName == HomeDeveloperProjectEditAdmissionSource.CreateAction);
        }
        catch { rig.PreserveStorage = true; throw; }
        finally { if (!rig.PreserveStorage) await File.WriteAllBytesAsync(rig.SavedMetadataPath, original, rig.Token); }
    }, canonicalSavedProject: true);

    [LinuxPublicationFact]
    public Task Registered_saved_source_refuses_actual_saved_revision_change_after_write_review() => Run(async rig =>
    {
        var source = await rig.OpenRegisteredReadAsync();
        var (write, request) = await rig.CreateAdmissionAsync(source, Reviewed);
        var saved = await rig.Keep(rig.SavedStore.SaveAsync(rig.Saved.Workspace with
        {
            Projects = [rig.Saved.Project with { Revision = rig.Saved.Project.Revision + 1 }]
        }, rig.Saved.Workspace.Revision, rig.Token));
        Assert.True(saved.Succeeded);
        Assert.True(saved.Value!.Revision > rig.Saved.Workspace.Revision);
        var changed = await File.ReadAllBytesAsync(rig.SavedMetadataPath, rig.Token);
        await rig.RefusesAsync(() => rig.Kernel.ApplyOriginalCapturedFileCreateAsync(
            source.Capture, source.Read, write, Target, Reviewed, rig.Token));
        Assert.False(File.Exists(rig.TargetPath));
        Assert.Equal(changed, await File.ReadAllBytesAsync(rig.SavedMetadataPath, rig.Token));
        await rig.CloseUnpublishedWriteAsync(write, request);
    }, canonicalSavedProject: true);

    [LinuxPublicationFact]
    public Task Registered_saved_source_refuses_same_bytes_metadata_inode_replacement_after_review() => Run(async rig =>
    {
        var source = await rig.OpenRegisteredReadAsync();
        var (write, request) = await rig.CreateAdmissionAsync(source, Reviewed);
        var replacement = await rig.ReplaceObservedFileAsync(rig.SavedMetadataPath);
        try
        {
            await rig.RefusesAsync(() => rig.Kernel.ApplyOriginalCapturedFileCreateAsync(
                source.Capture, source.Read, write, Target, Reviewed, rig.Token));
            Assert.False(File.Exists(rig.TargetPath));
            Assert.Equal(replacement.Bytes, await File.ReadAllBytesAsync(replacement.Path, rig.Token));
            await rig.CloseUnpublishedWriteAsync(write, request);
        }
        catch { rig.PreserveStorage = true; throw; }
        finally { if (!rig.PreserveStorage) rig.RestoreObservedFile(replacement); }
    }, canonicalSavedProject: true);

    [LinuxPublicationFact]
    public Task Registered_saved_source_refuses_same_bytes_registry_inode_replacement_after_review() => Run(async rig =>
    {
        var source = await rig.OpenRegisteredReadAsync();
        var (write, request) = await rig.CreateAdmissionAsync(source, Reviewed);
        var replacement = await rig.ReplaceObservedFileAsync(rig.SavedRegistryPath);
        try
        {
            await rig.RefusesAsync(() => rig.Kernel.ApplyOriginalCapturedFileCreateAsync(
                source.Capture, source.Read, write, Target, Reviewed, rig.Token));
            Assert.False(File.Exists(rig.TargetPath));
            Assert.Equal(replacement.Bytes, await File.ReadAllBytesAsync(replacement.Path, rig.Token));
            await rig.CloseUnpublishedWriteAsync(write, request);
        }
        catch { rig.PreserveStorage = true; throw; }
        finally { if (!rig.PreserveStorage) rig.RestoreObservedFile(replacement); }
    }, canonicalSavedProject: true);

    [LinuxPublicationFact]
    public Task Registered_saved_source_refuses_actual_registration_revision_change_after_review() => Run(async rig =>
    {
        var source = await rig.OpenRegisteredReadAsync();
        var (write, request) = await rig.CreateAdmissionAsync(source, Reviewed);
        await rig.RegisterSavedProjectAsync();
        var changed = await File.ReadAllBytesAsync(rig.SavedRegistryPath, rig.Token);
        await rig.RefusesAsync(() => rig.Kernel.ApplyOriginalCapturedFileCreateAsync(
            source.Capture, source.Read, write, Target, Reviewed, rig.Token));
        Assert.False(File.Exists(rig.TargetPath));
        Assert.Equal(changed, await File.ReadAllBytesAsync(rig.SavedRegistryPath, rig.Token));
        await rig.CloseUnpublishedWriteAsync(write, request);
    }, canonicalSavedProject: true);

    [LinuxPublicationFact]
    public Task Registered_native_source_refuses_foreign_actual_Files_issuer_and_saved_store_pairs() => Run(async first =>
    {
        await Run(async second =>
        {
            var firstSource = await first.OpenRegisteredReadAsync();
            var secondSource = await second.OpenRegisteredReadAsync();
            first.AssertRegisteredDescriptor(firstSource);
            second.AssertRegisteredDescriptor(secondSource);
            Assert.NotSame(first.Selections, second.Selections);
            Assert.NotSame(first.SavedStore, second.SavedStore);
            var firstMetadata = await File.ReadAllBytesAsync(first.SavedMetadataPath, first.Token);
            var secondMetadata = await File.ReadAllBytesAsync(second.SavedMetadataPath, second.Token);

            // A genuine READ from the first graph cannot be paired with the second graph's
            // genuine descriptor supplier. This is an owner mismatch, not a forged interface.
            var foreignIssuer = first.RetainRegisteredNativeSource(first.Kernel.CreateOriginalRegisteredProjectNativeSource(
                () => second.Selections, second.SavedStore));
            await first.RefusesForeignRegisteredOwnerAsync(() => foreignIssuer.CaptureRegisteredOriginalWithinSourceAsync(
                firstSource.Selection, firstSource.Read, body => body(), task => first.Keep(task), first.Token));

            // Keeping the correct Files issuer still cannot substitute another actual store.
            var foreignStore = first.RetainRegisteredNativeSource(first.Kernel.CreateOriginalRegisteredProjectNativeSource(
                () => first.Selections, second.SavedStore));
            await first.RefusesForeignRegisteredOwnerAsync(() => foreignStore.CaptureRegisteredOriginalWithinSourceAsync(
                firstSource.Selection, firstSource.Read, body => body(), task => first.Keep(task), first.Token));
            await first.CloseRegisteredNativeSourcesAsync();

            Assert.Single(first.Decisions, item => item.Scope.ActionName == HomeDeveloperProjectReadAdmissionSource.ReadAction);
            Assert.Single(second.Decisions, item => item.Scope.ActionName == HomeDeveloperProjectReadAdmissionSource.ReadAction);
            Assert.DoesNotContain(first.Decisions, item => item.Scope.ActionName == HomeDeveloperProjectEditAdmissionSource.CreateAction);
            Assert.DoesNotContain(second.Decisions, item => item.Scope.ActionName == HomeDeveloperProjectEditAdmissionSource.CreateAction);
            Assert.False(File.Exists(first.TargetPath));
            Assert.False(File.Exists(second.TargetPath));
            Assert.Equal(firstMetadata, await File.ReadAllBytesAsync(first.SavedMetadataPath, first.Token));
            Assert.Equal(secondMetadata, await File.ReadAllBytesAsync(second.SavedMetadataPath, second.Token));
        }, canonicalSavedProject: true);
    }, canonicalSavedProject: true);

    [LinuxPublicationFact]
    public async Task Manual_editor_CREATE_and_cold_read_use_real_registered_absent_approval_for_nonempty_and_empty_files()
    {
        foreach (var reviewed in new[] { Reviewed, "" })
            await Run(async rig =>
            {
                var owner = ManualEditor(rig);
                var preview = rig.Keep(owner.PreviewCreateAsync(rig.Saved, Target, rig.Token));
                await rig.DecideNextAsync(preview, HomeDeveloperProjectReadAdmissionSource.ReadAction, HomeApprovalChoice.Accept);
                await preview; Assert.False(File.Exists(rig.TargetPath));
                Assert.DoesNotContain(rig.Decisions, request => request.Scope.ActionName == HomeDeveloperProjectEditAdmissionSource.CreateAction);
                var create = rig.Keep(owner.CreateAsync(rig.Saved, Target, reviewed, rig.Token));
                await rig.DecideNextAsync(create, HomeDeveloperProjectReadAdmissionSource.ReadAction, HomeApprovalChoice.Accept);
                var request = await rig.DecideNextAsync(create, HomeDeveloperProjectEditAdmissionSource.CreateAction, HomeApprovalChoice.Accept);
                Assert.Equal(DeveloperProjectSourceWriteRevision.Absent, Assert.Single(request.Impact.ResourceBinding!.Scopes).Revision);
                Assert.Equal(Hash(reviewed), await create);
                var cold = rig.Keep(owner.ReadAsync(rig.Saved, Target, rig.Token));
                await rig.DecideNextAsync(cold, HomeDeveloperProjectReadAdmissionSource.ReadAction, HomeApprovalChoice.Accept);
                var read = await cold; Assert.Equal(reviewed, read.Text); Assert.Equal(Hash(reviewed), read.Sha256);
                Assert.Equal(Encoding.UTF8.GetBytes(reviewed), await File.ReadAllBytesAsync(rig.TargetPath, rig.Token));
                Assert.Equal(3, rig.Decisions.Count(decision => decision.Scope.ActionName == HomeDeveloperProjectReadAdmissionSource.ReadAction));
                Assert.Single(rig.Decisions, decision => decision.Scope.ActionName == HomeDeveloperProjectEditAdmissionSource.CreateAction);
            }, canonicalSavedProject: true);
    }

    [LinuxPublicationFact]
    public Task Manual_editor_CREATE_decline_closes_actual_admissions_without_publishing() => Run(async rig =>
    {
        var create = rig.Keep(ManualEditor(rig).CreateAsync(rig.Saved, Target, Reviewed, rig.Token));
        await rig.DecideNextAsync(create, HomeDeveloperProjectReadAdmissionSource.ReadAction, HomeApprovalChoice.Accept);
        var request = await rig.DecideNextAsync(create, HomeDeveloperProjectEditAdmissionSource.CreateAction, HomeApprovalChoice.Decline);
        await rig.RefusesAsync(() => create); Assert.False(File.Exists(rig.TargetPath));
        var observed = await rig.Keep(rig.Home.Permissions.ReadRequestObservationAsync(request.RequestId, rig.Token));
        Assert.Equal(HomePermissionRequestState.Denied, observed!.State);
    }, canonicalSavedProject: true);

    [LinuxPublicationFact]
    public Task Manual_editor_CREATE_refuses_empty_file_appearing_after_review_without_requesting_overwrite() => Run(async rig =>
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This interference fixture changes actual Linux file modes.");
        var owner = ManualEditor(rig);
        var preview = rig.Keep(owner.PreviewCreateAsync(rig.Saved, Target, rig.Token));
        await rig.DecideNextAsync(preview, HomeDeveloperProjectReadAdmissionSource.ReadAction, HomeApprovalChoice.Accept); await preview;
        await File.WriteAllTextAsync(rig.TargetPath, "", rig.Token); // external interference, never the publication writer
        File.SetUnixFileMode(rig.TargetPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var create = rig.Keep(owner.CreateAsync(rig.Saved, Target, Reviewed, rig.Token));
        await rig.DecideNextAsync(create, HomeDeveloperProjectReadAdmissionSource.ReadAction, HomeApprovalChoice.Accept);
        await rig.RefusesAsync(() => create);
        Assert.Equal("", await File.ReadAllTextAsync(rig.TargetPath, rig.Token));
        Assert.DoesNotContain(rig.Decisions, decision => decision.Scope.ActionName == HomeDeveloperProjectEditAdmissionSource.CreateAction);
    }, canonicalSavedProject: true);

    [LinuxPublicationFact]
    public Task Manual_editor_CREATE_refuses_a_substituted_saved_root_before_read_or_write_approval() => Run(async rig =>
    {
        var substitute = rig.Saved with { Root = rig.Saved.Root with { Location = Path.Combine(rig.ProjectRoot, "different-root") } };
        await rig.RefusesAsync(() => ManualEditor(rig).PreviewCreateAsync(substitute, Target, rig.Token));
        Assert.Empty(rig.Decisions); Assert.False(File.Exists(rig.TargetPath));
    }, canonicalSavedProject: true);

    private static DeveloperManualSourceEditOwner ManualEditor(Rig rig) => new(
        rig.Graph.GetRequiredService<NativeFilesWorkspaceService>(), rig.Selections, rig.Reads, rig.Edits, rig.Kernel);

    [LinuxPublicationFact]
    public Task Manual_console_Run_without_Task_uses_real_Home_review_executor_output_and_releases_Home_before_exit() => Run(async rig =>
    {
        var choice = Assert.Single(DeveloperRunConfigurationSelection.Observe(rig.Saved).Value!);
        var operation = rig.Keep(rig.ProcessOwner.RunAsync(rig.Saved, choice, body => body(), rig.Token));
        await rig.DecideNextAsync(operation, HomeDeveloperProjectReadAdmissionSource.ReadAction, HomeApprovalChoice.Accept);
        var request = await rig.DecideNextAsync(operation, HomeDeveloperManualProjectExecutionAdmissionSource.RunAction, HomeApprovalChoice.Accept);
        var scope = Assert.Single(request.Impact.ResourceBinding!.Scopes);
        Assert.Equal("dev.project.console", scope.Kind); Assert.Equal(ResourceAccess.Execute, scope.Access);
        Assert.False(File.Exists(Path.Combine(rig.ProjectRoot, "manual-finished.txt")));
        var deadline = Stopwatch.StartNew();
        while (!File.Exists(Path.Combine(rig.ProjectRoot, "manual-started.txt")) && deadline.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(10, rig.Token);
        Assert.True(File.Exists(Path.Combine(rig.ProjectRoot, "manual-started.txt")));
        Assert.False(operation.IsCompleted);
        // This actual Home store read must complete while the original process is still waiting.
        // Holding the Home entry until process exit would block it for the declared two seconds.
        await rig.Keep(rig.Home.Permissions.GetSnapshotAsync(cancellationToken: rig.Token)).WaitAsync(TimeSpan.FromSeconds(1), rig.Token);
        Assert.False(operation.IsCompleted);
        var observed = await operation;
        Assert.True(observed.ProcessSucceeded); Assert.True(observed.CleanupCompleted); Assert.Empty(observed.OriginalErrors);
        Assert.NotNull(observed.OperationId); Assert.NotEqual(Guid.Empty, observed.OperationId);
        Assert.Equal(choice.Configuration.Id, observed.ConfigurationId); Assert.Equal(choice.DefinitionSha256, observed.DefinitionSha256);
        Assert.Equal("manual-ready", observed.Process!.StandardOutput); Assert.Equal("manual-error", observed.Process.StandardError);
        Assert.Equal(0, observed.Process.ExitCode); Assert.False(observed.Process.TimedOut);
        var effect = Assert.Single(observed.PhysicalOutcome!.Effects);
        Assert.Equal(WorkspaceToolEffectKind.ProcessStart, effect.Kind); Assert.Equal(rig.ProjectRoot, effect.CanonicalTarget);
        Assert.True(effect.Admitted && effect.EffectKnown && effect.TerminalOutcomeKnown); Assert.NotNull(effect.ProcessId);
        Assert.Equal(HomePermissionRequestState.Succeeded, (await rig.Keep(rig.Home.Permissions.ReadRequestObservationAsync(request.RequestId, rig.Token)))!.State);
        Assert.Null(rig.Graph.GetService<Func<IWorkspaceOriginalProcessStartConsentSource>>());
        Assert.Same(rig.Runs, rig.Graph.GetRequiredService<IWorkspaceManualProcessFinalFenceValidator>());
        Assert.DoesNotContain(rig.Decisions, item => item.Scope.ActionName is HomeDeveloperProjectEditAdmissionSource.EditAction or HomeDeveloperProjectEditAdmissionSource.CreateAction);
    }, canonicalSavedProject: true, manualRunCommand: "printf started > manual-started.txt; sleep 2; printf 'manual-ready'; printf 'manual-error' >&2; printf finished > manual-finished.txt");

    [LinuxPublicationFact]
    public Task Manual_console_Run_declined_execution_preserves_READ_and_never_starts_the_command() => Run(async rig =>
    {
        var choice = Assert.Single(DeveloperRunConfigurationSelection.Observe(rig.Saved).Value!);
        var operation = rig.Keep(rig.ProcessOwner.RunAsync(rig.Saved, choice, body => body(), rig.Token));
        await rig.DecideNextAsync(operation, HomeDeveloperProjectReadAdmissionSource.ReadAction, HomeApprovalChoice.Accept);
        await rig.DecideNextAsync(operation, HomeDeveloperManualProjectExecutionAdmissionSource.RunAction, HomeApprovalChoice.Decline);
        var observed = await operation;
        Assert.False(observed.ProcessSucceeded); Assert.Null(observed.Process); Assert.Null(observed.PhysicalOutcome);
        Assert.NotEmpty(observed.OriginalErrors);
        foreach (var error in observed.OriginalErrors)
        {
            Assert.All(OriginalLeaves(error), cause => Assert.IsType<UnauthorizedAccessException>(cause));
            rig.Expect(error);
        }
        Assert.False(File.Exists(Path.Combine(rig.ProjectRoot, "manual-started.txt")));
        Assert.Single(rig.Decisions, item => item.Scope.ActionName == HomeDeveloperProjectReadAdmissionSource.ReadAction);
        Assert.Single(rig.Decisions, item => item.Scope.ActionName == HomeDeveloperManualProjectExecutionAdmissionSource.RunAction);
    }, canonicalSavedProject: true, manualRunCommand: "printf started > manual-started.txt");

    [LinuxPublicationFact]
    public Task Manual_Run_retirement_stops_the_original_process_before_its_declared_timeout_and_preserves_cancellation() => Run(async rig =>
    {
        var choice = Assert.Single(DeveloperRunConfigurationSelection.Observe(rig.Saved).Value!);
        var operation = rig.Keep(rig.ProcessOwner.RunAsync(rig.Saved, choice, body => body(), rig.Token));
        await rig.DecideNextAsync(operation, HomeDeveloperProjectReadAdmissionSource.ReadAction, HomeApprovalChoice.Accept);
        await rig.DecideNextAsync(operation, HomeDeveloperManualProjectExecutionAdmissionSource.RunAction, HomeApprovalChoice.Accept);
        var started = Path.Combine(rig.ProjectRoot, "manual-started.txt"); var deadline = Stopwatch.StartNew();
        while (!File.Exists(started) && deadline.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10, rig.Token);
        Assert.True(File.Exists(started)); Assert.False(operation.IsCompleted);
        rig.ProcessOwner.RequestOriginalRetirement();
        var observed = await operation.WaitAsync(TimeSpan.FromSeconds(5), rig.Token);
        Assert.False(observed.ProcessSucceeded); Assert.NotEmpty(observed.OriginalErrors);
        Assert.False(File.Exists(Path.Combine(rig.ProjectRoot, "manual-finished.txt")));
        Assert.NotNull(observed.PhysicalOutcome); Assert.False(observed.PhysicalOutcome!.KnownNoEffect);
        Assert.Contains(observed.PhysicalOutcome.Effects, effect => effect.Kind == WorkspaceToolEffectKind.ProcessStart && effect.Admitted);
        foreach (var error in observed.OriginalErrors)
        {
            Assert.All(OriginalLeaves(error), cause => Assert.IsAssignableFrom<OperationCanceledException>(cause));
            rig.Expect(error); // Only these exact observed cancellations are expected at owner drain.
        }
        Assert.Throws<InvalidOperationException>(() => { _ = rig.ProcessOwner.RunAsync(rig.Saved, choice, body => body(), rig.Token); });
    }, canonicalSavedProject: true, manualRunCommand: "printf started > manual-started.txt; sleep 20; printf finished > manual-finished.txt");

    [LinuxPublicationFact]
    public Task Manual_Run_cannot_adopt_changed_saved_definition_after_explicit_accept() => Run(async rig =>
    {
        var source = await rig.OpenRegisteredReadAsync();
        var choice = Assert.Single(DeveloperRunConfigurationSelection.Observe(rig.Saved).Value!);
        var binding = await rig.Keep(rig.Selections.CaptureOriginalManualExecutionAsync(source.Selection, source.Read, source.Capture,
            choice.Configuration.Id, choice.Configuration.Revision, choice.DefinitionSha256, rig.Token));
        var acquisition = rig.Keep(rig.Runs.AcquireOriginalManualExecutionAsync(binding, rig.Token));
        var request = await rig.DecideNextAsync(acquisition, HomeDeveloperManualProjectExecutionAdmissionSource.RunAction, HomeApprovalChoice.Accept);
        var admission = await acquisition;
        Assert.Null(admission.OriginalFence.OriginalAttempt); Assert.True(rig.FenceAuthority.IsIssuedOriginal(admission.OriginalFence));
        var legacy = new WorkspaceTaskRunEffectAuthority(rig.Graph.GetRequiredService<TaskRunPermissionAuthority>(),
            rig.Graph.GetRequiredService<IPermissionDecisionEngine>(), rig.Graph.GetRequiredService<IPermissionOriginalEffectFence>());
        Assert.False(legacy.IsIssuedOriginal(admission.OriginalFence));
        var document = JsonNode.Parse(await File.ReadAllTextAsync(rig.SavedMetadataPath, rig.Token))!;
        document["workspace"]!["runConfigurations"]![0]!["command"] = "printf replacement > manual-started.txt";
        await File.WriteAllTextAsync(rig.SavedMetadataPath, document.ToJsonString(), rig.Token);
        var physical = Assert.IsAssignableFrom<IWorkspaceOriginalInvocationSource>(rig.Graph.GetRequiredService<IWorkspaceToolService>());
        var invocation = rig.RetainInvocation(physical.AcquireOriginalInvocation(admission.OriginalFence));
        await rig.RefusesAsync(() => invocation.Tools.RunProcessAsync(WorkspaceToolProcessRequestFactory.CreateOriginal(
            binding.CanonicalRoot, binding.Command, binding.TimeoutSeconds), rig.Token));
        var outcome = await rig.Keep(invocation.CompleteOriginalAsync(false, rig.Token));
        Assert.True(physical.ValidateOriginalOutcome(invocation, outcome)); Assert.True(outcome.KnownNoEffect);
        Assert.All(outcome.Effects, effect => Assert.False(effect.Admitted)); Assert.False(File.Exists(Path.Combine(rig.ProjectRoot, "manual-started.txt")));
        admission.ObserveOriginalProcessOutcome(invocation, null, outcome);
        await rig.CloseUnpublishedRunAsync(admission, request);
    }, canonicalSavedProject: true, manualRunCommand: "printf original > manual-started.txt");

    [LinuxPublicationFact]
    public Task Newly_registered_manual_project_can_review_save_then_run_its_first_console_declaration_without_a_Task() => Run(async rig =>
    {
        Assert.Empty(rig.Saved.Project.RunConfigurationIds);
        var graph = rig.Graph;
        var reader = new DeveloperManualSourceReadOwner(graph.GetRequiredService<NativeFilesWorkspaceService>(), rig.Selections, rig.Reads, rig.Kernel);
        var reviewed = DeveloperRunConfigurationEdit.Prepare(rig.Saved, "first-run", "printf 'newly configured'", 20).Value!;
        var update = rig.Keep(reader.SaveReviewedRunConfigurationAsync(rig.SavedStore, null,
            graph.GetRequiredService<IProviderSecretStore>(), graph.GetRequiredService<IAppPaths>(), rig.Saved, reviewed, 0, body => body(), rig.Token));
        await rig.DecideNextAsync(update, HomeDeveloperProjectReadAdmissionSource.ReadAction, HomeApprovalChoice.Accept);
        var observed = await update; Assert.Empty(observed.OriginalErrors);
        Assert.NotNull(observed.OriginalStoreResult); Assert.True(observed.OriginalStoreResult!.Succeeded, observed.OriginalStoreResult.Error?.Message);
        var saved = observed.OriginalStoreResult.Value!; Assert.False(saved.MigratedRecovery);
        Assert.Equal(rig.Saved.Reference.WorkspaceRevision + 1, saved.Project.Reference.WorkspaceRevision);
        Assert.Equal(rig.Saved.Root, saved.Project.Root); Assert.Equal(rig.Saved.Repository, saved.Project.Repository);
        Assert.DoesNotContain(rig.Decisions, decision => decision.Scope.ActionName == HomeDeveloperManualProjectExecutionAdmissionSource.RunAction);
        var selected = Assert.Single(DeveloperRunConfigurationSelection.Observe(saved.Project).Value!);
        var operation = rig.Keep(rig.ProcessOwner.RunAsync(saved.Project, selected, body => body(), rig.Token));
        await rig.DecideNextAsync(operation, HomeDeveloperProjectReadAdmissionSource.ReadAction, HomeApprovalChoice.Accept);
        await rig.DecideNextAsync(operation, HomeDeveloperManualProjectExecutionAdmissionSource.RunAction, HomeApprovalChoice.Accept);
        var process = await operation;
        Assert.True(process.ProcessSucceeded); Assert.Equal("newly configured", process.Process!.StandardOutput);
        Assert.Equal(selected.DefinitionSha256, process.DefinitionSha256); Assert.Empty(process.OriginalErrors);
        Assert.Equal("existing independent source\n", await File.ReadAllTextAsync(rig.SeedPath, rig.Token));
    }, canonicalSavedProject: true);

    [LinuxPublicationFact]
    public Task Manual_start_pin_and_canonical_metadata_update_reservations_exclude_each_other_until_actual_native_close() => Run(async rig =>
    {
        var source = await rig.OpenRegisteredReadAsync();
        var choice = Assert.Single(DeveloperRunConfigurationSelection.Observe(rig.Saved).Value!);
        Task<IDeveloperProjectOriginalManualExecutionBinding> Bind() => rig.Keep(rig.Selections.CaptureOriginalManualExecutionAsync(
            source.Selection, source.Read, source.Capture, choice.Configuration.Id, choice.Configuration.Revision, choice.DefinitionSha256, rig.Token));
        var first = await Bind(); var pin = await rig.Keep(rig.Selections.AcquireOriginalManualExecutionPinAsync(first, rig.Token));
        var actor = rig.Selections.GetOriginalRegisteredDescriptor(source.Selection).OriginalActor;
        Assert.Throws<InvalidOperationException>(() => rig.Selections.AcquireOriginalManualMetadataUpdate(source.Selection, source.Capture, actor));
        await rig.Keep(rig.Selections.CloseOriginalManualExecutionPinAsync(first, pin));
        var second = await Bind();
        using (rig.Selections.AcquireOriginalManualMetadataUpdate(source.Selection, source.Capture, actor))
        {
            var acquiring = rig.Keep(rig.Selections.AcquireOriginalManualExecutionPinAsync(second, rig.Token));
            var refusal = await Assert.ThrowsAnyAsync<Exception>(() => acquiring);
            Assert.All(OriginalLeaves(refusal), cause =>
            {
                Assert.IsType<InvalidOperationException>(cause); Assert.Contains("changing target metadata", cause.Message);
            });
            rig.Expect(refusal);
        }
        var afterRelease = await rig.Keep(rig.Selections.AcquireOriginalManualExecutionPinAsync(second, rig.Token));
        rig.Selections.DemandOriginalManualExecutionPin(second, afterRelease);
        await rig.Keep(rig.Selections.CloseOriginalManualExecutionPinAsync(second, afterRelease));
        Assert.DoesNotContain(rig.Decisions, decision => decision.Scope.ActionName == HomeDeveloperManualProjectExecutionAdmissionSource.RunAction);
        Assert.False(File.Exists(Path.Combine(rig.ProjectRoot, "should-not-run.txt")));
    }, canonicalSavedProject: true, manualRunCommand: "printf forbidden > should-not-run.txt");

    private static IEnumerable<Exception> OriginalLeaves(Exception error) => error is AggregateException group
        ? group.InnerExceptions.SelectMany(OriginalLeaves) : [error];

    private static async Task Run(Func<Rig, Task> body, string? existingTarget = null,
        bool canonicalSavedProject = false, bool registerSavedProject = true, bool bindSavedStore = true, string? manualRunCommand = null)
    {
        Rig? rig = null;
        Exception? primary = null;
        var cleanup = new List<Exception>();
        try { rig = await Rig.CreateAsync(existingTarget, canonicalSavedProject, registerSavedProject, bindSavedStore, manualRunCommand); await body(rig); }
        catch (Exception error) { primary = error; if (rig is not null) rig.PreserveStorage = true; }
        finally
        {
            if (rig is not null)
                try { await rig.DisposeAsync(); }
                catch (Exception error) { cleanup.Add(error); }
        }
        if (primary is not null && cleanup.Count != 0)
            throw new AggregateException("Actual source publication body and independent cleanup failed.", new[] { primary }.Concat(cleanup));
        if (cleanup.Count != 0) throw new AggregateException("Actual source publication cleanup failed.", cleanup);
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    public sealed class LinuxPublicationFactAttribute : FactAttribute
    {
        public LinuxPublicationFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = 0)
            : base(sourceFilePath, sourceLineNumber)
        {
            if (!OperatingSystem.IsLinux()) Skip = "Requires the real Linux HomeLocalDomain and retained native Files publication graph; Windows needs its owning harness.";
        }
    }

    private sealed record Source(IDeveloperProjectOriginalReadSelection Selection,
        IDeveloperProjectOriginalReadAdmission Read, IDeveloperProjectOriginalExistingSourceCapture Capture);
    private sealed record ReplacedFixtureFile(string Path, string HeldPath, byte[] Bytes);

    private sealed class Rig : IAsyncDisposable
    {
        internal readonly CancellationToken Token = TestContext.Current.CancellationToken;
        internal readonly List<HomePermissionRequest> Decisions = [];
        private readonly List<Task> _raw = [];
        private readonly List<IAsyncDisposable> _fences = [];
        private readonly List<IDeveloperOriginalRegisteredProjectNativeSource> _registeredNativeSources = [];
        private readonly List<FileDeveloperWorkspaceStore> _savedStores = [];
        private readonly HashSet<Exception> _expected = new(ReferenceEqualityComparer.Instance);
        private CloudflareLocalDomainRegistration? _registration;
        private ServiceProvider? _graph;
        internal HomeLocalDomainComposition Home = null!;
        internal HomeDeveloperProjectReadAdmissionSource Reads = null!;
        internal HomeDeveloperProjectEditAdmissionSource Edits = null!;
        internal HomeDeveloperManualProjectExecutionAdmissionSource Runs = null!;
        internal DeveloperManualProjectProcessOwner ProcessOwner = null!;
        internal FilesDeveloperOriginalSourceSelection Selections = null!;
        internal IDeveloperProjectOriginalPhysicalCaptureSource Kernel = null!;
        private NativeFilesWorkspace _workspace = null!;
        private string _root = null!;
        private FileDeveloperWorkspaceStore? _savedStore;
        private DeveloperResolvedProject? _savedProject;
        private HostedItemId _savedFolder;
        private string? _savedRegistryPath;
        internal string ProjectRoot = null!;
        internal bool PreserveStorage;
        internal string TargetPath => Path.Combine(ProjectRoot, Target);
        internal string SeedPath => Path.Combine(ProjectRoot, "seed.txt");
        internal Guid FilesStoreId => _workspace.Configuration.StoreId;
        internal ServiceProvider Graph => _graph ?? throw new InvalidOperationException("The actual publication graph is not available.");
        internal IWorkspaceToolFinalFenceAuthority FenceAuthority => Graph.GetRequiredService<IWorkspaceToolFinalFenceAuthority>();
        internal FileDeveloperWorkspaceStore SavedStore => _savedStore ?? throw new InvalidOperationException("No saved-project fixture was configured.");
        internal DeveloperResolvedProject Saved => _savedProject ?? throw new InvalidOperationException("No real saved project was created.");
        internal string SavedMetadataPath => Path.Combine(SavedStore.OriginalWorkspaceMetadataDirectory,
            Saved.Reference.WorkspaceId.ToString("N") + ".json");
        internal string SavedRegistryPath => _savedRegistryPath ?? throw new InvalidOperationException("No actual project registry observation was captured.");

        internal static async Task<Rig> CreateAsync(string? existingTarget = null,
            bool canonicalSavedProject = false, bool registerSavedProject = true, bool bindSavedStore = true, string? manualRunCommand = null)
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This fixture requires the genuine Linux Home/Files native owners.");
            var rig = new Rig();
            try
            {
                rig._root = Directory.CreateTempSubdirectory("nine-to-one-original-publication-").FullName;
                File.SetUnixFileMode(rig._root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                if (canonicalSavedProject)
                {
                    var buildRoot = Directory.CreateDirectory(Path.Combine(rig._root, "Build")).FullName;
                    File.SetUnixFileMode(buildRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    rig._savedStore = new(buildRoot);
                    rig._savedStores.Add(rig._savedStore);
                }
                var homeRoot = Directory.CreateDirectory(Path.Combine(rig._root, "Home")).FullName;
                File.SetUnixFileMode(homeRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                NativeFilesWorkspaceService? files = null;
                NativeFilesWorkspaceAuthority? authority = null;
                HomeDeveloperProjectReadAdmissionSource? reads = null;
                HomeDeveloperProjectEditAdmissionSource? edits = null;
                HomeDeveloperManualProjectExecutionAdmissionSource? runs = null;
                var readPolicy = new HomeDeveloperProjectReadActionPolicySource();
                var editPolicy = new HomeDeveloperProjectEditActionPolicySource();
                var runPolicy = new HomeDeveloperManualProjectExecutionActionPolicySource();
                var runResolver = new HomeDeveloperManualProjectExecutionResourceResolver(() => runs ?? throw new InvalidOperationException("Actual manual Run owner is not resolved."));
                var readResolver = new HomeDeveloperProjectReadResourceResolver(() => reads ?? throw new InvalidOperationException("Actual READ owner is not resolved."));
                var editResolver = new HomeDeveloperProjectEditResourceResolver(() => edits ?? throw new InvalidOperationException("Actual WRITE owner is not resolved."));
                var destinationResolver = new FilesDeveloperOriginalSetupDestinationResolver(
                    () => rig.Graph.GetRequiredService<FilesDeveloperOriginalSetupScopeSource>());
                var resolvers = new List<ICanonicalResourceAccessResolver> { readResolver, editResolver, runResolver };
                if (canonicalSavedProject && bindSavedStore) resolvers.Add(destinationResolver);
                rig._registration = CloudflareLocalDomainRegistration.CreateOriginal(
                    new FileHomeCoreStateStore(Path.Combine(homeRoot, "state.json")), new OperatingSystemPrincipalSource(), new Paths(rig._root),
                    originalResolvers: resolvers, originalPolicies: [readPolicy, editPolicy, runPolicy],
                    configureOriginalStores: identity =>
                    {
                        files = new(identity.StateStore, identity.Profiles);
                        return new([files], new Dictionary<Type, object> { [typeof(NativeFilesWorkspaceService)] = files });
                    },
                    configureOriginalResolvers: components =>
                    {
                        authority = new(files!, components.Identity.Profiles, components.Ownership);
                        return new([], new Dictionary<Type, object> { [typeof(NativeFilesWorkspaceAuthority)] = authority });
                    });
                rig.Home = rig._registration.OriginalHome;
                var services = new ServiceCollection();
                services.AddHavenInfrastructure();
                services.AddHavenPlannerInfrastructure();
                services.AddHavenOriginalTaskExecutionServices();
                rig._registration.ConfigureOriginalServices(services);
                services.AddSingleton(files!); services.AddSingleton(authority!);
                services.AddFilesNativeHost();
                services.AddHavenOwnedDeveloperSourceReads(rig.Home,
                    provider => provider.GetRequiredService<FilesDeveloperOriginalSourceSelection>(), readResolver, readPolicy);
                services.AddHavenOwnedDeveloperSourceEdits(rig.Home,
                    provider => provider.GetRequiredService<FilesDeveloperOriginalSourceSelection>(), editResolver, editPolicy);
                services.AddHavenOwnedDeveloperManualExecution(rig.Home,
                    provider => provider.GetRequiredService<FilesDeveloperOriginalSourceSelection>(), runResolver, runPolicy);
                services.AddSingleton<DeveloperManualProjectProcessOwner>(provider => new(provider.GetRequiredService<NativeFilesWorkspaceService>(),
                    provider.GetRequiredService<FilesDeveloperOriginalSourceSelection>(), provider.GetRequiredService<HomeDeveloperProjectReadAdmissionSource>(),
                    provider.GetRequiredService<HomeDeveloperManualProjectExecutionAdmissionSource>(),
                    Assert.IsAssignableFrom<IWorkspaceOriginalInvocationSource>(provider.GetRequiredService<IWorkspaceToolService>())));
                Func<IServiceProvider, IDeveloperProjectOriginalPhysicalCaptureSource> kernelFactory = provider =>
                    Assert.IsType<WorkspaceToolService>(provider.GetRequiredService<IWorkspaceToolService>()).CreateOriginalDeveloperCaptureSource(
                        provider.GetRequiredService<IDeveloperProjectOriginalReadAdmissionSource>(),
                        // Pure join guards retain the SAME cycle owner after DI admission closes.
                        () => rig.Selections
                            ?? throw new InvalidOperationException("The SAME retained Files source selection owner is unavailable."),
                        () => throw new NotSupportedException("Project setup is not invoked by the source publication fixture."),
                        provider.GetRequiredService<IDeveloperProjectOriginalEditAdmissionSource>());
                if (canonicalSavedProject && bindSavedStore)
                {
                    // Exercise the actual production four-argument source factory. Setup-only
                    // callbacks remain unavailable and are never replaced with successful doubles.
                    services.AddFilesOriginalDeveloperSetups(destinationResolver, kernelFactory,
                        provider => provider.GetRequiredService<HomeDeveloperProjectSetupJournal>(),
                        provider => provider.GetRequiredService<IDeveloperProjectOriginalReadAdmissionSource>(),
                        provider => provider.GetRequiredService<IDeveloperProjectOriginalSetupPermissionSource>(),
                        _ => rig.SavedStore);
                }
                else
                {
                    services.AddSingleton<IDeveloperProjectOriginalPhysicalCaptureSource>(kernelFactory);
                    services.AddSingleton<FilesDeveloperOriginalSourceSelection>(provider => new(
                        provider.GetRequiredService<NativeFilesWorkspaceAuthority>(),
                        provider.GetRequiredService<IDeveloperProjectOriginalPhysicalCaptureSource>(),
                        provider.GetRequiredService<IDeveloperProjectOriginalReadAdmissionSource>()));
                }
                rig._graph = services.BuildServiceProvider();
                rig.Reads = reads = rig._graph.GetRequiredService<HomeDeveloperProjectReadAdmissionSource>();
                rig.Edits = edits = rig._graph.GetRequiredService<HomeDeveloperProjectEditAdmissionSource>();
                rig.Runs = runs = rig._graph.GetRequiredService<HomeDeveloperManualProjectExecutionAdmissionSource>();
                rig.ProcessOwner = rig._graph.GetRequiredService<DeveloperManualProjectProcessOwner>();
                rig.Kernel = rig._graph.GetRequiredService<IDeveloperProjectOriginalPhysicalCaptureSource>();
                rig.Selections = rig._graph.GetRequiredService<FilesDeveloperOriginalSourceSelection>();
                Assert.Same(rig.Edits, rig._graph.GetRequiredService<IWorkspaceManualEditFinalFenceValidator>());
                Assert.Same(rig.Reads, rig._graph.GetRequiredService<IDeveloperProjectOriginalReadAdmissionSource>());
                var filesRoot = Directory.CreateDirectory(Path.Combine(rig._root, "chosen-files")).FullName;
                rig._workspace = await rig.Keep(files!.ConfigureNewAsync(filesRoot, rig.Home.LocalStoreOwnership, rig.Token));
                rig.ProjectRoot = Directory.CreateDirectory(Path.Combine(filesRoot, "existing-project")).FullName;
                File.SetUnixFileMode(rig.ProjectRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                await File.WriteAllTextAsync(rig.SeedPath, "existing independent source\n", rig.Token);
                if (existingTarget is not null) await File.WriteAllTextAsync(rig.TargetPath, existingTarget, rig.Token);
                if (canonicalSavedProject) await rig.CreateSavedFixtureAsync(registerSavedProject, manualRunCommand);
                return rig;
            }
            catch (Exception primary)
            {
                rig.PreserveStorage = true;
                try { await rig.DisposeAsync(); }
                catch (Exception cleanup) { throw new AggregateException("Actual publication fixture acquisition and cleanup failed.", primary, cleanup); }
                throw;
            }
        }

        internal Task<T> Keep<T>(Task<T> actual) { _raw.Add(actual); return actual; }
        internal Task Keep(Task actual) { _raw.Add(actual); return actual; }
        internal IWorkspaceOriginalInvocation RetainInvocation(IWorkspaceOriginalInvocation invocation)
        { _fences.Add(invocation); return invocation; }
        internal IWorkspaceToolFinalFence RetainFence(IWorkspaceToolFinalFence fence)
        {
            _fences.Add(Assert.IsAssignableFrom<IAsyncDisposable>(fence));
            return fence;
        }
        internal IDeveloperOriginalRegisteredProjectNativeSource RetainRegisteredNativeSource(
            IDeveloperOriginalRegisteredProjectNativeSource source)
        {
            _registeredNativeSources.Add(source);
            return source;
        }
        internal void AssertGenuineManualComposition(IWorkspaceToolFinalFence fence)
        {
            // An optional, absent process-consent dependency must not make DI select the legacy
            // constructor and silently discard the actual Home manual-fence validator.
            Assert.Null(Graph.GetService<Func<IWorkspaceOriginalProcessStartConsentSource>>());
            Assert.Same(Edits, Graph.GetRequiredService<IWorkspaceManualEditFinalFenceValidator>());
            Assert.True(FenceAuthority.IsIssuedOriginal(fence));
            // Independent negative control: the preserved three-argument authority has no manual
            // validator. Merely holding a genuine fence from another composition grants nothing.
            var legacy = new WorkspaceTaskRunEffectAuthority(Graph.GetRequiredService<TaskRunPermissionAuthority>(),
                Graph.GetRequiredService<IPermissionDecisionEngine>(), Graph.GetRequiredService<IPermissionOriginalEffectFence>());
            Assert.False(legacy.IsIssuedOriginal(fence));
        }
        internal void Expect(Exception error)
        {
            if (error is AggregateException group) foreach (var child in group.InnerExceptions) Expect(child);
            else _expected.Add(error);
        }
        internal async Task RefusesAsync(Func<Task> operation, bool unsupportedCapability = false)
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(async () => await Keep(operation()));
            // Exact observed refusal occurrences alone are expected at independent owner drain.
            // New cleanup failures are never suppressed because a negative test was requested.
            Expect(error);
            Assert.All(Leaves(error), cause => Assert.True(unsupportedCapability
                    ? cause is NotSupportedException
                    : cause is UnauthorizedAccessException or IOException or ArgumentException or ObjectDisposedException,
                "Expected the declared source/permission refusal or explicitly requested unsupported capability, not an unrelated failure: " + cause));
        }
        internal async Task<Source> OpenReadAsync()
        {
            var selection = await Keep(Selections.SelectOriginalExistingProjectAsync(_workspace.Configuration.StoreId, ProjectRoot, Token));
            var operation = Keep(Reads.AcquireOriginalAsync(selection, Token));
            await DecideNextAsync(operation, HomeDeveloperProjectReadAdmissionSource.ReadAction, HomeApprovalChoice.Accept);
            var read = await operation;
            return new(selection, read, await Keep(Selections.CaptureOriginalAsync(selection, read, Token)));
        }
        private async Task CreateSavedFixtureAsync(bool register, string? manualRunCommand)
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This fixture owns Linux saved-project metadata.");
            var projectId = Guid.NewGuid();
            var rootId = Guid.NewGuid();
            var repository = new DeveloperSourceControlBinding("manual-local-source", rootId, "git", "local-fixture/" + projectId.ToString("N"));
            var draft = DeveloperWorkspace.Create([new(rootId, ProjectRoot)]) with
            {
                Projects = [new(projectId, "generic", "Actual manual saved source", [rootId], "CUI", null, "none", [], [], [], [], null)],
                SourceControlBindings = [repository]
            };
            if (manualRunCommand is not null)
                draft = draft with
                {
                    Projects = [draft.Projects[0] with { RunConfigurationIds = ["manual-console"] }],
                    RunConfigurations = [new("manual-console", DeveloperRunConfigurationSelection.ProviderId, projectId.ToString("D"), "console", [],
                        rootId.ToString("D"), "local", [], null) { Command = manualRunCommand, TimeoutSeconds = 10 }]
                };
            var created = await Keep(SavedStore.CreateAsync(draft, Token));
            Assert.True(created.Succeeded);
            var workspace = Assert.IsType<DeveloperWorkspace>(created.Value);
            var project = Assert.Single(workspace.Projects);
            var root = Assert.Single(workspace.Roots);
            _savedProject = new(new(workspace.WorkspaceId, workspace.Revision, project.ProjectId, project.Revision, root.RootId, repository.BindingId),
                workspace, project, root, Assert.Single(workspace.SourceControlBindings));
            File.SetUnixFileMode(SavedStore.OriginalWorkspaceMetadataDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(SavedMetadataPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            // Fixture setup uses the genuine owning provider and registry, with no Task/Conversation/
            // Container and no reconstructed setup ACK. This is not a production source publication.
            _savedFolder = HostedItemId.New();
            var now = DateTimeOffset.UtcNow;
            var operation = new FilesOperation(new(Guid.NewGuid()), _workspace.Actor.ActorId, _savedFolder, null, null,
                "CreateFolder", null, null, FilesOperationState.Pending, now, now, null, null);
            Assert.True((await Keep(_workspace.Provider.MutateAsync(operation, "actual-manual-saved-project", Token))).IsSuccess);
            if (register) await RegisterSavedProjectAsync();
        }
        internal async Task RegisterSavedProjectAsync()
        {
            var registered = await Keep(_workspace.Directories.RegisterProfileAsync(Guid.Parse(_workspace.Actor.ProfileId), _savedFolder,
                "dev.project." + Saved.Project.ProjectId.ToString("N"), ProjectRoot, Token));
            Assert.True(registered.IsSuccess);
            var observed = await Keep(_workspace.Directories.ObserveOriginalCurrentProjectRegistrationAsync(
                Guid.Parse(_workspace.Actor.ProfileId), "dev.project." + Saved.Project.ProjectId.ToString("N"),
                _workspace.Provider, _workspace.Configuration.StoreId, _workspace.Configuration.RootDirectory,
                body => body(), task => _raw.Add(task), Token));
            _savedRegistryPath = observed.StatePath;
            Assert.Equal(ProjectRoot, observed.Binding.DirectoryPath);
            Assert.Equal(_savedFolder, observed.Binding.FolderId);
        }
        internal async Task<Source> OpenRegisteredReadAsync(DeveloperResolvedProject? selected = null)
        {
            var selection = await Keep(Selections.SelectOriginalRegisteredProjectAsync(
                _workspace.Configuration.StoreId, selected ?? Saved, Token));
            var operation = Keep(Reads.AcquireOriginalAsync(selection, Token));
            await DecideNextAsync(operation, HomeDeveloperProjectReadAdmissionSource.ReadAction, HomeApprovalChoice.Accept);
            var read = await operation;
            return new(selection, read, await Keep(Selections.CaptureOriginalAsync(selection, read, Token)));
        }
        internal void AssertRegisteredDescriptor(Source source)
        {
            var owner = Assert.IsAssignableFrom<IDeveloperOriginalRegisteredProjectSelectionSource>(Selections);
            var descriptor = owner.GetOriginalRegisteredDescriptor(source.Selection);
            Assert.True(owner.IsIssuedOriginalRegisteredDescriptor(source.Selection, descriptor));
            Assert.Same(SavedStore, descriptor.OriginalStore);
            Assert.Equal(Saved.Reference.WorkspaceId, descriptor.WorkspaceId);
            Assert.Equal(Saved.Reference.WorkspaceRevision, descriptor.WorkspaceRevision);
            Assert.Equal(Saved.Reference.ProjectId, descriptor.ProjectId);
            Assert.Equal(Saved.Reference.ProjectRevision, descriptor.ProjectRevision);
            Assert.Equal(Saved.Reference.RootId, descriptor.RootId);
            Assert.Equal(Saved.Reference.RepositoryBindingId, descriptor.RepositoryBindingId);
            Assert.Equal(ProjectRoot, descriptor.RegisteredProjectRoot);
            Assert.Equal(ProjectRoot, source.Capture.OriginalExistingProjectRoot);
            Assert.Equal(SavedRegistryPath, descriptor.OriginalRegistrationStatePath);
            Assert.Equal(_workspace.Actor, descriptor.OriginalActor);
            Assert.Same(Selections, Graph.GetRequiredService<IDeveloperProjectOriginalPhysicalReadSelectionSource>());
            Assert.Same(Selections, Graph.GetRequiredService<IDeveloperProjectOriginalEditScopeSource>());
            Assert.Same(Selections, Graph.GetRequiredService<IDeveloperProjectOriginalCaptureAuthority>());
            Assert.Null(Graph.GetService<HomeDeveloperProjectSetupJournal>());
            Assert.Null(Graph.GetService<IDeveloperProjectOriginalSetupPermissionSource>());
            var snapshot = Assert.IsAssignableFrom<IDeveloperOriginalRegisteredProjectSnapshotDescriptor>(descriptor);
            AssertSameJson(Saved.Project, snapshot.ExactSelectedProjectJson);
            AssertSameJson(Saved.Root, snapshot.ExactSelectedRootJson);
            Assert.NotNull(snapshot.ExactSelectedRepositoryJson);
            AssertSameJson(Saved.Repository, snapshot.ExactSelectedRepositoryJson!);
            var scope = Assert.Single(Selections.GetOriginalReadScopes(source.Selection));
            AssertScopeIdentity(scope.Id, Saved.Reference.WorkspaceId);
            AssertScopeIdentity(scope.Id, Saved.Reference.ProjectId);
            AssertScopeIdentity(scope.Id, Saved.Reference.RootId);
        }
        internal void AssertRegisteredWriteScope(HomePermissionRequest request)
        {
            var binding = Assert.IsType<HomeCanonicalResourceBinding>(request.Impact.ResourceBinding);
            Assert.Equal(_workspace.Actor, binding.OriginalActor);
            var scope = Assert.Single(binding.Scopes);
            Assert.Equal(ResourceAccess.Write, scope.Access);
            AssertScopeIdentity(scope.Id, Saved.Reference.WorkspaceId);
            AssertScopeIdentity(scope.Id, Saved.Reference.ProjectId);
            AssertScopeIdentity(scope.Id, Saved.Reference.RootId);
        }
        private static void AssertScopeIdentity(string scope, Guid identity) => Assert.True(
            scope.Contains(identity.ToString("D"), StringComparison.Ordinal) || scope.Contains(identity.ToString("N"), StringComparison.Ordinal),
            "The real reviewed scope must retain its issuer-validated canonical ID: " + identity);
        private static void AssertSameJson<T>(T expected, string actual)
        {
            using var document = JsonDocument.Parse(actual);
            Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(expected, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                document.RootElement), "The observed descriptor must retain the exact selected saved record.");
        }
        internal async Task<DeveloperResolvedProject> CreateForeignSavedReferenceAsync()
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This fixture owns Linux saved-project metadata.");
            var root = Directory.CreateDirectory(Path.Combine(_root, "ForeignBuild")).FullName;
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var foreignStore = new FileDeveloperWorkspaceStore(root);
            _savedStores.Add(foreignStore);
            var created = await Keep(foreignStore.CreateAsync(Saved.Workspace with { WorkspaceId = Guid.NewGuid() }, Token));
            Assert.True(created.Succeeded);
            var workspace = Assert.IsType<DeveloperWorkspace>(created.Value);
            return new(Saved.Reference with { WorkspaceId = workspace.WorkspaceId, WorkspaceRevision = workspace.Revision },
                workspace, Assert.Single(workspace.Projects), Assert.Single(workspace.Roots), Assert.Single(workspace.SourceControlBindings));
        }
        internal async Task<ReplacedFixtureFile> ReplaceObservedFileAsync(string path)
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This fixture owns Linux native replacement controls.");
            var bytes = await File.ReadAllBytesAsync(path, Token);
            var held = path + ".held-original-" + Guid.NewGuid().ToString("N");
            File.Move(path, held);
            try
            {
                await File.WriteAllBytesAsync(path, bytes, Token);
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                return new(path, held, bytes);
            }
            catch { PreserveStorage = true; throw; }
        }
        internal void RestoreObservedFile(ReplacedFixtureFile replacement)
        {
            File.Delete(replacement.Path);
            File.Move(replacement.HeldPath, replacement.Path);
        }
        internal async Task<(IDeveloperProjectOriginalEditAdmission Write, HomePermissionRequest Request)> CreateAdmissionAsync(Source source, string text)
        {
            var operation = Keep(Edits.AcquireOriginalCreateAsync(source.Selection, source.Capture, Target, text, Token));
            var request = await DecideNextAsync(operation, HomeDeveloperProjectEditAdmissionSource.CreateAction, HomeApprovalChoice.Accept);
            return (await operation, request);
        }
        internal async Task AssertUnpublishedRequestAsync(HomePermissionRequest request)
        {
            var observed = await Keep(Home.Permissions.ReadRequestObservationAsync(request.RequestId, Token));
            Assert.NotNull(observed);
            Assert.Contains(observed!.State, new[] { HomePermissionRequestState.Failed, HomePermissionRequestState.Cancelled });
            Assert.NotEqual(HomePermissionRequestState.Succeeded, observed.State);
        }
        internal async Task CloseUnpublishedRunAsync(IDeveloperProjectOriginalManualExecutionAdmission run, HomePermissionRequest request)
        {
            var unexpected = new List<Exception>(); Task? actual = null;
            try { actual = Keep(run.DisposeAsync().AsTask()); await actual; }
            catch (Exception error) { AddUnexpected(actual?.Exception ?? error, unexpected); }
            if (unexpected.Count != 0) throw new AggregateException("New manual Run cleanup failures remain visible.", unexpected);
            await AssertUnpublishedRequestAsync(request);
        }
        internal async Task CloseUnpublishedWriteAsync(IDeveloperProjectOriginalEditAdmission write, HomePermissionRequest request)
        {
            var unexpected = new List<Exception>();
            Task? actual = null;
            try { actual = Keep(write.DisposeAsync().AsTask()); await actual; }
            catch (Exception error) { AddUnexpected(actual?.Exception ?? error, unexpected); }
            if (unexpected.Count != 0) throw new AggregateException("New unpublished CREATE cleanup failures remain visible.", unexpected);
            await AssertUnpublishedRequestAsync(request);
        }
        internal async Task<HomePermissionRequest> DecideNextAsync(Task actual, string action, HomeApprovalChoice choice)
        {
            var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < TimeSpan.FromSeconds(15))
            {
                var snapshot = await Keep(Home.Permissions.GetSnapshotAsync(cancellationToken: Token));
                var pending = snapshot.PendingRequests.Where(item => item.Scope.ActionName == action &&
                    Decisions.All(decided => decided.RequestId != item.RequestId)).ToArray();
                if (pending.Length != 0)
                {
                    var request = Assert.Single(pending);
                    Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
                    Assert.True(request.Policy.RequiresPerActionApproval);
                    Assert.False(actual.IsCompleted, "The protected operation must await its actual Home decision.");
                    if (action == HomeDeveloperProjectEditAdmissionSource.CreateAction) Assert.False(File.Exists(TargetPath));
                    Assert.True((await Keep(Home.Permissions.DecideAsync(request.RequestId, choice, cancellationToken: Token))).Succeeded);
                    Decisions.Add(request); return request;
                }
                if (actual.IsCompleted) { await actual; throw new InvalidOperationException("The original operation completed without its separate Home approval."); }
                await Task.Delay(TimeSpan.FromMilliseconds(10), Token);
            }
            throw new TimeoutException("No genuine pending Home request for " + action);
        }
        internal async Task<HomePermissionRequest> ObservePendingAsync(Task actual, string action)
        {
            var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < TimeSpan.FromSeconds(15))
            {
                var snapshot = await Keep(Home.Permissions.GetSnapshotAsync(cancellationToken: Token));
                var pending = snapshot.PendingRequests.Where(item => item.Scope.ActionName == action &&
                    Decisions.All(decided => decided.RequestId != item.RequestId)).ToArray();
                if (pending.Length != 0)
                {
                    var request = Assert.Single(pending);
                    Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
                    Assert.True(request.Policy.RequiresPerActionApproval);
                    Assert.False(actual.IsCompleted);
                    return request;
                }
                if (actual.IsCompleted) { await actual; throw new InvalidOperationException("The source did not wait for genuine READ approval."); }
                await Task.Delay(TimeSpan.FromMilliseconds(10), Token);
            }
            throw new TimeoutException("No actual pending Home READ before native metadata observation.");
        }
        internal async Task RefusesInvalidSavedMetadataAsync(Func<Task> operation)
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(async () => await Keep(operation()));
            Expect(error);
            Assert.All(Leaves(error), cause => Assert.True(cause is JsonException,
                "The deliberately malformed stored document must produce only its actual JSON parse failure, not an unrelated failure: " + cause));
        }
        internal async Task RefusesForeignRegisteredOwnerAsync(Func<Task> operation)
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(async () => await Keep(operation()));
            Expect(error);
            Assert.All(Leaves(error), cause => Assert.IsType<UnauthorizedAccessException>(cause));
        }
        internal async Task CloseRegisteredNativeSourcesAsync()
        {
            var errors = new List<Exception>();
            foreach (var source in _registeredNativeSources)
            {
                Task? actual = null;
                try { actual = Keep(source.CloseAndDrainOriginalAsync()); await actual; }
                catch (Exception error) { AddUnexpected(actual?.Exception ?? error, errors); }
            }
            if (errors.Count != 0) throw new AggregateException("Independent registered native-source cleanup failed.", errors);
        }
        public async ValueTask DisposeAsync()
        {
            var errors = new List<Exception>();
            async Task Close(Func<Task> source)
            {
                Task? actual = null;
                try { actual = source(); await actual; }
                catch (Exception error) { AddUnexpected(actual?.Exception ?? error, errors); }
            }
            // Acquire and join each original close independently, preserving new cleanup failures.
            foreach (var fence in _fences) await Close(() => fence.DisposeAsync().AsTask());
            // Each explicitly borrowed native owner is closed independently. Only the exact
            // already-observed operation failures are expected; a new close failure still fails.
            foreach (var source in _registeredNativeSources) await Close(() => Keep(source.CloseAndDrainOriginalAsync()));
            if (ProcessOwner is not null) await Close(ProcessOwner.CloseAndDrainOriginalAsync);
            if (Runs is not null) await Close(Runs.CloseAndDrainOriginalManualExecutionsAsync);
            if (Edits is not null) await Close(Edits.CloseAndDrainOriginalEditsAsync);
            if (Reads is not null) await Close(Reads.CloseAndDrainOriginalReadsAsync);
            if (Selections is not null) await Close(Selections.CloseAndDrainOriginalSelectionsAsync);
            if (Kernel is not null) await Close(Kernel.CloseAndDrainOriginalCapturesAsync);
            // The saved store has no owner-level disposal contract: each operation owns its
            // file/lock lifetime. Join every enrolled store Task before removing fixture data.
            foreach (var actual in _raw.Distinct<Task>(ReferenceEqualityComparer.Instance))
                try { await actual; } catch (Exception error) { AddUnexpected(actual.Exception ?? error, errors); }
            GC.KeepAlive(_savedStores);
            if (_graph is not null) await Close(() => _graph.DisposeAsync().AsTask());
            if (_registration is not null) await Close(_registration.CloseOriginalHomeAfterBorrowersAsync);
            else if (Home is not null) await Close(Home.CloseAndDrainAsync);
            if (errors.Count != 0) throw new AggregateException("Genuine publication owners or independent cleanup did not settle; fixture retained at " + _root, errors);
            if (!PreserveStorage && _root is not null && Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        private void AddUnexpected(Exception error, List<Exception> errors)
        {
            if (error is AggregateException group) { foreach (var child in group.InnerExceptions) AddUnexpected(child, errors); return; }
            if (!_expected.Contains(error) && !errors.Any(prior => ReferenceEquals(prior, error))) errors.Add(error);
        }
        private static IEnumerable<Exception> Leaves(Exception error) => error is AggregateException group
            ? group.InnerExceptions.SelectMany(Leaves) : [error];
    }

    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
