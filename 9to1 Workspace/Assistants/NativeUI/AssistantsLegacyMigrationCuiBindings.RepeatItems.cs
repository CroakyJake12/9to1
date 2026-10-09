using CakeOS.Cui.Runtime;
using HavenOS.Apps.Assistants.Migration;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsLegacyMigrationCuiBindings : ICuiRepeatItemBindingContext
{
    private LegacyAgentMigrationPreview? _notesPreview;
    private IReadOnlyList<NoteRow> _noteRows = [];
    private IReadOnlyList<LegacyAgentLink>? _linksSource;
    private IReadOnlyList<LinkRow> _linkRows = [];
    private void PublishMigrationRepeatRows(LegacyAgentMigrationPreview? preview)
    {
        if (!ReferenceEquals(_notesPreview, preview))
        { _notesPreview = preview; _noteRows = preview?.CompatibilityNotes.Select((note, index) => new NoteRow(index, note)).ToArray() ?? []; }
        if (!ReferenceEquals(_linksSource, _links))
        { _linksSource = _links; _linkRows = _links.Select((link, index) => new LinkRow(index, link.Kind, link.Title ?? link.Id, link.RelatedId ?? "")).ToArray(); }
        Set("MigrationNotes", _noteRows); Set("MigrationLinks", _linkRows);
    }
    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = null; if (!Current) return false;
        if (item is LegacyRow legacy && _rows.Any(actual => ReferenceEquals(actual, legacy)))
            value = path switch { "Id" => legacy.Id, "Name" => legacy.Name, "Description" => legacy.Description, "State" => legacy.State, _ => null };
        else if (item is NoteRow note && _noteRows.Any(actual => ReferenceEquals(actual, note)))
            value = path switch { "Id" => note.Id, "Text" => note.Text, _ => null };
        else if (item is LinkRow link && _linkRows.Any(actual => ReferenceEquals(actual, link)))
            value = path switch { "Id" => link.Id, "Kind" => link.Kind, "Title" => link.Title, "Related" => link.Related, _ => null };
        return value is not null;
    }
    public bool TrySetItemValue(object item, string path, object? value) => false;
}
