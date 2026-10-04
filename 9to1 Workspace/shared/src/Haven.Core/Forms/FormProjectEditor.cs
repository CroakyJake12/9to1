using System.Text.Json;

namespace Haven.Core.Forms;

/// <summary>Canonical authoring transformations. The owning application persists the returned revision atomically.
/// Every operation captures the entire input, so neither undo state nor the next project aliases caller lists.</summary>
public static class FormProjectEditor
{
    public static FormProject Create(string title, FormModeKind mode, DateTimeOffset now)
    {
        var project = new FormProject(1, Guid.NewGuid(), title, new(Guid.NewGuid(), mode), [],
            [new(Guid.NewGuid(), "Page 1", [], new())], [], [], new("default"), null, [],
            new([], FormResultRelease.AfterSubmission), [], new(true), new(FormRespondentAccess.OwnerOnly, []),
            new(), now, now, 1);
        return FormProjectCodec.Capture(project);
    }

    public static FormProject AddPage(FormProject project, long revision, FormPage page, DateTimeOffset now) =>
        Change(project, revision, now, current => current with { Pages = current.Pages.Append(page).ToArray() });

    public static FormProject AddField(FormProject project, long revision, Guid pageID, FormField field, DateTimeOffset now) =>
        Change(project, revision, now, current =>
        {
            if (!current.Pages.Any(page => page.PageID == pageID)) throw new KeyNotFoundException("PageNotFound");
            return current with { Fields = current.Fields.Append(field).ToArray(), Pages = current.Pages.Select(page => page.PageID != pageID
                ? page : page with { Children = page.Children.Append(new(FormChildKind.Field, field.FieldID)).ToArray(),
                    Revision = checked(page.Revision + 1) }).ToArray() };
        });

    public static FormProject UpdateField(FormProject project, long revision, FormField replacement, DateTimeOffset now) =>
        Change(project, revision, now, current =>
        {
            var previous = current.Fields.SingleOrDefault(field => field.FieldID == replacement.FieldID)
                ?? throw new KeyNotFoundException("FieldNotFound");
            if (previous.Revision != replacement.Revision) throw new InvalidOperationException("RevisionConflict");
            return current with { Fields = current.Fields.Select(field => field.FieldID == previous.FieldID
                ? replacement with { Revision = checked(previous.Revision + 1) } : field).ToArray() };
        });

    public static FormProject MoveField(FormProject project, long revision, Guid fieldID, Guid pageID, int index, DateTimeOffset now) =>
        Change(project, revision, now, current =>
        {
            if (!current.Fields.Any(field => field.FieldID == fieldID)) throw new KeyNotFoundException("FieldNotFound");
            var target = current.Pages.SingleOrDefault(page => page.PageID == pageID) ?? throw new KeyNotFoundException("PageNotFound");
            var reference = new FormChildReference(FormChildKind.Field, fieldID);
            var targetChildren = target.Children.Where(child => child != reference).ToList();
            if (index < 0 || index > targetChildren.Count) throw new ArgumentOutOfRangeException(nameof(index));
            targetChildren.Insert(index, reference);
            return current with { Pages = current.Pages.Select(page => page.PageID == pageID
                ? page with { Children = targetChildren.ToArray(), Revision = checked(page.Revision + 1) }
                : page.Children.Contains(reference) ? page with { Children = page.Children.Where(child => child != reference).ToArray(),
                    Revision = checked(page.Revision + 1) } : page).ToArray() };
        });

    public static FormProject SetMode(FormProject project, long revision, FormModeDefinition mode, DateTimeOffset now) =>
        Change(project, revision, now, current => current with { ModeDefinition = mode });
    public static FormProject SetTheme(FormProject project, long revision, FormThemeReference theme, DateTimeOffset now) =>
        Change(project, revision, now, current => current with { Theme = theme });
    public static FormProject BindData(FormProject project, long revision, FormDataBinding binding, DateTimeOffset now) =>
        Change(project, revision, now, current =>
        {
            var field = current.Fields.SingleOrDefault(field => field.FieldID == binding.FieldID) ?? throw new KeyNotFoundException("FieldNotFound");
            if (field.DataBindingID is { } existing && existing != binding.BindingID) throw new InvalidOperationException("DataSyncConflict");
            return current with { DataBindings = current.DataBindings.Where(item => item.BindingID != binding.BindingID).Append(binding).ToArray(),
                Fields = current.Fields.Select(item => item.FieldID == field.FieldID
                    ? item with { DataBindingID = binding.BindingID, Revision = checked(item.Revision + 1) } : item).ToArray() };
        });

    private static FormProject Change(FormProject project, long revision, DateTimeOffset now, Func<FormProject, FormProject> change)
    {
        var captured = FormProjectCodec.Capture(project);
        if (revision != captured.Revision) throw new InvalidOperationException("RevisionConflict");
        if (now < captured.ModifiedAt) throw new ArgumentOutOfRangeException(nameof(now));
        return FormProjectCodec.Capture(change(captured) with { Revision = checked(revision + 1), ModifiedAt = now });
    }

    public static JsonElement Project(FormProject project)
    {
        using var document = JsonDocument.Parse(FormProjectCodec.Encode(project));
        return document.RootElement.Clone();
    }
}
