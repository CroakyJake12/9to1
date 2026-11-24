using HavenOS.Apps.Dev;

namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperProjectWorkbenchPage
{
    private sealed record SavedTestReportView(Guid Tab, string Path, long Revision, string Text,
        string Status, DeveloperSavedTestEvidence? Evidence);
    private SavedTestReportView? _savedTestReport;

    // Only inspect complete, already authorised saved text. A draft is not test execution evidence.
    private bool CanInspectSavedTestReport() => CurrentSourceTab() is { } source &&
        DeveloperTrxTestResults.IsReportPath(source.Path) &&
        (_editor is { } editor ? !editor.IsDirty : _manual && StringComparer.Ordinal.Equals(_manualDraftBaseline, CurrentEditorText()));

    private bool IsSavedTestCaptureCurrent(Guid tab, string path, long revision, string text) =>
        CanInspectSavedTestReport() && CurrentSourceTab() is { } now && now.Id == tab &&
        StringComparer.Ordinal.Equals(now.Path, path) && CurrentDraftRevision() == revision &&
        StringComparer.Ordinal.Equals(CurrentEditorText(), text);

    private bool TryGetSavedTestReportValue(string path, out object? value)
    {
        var current = _savedTestReport is { } result &&
            IsSavedTestCaptureCurrent(result.Tab, result.Path, result.Revision, result.Text) ? result : null;
        value = path switch
        {
            "CanInspectSavedTestReport" => IsActionAvailable("dev.inspect-saved-test-report"),
            "SavedTestReportStatus" => current?.Status ??
                "Open an unchanged saved .trx file through Project files to inspect its recorded test outcomes.",
            "SavedTestReportDetails" => current?.Evidence?.Details ?? "",
            _ => null
        };
        return path is "CanInspectSavedTestReport" or "SavedTestReportStatus" or "SavedTestReportDetails";
    }

    private ValueTask DispatchSavedTestReportAsync(CancellationToken token)
    {
        long generation, revision;
        Guid tabId;
        string path, text;
        lock (_gate)
        {
            if (IsActionAvailable("dev.inspect-saved-test-report") != true || CurrentSourceTab() is not { } tab)
                throw new InvalidOperationException("Open a saved .trx file without unsaved edits first.");
            generation = _generation; revision = CurrentDraftRevision(); tabId = tab.Id;
            path = tab.Path; text = CurrentEditorText(); _pending = true;
        }
        try
        {
            return new(_work.RunAsync(async original =>
            {
                original.BindPublicationGuard(() => IsCurrent(generation));
                try
                {
                    var report = await original.AwaitAsync(Task.Run(() =>
                    {
                        var parsed = DeveloperTrxTestResults.Parse(text, token);
                        return (Error: parsed.Error?.Message.Replace(" The previous report is kept.", "", StringComparison.Ordinal), Evidence: parsed.Value is { } loaded
                            ? DeveloperSavedTestEvidence.FromReport(loaded) : null);
                    }, token)).ConfigureAwait(false);
                    await PublishAsync(original, () =>
                    {
                        token.ThrowIfCancellationRequested();
                        if (!IsSavedTestCaptureCurrent(tabId, path, revision, text)) return;
                        _savedTestReport = new(tabId, path, revision, text,
                            report.Evidence?.Summary ?? "Saved report not available. " + report.Error, report.Evidence);
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
