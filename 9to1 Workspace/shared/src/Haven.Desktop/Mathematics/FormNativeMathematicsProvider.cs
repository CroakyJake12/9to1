using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Haven.Core.Forms;
using Haven.Core.Mathematics;
using HavenOS.Forms;

namespace Haven.Desktop.Mathematics;

/// <summary>The actual shared maintained native math/graph controls, consumed through the
/// existing Forms capability seam. This bounded implementation supports numeric mathematical
/// answers and a single editable graph point; other response tools remain explicit unavailable.</summary>
public sealed class FormNativeMathematicsProvider : IFormNativeMathematicsProvider
{
    public bool Supports(FormField field)
    {
        if (field.Kind == FormFieldKind.Mathematical && field.Mathematics is not null)
            return new CSharpMathSyntaxAdapter().Parse(field.Mathematics.LaTeX, new()).Succeeded;
        if (field.Kind != FormFieldKind.Graph || field.Graph is not { } graph
            || !graph.ResponseTools.SequenceEqual([GraphResponseTool.PlacePoint])) return false;
        try { ScottPlotGraphAdapter.RequireDisplayable(graph); return true; }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return false; }
    }
    public IFormNativeMathematicsInput Create(FormField field, JsonElement? answer, Action<JsonElement> changed)
    {
        if (!Supports(field)) throw new NotSupportedException("CapabilityUnavailable: mathematical response tool is not registered.");
        var cleanup = new List<Action>(); var panel = new StackPanel { Spacing = 8 }; var disposed = false;
        void Change(JsonElement value) { if (!disposed && panel.IsEffectivelyEnabled) changed(value); }
        try
        {
            if (field.Kind == FormFieldKind.Mathematical)
            {
                var question = new SharedMathEditorControl(field.Mathematics!) { IsEnabled = false };
                panel.Children.Add(question); cleanup.Add(question.Dispose);
                MathAnswer? initial = null;
                if (answer is { ValueKind: JsonValueKind.Object } numeric && FormMathematics.Validate(field, numeric) is null)
                    initial = MathObjectCodec.Decode<MathAnswer>(Encoding.UTF8.GetBytes(numeric.GetRawText()));
                string? literalDraft = null, unitsDraft = null;
                if (answer is { ValueKind: JsonValueKind.Object } draft && draft.TryGetProperty("mathDraft", out var retained)
                    && retained.ValueKind == JsonValueKind.Object)
                {
                    if (retained.TryGetProperty("literal", out var text) && text.ValueKind == JsonValueKind.String) literalDraft = text.GetString();
                    if (retained.TryGetProperty("units", out var units) && units.ValueKind == JsonValueKind.String) unitsDraft = units.GetString();
                    if (retained.TryGetProperty("lastValid", out var valid) && valid.ValueKind == JsonValueKind.Object
                        && FormMathematics.Validate(field, valid) is null)
                        initial = MathObjectCodec.Decode<MathAnswer>(Encoding.UTF8.GetBytes(valid.GetRawText()));
                }
                var input = new NumericMathAnswerEditorControl(initial, draftLiteral: literalDraft, draftUnits: unitsDraft, allowEmpty:!field.Required);
                void Edited(object? sender, MathAnswerDraftChangedEventArgs args) => Change(args.Answer is null
                    ? args.Diagnostic is null ? JsonSerializer.SerializeToElement<object?>(null)
                        : JsonSerializer.SerializeToElement(new { mathDraft = new { literal = input.DraftLiteral, units = input.DraftUnits,
                            lastValid = input.LastValid is { } valid ? Element(valid) : (JsonElement?)null } }) : Element(args.Answer));
                input.DraftChanged += Edited; cleanup.Add(() => input.DraftChanged -= Edited); cleanup.Add(input.Dispose);
                panel.Children.Add(input);
                if (answer is { } invalid && initial is null && invalid.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                    panel.Children.Add(new TextBlock { Text = "The retained draft is invalid. Enter a supported number to replace it." });
            }
            else
            {
                GraphResponse? initial = null;
                if (answer is { ValueKind: JsonValueKind.Object } graph && FormMathematics.Validate(field, graph) is null)
                    initial = MathObjectCodec.Decode<GraphResponse>(Encoding.UTF8.GetBytes(graph.GetRawText()));
                if (answer is { ValueKind: JsonValueKind.Object } priorDraft && priorDraft.TryGetProperty("graphDraft", out var prior)
                    && prior.ValueKind == JsonValueKind.Object && prior.TryGetProperty("lastValid", out var lastGraph)
                    && lastGraph.ValueKind == JsonValueKind.Object && FormMathematics.Validate(field, lastGraph) is null)
                    initial = MathObjectCodec.Decode<GraphResponse>(Encoding.UTF8.GetBytes(lastGraph.GetRawText()));
                var input = SharedGraphEditorControl.ForResponse(field.Graph!, initial);
                if (answer is { ValueKind: JsonValueKind.Object } draft && draft.TryGetProperty("graphDraft", out var retained)
                    && retained.ValueKind == JsonValueKind.Object)
                {
                    if (retained.TryGetProperty("x", out var x) && x.ValueKind == JsonValueKind.String) input.TrySetValue("X", x.GetString());
                    if (retained.TryGetProperty("y", out var y) && y.ValueKind == JsonValueKind.String) input.TrySetValue("Y", y.GetString());
                }
                void Edited(object? sender, EventArgs args) => Change(input.HasUncommittedCoordinates || input.LastResponse is null
                    ? JsonSerializer.SerializeToElement(new { graphDraft = new { x = input.CoordinateDraft.X, y = input.CoordinateDraft.Y,
                        lastValid = input.LastResponse is { } valid ? Element(valid) : (JsonElement?)null } }) : Element(input.LastResponse));
                input.ResponseDraftChanged += Edited; cleanup.Add(() => input.ResponseDraftChanged -= Edited); cleanup.Add(input.Dispose);
                panel.Children.Add(input);
                if (answer is { } invalid && initial is null && invalid.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                    panel.Children.Add(new TextBlock { Text = "The retained graph draft is invalid. Place a point to replace it." });
            }
            return new OwnedInput(panel, () => { disposed = true; foreach (var action in cleanup) action(); });
        }
        catch { disposed = true; foreach (var action in cleanup) action(); throw; }
    }
    public static JsonElement Element<T>(T value) where T : class
    {
        using var document = JsonDocument.Parse(MathObjectCodec.Encode(value));
        return document.RootElement.Clone();
    }
    private sealed class OwnedInput(Control control, Action dispose) : IFormNativeMathematicsInput
    {
        private bool _disposed;
        public Control Control { get; } = control;
        public void Dispose() { if (_disposed) return; _disposed = true; dispose(); Control.IsEnabled = false; }
    }
}
