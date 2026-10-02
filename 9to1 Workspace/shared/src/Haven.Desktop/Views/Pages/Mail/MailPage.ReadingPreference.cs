using Avalonia.Controls;
using Haven.Application;

namespace Haven.Desktop.Views.Pages.Mail;

public sealed record MailReadingPanePreference(int SchemaVersion, MailReadingPanePlacement Placement);

public sealed partial class MailPage
{
    internal const string ReadingPreferenceKey = "mail.reading-pane.v1";
    private IVersionedSettingsStore? _readingPreferences;
    private readonly SemaphoreSlim _readingPreferenceWrites = new(1, 1);
    private long _readingPreferenceGeneration;
    private bool _applyingReadingPreference;
    internal Task PendingReadingPreference { get; private set; } = Task.CompletedTask;

    private async Task LoadReadingPreferenceAsync()
    {
        if (_readingPreferences is null) return;
        var generation = _readingPreferenceGeneration;
        try
        {
            var value = await _readingPreferences.GetAsync<MailReadingPanePreference>(ReadingPreferenceKey, CancellationToken.None);
            if (_disposed || generation != _readingPreferenceGeneration || value is null) return;
            if (value.SchemaVersion != 1 || !Enum.IsDefined(value.Placement))
                throw new InvalidDataException("Unsupported local reading pane preference.");
            _applyingReadingPreference = true;
            try { ReadingPlacementPicker.SelectedIndex = (int)value.Placement; }
            finally { _applyingReadingPreference = false; }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            if (!_disposed && generation == _readingPreferenceGeneration)
                ToolTip.SetTip(ReadingPlacementPicker, "Couldn’t load the saved reading pane preference. Your current choice remains available.");
        }
    }

    private async Task SaveReadingPreferenceAsync(MailReadingPanePlacement placement, long generation)
    {
        var store = _readingPreferences;
        if (store is null) return;
        await _readingPreferenceWrites.WaitAsync();
        try
        {
            if (generation != _readingPreferenceGeneration) return;
            // Persist the actual requested local layout even if the page closes during the write.
            await store.SetAsync(ReadingPreferenceKey, new MailReadingPanePreference(1, placement), CancellationToken.None);
            if (!_disposed && generation == _readingPreferenceGeneration)
                ToolTip.SetTip(ReadingPlacementPicker, "Reading pane preference saved for next time.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            if (!_disposed && generation == _readingPreferenceGeneration)
                ToolTip.SetTip(ReadingPlacementPicker, "Reading pane changed for this window. Couldn’t confirm the preference was saved for next time.");
        }
        finally { _readingPreferenceWrites.Release(); }
    }
}
