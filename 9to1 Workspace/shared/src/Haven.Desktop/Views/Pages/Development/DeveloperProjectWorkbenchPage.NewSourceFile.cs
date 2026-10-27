using System.Text;
using Haven.Desktop.Services;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperProjectWorkbenchPage
{
    private sealed record NewFileReview(DeveloperProjectReference Project, string RootLocation, DeveloperNewSourceFile File, string AfterSha256);
    private string _newFileName = "Page.cui", _newFileStatus = "Review a CUI page or empty source file before creating it in this project root.";
    private bool _newFileCuiTemplate = true;
    private long _newFileInputRevision;
    private NewFileReview? _newFileReview;
    public DeveloperSourceFilePublication? OriginalNewSourceFilePublication { get; private set; }
    private bool HasNewFileSlot(string name) => _sourceTabs.Count < DeveloperSourceReviewSession.MaximumOpenDocuments &&
        !_sourceTabs.Any(tab => string.Equals(tab.Path, name, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
    private bool CurrentNewFileReview() => _newFileReview is { } review && review.Project == _project.Reference &&
        review.File.RelativePath == _newFileName && review.File.TemplateId == (_newFileCuiTemplate ? "cui-page" : "empty-source") &&
        review.AfterSha256 == DeveloperManualSourceEditOwner.Sha256(review.File.Text) && HasNewFileSlot(_newFileName);
    private void ChangeNewFileName(string name)
    {
        if (_newFileName == name) return;
        _newFileName = name; _newFileInputRevision = checked(_newFileInputRevision + 1); _newFileReview = null; _newFileStatus = "The file name changed. Review the new source before creating it.";
    }
    private bool SetNewFileTemplate(object? value)
    {
        if (value is not bool cui) return false;
        long generation; lock (_gate) generation = _generation;
        _work.RunSynchronous(original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication();
            lock (_gate)
                if (_newFileCuiTemplate != cui)
                { _newFileCuiTemplate = cui; _newFileInputRevision = checked(_newFileInputRevision + 1); _newFileReview = null; _newFileStatus = "The template changed. Review the new source before creating it."; }
            NotifyOriginal(original);
        });
        return true;
    }
    private bool TryGetNewFileValue(string path, out object? value)
    {
        if (path is not ("NewFileName" or "NewFileCuiTemplate" or "NewFileStatus" or "NewFileReview" or "NewFileSource" or "CanReviewNewFile" or "CanCreateNewFile" or "CanConfigureNewFile" or "TemplateImportStatus"))
        { value = null; return false; }
        var prepared = DeveloperNewSourceFile.Prepare(_newFileName, _newFileCuiTemplate);
        value = path switch
        {
            "NewFileName" => _newFileName, "NewFileCuiTemplate" => _newFileCuiTemplate,
            "NewFileStatus" => !_manual ? "Create reviewed files from manual project mode. Opening a task does not grant this CREATE action."
                : _manualEditor is null ? "Reviewed file creation is unavailable in this host. Open the project in a configured manual workspace."
                : _project.Root.EnvironmentId != "local" ? "Select a local registered project root to create files here. Remote CREATE is not provided by this owner."
                : !prepared.Succeeded ? prepared.Error!.Message
                : !HasNewFileSlot(_newFileName) ? "Close a saved tab or choose another file name. An existing open tab cannot be replaced by CREATE."
                : _newFileStatus,
            "NewFileReview" => _newFileReview is not { } review ? "No current new-file review."
                : !CurrentNewFileReview() ? "This new-file review is no longer current. Review the current project and file request again."
                : $"{review.File.RelativePath}; {review.RootLocation}; {review.File.TemplateId}; {Encoding.UTF8.GetByteCount(review.File.Text)} UTF-8 bytes; AFTER SHA-256 {review.AfterSha256}. Target must remain absent; CREATE never overwrites an existing or empty file.",
            "NewFileSource" => _newFileReview?.File.Text ?? "",
            "TemplateImportStatus" => _templateImportStatus,
            "CanReviewNewFile" => IsActionAvailable("dev.review-new-source-file"),
            "CanCreateNewFile" => IsActionAvailable("dev.create-new-source-file"),
            "CanConfigureNewFile" => _manual && _manualEditor is not null && _project.Root.EnvironmentId == "local" && !_pending,
            _ => null
        };
        return true;
    }

    private ValueTask DispatchNewSourceFileAsync(string command, CancellationToken token)
    {
        long generation, inputRevision; DeveloperNewSourceFile file; DeveloperProjectReference reference; NewFileReview? reviewed;
        lock (_gate)
        {
            if (IsActionAvailable(command) != true) throw new InvalidOperationException("Review one supported absent project-root file before creating it.");
            reviewed = _newFileReview;
            file = command == "dev.create-new-source-file" ? reviewed!.File : DeveloperNewSourceFile.Prepare(_newFileName, _newFileCuiTemplate).Value!;
            reference = _project.Reference; generation = _generation; inputRevision = _newFileInputRevision; _pending = true;
        }
        try
        {
            return new(_work.RunAsync(async original =>
            {
                original.BindPublicationGuard(() => IsCurrent(generation));
                try
                {
                    token.ThrowIfCancellationRequested();
                    var project = await ResolveProjectAsync(original, token).ConfigureAwait(false);
                    original.DemandPublication();
                    lock (_gate)
                    {
                        if (project.Reference != reference || _project.Reference != reference || _newFileInputRevision != inputRevision || !HasNewFileSlot(file.RelativePath) ||
                            _newFileName != file.RelativePath || file.TemplateId != (_newFileCuiTemplate ? "cui-page" : "empty-source") ||
                            command == "dev.create-new-source-file" && !ReferenceEquals(_newFileReview, reviewed))
                            throw new InvalidOperationException("The project, name or source review changed. Review the current request again.");
                        _newFileReview = null; // never replay a consumed or uncertain create automatically
                    }
                    if (command == "dev.review-new-source-file")
                    {
                        await original.AwaitAsync(InvokeOriginalSource(() => _manualEditor!.PreviewCreateAsync(project, file.RelativePath, _businessToken))).ConfigureAwait(false);
                        await PublishAsync(original, () =>
                        {
                            if (_project.Reference != reference || _newFileInputRevision != inputRevision || _newFileName != file.RelativePath || file.TemplateId != (_newFileCuiTemplate ? "cui-page" : "empty-source")) return;
                            _newFileReview = new(reference, project.Root.Location, file, DeveloperManualSourceEditOwner.Sha256(file.Text));
                            _newFileStatus = "The current target is absent. Review the exact source below; Create requires a separate Home WRITE approval and rechecks absence.";
                        }).ConfigureAwait(false);
                        return;
                    }
                    var hash = await original.AwaitAsync(InvokeOriginalSource(() => _manualEditor!.CreateAsync(project, file.RelativePath, file.Text, _businessToken))).ConfigureAwait(false);
                    // Preserve confirmed effect evidence before any later cold-read/presentation
                    // failure. This is a result observation, never a minted Files identity/grant.
                    lock (_gate) OriginalNewSourceFilePublication = new(reference, file.RelativePath, hash, false);
                    var read = await original.AwaitAsync(InvokeOriginalSource(() => _manualEditor!.ReadAsync(project, file.RelativePath, _businessToken))).ConfigureAwait(false);
                    if (read.Sha256 != hash || read.Text != file.Text)
                        throw new InvalidOperationException("The file was created but changed before its fresh read. Inspect the current source; no draft was substituted.");
                    lock (_gate) OriginalNewSourceFilePublication = new(reference, file.RelativePath, hash, true);
                    await PublishAsync(original, () =>
                    {
                        if (_project.Reference != reference || !HasNewFileSlot(file.RelativePath))
                            throw new InvalidOperationException("The file was created and verified, but this view changed. Open that existing source from its current project.");
                        StoreSelectedTab(); var group = CurrentSourceTab()?.GroupId ?? "default";
                        // This Guid selects a view buffer only. Files identity is not invented.
                        var tab = new SourceTab(Guid.NewGuid(), file.RelativePath, null,
                            new(read.Text, read.Text, read.Text, "", read.Sha256, "", 1, -1, false))
                            { IsPinned = true, IsPreview = false, GroupId = group };
                        _sourceTabs.Add(tab); _project = project; SelectTab(tab); _projectEntries = null;
                        _newFileStatus = "The file was created through Home CREATE approval, cold-read and opened. Further edits use the normal draft, Undo, review and recovery workflow.";
                        _status = _newFileStatus;
                    }).ConfigureAwait(false);
                    await SaveCleanSessionAsync(original, project, token).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    original.Retain(error);
                    if (IsCurrent(generation))
                        try { await PublishAsync(original, () =>
                        {
                            _newFileStatus = "The original operation needs inspection: " + error.Message + " No automatic create retry is performed; use Open source to inspect any published file.";
                            _status = _newFileStatus;
                        }).ConfigureAwait(false); }
                        catch (Exception publication) { original.Retain(publication); }
                }
                finally
                {
                    lock (_gate) _pending = false;
                    if (IsCurrent(generation))
                        try { await PublishAsync(original, () => { }).ConfigureAwait(false); }
                        catch (Exception error) { original.Retain(error); }
                    original.ThrowRetained();
                }
            }));
        }
        catch { lock (_gate) _pending = false; throw; }
    }
}
