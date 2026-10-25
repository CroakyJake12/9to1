using System.Globalization;
using CakeOS.Cui.Runtime;
using Haven.Core;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperProjectWorkbenchPage
{
    private sealed record CuiSourceItem(string SelectionKey, string Label, string AccessibleName, bool CanNavigate);
    private long CurrentDraftRevision() => _manual && _editor is null ? _manualDraftRevision : _editor?.DraftRevision ?? -1;
    private DeveloperCuiSourceObservation? CurrentCuiAnalysis() => CurrentSourceTab() is { CuiAnalysis: { } analysis } tab &&
        tab.CuiAnalysisRevision == CurrentDraftRevision() && StringComparer.Ordinal.Equals(tab.Path, analysis.RelativePath) &&
        StringComparer.Ordinal.Equals(tab.CuiAnalysisText, CurrentEditorText()) ? analysis : null;

    private bool TryGetCuiAnalysisValue(string path, out object? value)
    {
        var current = CurrentCuiAnalysis();
        value = path switch
        {
            "CanAnalyzeCuiDraft" => IsActionAvailable("dev.analyze-cui-draft"),
            "CuiDraftAnalysisStatus" => CurrentSourceTab() is not { } tab ? "Open CUI source to check the current draft."
                : !DeveloperCuiSourceAnalysis.Supports(tab.Path, CurrentEditorText()) ? "Draft checks support CUI source up to 256,000 characters. Other languages and larger sources require their configured toolchain."
                : current is null ? "Check the current CUI draft for syntax/action problems and its component outline."
                : $"{current.Problems.Count} syntax/action problems; {current.Outline.Count} outline elements" + (current.HasMoreElements ? " (first 500)" : "") +
                    ". These draft checks do not resolve imports or replace a full project build." + (current.Problems.Count > 200 ? " Showing the first 200 problems." : ""),
            "CuiDraftProblems" => current is null ? Array.Empty<CuiSourceItem>() : current.Problems.Take(200).Select((problem, index) => new CuiSourceItem(Key(current, "p", index),
                $"{problem.Severity} {problem.Code} — {problem.Message} ({problem.Range.Start.Line + 1}:{problem.Range.Start.Character + 1})",
                $"Go to {problem.Source} {problem.Code} in {problem.RelativePath}, line {problem.Range.Start.Line + 1}, column {problem.Range.Start.Character + 1}", !_pending)).ToArray(),
            "CuiDraftOutline" => current is null ? Array.Empty<CuiSourceItem>() : current.Outline.Select((element, index) => new CuiSourceItem(Key(current, "e", index),
                new string(' ', Math.Min(element.Depth, 16) * 2) + element.Name + " — " + element.Type,
                $"Go to CUI {element.Type} {element.Name}, line {element.Range.Start.Line + 1}", !_pending)).ToArray(),
            _ => null
        };
        return path is "CanAnalyzeCuiDraft" or "CuiDraftAnalysisStatus" or "CuiDraftProblems" or "CuiDraftOutline";
    }

    private static string Key(DeveloperCuiSourceObservation analysis, string kind, int index) =>
        analysis.Id.ToString("D") + ":" + kind + ":" + index.ToString(CultureInfo.InvariantCulture);

    private ValueTask DispatchCuiAnalysisAsync(CancellationToken token)
    {
        long generation, revision; Guid tabId; string path, text;
        lock (_gate)
        {
            if (IsActionAvailable("dev.analyze-cui-draft") != true || CurrentSourceTab() is not { } tab)
                throw new InvalidOperationException("Open supported CUI source before checking its draft.");
            generation = _generation; revision = CurrentDraftRevision(); tabId = tab.Id; path = tab.Path; text = CurrentEditorText(); _pending = true;
        }
        try
        {
            return new(_work.RunAsync(async original =>
            {
                original.BindPublicationGuard(() => IsCurrent(generation));
                try
                {
                    token.ThrowIfCancellationRequested();
                    // Pure bounded CPU work over already admitted draft text. Its actual Task is
                    // retained/joined by this original; it performs no IO or process acquisition.
                    var analysisTask = Task.Run(() => DeveloperCuiSourceAnalysis.Analyze(path, text, token), token);
                    var analysis = await original.AwaitAsync(analysisTask).ConfigureAwait(false);
                    await PublishAsync(original, () =>
                    {
                        token.ThrowIfCancellationRequested();
                        if (CurrentSourceTab() is not { } current || current.Id != tabId || CurrentDraftRevision() != revision ||
                            !StringComparer.Ordinal.Equals(path, current.Path) || !StringComparer.Ordinal.Equals(text, CurrentEditorText()))
                        { _status = "The draft changed while checking CUI. Check the current draft again."; return; }
                        current.CuiAnalysis = analysis; current.CuiAnalysisText = text; current.CuiAnalysisRevision = revision;
                        _status = "CUI draft syntax/action checks finished. Select a problem or outline element to navigate its current source.";
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

    private ValueTask DispatchCuiSourceNavigationAsync(string command, object? parameter, CancellationToken token)
    {
        long generation; Guid tabId; DeveloperCuiSourceObservation analysis; CodeRange range; string text;
        lock (_gate)
        {
            if (IsActionAvailable(command) != true || CurrentSourceTab() is not { } tab || CurrentCuiAnalysis() is not { } current || parameter is not string key)
                throw new InvalidOperationException("Check the current draft and select one of its current CUI source locations.");
            var kind = command == "dev.go-to-cui-problem" ? "p" : "e"; var prefix = current.Id.ToString("D") + ":" + kind + ":";
            if (!key.StartsWith(prefix, StringComparison.Ordinal) || !int.TryParse(key.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                index < 0 || key != Key(current, kind, index) || index >= (kind == "p" ? Math.Min(200, current.Problems.Count) : current.Outline.Count))
                throw new InvalidOperationException("This CUI source location is stale or is not part of the displayed draft analysis.");
            analysis = current; range = kind == "p" ? current.Problems[index].Range : current.Outline[index].Range;
            generation = _generation; tabId = tab.Id; text = CurrentEditorText(); _pending = true;
        }
        try
        {
            return new(_work.RunAsync(async original =>
            {
                original.BindPublicationGuard(() => IsCurrent(generation));
                try
                {
                    await PublishAsync(original, () =>
                    {
                        token.ThrowIfCancellationRequested();
                        if (CurrentSourceTab()?.Id != tabId || !ReferenceEquals(CurrentCuiAnalysis(), analysis) || !StringComparer.Ordinal.Equals(text, CurrentEditorText()))
                        { _status = "The draft analysis changed. Check the current draft again before navigating."; return; }
                        var location = FormattableString.Invariant($"{range.Start.Line + 1}:{range.Start.Character + 1}");
                        if (!DeveloperTextNavigation.TryResolve(text, location, out var offset, token))
                        { _status = "This parser location cannot be mapped to the current source text. Inspect the reported line and column."; return; }
                        _sourceLocation = location;
                        _editorTextSelection = CuiTextSelection.ForText(checked(++_editorNavigationRequest), text, offset, 0);
                        _status = "Navigated to the current CUI draft source location.";
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
