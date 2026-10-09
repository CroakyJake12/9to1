using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Dev;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesDeveloperOriginalCurrentProjectExecutionBridge
{
    private sealed class Binding(FilesDeveloperOriginalCurrentProjectExecutionBridge owner, CommandFrame frame,
        Original original, DeveloperResolvedProject project, string documentSha, string rootFingerprint)
        : IDeveloperWorkspaceOriginalExecutionBinding
    {
        internal FilesDeveloperOriginalCurrentProjectExecutionBridge Owner => owner;
        internal CommandFrame Frame => frame;
        internal Original Original => original;
        internal DeveloperResolvedProject Project => project;
        internal string DocumentSha => documentSha;
        internal string RootFingerprint => rootFingerprint;
        internal string Receipt { get; } = "dev-current-execution:" + Guid.NewGuid().ToString("D");
        public Guid WorkspaceId => frame.Project.WorkspaceId;
        public Guid ProjectId => frame.Project.ProjectId;
        public Guid RootId => frame.Project.RootId;
        public long WorkspaceRevision => frame.Project.WorkspaceRevision;
        public string CanonicalRoot => frame.Read.OriginalDescriptor.RegisteredProjectRoot;
        public AuthenticatedResourceActor OriginalActor => frame.Read.OriginalDescriptor.OriginalActor;
    }
    private Binding RequireBinding(IDeveloperWorkspaceOriginalExecutionBinding value)
    {
        Binding binding;
        lock (_gate)
        {
            if (_retiring || value is not Binding actual || !ReferenceEquals(actual.Owner, this) ||
                !_bindings.Contains(actual) || !actual.Original.Driver.IsCompletedSuccessfully)
                throw new UnauthorizedAccessException("No SAME successful live private current-project binding exists.");
            binding = actual;
        }
        if (!_reads.IsIssuedOriginalCommandRead(binding.Frame.Read))
            throw new UnauthorizedAccessException("The exact original command READ is no longer issued.");
        return binding;
    }
    public bool IsIssuedOriginalBinding(IDeveloperWorkspaceOriginalExecutionBinding sameBinding)
    { try { RequireBinding(sameBinding); return true; } catch (UnauthorizedAccessException) { return false; } }
    public IReadOnlyList<ResourceScope> GetOriginalExecutionScopes(IDeveloperWorkspaceOriginalExecutionBinding sameBinding)
    {
        var binding = RequireBinding(sameBinding);
        return [new("dev.workspace.execute", binding.Receipt,
            binding.DocumentSha + ":" + binding.RootFingerprint, ResourceAccess.Execute)];
    }
    public Task<IDeveloperWorkspaceOriginalExecutionBinding> ResolveOriginalBindingAsync(
        DeveloperResolvedProject sameProject, Action<Action> scope, Action<Task> retain, CancellationToken token)
        => ResolveOriginalProjectBindingAsync(sameProject, scope, retain, token);
    public Task<IDeveloperWorkspaceOriginalExecutionBinding> ResolveOriginalProjectBindingAsync(
        DeveloperResolvedProject sameProject, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var frame = _command.Value ?? throw new UnauthorizedAccessException("The actual Dev command lacks its private current READ invocation frame.");
        return Start<IDeveloperWorkspaceOriginalExecutionBinding>(scope, retain, async (original, sources) =>
        {
            sources.Invoke(() => { DemandFrame(frame, sameProject); return true; });
            await sources.ObserveVoid(() => _reads.ValidateOriginalCommandReadWithinSourceAsync(frame.Read,
                sources.OriginalSynchronousScope, sources.RetainOriginalTask, token)).ConfigureAwait(false);
            IDeveloperOriginalProjectCommandNativeRead? captured = null; var errors = new List<Exception>();
            Task<IDeveloperOriginalProjectCommandNativeRead>? actual = null;
            Binding? binding = null;
            try
            {
                captured = await Capture(frame, original, sources, task => actual = task, value => captured = value, token).ConfigureAwait(false);
                sources.Invoke(() =>
                {
                    DemandResolvedDocument(captured.OriginalRead, sameProject);
                    var reviewed = frame.Read.OriginalIdentity;
                    if (reviewed.SavedWorkspaceDocumentSha256 != captured.OriginalRead.OriginalWorkspaceDocumentSha256 ||
                        reviewed.RegisteredRootFingerprint != captured.OriginalRead.OriginalRegisteredRootFingerprint)
                        throw new UnauthorizedAccessException("The actual document/root differs from the SAME manual READ observation.");
                    captured.OriginalRead.DemandOriginalExecutionBinding();
                    binding = new(this, frame, original, sameProject,
                        captured.OriginalRead.OriginalWorkspaceDocumentSha256, captured.OriginalRead.OriginalRegisteredRootFingerprint);
                    return true;
                });
            }
            catch (Exception error) { AddTask(errors, actual, error); }
            finally
            {
                if (captured is not null)
                    await CloseOwnedNative(frame, captured, original, sources, errors).ConfigureAwait(false);
            }
            if (actual?.IsCanceled == true && !Volatile.Read(ref original.CallbackFailed) && errors.All(Canceled))
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            Throw(errors);
            await sources.ObserveVoid(() => _reads.ValidateOriginalCommandReadWithinSourceAsync(frame.Read,
                sources.OriginalSynchronousScope, sources.RetainOriginalTask, token)).ConfigureAwait(false);
            sources.Invoke(() =>
            {
                token.ThrowIfCancellationRequested(); DemandFrame(frame, sameProject);
                lock (_gate)
                {
                    if (_retiring || _bindings.Count >= 128 || binding is null)
                        throw new InvalidOperationException("Fresh execution binding custody is sealed or full.");
                    _bindings.Add(binding);
                }
                return true;
            });
            return binding!;
        });
    }
    private void DemandFrame(CommandFrame frame, DeveloperResolvedProject project)
    {
        if (project.Reference != frame.Project || !_reads.IsIssuedOriginalCommandRead(frame.Read))
            throw new UnauthorizedAccessException("This resolved project does not belong to the SAME live command READ.");
        DemandDescriptor(frame.Read.OriginalDescriptor, project.Reference, frame.Context);
        if (project.Root.Location != frame.Read.OriginalDescriptor.RegisteredProjectRoot || project.Root.EnvironmentId != "local")
            throw new UnauthorizedAccessException("The actual resolved project root differs from the issued current selection.");
    }
    private static void DemandResolvedDocument(IDeveloperOriginalCurrentProjectNativeRead read, DeveloperResolvedProject project)
    {
        using var document = JsonDocument.Parse(read.OriginalWorkspaceDocument);
        var workspace = document.RootElement.GetProperty("workspace").Deserialize<DeveloperWorkspace>(Json)
            ?? throw new InvalidDataException("The actual saved workspace descriptor returned no document.");
        if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 1 || workspace.Validate() is not null ||
            JsonSerializer.Serialize(workspace, Json) != JsonSerializer.Serialize(project.Workspace, Json) ||
            JsonSerializer.Serialize(workspace.Projects.SingleOrDefault(row => row.ProjectId == project.Reference.ProjectId), Json) != JsonSerializer.Serialize(project.Project, Json) ||
            workspace.Roots.SingleOrDefault(row => row.RootId == project.Reference.RootId) != project.Root ||
            JsonSerializer.Serialize(project.Reference.RepositoryBindingId is null ? null : workspace.SourceControlBindings.SingleOrDefault(
                row => row.BindingId == project.Reference.RepositoryBindingId && row.RootId == project.Reference.RootId), Json) != JsonSerializer.Serialize(project.Repository, Json))
            throw new UnauthorizedAccessException("The complete real saved document/project/root/repository differs from the current Dev observation.");
    }
    public Task ValidateOriginalBindingAsync(DeveloperResolvedProject project, IDeveloperWorkspaceOriginalExecutionBinding binding,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
        => ValidateOriginalProjectBindingAsync(project, binding, scope, retain, token);
    public Task ValidateOriginalProjectBindingAsync(DeveloperResolvedProject sameProject,
        IDeveloperWorkspaceOriginalExecutionBinding sameBinding, Action<Action> scope, Action<Task> retain, CancellationToken token)
        => Start(scope, retain, async (original, sources) =>
        {
            var binding = sources.Invoke(() => RequireBinding(sameBinding));
            sources.Invoke(() =>
            {
                if (!ReferenceEquals(_command.Value, binding.Frame))
                    throw new UnauthorizedAccessException("A different actual command cannot borrow this binding.");
                DemandFrame(binding.Frame, sameProject);
                if (JsonSerializer.Serialize(sameProject, Json) != JsonSerializer.Serialize(binding.Project, Json))
                    throw new UnauthorizedAccessException("The complete original project observation changed.");
                return true;
            });
            await RevalidateBody(binding, original, sources, token).ConfigureAwait(false); return true;
        });
    public Task RevalidateOriginalAsync(IDeveloperWorkspaceOriginalExecutionBinding binding,
        AuthenticatedResourceActor actor, CancellationToken token)
        => RevalidateOriginalWithinSourceAsync(binding, actor, action => action(), _ => { }, token);
    public Task RevalidateOriginalWithinSourceAsync(IDeveloperWorkspaceOriginalExecutionBinding sameBinding,
        AuthenticatedResourceActor sameActor, Action<Action> scope, Action<Task> retain, CancellationToken token)
        => Start(scope, retain, async (original, sources) =>
        {
            var binding = sources.Invoke(() => RequireBinding(sameBinding));
            sources.Invoke(() =>
            {
                if (sameActor != binding.OriginalActor)
                    throw new UnauthorizedAccessException("The original Home resource actor changed.");
                return true;
            });
            await RevalidateBody(binding, original, sources, token).ConfigureAwait(false); return true;
        });
    private async Task RevalidateBody(Binding binding, Original original, FilesOriginalReadSourceScope sources, CancellationToken token)
    {
        await sources.ObserveVoid(() => _reads.ValidateOriginalCommandReadWithinSourceAsync(binding.Frame.Read,
            sources.OriginalSynchronousScope, sources.RetainOriginalTask, token)).ConfigureAwait(false);
        IDeveloperOriginalProjectCommandNativeRead? acquired = null; Task<IDeveloperOriginalProjectCommandNativeRead>? actual = null;
        var errors = new List<Exception>();
        try
        {
            acquired = await Capture(binding.Frame, original, sources, task => actual = task, value => acquired = value, token).ConfigureAwait(false);
            sources.Invoke(() =>
            {
                DemandResolvedDocument(acquired.OriginalRead, binding.Project);
                if (acquired.OriginalRead.OriginalWorkspaceDocumentSha256 != binding.DocumentSha ||
                    acquired.OriginalRead.OriginalRegisteredRootFingerprint != binding.RootFingerprint)
                    throw new UnauthorizedAccessException("The actual document inode/version or registered root changed during execution review.");
                acquired.OriginalRead.DemandOriginalExecutionBinding(); return true;
            });
        }
        catch (Exception error) { AddTask(errors, actual, error); }
        finally { if (acquired is not null) await CloseOwnedNative(binding.Frame, acquired, original, sources, errors).ConfigureAwait(false); }
        if (actual?.IsCanceled == true && !Volatile.Read(ref original.CallbackFailed) && errors.All(Canceled)) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        Throw(errors);
        sources.Invoke(() => { token.ThrowIfCancellationRequested(); RequireBinding(binding); return true; });
    }
    private async Task<IDeveloperOriginalProjectCommandNativeRead> Capture(CommandFrame frame, Original original,
        FilesOriginalReadSourceScope sources, Action<Task<IDeveloperOriginalProjectCommandNativeRead>> retainCapture,
        Action<IDeveloperOriginalProjectCommandNativeRead> captureActualProduct, CancellationToken token)
    {
        Task<IDeveloperOriginalProjectCommandNativeRead>? actual = null; IDeveloperOriginalProjectCommandNativeRead? result = null;
        var errors = new List<Exception>();
        try
        {
            sources.Invoke(() =>
            {
                actual = _reads.CaptureOriginalCommandNativeReadWithinSourceAsync(frame.Read,
                    sources.OriginalSynchronousScope, sources.RetainOriginalTask, token);
                retainCapture(actual); sources.RetainOriginalTask(actual); return true;
            });
        }
        catch (Exception error) { Add(errors, error); }
        if (actual is not null)
            try { result = await actual.ConfigureAwait(false); captureActualProduct(result); }
            catch (Exception error) { AddTask(errors, actual, error); }
        if (result is not null)
            try
            {
                sources.Invoke(() =>
                {
                    if (!ReferenceEquals(result.OriginalCommandRead, frame.Read) ||
                        !_reads.IsIssuedOriginalCommandNativeRead(frame.Read, result) ||
                        !_native.IsIssuedOriginalRead(frame.Read.OriginalSelection, result.OriginalCaptureTask, result.OriginalRead))
                        throw new UnauthorizedAccessException("No SAME configured live native capture result exists.");
                    sources.RetainOriginalTask(result.OriginalCaptureTask); return true;
                });
            }
            catch (Exception error) { Add(errors, error); }
        if (actual?.IsCanceled == true && !Volatile.Read(ref original.CallbackFailed) && errors.All(Canceled)) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        Throw(errors); return result ?? throw new UnauthorizedAccessException("No actual native capture product was returned.");
    }
    private bool OwnsNative(CommandFrame frame, IDeveloperOriginalProjectCommandNativeRead result)
        => ReferenceEquals(result.OriginalCommandRead, frame.Read) && _reads.IsOwnedOriginalCommandNativeRead(frame.Read, result) &&
            _native.IsOwnedOriginalRead(frame.Read.OriginalSelection, result.OriginalCaptureTask, result.OriginalRead);
    private async Task CloseOwnedNative(CommandFrame frame, IDeveloperOriginalProjectCommandNativeRead result,
        Original original, FilesOriginalReadSourceScope sources, List<Exception> errors)
    {
        Task? close = null;
        // Only the two configured private issuers can establish cleanup ownership. Fixed
        // recognized cleanup executes under our physical guard even if a parent refuses.
        try
        {
            Scope(() =>
            {
                if (!OwnsNative(frame, result)) throw new UnauthorizedAccessException("No private native cleanup ownership exists.");
                close = result.OriginalRead.DisposeAsync().AsTask(); lock (_gate) original.Raw.Add(close);
            });
        }
        catch (Exception error) { Add(errors, error); }
        if (close is not null)
        {
            try { sources.RetainOriginalTask(close); } catch (Exception error) { Add(errors, error); }
            try { await close.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, close, error); }
            try
            {
                Scope(() =>
                {
                    if (!_reads.IsClosedOriginalCommandNativeRead(frame.Read, result, close) ||
                        !_native.IsClosedOriginalRead(frame.Read.OriginalSelection, result.OriginalCaptureTask, result.OriginalRead, close))
                        throw new UnauthorizedAccessException("The exact original native close did not complete successfully.");
                });
            }
            catch (Exception error) { Add(errors, error); }
        }
    }
}
