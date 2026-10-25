using System.Globalization;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperProjectWorkbenchPage
{
    private sealed record BuildProblemObservation(Guid Id, DeveloperResolvedProject Project, object OriginalProcessObservation,
        ProcessResult? Process, CapturedBuildDiagnostics Report);
    private sealed record BuildProblemSelection(BuildProblemObservation Observation, int Index, string RelativePath);
    private sealed record BuildProblemItem(string SelectionKey, string Label, string AccessibleName, bool CanOpen);
    private BuildProblemObservation? _buildProblems;

    private BuildProblemObservation? CurrentBuildProblems() => _buildProblems is { } observed &&
        observed.Project.Reference == _project.Reference && observed.Project.Root == _project.Root &&
        observed.Project.Repository == _project.Repository ? observed : null;

    private bool TryGetBuildProblemsValue(string path, out object? value)
    {
        var current = CurrentBuildProblems();
        value = path switch
        {
            "BuildProblemsStatus" => current is null ? "Run a command or saved stage to see reported problems. Supported format: .NET/MSBuild."
                : current.Process is null ? "No process result was returned. Check the command output."
                : $"{current.Report.Diagnostics.Count} reported problems" + (current.Report.Truncated ? " (list limited)" : "") +
                    (current.Report.RejectedLineCount == 0 ? ". " : $"; {current.Report.RejectedLineCount} malformed or overlong rows omitted. ") +
                    "Locations refer to saved files; current content may differ. Other output formats stay in command output.",
            "BuildProblems" => current is null ? Array.Empty<BuildProblemItem>() : current.Report.Diagnostics.Select((diagnostic, index) =>
            {
                var label = $"{diagnostic.RelativePath}:{(long)diagnostic.Range.Start.Line + 1}:{(long)diagnostic.Range.Start.Character + 1} — {diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}";
                var supported = DeveloperBuildProblemNavigation.TryGetRelativePath(diagnostic, out _);
                return new BuildProblemItem(BuildProblemKey(current, index), label,
                    supported ? "Open reported source location " + label : "Source location outside this project or unsupported: " + label,
                    !_pending && supported && (_manual ? _manualReader is not null : _review is not null));
            }).ToArray(),
            _ => null
        };
        return path is "BuildProblemsStatus" or "BuildProblems";
    }
    private static string BuildProblemKey(BuildProblemObservation observation, int index) =>
        observation.Id.ToString("N") + ":" + index.ToString(CultureInfo.InvariantCulture);

    private BuildProblemSelection CaptureBuildProblemSelection(object? parameter)
    {
        var current = CurrentBuildProblems();
        if (current is null || parameter is not string key || !key.StartsWith(current.Id.ToString("N") + ":", StringComparison.Ordinal) ||
            !int.TryParse(key.AsSpan(33), NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
            index < 0 || index >= current.Report.Diagnostics.Count || key != BuildProblemKey(current, index) ||
            !DeveloperBuildProblemNavigation.TryGetRelativePath(current.Report.Diagnostics[index], out var relative))
            throw new InvalidOperationException("These reported problems changed or the source is outside this project. Choose a current project problem.");
        return new(current, index, relative);
    }
    private void DemandBuildProblemSelection(BuildProblemSelection selected, DeveloperResolvedProject project)
    {
        lock (_gate)
            if (!ReferenceEquals(CurrentBuildProblems(), selected.Observation) || selected.Observation.Project.Reference != project.Reference ||
                selected.Observation.Project.Root != project.Root || selected.Observation.Project.Repository != project.Repository ||
                CaptureBuildProblemSelection(BuildProblemKey(selected.Observation, selected.Index)).RelativePath != selected.RelativePath)
                throw new InvalidOperationException("The project or reported problems changed. Choose a current problem. Your drafts are unchanged.");
    }
    private static async Task<BuildProblemObservation> CaptureBuildProblemsAsync(DesktopOriginalWorkLifetime.Original original,
        DeveloperResolvedProject project, object originalObservation, ProcessResult? process)
    {
        // A pure retained CPU task over THESE actual process strings, never terminal text scraping,
        // direct project IO, a language-server launch or an invented document/source identity.
        var report = await original.AwaitAsync(Task.Run(() => ProductionCodeIntelligenceService.ParseCapturedBuildDiagnostics(
            process is null ? "" : process.StandardOutput + "\n" + process.StandardError, project.Root.Location))).ConfigureAwait(false);
        return new(Guid.NewGuid(), project, originalObservation, process, report);
    }
    private void NavigateOpenedBuildProblem(BuildProblemSelection selected, string actualSavedText, CuiTextSelection? previousSelection)
    {
        var text = CurrentEditorText();
        if (!StringComparer.Ordinal.Equals(text, actualSavedText))
        {
            _editorTextSelection = previousSelection;
            _status = "Opened the retained draft. It differs from saved source, so the reported location was not selected.";
            return;
        }
        var diagnostic = selected.Observation.Report.Diagnostics[selected.Index];
        if (!DeveloperBuildProblemNavigation.TryResolveCurrentSavedText(diagnostic, text, out var offset))
        {
            _editorTextSelection = previousSelection;
            _status = "Source opened. The reported location is not present in the current saved text.";
            return;
        }
        _editorTextSelection = CuiTextSelection.ForText(checked(++_editorNavigationRequest), text, offset, 0);
        _status = "Opened the reported location in current saved source. Content may have changed since the command ran.";
    }
}
