using System.Text;
using System.Text.Json;
using Haven.Core.Forms;

namespace Haven.Application;

/// <summary>Typed authoring entrypoints over the existing durable, authority-checked publication store.
/// Publication revision is the optimistic transaction token; project revision remains authored content identity.</summary>
public sealed class FormAuthoringService(FormPublicationService publications, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    public Task<FormPublicationResult> CreateAsync(string title, FormModeKind mode, CancellationToken token = default)
    {
        var project = FormProjectEditor.Create(title, mode, _clock.GetUtcNow());
        return publications.CreateAsync(project.FormID, FormProjectEditor.Project(project), token);
    }
    public Task<FormPublicationResult> AddPageAsync(Guid formID, long publicationRevision, FormPage page, CancellationToken token = default)
    {
        var captured = JsonSerializer.Deserialize<FormPage>(JsonSerializer.Serialize(page))!;
        return EditAsync(formID, publicationRevision, project => FormProjectEditor.AddPage(project, project.Revision, captured, _clock.GetUtcNow()), token);
    }
    public Task<FormPublicationResult> AddFieldAsync(Guid formID, long publicationRevision, Guid pageID, FormField field, CancellationToken token = default)
    {
        var captured = JsonSerializer.Deserialize<FormField>(JsonSerializer.Serialize(field))!;
        return EditAsync(formID, publicationRevision, project => FormProjectEditor.AddField(project, project.Revision, pageID, captured, _clock.GetUtcNow()), token);
    }
    public Task<FormPublicationResult> UpdateFieldAsync(Guid formID, long publicationRevision, FormField field, CancellationToken token = default)
    {
        var captured = JsonSerializer.Deserialize<FormField>(JsonSerializer.Serialize(field))!;
        return EditAsync(formID, publicationRevision, project => FormProjectEditor.UpdateField(project, project.Revision, captured, _clock.GetUtcNow()), token);
    }
    public Task<FormPublicationResult> MoveFieldAsync(Guid formID, long publicationRevision, Guid fieldID, Guid pageID, int index, CancellationToken token = default) =>
        EditAsync(formID, publicationRevision, project => FormProjectEditor.MoveField(project, project.Revision, fieldID, pageID, index, _clock.GetUtcNow()), token);
    public Task<FormPublicationResult> SetModeAsync(Guid formID, long publicationRevision, FormModeDefinition mode, CancellationToken token = default) =>
        EditAsync(formID, publicationRevision, project => FormProjectEditor.SetMode(project, project.Revision, mode, _clock.GetUtcNow()), token);
    public Task<FormPublicationResult> SetThemeAsync(Guid formID, long publicationRevision, FormThemeReference theme, CancellationToken token = default) =>
        EditAsync(formID, publicationRevision, project => FormProjectEditor.SetTheme(project, project.Revision, theme, _clock.GetUtcNow()), token);
    public Task<FormPublicationResult> BindDataAsync(Guid formID, long publicationRevision, FormDataBinding binding, CancellationToken token = default) =>
        EditAsync(formID, publicationRevision, project => FormProjectEditor.BindData(project, project.Revision, binding, _clock.GetUtcNow()), token);

    public async Task<FormResponseRuntime> PreviewAsync(Guid formID, long publicationRevision, CancellationToken token = default)
    {
        var loaded = await publications.ReadAsync(formID, token).ConfigureAwait(false);
        if (!loaded.Success) throw new InvalidOperationException(loaded.Code);
        if (loaded.Publication!.Revision != publicationRevision) throw new InvalidOperationException("RevisionConflict");
        return new FormResponseRuntime(Decode(loaded.Publication.Draft), Guid.NewGuid(), _clock);
    }

    private async Task<FormPublicationResult> EditAsync(Guid formID, long revision, Func<FormProject, FormProject> edit, CancellationToken token)
    {
        var loaded = await publications.ReadAsync(formID, token).ConfigureAwait(false);
        if (!loaded.Success) return loaded;
        if (loaded.Publication!.Revision != revision) return new(false, "RevisionConflict", null);
        var updated = edit(Decode(loaded.Publication.Draft));
        return await publications.SaveDraftAsync(formID, revision, FormProjectEditor.Project(updated), token).ConfigureAwait(false);
    }
    internal static FormProject Decode(JsonElement value) => FormProjectCodec.Decode(Encoding.UTF8.GetBytes(value.GetRawText()));
}
