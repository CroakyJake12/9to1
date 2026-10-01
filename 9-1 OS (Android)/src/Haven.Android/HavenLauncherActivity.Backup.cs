using System.Text;
using Android.App;
using Android.Content;
using Android.Widget;
using NineToOne.Launcher;

namespace Haven.Android;

public sealed partial class HavenLauncherActivity
{
    private const int ExportLayoutRequest = 8103;
    private const int ImportLayoutRequest = 8104;
    private LauncherSessionSnapshot? _pendingLayoutDocumentSession;
    private int _pendingLayoutDocumentRequest;

    private void PickLayoutDocument(bool export, LauncherStoredLayout displayed)
    {
        try
        {
            _pendingLayoutDocumentSession = DisplayedLayouts.Require(displayed);
            _pendingLayoutDocumentRequest = export ? ExportLayoutRequest : ImportLayoutRequest;
            var intent = new Intent(export ? Intent.ActionCreateDocument : Intent.ActionOpenDocument);
            intent.AddCategory(Intent.CategoryOpenable); intent.SetType("application/json");
            if (export) intent.PutExtra(Intent.ExtraTitle, "9to1-launcher-layout.json");
            StartActivityForResult(intent, export ? ExportLayoutRequest : ImportLayoutRequest);
        }
        catch (Exception error) when (error is ActivityNotFoundException or InvalidOperationException or UnauthorizedAccessException)
        { _pendingLayoutDocumentSession = null; Toast.MakeText(this, error.Message, ToastLength.Long)?.Show(); }
    }

    private async Task CompleteLayoutDocumentAsync(int request, Result result, global::Android.Net.Uri? uri)
    {
        var expected = request == _pendingLayoutDocumentRequest ? _pendingLayoutDocumentSession : null;
        _pendingLayoutDocumentSession = null; _pendingLayoutDocumentRequest = 0;
        if (result != Result.Ok || uri is null) return;
        try
        {
            if (expected is null || !await WidgetSessions.IsCurrentAsync(expected, _launcherLifetime.Token))
                throw new UnauthorizedAccessException("Home changed during document selection. Select the layout document again.");
            var snapshot = DisplayedLayouts.Bind(expected);
            if (request == ExportLayoutRequest)
            {
                var bytes = Encoding.UTF8.GetBytes(LauncherLayoutExchange.Export(snapshot));
                await using var output = ContentResolver?.OpenOutputStream(uri, "wt") ?? throw new IOException("The selected document cannot be written.");
                await output.WriteAsync(bytes, _launcherLifetime.Token); await output.FlushAsync(_launcherLifetime.Token);
                Toast.MakeText(this, "Launcher layout exported.", ToastLength.Short)?.Show();
                return;
            }
            await using var input = ContentResolver?.OpenInputStream(uri) ?? throw new IOException("The selected document cannot be read.");
            using var buffer = new MemoryStream(); var chunk = new byte[8192]; int count;
            while ((count = await input.ReadAsync(chunk, _launcherLifetime.Token)) != 0)
            {
                if (buffer.Length + count > LauncherLayoutExchange.MaximumBytes) throw new InvalidDataException("Select a launcher backup of at most 4 MiB.");
                await buffer.WriteAsync(chunk.AsMemory(0, count), _launcherLifetime.Token);
            }
            if (!await WidgetSessions.IsCurrentAsync(expected, _launcherLifetime.Token))
                throw new UnauthorizedAccessException("Home changed while reading the layout document.");
            var layout = LauncherLayoutExchange.Import(new UTF8Encoding(false, true).GetString(buffer.ToArray()), snapshot.AuthorityId);
            var dialog = new AlertDialog.Builder(this); dialog.SetTitle("Restore launcher layout?");
            dialog.SetMessage($"Replace the current layout with {layout.Pages.Count} pages and {LauncherLayoutEdits.Placements(layout).Count()} items, including {layout.Drawer?.Categories.Count ?? 0} drawer categories, hidden apps, appearance and gesture settings. The current layout remains available through Restore previous layout.");
            dialog.SetPositiveButton("Restore", (_, _) => _ = EditLayoutAsync(_ => layout, snapshot));
            dialog.SetNegativeButton("Cancel", (_, _) => { }); dialog.Show();
        }
        catch (OperationCanceledException) when (_launcherLifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or global::Java.Lang.SecurityException)
        { Toast.MakeText(this, ex.Message, ToastLength.Long)?.Show(); }
    }
}
