using System.Globalization;
using Haven.Desktop.Services;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperProjectWorkbenchPage
{
    private const int FileNavigationPageSize = 50;
    private string _fileNavigationQuery = "";
    private long _fileNavigationQueryVersion;
    private int _fileNavigationPage;
    private string _fileNavigationStatus = "Find a literal filename or path fragment across the current project. Results are freshly captured through Home READ; opening reads the selected source again.";
    private FileNavigationObservation? _fileNavigationObservation;
    private sealed record FileNavigationObservation(Guid Id, DeveloperResolvedProject Project,
        IReadOnlyList<DeveloperProjectTreeEntry> Manifest, long QueryVersion, DeveloperProjectFileNavigationResult Result);
    private sealed record FileNavigationSelection(FileNavigationObservation Observation, int Index, string RelativePath);
    private sealed record FileNavigationItem(string SelectionKey, string RelativePath, string AccessibleName, bool CanOpen);

    private FileNavigationObservation? CurrentFileNavigationObservation() =>
        _fileNavigationObservation is { } value && value.QueryVersion == _fileNavigationQueryVersion &&
        value.Result.Query == _fileNavigationQuery && ReferenceEquals(value.Manifest, _projectEntries) &&
        value.Project.Reference == _project.Reference && value.Project.Root == _project.Root && value.Project.Repository == _project.Repository ? value : null;

    private bool TryGetFileNavigationValue(string path, out object? value)
    {
        var current = CurrentFileNavigationObservation();
        value = path switch
        {
            "FileNavigationQuery" => _fileNavigationQuery,
            "CanFindProjectFile" => IsActionAvailable("dev.find-project-file"),
            "CanFileNavigationPrevious" => IsActionAvailable("dev.file-navigation-previous"),
            "CanFileNavigationNext" => IsActionAvailable("dev.file-navigation-next"),
            "FileNavigationStatus" => current is null ? _fileNavigationStatus :
                $"{current.Result.Matches.Count} matching files; page {_fileNavigationPage + 1} of {Math.Max(1, (current.Result.Matches.Count + FileNavigationPageSize - 1) / FileNavigationPageSize)} ({FileNavigationPageSize} per page). Filename matches precede path matches. Open rechecks current project authority and preserves existing dirty tabs.",
            "FileNavigationMatches" => current is null ? Array.Empty<FileNavigationItem>() : current.Result.Matches
                .Skip(_fileNavigationPage * FileNavigationPageSize).Take(FileNavigationPageSize)
                .Select((match, index) => new FileNavigationItem(FileNavigationKey(current, _fileNavigationPage * FileNavigationPageSize + index),
                    match.RelativePath, "Open current project source " + match.RelativePath, !_pending)).ToArray(),
            _ => null
        };
        return path is "FileNavigationQuery" or "CanFindProjectFile" or "CanFileNavigationPrevious" or "CanFileNavigationNext" or "FileNavigationStatus" or "FileNavigationMatches";
    }

    private void ChangeFileNavigationQuery(string text)
    {
        if (text == _fileNavigationQuery) return;
        _fileNavigationQuery = text; _fileNavigationQueryVersion++; _fileNavigationPage = 0; _fileNavigationObservation = null;
        _fileNavigationStatus = DeveloperProjectFileNavigation.IsValidQuery(text)
            ? "Query changed. Find files to capture and search the current project."
            : "Enter a literal filename or path fragment up to 256 characters.";
    }

    private static string FileNavigationKey(FileNavigationObservation observation, int index) =>
        observation.Id.ToString("N") + ":" + index.ToString(CultureInfo.InvariantCulture);

    private FileNavigationSelection CaptureFileNavigationSelection(object? parameter)
    {
        var observation = CurrentFileNavigationObservation();
        if (observation is null || parameter is not string key || !key.StartsWith(observation.Id.ToString("N") + ":", StringComparison.Ordinal) ||
            !int.TryParse(key.AsSpan(33), NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index < _fileNavigationPage * FileNavigationPageSize ||
            index >= Math.Min(observation.Result.Matches.Count, (_fileNavigationPage + 1) * FileNavigationPageSize) || key != FileNavigationKey(observation, index))
            throw new InvalidOperationException("Choose a displayed file from the current captured query; this selection is stale or foreign.");
        var relative = observation.Result.Matches[index].RelativePath;
        if (!observation.Manifest.Any(entry => !entry.IsDirectory && StringComparer.Ordinal.Equals(entry.RelativePath, relative)))
            throw new InvalidOperationException("The selected file is absent from this exact captured manifest.");
        return new(observation, index, relative);
    }

    private void DemandFileNavigationSelection(FileNavigationSelection selection, DeveloperResolvedProject project)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(CurrentFileNavigationObservation(), selection.Observation) ||
                selection.Observation.Project.Reference != project.Reference || selection.Observation.Project.Root != project.Root ||
                selection.Observation.Project.Repository != project.Repository ||
                CaptureFileNavigationSelection(FileNavigationKey(selection.Observation, selection.Index)).RelativePath != selection.RelativePath)
                throw new InvalidOperationException("The query, manifest or current project changed before source publication. Existing drafts are preserved; find the file again.");
        }
    }

    private ValueTask DispatchFileNavigationAsync(string command, CancellationToken token)
    {
        long generation, queryVersion; string query;
        lock (_gate)
        {
            if (IsActionAvailable(command) != true) throw new InvalidOperationException("This current-project file navigation operation is unavailable.");
            generation = _generation; queryVersion = _fileNavigationQueryVersion; query = _fileNavigationQuery; _pending = true;
        }
        try
        {
            return new(_work.RunAsync(async original =>
            {
                original.BindPublicationGuard(() => IsCurrent(generation));
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (command != "dev.find-project-file")
                    {
                        await PublishAsync(original, () =>
                        {
                            if (queryVersion != _fileNavigationQueryVersion || CurrentFileNavigationObservation() is not { } observation) return;
                            if (command == "dev.file-navigation-previous") _fileNavigationPage = Math.Max(0, _fileNavigationPage - 1);
                            else if (observation.Result.Matches.Count > (_fileNavigationPage + 1) * FileNavigationPageSize) _fileNavigationPage++;
                        }).ConfigureAwait(false);
                        return;
                    }
                    var project = await ResolveProjectAsync(original, token).ConfigureAwait(false);
                    original.DemandPublication();
                    var entries = await original.AwaitAsync(InvokeOriginalSource(() => _manualReader!.ListEntriesAsync(project, token))).ConfigureAwait(false);
                    var paths = entries.Where(value => !value.IsDirectory).Select(value => value.RelativePath).ToArray();
                    var result = await original.AwaitAsync(Task.Run(() => DeveloperProjectFileNavigation.Search(paths, query, token), token)).ConfigureAwait(false);
                    await PublishAsync(original, () =>
                    {
                        token.ThrowIfCancellationRequested();
                        if (queryVersion != _fileNavigationQueryVersion || query != _fileNavigationQuery)
                        { _status = "The file query changed during capture. Find the current query again; source tabs are unchanged."; return; }
                        _project = project; PublishProjectTree(entries);
                        _fileNavigationPage = 0;
                        _fileNavigationObservation = result.Succeeded && result.Value is { } matches
                            ? new(Guid.NewGuid(), project, entries, queryVersion, matches) : null;
                        _fileNavigationStatus = result.Error?.Message ?? "Current project file query finished.";
                        _status = "Current-project file navigation used the genuine captured Home/Files manifest; no source was opened or changed.";
                    }).ConfigureAwait(false);
                }
                catch (Exception error) { original.Retain(error); }
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
