using System.ComponentModel;
using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace CakeOS.Apps.Boards.App;

internal static class GraphSliderCui
{
    public static Control Build(BoardsViewModel viewModel, string blockId, RichGraphView graph,
        Action valuesChanged)
    {
        var context = new GraphSliderContext(viewModel, blockId, graph);
        var loader = new CuiControlLoader();
        loader.SetBindingContext(context);
        (Control? Root, IReadOnlyList<CuiDiagnostic> Diagnostics) loaded;
        try
        {
            loaded = loader.TryLoad(BoardsCuiDocuments.Load(BoardsCuiDocuments.GraphSlidersSourceName));
        }
        catch (Exception error)
        {
            loader.Dispose();
            return Unavailable(error.Message);
        }
        var (root, diagnostics) = loaded;
        if (root is null)
        {
            loader.Dispose();
            return Unavailable(diagnostics.FirstOrDefault()?.Message ?? "Graph slider controls are unavailable.");
        }

        loader.WireBindings(root);
        context.ValuesChanged += valuesChanged;
        root.DetachedFromVisualTree += (_, _) =>
        {
            context.ValuesChanged -= valuesChanged;
            loader.Dispose();
        };
        return root;
    }

    private static Control Unavailable(string message)
    {
        var unavailable = new TextBlock
        {
            Text = message,
            Foreground = BoardsTheme.ErrorBrush,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(unavailable, "Graph slider controls unavailable");
        return unavailable;
    }

    private sealed class GraphSliderContext : ICuiWritableBindingContext, ICuiRepeatItemBindingContext
    {
        private readonly BoardsViewModel _viewModel;
        private readonly string _blockId;
        private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

        public GraphSliderContext(BoardsViewModel viewModel, string blockId, RichGraphView graph)
        {
            _viewModel = viewModel;
            _blockId = blockId;
            var values = viewModel.GetGraphSliderValues(blockId);
            Rows = graph.Sliders.Select(slider => new GraphSliderRow(slider,
                values.GetValueOrDefault(slider.Name, slider.DefaultValue))).ToArray();
            _values["Rows"] = Rows;
        }

        public IReadOnlyList<GraphSliderRow> Rows { get; }
        public event Action? ValuesChanged;

        public bool TryGetValue(string path, out object? value)
        {
            var key = path[(path.LastIndexOf('.') + 1)..];
            return _values.TryGetValue(key, out value);
        }

        public bool TrySetValue(string path, object? value) => false;

        public bool TryGetItemValue(object item, string path, out object? value)
        {
            value = null;
            if (item is not GraphSliderRow row || !Rows.Any(actual => ReferenceEquals(actual, row))) return false;
            value = path switch
            {
                "VariableId" => row.VariableId,
                "Name" => row.Name,
                "Minimum" => row.Minimum,
                "Maximum" => row.Maximum,
                "Step" => row.Step,
                "Value" => row.Value,
                "ValueLabel" => row.ValueLabel,
                "AccessibleName" => row.AccessibleName,
                _ => null,
            };
            return value is not null;
        }

        public bool TrySetItemValue(object item, string path, object? value)
        {
            if (item is not GraphSliderRow row || !Rows.Any(actual => ReferenceEquals(actual, row)) ||
                path != "Value" || !TryDouble(value, out var number) || !double.IsFinite(number)) return false;
            var snapped = Math.Clamp(row.Minimum +
                Math.Round((Math.Clamp(number, row.Minimum, row.Maximum) - row.Minimum) / row.Step) * row.Step,
                row.Minimum, row.Maximum);
            _viewModel.SetGraphSliderValue(_blockId, row.VariableId, snapped);
            row.SetValue(snapped);
            ValuesChanged?.Invoke();
            return true;
        }

        private static bool TryDouble(object? value, out double number)
        {
            switch (value)
            {
                case double exact: number = exact; return true;
                case float single: number = single; return true;
                case int integer: number = integer; return true;
                case long longInteger: number = longInteger; return true;
                case string text: return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
                default: number = 0; return false;
            }
        }
    }

    private sealed class GraphSliderRow : INotifyPropertyChanged
    {
        private double _value;
        public GraphSliderRow(RichGraphSliderView definition, double value)
        {
            VariableId = definition.VariableId;
            Name = definition.Name;
            Minimum = definition.Minimum;
            Maximum = definition.Maximum;
            Step = definition.Step;
            _value = value;
        }

        public string VariableId { get; }
        public string Name { get; }
        public double Minimum { get; }
        public double Maximum { get; }
        public double Step { get; }
        public double Value => _value;
        public string ValueLabel => _value.ToString("0.##", CultureInfo.CurrentCulture);
        public string AccessibleName => "Graph variable " + Name;
        public event PropertyChangedEventHandler? PropertyChanged;
        public void SetValue(double value)
        {
            _value = value;
            PropertyChanged?.Invoke(this, new(nameof(Value)));
            PropertyChanged?.Invoke(this, new(nameof(ValueLabel)));
        }
    }
}
