using System.Text.Json;
using Haven.Core.Forms;
using HavenOS.Forms;

namespace HavenOS.Forms.Tests;

public sealed class FormNativePublicationValidatorTests
{
    [Theory]
    [InlineData("audience")]
    [InlineData("data")]
    [InlineData("review")]
    [InlineData("graph")]
    [InlineData("renderer")]
    [InlineData("theme")]
    [InlineData("layout")]
    public void Draft_structure_is_editable_but_publication_requires_actual_configured_capability(string capability)
    {
        var now = DateTimeOffset.UtcNow;
        var project = FormProjectEditor.Create("Advanced draft", FormModeKind.Form, now);
        var field = new FormField(Guid.NewGuid(), FormFieldKind.ShortText, "Question", null,
            JsonSerializer.SerializeToElement(new { }), false, new());
        project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID, field, now);
        project = capability switch
        {
            "audience" => project with { AccessPolicy = new(FormRespondentAccess.Authenticated, []) },
            "data" => FormProjectEditor.BindData(project, project.Revision,
                new(Guid.NewGuid(), field.FieldID, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), FormDataBindingKind.AppendRecord), now),
            "review" => project with { MarkingScheme = new([], FormResultRelease.AfterReview) },
            "graph" => project with { ModeDefinition = new(Guid.NewGuid(), FormModeKind.Custom, Guid.NewGuid(), Guid.NewGuid()) },
            "renderer" => project with { Fields = [field with { Kind = FormFieldKind.Graph }] },
            "theme" => project with { Theme = new("custom") },
            "layout" => project with { Pages = [project.Pages[0] with { Layout = new(Columns: 2) }] },
            _ => throw new ArgumentOutOfRangeException(nameof(capability))
        };
        var validator = new FormNativePublicationValidator();
        var captured = FormProjectEditor.Project(project);
        validator.Validate(project.FormID, captured);
        Assert.Throws<NotSupportedException>(() => validator.ValidateForPublication(project.FormID, captured));
        Assert.Equal(project.FormID, captured.GetProperty("FormID").GetGuid());
    }
}
