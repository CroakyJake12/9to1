using System.Text.Json;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Boards;

public sealed partial class BoardsPage
{
    private long _notebookNavigationGeneration;
    private static readonly JsonSerializerOptions NavigationJson = new(JsonSerializerDefaults.Web);

    private async Task<bool> TrySwitchNotebookAsync(Guid id, Guid? sectionId, Guid? pageId,
        CancellationToken ct, Func<bool>? originalPickerCurrent = null)
    {
        if (_disposed || originalPickerCurrent is not null && !originalPickerCurrent()) return false;
        var original = _document;
        var generation = ++_notebookNavigationGeneration;
        bool Current() => !_disposed && generation == _notebookNavigationGeneration &&
            ReferenceEquals(_document, original) && (originalPickerCurrent is null || originalPickerCurrent());
        if (original?.Id == id)
        {
            ActivateNotebook(original, sectionId, pageId); // Keep the live unsaved tree on same-board navigation.
            return true;
        }
        var currentSnapshotSaved = false;
        try
        {
            // Same structured serializer/IDs as the canonical Notes owner; the save candidate is
            // detached before the first await and cannot adopt later live editor changes.
            var before = original is null ? null : JsonSerializer.Serialize(original, NavigationJson);
            var snapshot = before is null ? null : JsonSerializer.Deserialize<NotesDocument>(before, NavigationJson)
                ?? throw new InvalidDataException("The current board snapshot is unavailable.");
            SetStatus("Opening notebook...");
            var next = await _boards.OpenNotebookAsync(id, ct);
            ct.ThrowIfCancellationRequested();
            if (!Current()) return false;
            if (next is null) { SetStatus("That Boards notebook is unavailable or no longer exists."); return false; }
            if (snapshot is not null)
            {
                await _boards.SaveAsync(snapshot, "Autosave before switching Boards notebook", ct);
                currentSnapshotSaved = true;
                // A returned owning save is known acknowledged. Retain its version on the same
                // live tree even when newer edits mean navigation must stay on that tree.
                var laterChanges = original is not null && JsonSerializer.Serialize(original, NavigationJson) != before;
                if (!_disposed && ReferenceEquals(_document, original))
                {
                    original!.Version = Math.Max(original.Version, snapshot.Version);
                    if (!laterChanges) original.UpdatedAt = snapshot.UpdatedAt;
                    original.Recovery.LastAutosaveAt = snapshot.Recovery.LastAutosaveAt;
                    original.Recovery.LastValidSha256 = snapshot.Recovery.LastValidSha256;
                    original.Recovery.HasUnsavedRecovery = laterChanges;
                }
                if (!Current()) return false;
                if (laterChanges)
                {
                    SetStatus("Saved the captured board. Later edits remain open; switch again when ready.");
                    return false;
                }
                // Refresh the actual destination after saving. Never use its earlier read as a
                // replacement for the latest canonical tree, or hide an intervening live edit.
                var retained = JsonSerializer.Serialize(original, NavigationJson);
                next = await _boards.OpenNotebookAsync(id, ct);
                ct.ThrowIfCancellationRequested();
                if (!Current()) return false;
                if (JsonSerializer.Serialize(original, NavigationJson) != retained)
                {
                    original!.Recovery.HasUnsavedRecovery = true;
                    SetStatus("Later edits remain open. Switch again when ready.");
                    return false;
                }
                if (next is null) { SetStatus("That Boards notebook is unavailable or no longer exists."); return false; }
            }
            if (!Current()) return false;
            ActivateNotebook(next, sectionId, pageId);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (Current()) SetStatus(currentSnapshotSaved
                ? "Your board was saved. Switching was cancelled; the current board remains open."
                : "Board switching was cancelled. Your current board remains open.");
            return false;
        }
        catch (Exception)
        {
            if (Current()) SetStatus(currentSnapshotSaved
                ? "Your board was saved. Couldn’t open the destination; the current board remains open."
                : "Couldn’t save or open the board. Your current edits remain open.");
            return false;
        }
    }
}
