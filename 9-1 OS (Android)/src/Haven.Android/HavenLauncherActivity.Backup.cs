using System.Text;
using Android.App;
using Android.Content;
using Android.Widget;
using NineToOne.Launcher;

namespace Haven.Android;

public sealed partial class HavenLauncherActivity
{
    private readonly AndroidLauncherLayoutDocumentSelections _layoutDocuments = new();
    private AlertDialog? _layoutDocumentDialog;
    private void CloseLayoutDocumentDialogs()
    {
        _layoutDocuments.Cancel();
        _layoutDocumentDialog?.Dismiss(); _layoutDocumentDialog = null;
    }

    private void PickLayoutDocument(bool export, LauncherStoredLayout displayed)
    {
        try
        {
            if (!_activityStarted || _launcherLifetime.IsCancellationRequested) return;
            CloseLayoutDocumentDialogs();
            var selection = _layoutDocuments.Issue(DisplayedLayouts.Require(displayed), export);
            var intent = new Intent(export ? Intent.ActionCreateDocument : Intent.ActionOpenDocument);
            intent.AddCategory(Intent.CategoryOpenable); intent.SetType("application/json");
            if (export) intent.PutExtra(Intent.ExtraTitle, "9to1-launcher-layout.json");
            StartActivityForResult(intent, selection.RequestCode);
        }
        catch (Exception error) when (error is ActivityNotFoundException or InvalidOperationException or UnauthorizedAccessException)
        { _layoutDocuments.Cancel(); Toast.MakeText(this, error.Message, ToastLength.Long)?.Show(); }
    }

    private async Task CompleteLayoutDocumentAsync(int request, Result result, global::Android.Net.Uri? uri)
    {
        // Only the same process-issued picker identity consumes its retained original selection.
        var selection = _layoutDocuments.Take(request);
        if (selection is null) return;
        var expected = selection.Original; var ct = _launcherLifetime.Token;
        bool CurrentSelection() => !_launcherLifetime.IsCancellationRequested && !IsDestroyed && !IsFinishing && _layoutDocuments.IsCurrent(selection);
        if (result != Result.Ok || uri is null) return;
        try
        {
            if (expected is null || !CurrentSelection() || !await WidgetSessions.IsCurrentAsync(expected, ct) || !CurrentSelection())
                throw new UnauthorizedAccessException("Home changed during document selection. Select the layout document again.");
            var snapshot = DisplayedLayouts.Bind(expected);
            if (selection.Export)
            {
                var bytes = Encoding.UTF8.GetBytes(LauncherLayoutExchange.Export(snapshot));
                async Task RequireExportSelection()
                {
                    ct.ThrowIfCancellationRequested();
                    if (!CurrentSelection() || !await WidgetSessions.IsCurrentAsync(expected, ct) || !CurrentSelection())
                        throw new UnauthorizedAccessException("The original export selection changed. The document may contain a partial write; choose it again.");
                }
                await RequireExportSelection();
                await using var output = ContentResolver?.OpenOutputStream(uri, "wt") ?? throw new IOException("The selected document cannot be written.");
                await RequireExportSelection();
                await output.WriteAsync(bytes, ct);
                await RequireExportSelection();
                await output.FlushAsync(ct);
                await RequireExportSelection();
                if (_activityStarted && CurrentSelection()) Toast.MakeText(this, "Launcher layout exported.", ToastLength.Short)?.Show();
                return;
            }
            await using var input = ContentResolver?.OpenInputStream(uri) ?? throw new IOException("The selected document cannot be read.");
            var layout = await AndroidLauncherLayoutDocumentReader.ReadAsync(WidgetSessions, expected, input, CurrentSelection, ct);
            if (!CurrentSelection() || !_activityStarted) throw new UnauthorizedAccessException("The original launcher backup view changed.");
            var dialog = new AlertDialog.Builder(this); dialog.SetTitle("Restore launcher layout?");
            dialog.SetMessage($"Replace the current layout with {layout.Pages.Count} pages and {LauncherLayoutEdits.Placements(layout).Count()} items, including {layout.Drawer?.Categories.Count ?? 0} drawer categories, hidden apps, appearance and gesture settings. The current layout remains available through Restore previous layout.");
            dialog.SetPositiveButton("Restore", (_, _) =>
            {
                if (CurrentSelection() && _activityStarted) _ = EditLayoutAsync(_ => layout, snapshot);
            });
            dialog.SetNegativeButton("Cancel", (_, _) => { });
            _layoutDocumentDialog = dialog.Show();
        }
        catch (OperationCanceledException) when (_launcherLifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or global::Java.Lang.SecurityException)
        { if (CurrentSelection() && _activityStarted) Toast.MakeText(this, ex.Message, ToastLength.Long)?.Show(); }
    }
}
