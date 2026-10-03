using System.Text.Json;
using Haven.Core.Mathematics;
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;

namespace Haven.Core.Forms;

public enum FormModeKind { Form, Test, Quiz, Custom }
public enum FormFieldKind
{
    ShortText, LongText, Number, Decimal, Currency, Email, Phone, Date, Time, DateTime, Duration,
    SingleChoice, MultipleChoice, Dropdown, CheckboxSet, Rating, Ranking, Matrix, FileUpload,
    Signature, PersonReference, DataForeignKey, Calculated, Hidden, Mathematical, Graph, RepeatingGroup, TableInput
}
public enum FormComponentKind { RichText, Image, Media, ReusableGroup }
public enum FormChildKind { Field, Component }
public enum FormResultRelease { Immediate, AfterQuestion, AfterSubmission, AfterReview, ConfiguredState }
public enum FormRespondentAccess { OwnerOnly, Authenticated, Invitation, Public }
public enum FormDataBindingKind { Lookup, AppendRecord, UpdateRecord, ParentChildRecords }

public sealed record FormModeDefinition(Guid ModeID, FormModeKind Kind, Guid? StateGraphID = null, Guid? StartNodeID = null,
    long? StateGraphRevision = null);
public sealed record FormVersionReference(Guid FormVersionID, long SourceRevision);
public sealed record FormChildReference(FormChildKind Kind, Guid ID);
public sealed record FormLayout(int Columns = 1, decimal Gap = 8, decimal? MinimumWidth = null, decimal? MaximumWidth = null);
public sealed record FormPage(Guid PageID, string Title, IReadOnlyList<FormChildReference> Children,
    FormLayout Layout, long Revision = 1, Guid? VisibilityNodeID = null);
public sealed record FormChoiceOption(Guid OptionID, string Label);
public sealed record FormFieldAssessment(decimal MaximumPoints, decimal Weight,
    IReadOnlyList<FormMarkingRule> Rules, FormResultRelease Release = FormResultRelease.AfterSubmission,
    MathNumericRule? Mathematics = null, GraphCoordinateMarkingRule? Graph = null, GraphDefinition? ExpectedGraph = null);
public sealed record FormField(Guid FieldID, FormFieldKind Kind, string Label, string? Help,
    JsonElement ResponseSchema, bool Required, FormLayout Layout, long Revision = 1,
    IReadOnlyList<FormChoiceOption>? Options = null, FormFieldAssessment? Assessment = null,
    FormTableInputDefinition? Table = null, IReadOnlyList<Guid>? RepeatedFieldIDs = null,
    Guid? ValidationNodeID = null, Guid? DataBindingID = null,
    MathExpression? Mathematics = null, GraphDefinition? Graph = null);
/// <summary>Content assets and reusable children retain canonical identity; configuration belongs to the registered CUI component schema.</summary>
public sealed record FormComponent(Guid ComponentID, FormComponentKind Kind, FormLayout Layout,
    IReadOnlyList<Guid> ChildFieldIDs, JsonElement Configuration, long Revision = 1, Guid? AssetID = null);
public sealed record FormThemeReference(string ThemeID, Guid? StyleAssetID = null);
public sealed record FormDataBinding(Guid BindingID, Guid FieldID, Guid WorkbookID, Guid TableID,
    Guid ColumnID, FormDataBindingKind Kind, Guid? ParentBindingID = null, Guid? SourceColumnID = null);
public sealed record FormGradeBand(Guid GradeID, string Label, decimal MinimumPercentage);
public sealed record FormMarkingScheme(IReadOnlyList<FormGradeBand> Grades, FormResultRelease Release);
public sealed record FormQuestionBank(Guid BankID, string Name, IReadOnlyList<Guid> FieldIDs);
public sealed record FormPublishingSettings(bool AcceptResponses, Guid? PublishedVersionID = null);
/// <summary>Declared publishing policy, not a grant. The host resolves current audience and ownership before access.</summary>
public sealed record FormAccessPolicy(FormRespondentAccess Respondents, IReadOnlyList<Guid> AudienceIDs);
public sealed record FormRuntimeSettings(int MaximumAttempts = 1, TimeSpan? TimeLimit = null,
    bool ShuffleQuestions = false, bool ShuffleChoices = false, bool AllowResume = true);

/// <summary>The same authored project is edited, previewed and published. Storage and resource grants remain host-owned.</summary>
public sealed record FormProject(int SchemaVersion, Guid FormID, string Title, FormModeDefinition ModeDefinition,
    IReadOnlyList<FormVersionReference> FormVersions, IReadOnlyList<FormPage> Pages,
    IReadOnlyList<FormField> Fields, IReadOnlyList<FormComponent> Components, FormThemeReference Theme,
    Guid? LogicGraphID, IReadOnlyList<FormDataBinding> DataBindings, FormMarkingScheme MarkingScheme,
    IReadOnlyList<FormQuestionBank> QuestionBanks, FormPublishingSettings PublishingSettings,
    FormAccessPolicy AccessPolicy, FormRuntimeSettings RuntimeSettings, DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt, long Revision)
{
    public const int CurrentSchemaVersion = 1;
    public long? LogicGraphRevision { get; init; }
}

/// <summary>Structural validation never claims registered renderer, graph, Data or audience availability.
/// The publication host must additionally validate those actual capabilities and resources.</summary>
public static class FormProjectCodec
{
    public const int MaximumBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public static byte[] Encode(FormProject project)
    {
        Validate(project);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(project, Options);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Form project exceeds the byte limit.");
        return bytes;
    }

    public static FormProject Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaximumBytes) throw new InvalidDataException("Form project exceeds the byte limit.");
        try
        {
            var project = JsonSerializer.Deserialize<FormProject>(bytes, Options)
                ?? throw new InvalidDataException("Form project is missing.");
            Validate(project);
            return project;
        }
        catch (JsonException exception) { throw new InvalidDataException("Unsupported or corrupt form project; preserve its original bytes.", exception); }
    }

    public static FormProject Capture(FormProject project) => Decode(Encode(project));

    public static void Validate(FormProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        void Require([DoesNotReturnIf(false)] bool condition, string code) { if (!condition) throw new InvalidDataException(code); }
        Require(project.SchemaVersion == FormProject.CurrentSchemaVersion && project.FormID != Guid.Empty
            && project.Revision > 0 && !string.IsNullOrWhiteSpace(project.Title) && project.Title.Length <= 4096
            && project.CreatedAt != default && project.ModifiedAt >= project.CreatedAt, "Invalid form identity, schema or revision.");
        Require(project.Pages is { Count: > 0 and <= 256 } && project.Fields is { Count: <= 4096 }
            && project.Components is { Count: <= 4096 } && project.DataBindings is { Count: <= 4096 }
            && project.QuestionBanks is { Count: <= 256 } && project.FormVersions is { Count: <= 10000 }, "Invalid form collection bounds.");
        var ids = new HashSet<Guid>();
        void Identity(Guid id) => Require(id != Guid.Empty && ids.Add(id), "Duplicate or empty authored object identity.");
        void Layout(FormLayout layout) => Require(layout is not null && layout.Columns is >= 1 and <= 64
            && layout.Gap is >= 0 and <= 4096 && (layout.MinimumWidth is null or >= 0)
            && (layout.MaximumWidth is null or > 0) && !(layout.MinimumWidth > layout.MaximumWidth), "Invalid form layout.");
        Identity(project.FormID);
        Require(project.ModeDefinition is not null && Enum.IsDefined(project.ModeDefinition.Kind), "Invalid form mode.");
        Identity(project.ModeDefinition.ModeID);
        Require(project.ModeDefinition.StateGraphID is null
            ? project.ModeDefinition.StateGraphRevision is null
            : project.ModeDefinition.StateGraphID != Guid.Empty && project.ModeDefinition.StateGraphRevision is null or > 0,
            "Invalid state graph revision reference.");
        Require(project.LogicGraphID is null ? project.LogicGraphRevision is null
            : project.LogicGraphID != Guid.Empty && project.LogicGraphRevision is null or > 0,
            "Invalid logic graph revision reference.");
        Require(project.ModeDefinition.Kind != FormModeKind.Custom ||
            project.ModeDefinition.StateGraphID is { } graph && graph != Guid.Empty && project.ModeDefinition.StartNodeID is { } start && start != Guid.Empty,
            "Custom mode requires the canonical state graph and start node.");
        foreach (var field in project.Fields)
        {
            Require(field is not null, "Missing field."); Identity(field.FieldID); Layout(field.Layout);
            Require(Enum.IsDefined(field.Kind) && field.Revision > 0 && !string.IsNullOrWhiteSpace(field.Label)
                && field.Label.Length <= 65536 && field.ResponseSchema.ValueKind == JsonValueKind.Object, "Invalid typed field.");
            foreach (var option in field.Options ?? []) { Require(option is not null && !string.IsNullOrWhiteSpace(option.Label), "Invalid choice."); Identity(option.OptionID); }
            if (field.Kind is FormFieldKind.SingleChoice or FormFieldKind.MultipleChoice or FormFieldKind.Dropdown or FormFieldKind.CheckboxSet or FormFieldKind.Ranking)
                Require(field.Options is { Count: > 0 and <= 4096 }, "Choice field requires stable options.");
            if (field.Kind == FormFieldKind.TableInput)
            {
                Require(field.Table is not null && field.Table.FieldID == field.FieldID, "Table input must use the canonical field identity.");
                FormTableInput.ValidateDefinition(field.Table!);
            }
            else Require(field.Table is null, "Table definition belongs only to table input fields.");
            if (field.Mathematics is { } expression)
            {
                Require(field.Kind == FormFieldKind.Mathematical, "Math expression belongs only to mathematical fields.");
                MathObjectCodec.Validate(expression);
            }
            if (field.Graph is { } fieldGraph)
            {
                Require(field.Kind == FormFieldKind.Graph, "Graph belongs only to graph fields.");
                MathObjectCodec.Validate(fieldGraph);
            }
            if (field.Assessment is { } marking)
            {
                Require(marking.Weight is > 0 and <= 1000 && marking.MaximumPoints >= 0 && marking.MaximumPoints <= decimal.MaxValue / 4096 / 1000 && Enum.IsDefined(marking.Release), "Invalid field marking.");
                _ = FormMarking.Evaluate(JsonSerializer.SerializeToElement<object?>(null), marking.Rules, marking.MaximumPoints);
                Require(!(marking.Mathematics is not null && marking.Graph is not null)
                    && (marking.Mathematics is null && marking.Graph is null || marking.Rules.Count == 0),
                    "Typed mathematical marking must have one authoritative policy.");
                if (marking.Mathematics is { } numericRule)
                {
                    Require(field.Kind == FormFieldKind.Mathematical && field.Mathematics is not null,
                        "Numeric mathematics marking requires its canonical question.");
                    _ = MathMarking.Evaluate(new(Guid.NewGuid(), 1, new NumericMathAnswer("0")), numericRule);
                }
                if (marking.Graph is { } graphRule)
                {
                    Require(field.Kind == FormFieldKind.Graph && field.Graph is not null && marking.ExpectedGraph is not null,
                        "Graph marking requires canonical question and private expected graph.");
                    MathObjectCodec.Validate(marking.ExpectedGraph!);
                    GraphMarking.ValidatePolicy(field.Graph!, marking.ExpectedGraph!, graphRule);
                }
                else Require(marking.ExpectedGraph is null, "Expected graph belongs only to graph marking.");
            }
        }
        var fieldIDs = project.Fields.Select(field => field.FieldID).ToHashSet();
        foreach (var component in project.Components)
        {
            Require(component is not null, "Missing component."); Identity(component.ComponentID); Layout(component.Layout);
            Require(Enum.IsDefined(component.Kind) && component.Revision > 0 && component.Configuration.ValueKind == JsonValueKind.Object
                && component.ChildFieldIDs is not null && component.ChildFieldIDs.Count <= 4096
                && component.ChildFieldIDs.Distinct().Count() == component.ChildFieldIDs.Count && component.ChildFieldIDs.All(fieldIDs.Contains), "Invalid reusable component.");
        }
        var componentIDs = project.Components.Select(component => component.ComponentID).ToHashSet();
        foreach (var page in project.Pages)
        {
            Require(page is not null, "Missing page."); Identity(page.PageID); Layout(page.Layout);
            Require(page.Revision > 0 && page.Children is { Count: <= 4096 } && page.Children.Distinct().Count() == page.Children.Count
                && page.Children.All(child => child is not null && (child.Kind == FormChildKind.Field ? fieldIDs.Contains(child.ID)
                    : child.Kind == FormChildKind.Component && componentIDs.Contains(child.ID))), "Invalid page children.");
        }
        foreach (var field in project.Fields)
            Require(field.RepeatedFieldIDs is null || field.Kind == FormFieldKind.RepeatingGroup && field.RepeatedFieldIDs.Count is > 0 and <= 4096
                && field.RepeatedFieldIDs.Distinct().Count() == field.RepeatedFieldIDs.Count && field.RepeatedFieldIDs.All(id => id != field.FieldID && fieldIDs.Contains(id)), "Invalid repeating group references.");
        void Acyclic(Guid id, IReadOnlyDictionary<Guid, Guid[]> edges, HashSet<Guid> active, HashSet<Guid> done, int depth)
        {
            Require(depth <= 64 && !active.Contains(id), "Cyclic or excessively deep form dependencies.");
            if (done.Contains(id)) return;
            active.Add(id);
            foreach (var child in edges[id]) Acyclic(child, edges, active, done, depth + 1);
            active.Remove(id); done.Add(id);
        }
        var repeatingEdges = project.Fields.ToDictionary(field => field.FieldID, field => field.RepeatedFieldIDs?.ToArray() ?? []);
        var repeatedDone = new HashSet<Guid>();
        foreach (var id in fieldIDs) Acyclic(id, repeatingEdges, [], repeatedDone, 0);
        foreach (var binding in project.DataBindings)
        {
            Require(binding is not null, "Missing binding."); Identity(binding.BindingID);
            Require(fieldIDs.Contains(binding.FieldID) && binding.WorkbookID != Guid.Empty && binding.TableID != Guid.Empty
                && binding.ColumnID != Guid.Empty && Enum.IsDefined(binding.Kind), "Invalid canonical Data binding.");
            Require(binding.SourceColumnID is null || project.Fields.Single(field => field.FieldID == binding.FieldID).Table?.Columns
                .Any(column => column.ColumnID == binding.SourceColumnID) == true, "Binding must reference the actual table input column.");
        }
        var bindingIDs = project.DataBindings.Select(binding => binding.BindingID).ToHashSet();
        foreach (var binding in project.DataBindings) Require(binding.ParentBindingID is null || binding.ParentBindingID != binding.BindingID && bindingIDs.Contains(binding.ParentBindingID.Value), "Invalid parent binding.");
        var bindingEdges = project.DataBindings.ToDictionary(binding => binding.BindingID,
            binding => binding.ParentBindingID is { } parent ? new[] { parent } : []);
        var bindingDone = new HashSet<Guid>();
        foreach (var id in bindingIDs) Acyclic(id, bindingEdges, [], bindingDone, 0);
        foreach (var field in project.Fields) Require(field.DataBindingID is null || project.DataBindings.Any(binding => binding.BindingID == field.DataBindingID && binding.FieldID == field.FieldID), "Field binding is not canonical.");
        foreach (var bank in project.QuestionBanks)
        {
            Require(bank is not null, "Missing question bank."); Identity(bank.BankID);
            Require(!string.IsNullOrWhiteSpace(bank.Name) && bank.FieldIDs is { Count: > 0 and <= 4096 }
                && bank.FieldIDs.Distinct().Count() == bank.FieldIDs.Count && bank.FieldIDs.All(fieldIDs.Contains), "Invalid question bank.");
        }
        Require(project.Theme is not null && !string.IsNullOrWhiteSpace(project.Theme.ThemeID)
            && project.PublishingSettings is not null && project.AccessPolicy is not null && Enum.IsDefined(project.AccessPolicy.Respondents)
            && project.AccessPolicy.AudienceIDs is { Count: <= 4096 } && project.AccessPolicy.AudienceIDs.All(id => id != Guid.Empty)
            && project.RuntimeSettings is not null && project.RuntimeSettings.MaximumAttempts is >= 1 and <= 10000
            && (project.RuntimeSettings.TimeLimit is null || project.RuntimeSettings.TimeLimit > TimeSpan.Zero && project.RuntimeSettings.TimeLimit <= TimeSpan.FromDays(30))
            && project.MarkingScheme is not null && Enum.IsDefined(project.MarkingScheme.Release)
            && project.MarkingScheme.Grades is { Count: <= 256 }, "Invalid theme, publishing, audience or runtime configuration.");
        foreach (var grade in project.MarkingScheme.Grades) { Require(grade is not null && !string.IsNullOrWhiteSpace(grade.Label) && grade.MinimumPercentage is >= 0 and <= 100, "Invalid grade."); Identity(grade.GradeID); }
        foreach (var version in project.FormVersions) { Require(version is not null && version.SourceRevision >= 1 && version.SourceRevision <= project.Revision, "Invalid version reference."); Identity(version.FormVersionID); }
        Require(project.PublishingSettings.PublishedVersionID is null || project.FormVersions.Any(version => version.FormVersionID == project.PublishingSettings.PublishedVersionID), "Published version reference is missing.");
    }
}
