using System.Collections.Immutable;
using System.Text;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperProjectWorkbenchPage
{
    private string _templateImportStatus = "";
    private bool _templateImportFailed;
    private readonly List<DeveloperTemplateFileCreationObservation> _templateFileCreations = [];

    internal bool HasRetainedTemplateImportFailure
    {
        get { lock (_gate) return _templateImportFailed; }
    }

    /// <summary>Creates the exact reviewed provider files in this already-registered manual
    /// project. Every absent-target file uses the existing Home READ + distinct CREATE WRITE
    /// owner, and every successful write is cold-read and hash-checked before the next file begins.
    /// A partial result is retained and never replayed automatically.</summary>
    public Task CreateTemplateProjectFilesAsync(DeveloperProjectTemplateMaterialization materialization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(materialization);
        var manualEditor = _manualEditor;
        if (!_manual || manualEditor is null || _project.Root.EnvironmentId != "local")
            throw new NotSupportedException("Template files require the configured local manual-project CREATE owner.");
        if (materialization.Files.IsDefaultOrEmpty || materialization.Files.Length > DeveloperProjectTemplateMaterializer.MaximumFiles)
            throw new InvalidDataException("The reviewed template contains no supported source files.");
        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var file in materialization.Files)
        {
            if (file is null || string.IsNullOrWhiteSpace(file.RelativePath) || file.RelativePath.Contains('/') ||
                file.RelativePath.Contains('\\') || !StringComparer.Ordinal.Equals(file.RelativePath, Path.GetFileName(file.RelativePath)) ||
                file.Text is null || !paths.Add(file.RelativePath) ||
                !StringComparer.Ordinal.Equals(DeveloperManualSourceEditOwner.Sha256(file.Text), file.ContentSha256) ||
                Encoding.UTF8.GetByteCount(file.Text) != file.SizeBytes)
                throw new InvalidDataException("The template file paths or captured UTF-8 hashes are invalid.");
        }

        long generation;
        lock (_gate)
        {
            generation = _generation;
            _templateImportStatus = "Creating the reviewed project files through Home…";
            _templateImportFailed = false;
            _templateFileCreations.Clear();
        }

        return _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(original.Token,
                cancellationToken, _connectionLifetime, _windowLifetime);
            var token = linked.Token;
            var reference = _project.Reference;
            try
            {
                var project = await ResolveProjectAsync(original, token).ConfigureAwait(false);
                if (project.Reference != reference || _project.Reference != reference)
                    throw new InvalidOperationException("The registered project changed before template creation.");

                var created = new List<(DeveloperMaterializedProjectFile File, string Hash, string Text)>();
                foreach (var file in materialization.Files)
                {
                    original.DemandPublication(); token.ThrowIfCancellationRequested();
                    // The exact file is reviewed in the landing. This fresh absent-target check
                    // and the following Home CREATE approval are still required for each write.
                    await original.AwaitAsync(InvokeOriginalSource(() =>
                        manualEditor.PreviewCreateAsync(project, file.RelativePath, token))).ConfigureAwait(false);
                    lock (_gate) _templateFileCreations.Add(new(file.RelativePath, file.ContentSha256,
                        "Target absent; Home CREATE has not started."));
                    SetTemplateFileOutcome(file.RelativePath, "Home CREATE is in progress; inspect this target if interrupted.");
                    var hash = await original.AwaitAsync(InvokeOriginalSource(() =>
                        manualEditor.CreateAsync(project, file.RelativePath, file.Text, token))).ConfigureAwait(false);
                    SetTemplateFileOutcome(file.RelativePath, "Home CREATE returned success; cold-read verification is pending.");

                    var read = await original.AwaitAsync(InvokeOriginalSource(() =>
                        manualEditor.ReadAsync(project, file.RelativePath, token))).ConfigureAwait(false);
                    if (!StringComparer.Ordinal.Equals(hash, file.ContentSha256) ||
                        !StringComparer.Ordinal.Equals(read.Sha256, file.ContentSha256) ||
                        !StringComparer.Ordinal.Equals(read.Text, file.Text))
                        throw new InvalidDataException("A created template file differs from its exact reviewed bytes. Inspect the retained project; no file is replaced or replayed.");
                    SetTemplateFileOutcome(file.RelativePath, "Home CREATE confirmed and exact cold-read verified.");
                    created.Add((file, hash, read.Text));
                }

                var initial = created.FirstOrDefault(item => item.File.RelativePath.EndsWith(".cui", StringComparison.OrdinalIgnoreCase));
                if (initial.File is not null)
                {
                    await PublishAsync(original, () =>
                    {
                        if (_project.Reference != reference) throw new InvalidOperationException("The project changed after its template files were created.");
                        StoreSelectedTab();
                        var group = CurrentSourceTab()?.GroupId ?? "default";
                        var tab = new SourceTab(Guid.NewGuid(), initial.File.RelativePath, null,
                            new(initial.Text, initial.Text, initial.Text, "", initial.Hash, "", 1, -1, false))
                        { IsPinned = true, IsPreview = false, GroupId = group };
                        _sourceTabs.Add(tab); SelectTab(tab);
                        _templateImportStatus = $"Created and verified {created.Count} template files through Home CREATE. Opened {initial.File.RelativePath}. " + FormatTemplateFileOutcomes();
                    }).ConfigureAwait(false);
                }
                else
                    await PublishAsync(original, () => _templateImportStatus =
                        $"Created and verified {created.Count} template files through Home CREATE. No CUI source tab was selected. " + FormatTemplateFileOutcomes()).ConfigureAwait(false);
            }
            catch (OperationCanceledException error)
            {
                original.Retain(error);
                try { await PublishAsync(original, () =>
                {
                    _templateImportFailed = true;
                    _templateImportStatus = "Template creation was canceled. Inspect the retained project and these per-file outcomes before continuing: " + FormatTemplateFileOutcomes();
                }).ConfigureAwait(false); }
                catch (Exception publicationError) { original.Retain(publicationError); }
                original.ThrowRetained();
            }
            catch (Exception error)
            {
                original.Retain(error);
                try
                {
                    await PublishAsync(original, () => _templateImportStatus =
                    {
                        _templateImportFailed = true;
                        _templateImportStatus = "Template creation stopped: " + error.Message +
                            " Inspect these exact outcomes; the setup and file CREATE steps will not be replayed. " + FormatTemplateFileOutcomes();
                    }).ConfigureAwait(false);
                }
                catch (Exception publicationError) { original.Retain(publicationError); }
                original.ThrowRetained();
            }
        });
    }

    private void SetTemplateFileOutcome(string path, string state)
    {
        lock (_gate)
        {
            var index = _templateFileCreations.FindLastIndex(item => item.RelativePath == path);
            if (index < 0) throw new InvalidOperationException("The actual template file outcome was not retained.");
            _templateFileCreations[index] = _templateFileCreations[index] with { State = state };
        }
    }

    private string FormatTemplateFileOutcomes() => _templateFileCreations.Count == 0
        ? "No per-file Home CREATE outcome was confirmed."
        : string.Join(" | ", _templateFileCreations.Select(item =>
            $"{item.RelativePath} · SHA-256 {item.Sha256} · {item.State}"));

    private sealed record DeveloperTemplateFileCreationObservation(string RelativePath, string Sha256, string State);
}
