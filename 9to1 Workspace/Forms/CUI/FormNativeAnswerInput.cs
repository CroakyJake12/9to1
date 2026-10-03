using System.Text.Json;
using Avalonia.Controls;
using Haven.Core.Forms;
using Haven.Application;

namespace HavenOS.Forms;

/// <summary>Shared owning input construction; validation and persistence belong to the response owner.</summary>
internal sealed class FormNativeAnswerInput(Control control, IReadOnlyList<Action> detach) : IDisposable
{
    public Control Control { get; } = control;
    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var action in detach) action();
        Control.IsEnabled = false;
    }
    public static FormNativeAnswerInput Create(FormField definition, JsonElement? answer, Action<JsonElement> changed)
        => Create(definition, answer, changed, null);

    public static FormNativeAnswerInput Create(FormField definition, JsonElement? answer, Action<JsonElement> changed,
        Func<Guid, Func<bool>, CancellationToken, Task<IFormDataReferenceLookupSession?>>? referenceLookup)
    {
        var detach = new List<Action>();
        Control input;
        if (definition.Kind == FormFieldKind.Ranking)
        {
            var ranking = new FormNativeRankingInput(definition, answer, value => changed(value));
            detach.Add(ranking.Dispose); input = ranking;
        }
        else if (definition.Kind == FormFieldKind.TableInput)
        {
            var table = new FormNativeTableInput(definition.Table!, answer, value => changed(value), referenceLookup);
            detach.Add(table.Dispose); input = table;
        }
        else if (definition.Kind is FormFieldKind.MultipleChoice or FormFieldKind.CheckboxSet)
        {
            var choices = new FormNativeChoiceInput(definition, answer, value => changed(value));
            detach.Add(choices.Dispose); input = choices;
        }
        else if (definition.Kind is FormFieldKind.SingleChoice or FormFieldKind.Dropdown)
        {
            var options = definition.Options!.ToArray();
            var choice = new ComboBox { ItemsSource = options.Select(option => option.Label).ToArray(),
                SelectedIndex = answer is { ValueKind: JsonValueKind.String } value && value.TryGetGuid(out var selected)
                    ? Array.FindIndex(options, option => option.OptionID == selected) : -1 };
            void Choose(object? sender, SelectionChangedEventArgs args)
            {
                if (choice.IsEnabled && choice.SelectedIndex >= 0 && choice.SelectedIndex < options.Length)
                    changed(JsonSerializer.SerializeToElement(options[choice.SelectedIndex].OptionID));
            }
            choice.SelectionChanged += Choose; detach.Add(() => choice.SelectionChanged -= Choose); input = choice;
        }
        else if (definition.Kind is FormFieldKind.Number or FormFieldKind.Decimal or FormFieldKind.Currency or FormFieldKind.Rating)
        {
            var number = new FormNativeNumberInput(answer is { ValueKind: JsonValueKind.Number } value ? value.GetDecimal() : null,
                candidate => changed(candidate), answer is { ValueKind: JsonValueKind.String } textDraft ? textDraft.GetString() : null);
            detach.Add(number.Dispose); input = number;
        }
        else
        {
            var text = new TextBox { Text = answer is { ValueKind: JsonValueKind.String } value ? value.GetString() : "",
                AcceptsReturn = definition.Kind == FormFieldKind.LongText,
                PlaceholderText = definition.Kind switch
                {
                    FormFieldKind.Date => "yyyy-MM-dd", FormFieldKind.Time => "HH:mm:ss",
                    FormFieldKind.DateTime => "yyyy-MM-ddTHH:mm:ss.fffffff+00:00", FormFieldKind.Duration => "d.hh:mm:ss", _ => null
                } };
            // TextChanged is queued by Avalonia. Observe the actual property synchronously so
            // Next/Submit cannot use a previous valid answer while a changed input awaits that event.
            void TextEdited(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
            {
                if (args.Property != TextBox.TextProperty || !text.IsEnabled) return;
                var current = text.Text ?? "";
                var emptyTypedValue = current.Length == 0 && !definition.Required && definition.Kind is
                    FormFieldKind.Email or FormFieldKind.Date or FormFieldKind.Time or FormFieldKind.DateTime or FormFieldKind.Duration;
                changed(emptyTypedValue ? JsonSerializer.SerializeToElement<object?>(null) : JsonSerializer.SerializeToElement(current));
            }
            text.PropertyChanged += TextEdited; detach.Add(() => text.PropertyChanged -= TextEdited); input = text;
        }
        return new(input, detach);
    }
}
