using System.Globalization;
using System.Text;

namespace Haven.Application;

public enum DataFormulaErrorCode { DivideByZero, Reference, Value, Name, NotAvailable, Cycle, Number, Parse }
public sealed record DataFormulaIssue(Guid SheetId, string SheetName, string CellAddress, DataFormulaErrorCode Code, string Message);
public sealed record DataFormulaCellAddress(Guid SheetId, string SheetName, int Row, int Column)
{
    public string A1 => DataFormulaReferenceUtility.ColumnName(Column) + (Row + 1).ToString(CultureInfo.InvariantCulture);
}
public sealed record DataFormulaRecalculationReport(int FormulaCells, int EvaluatedCells, int ChangedCells, IReadOnlyList<DataFormulaIssue> Issues);
public sealed record DataFormulaDependencyGraph(IReadOnlyDictionary<DataFormulaCellAddress, IReadOnlySet<DataFormulaCellAddress>> Dependencies);

internal sealed record DataFormulaValue(object? Scalar, IReadOnlyList<DataFormulaValue>? Items = null, int Rows = 1, int Columns = 1, DataFormulaErrorCode? ErrorCode = null, string ErrorMessage = "")
{
    public bool IsCellReference { get; init; }
    public bool IsLookupNotFound { get; init; }
    public bool IsError => ErrorCode is not null;
    public bool IsRange => Items is not null;
    public static DataFormulaValue Empty { get; } = new((object?)null);
    public static DataFormulaValue Number(double value) => double.IsFinite(value) ? new(value) : Error(DataFormulaErrorCode.Number, "The formula produced a non-finite number.");
    public static DataFormulaValue Text(string? value) => new(value ?? string.Empty);
    public static DataFormulaValue Boolean(bool value) => new(value);
    public static DataFormulaValue Date(DateTimeOffset value) => new(value);
    public static DataFormulaValue Range(IReadOnlyList<DataFormulaValue> values, int rows, int columns) => new(null, values, Math.Max(0, rows), Math.Max(0, columns));
    public static DataFormulaValue Error(DataFormulaErrorCode code, string message) => new(null, null, 1, 1, code, message);

    public string Display()
    {
        if (ErrorCode is { } code) return code switch
        {
            DataFormulaErrorCode.DivideByZero => "#DIV/0!", DataFormulaErrorCode.Reference => "#REF!", DataFormulaErrorCode.Value => "#VALUE!",
            DataFormulaErrorCode.Name => "#NAME?", DataFormulaErrorCode.NotAvailable => "#N/A", DataFormulaErrorCode.Cycle => "#CYCLE!",
            DataFormulaErrorCode.Number => "#NUM!", _ => "#ERROR!"
        };
        if (IsRange) return Items!.Count == 1 ? Items[0].Display() : "#VALUE!";
        return Scalar switch
        {
            null => string.Empty, bool value => value ? "TRUE" : "FALSE", double value => value.ToString("G15", CultureInfo.InvariantCulture),
            DateTimeOffset value => value.TimeOfDay == TimeSpan.Zero ? value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            _ => Convert.ToString(Scalar, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }
}

internal static class DataFormulaConversions
{
    public static DataFormulaValue First(DataFormulaValue value) => value.IsRange ? value.Items!.FirstOrDefault() ?? DataFormulaValue.Empty : value;

    public static bool TryNumber(DataFormulaValue source, out double value)
    {
        source = First(source); value = 0; if (source.IsError) return false;
        switch (source.Scalar)
        {
            case null: return true;
            case double number: value = number; return true;
            case bool boolean: value = boolean ? 1 : 0; return true;
            case DateTimeOffset date: value = date.UtcDateTime.ToOADate(); return true;
            case string text when string.IsNullOrWhiteSpace(text): return true;
            case string text when double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed): value = parsed; return true;
            default: return false;
        }
    }

    public static bool TryBoolean(DataFormulaValue source, out bool value)
    {
        source = First(source); value = false; if (source.IsError) return false;
        switch (source.Scalar)
        {
            case null: return true; case bool boolean: value = boolean; return true; case double number: value = Math.Abs(number) > double.Epsilon; return true;
            case string text when bool.TryParse(text, out var parsed): value = parsed; return true; case string text when string.IsNullOrWhiteSpace(text): return true; default: return false;
        }
    }

    public static string Text(DataFormulaValue source) => First(source).Display();
    public static IEnumerable<DataFormulaValue> Flatten(IEnumerable<DataFormulaValue> values) => values.SelectMany(value => value.IsRange ? value.Items! : [value]);
    public static DataFormulaValue? FirstError(IEnumerable<DataFormulaValue> values) => Flatten(values).FirstOrDefault(value => value.IsError);
}

internal static class DataFormulaFunctions
{
    private const int DefaultTwoDigitYearStart = 1950;

    private static DataFormulaValue AsRange(DataFormulaValue value)
    {
        if (value.IsError || value.IsRange || !value.IsCellReference) return value;
        // A single-cell reference is still a valid one-cell vector/table.
        return DataFormulaValue.Range([value with { IsCellReference = false }], 1, 1);
    }

    public static DataFormulaValue Evaluate(string name, IReadOnlyList<DataFormulaValue> arguments, DateTimeOffset now)
    {
        var upper = name.ToUpperInvariant();
        // These functions inspect error-bearing values or select one cell from
        // a range, so a blanket range-error precheck would return the wrong cell.
        if (upper != "IFERROR" && upper != "COUNT" && upper != "COUNTA" && upper != "COUNTBLANK" && upper != "ROWS" && upper != "COLUMNS" && upper != "ISBLANK" && upper != "ISERROR" && upper != "ISERR" && upper != "ISNA" && upper != "ISNUMBER" && upper != "ISTEXT" && upper != "ISLOGICAL" && upper != "ISNONTEXT" && upper != "INDEX" && upper != "XLOOKUP" && upper != "LOOKUP" && upper != "VLOOKUP" && upper != "HLOOKUP" && DataFormulaConversions.FirstError(arguments) is { } error) return error;
        return upper switch
        {
            "SUM" => Aggregate(arguments, values => values.Sum()),
            "AVERAGE" or "AVG" => Average(arguments),
            "MIN" => Aggregate(arguments, values => values.Count == 0 ? 0 : values.Min()),
            "MAX" => Aggregate(arguments, values => values.Count == 0 ? 0 : values.Max()),
            "PRODUCT" => Aggregate(arguments, values => values.Count == 0 ? 0 : values.Aggregate(1d, (product, value) => product * value)),
            "SUMSQ" => Aggregate(arguments, values => values.Sum(value => value * value)),
            "COUNT" => DataFormulaValue.Number(Numbers(arguments).Count),
            "COUNTA" => DataFormulaValue.Number(DataFormulaConversions.Flatten(arguments).Count(value => value.Scalar is not null || value.IsError)),
            "COUNTBLANK" => CountBlank(arguments),
            "MEDIAN" => Median(arguments),
            "STDEV.S" or "STDEV" => SampleStatistic(arguments, standardDeviation: true),
            "VAR.S" or "VAR" => SampleStatistic(arguments, standardDeviation: false),
            "ABS" => UnaryNumber(arguments, Math.Abs),
            "SQRT" => UnaryNumber(arguments, value => value < 0 ? double.NaN : Math.Sqrt(value)),
            "INT" => UnaryNumber(arguments, Math.Floor),
            "EXP" => UnaryNumber(arguments, Math.Exp),
            "LN" => UnaryNumber(arguments, value => value <= 0 ? double.NaN : Math.Log(value)),
            "LOG10" => UnaryNumber(arguments, value => value <= 0 ? double.NaN : Math.Log10(value)),
            "ROUND" => Round(arguments, MidpointRounding.AwayFromZero),
            "ROUNDUP" => RoundDirectional(arguments, up: true),
            "ROUNDDOWN" => RoundDirectional(arguments, up: false),
            "MOD" => Mod(arguments),
            "POWER" => Power(arguments),
            "PI" => arguments.Count == 0 ? DataFormulaValue.Number(Math.PI) : ValueError("PI takes no arguments."),
            "NA" => arguments.Count == 0 ? DataFormulaValue.Error(DataFormulaErrorCode.NotAvailable, "NA did not return an available value.") : ValueError("NA takes no arguments."),
            "AND" => Logical(arguments, and: true),
            "OR" => Logical(arguments, and: false),
            "NOT" => Not(arguments),
            "ISBLANK" => IsBlank(arguments),
            "ISERROR" => IsError(arguments, includeNotAvailable: true),
            "ISERR" => IsError(arguments, includeNotAvailable: false),
            "ISNA" => IsNotAvailable(arguments),
            "ISNUMBER" => IsNumber(arguments),
            "ISTEXT" => IsText(arguments),
            "ISLOGICAL" => IsLogical(arguments),
            "ISNONTEXT" => IsNonText(arguments),
            "LEN" => arguments.Count == 1 ? DataFormulaValue.Number(UnicodeCodePoints(DataFormulaConversions.Text(arguments[0])).Length) : ValueError("LEN expects one argument."),
            "LEFT" => LeftRight(arguments, left: true),
            "RIGHT" => LeftRight(arguments, left: false),
            "MID" => Mid(arguments),
            "LOWER" => arguments.Count == 1 ? DataFormulaValue.Text(DataFormulaConversions.Text(arguments[0]).ToLowerInvariant()) : ValueError("LOWER expects one argument."),
            "UPPER" => arguments.Count == 1 ? DataFormulaValue.Text(DataFormulaConversions.Text(arguments[0]).ToUpperInvariant()) : ValueError("UPPER expects one argument."),
            "TRIM" => arguments.Count == 1 ? DataFormulaValue.Text(string.Join(' ', DataFormulaConversions.Text(arguments[0]).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))) : ValueError("TRIM expects one argument."),
            "CONCAT" or "CONCATENATE" => DataFormulaValue.Text(string.Concat(DataFormulaConversions.Flatten(arguments).Select(DataFormulaConversions.Text))),
            "REPT" => Repeat(arguments),
            "REPLACE" => Replace(arguments),
            "SUBSTITUTE" => Substitute(arguments),
            "DATE" => Date(arguments),
            "TODAY" => arguments.Count == 0 ? DataFormulaValue.Date(new DateTimeOffset(now.Date, now.Offset)) : ValueError("TODAY takes no arguments."),
            "NOW" => arguments.Count == 0 ? DataFormulaValue.Date(now) : ValueError("NOW takes no arguments."),
            "YEAR" => DatePart(arguments, value => value.Year),
            "MONTH" => DatePart(arguments, value => value.Month),
            "DAY" => DatePart(arguments, value => value.Day),
            "ROWS" => RangeDimension(arguments, rows: true),
            "COLUMNS" => RangeDimension(arguments, rows: false),
            "MATCH" => Match(arguments),
            "LOOKUP" => Lookup(arguments),
            "INDEX" => Index(arguments),
            "XLOOKUP" => XLookup(arguments),
            "VLOOKUP" => VLookup(arguments),
            "HLOOKUP" => HLookup(arguments),
            _ => DataFormulaValue.Error(DataFormulaErrorCode.Name, $"Unknown function '{name}'.")
        };
    }

    private static List<double> Numbers(IEnumerable<DataFormulaValue> arguments)
    {
        var result = new List<double>();
        foreach (var argument in arguments)
        {
            if (argument.IsRange)
            {
                foreach (var item in argument.Items!)
                {
                    if (item.Scalar is double number) result.Add(number);
                    else if (item.Scalar is DateTimeOffset date) result.Add(date.UtcDateTime.ToOADate());
                }
                continue;
            }
            if (argument.IsCellReference)
            {
                if (argument.Scalar is double number) result.Add(number);
                else if (argument.Scalar is DateTimeOffset date) result.Add(date.UtcDateTime.ToOADate());
                continue;
            }
            if (DataFormulaConversions.TryNumber(argument, out var value) && argument.Scalar is not null) result.Add(value);
        }
        return result;
    }
    private static DataFormulaValue Average(IReadOnlyList<DataFormulaValue> args)
    {
        var values = Numbers(args);
        if (values.Count == 0) return DataFormulaValue.Error(DataFormulaErrorCode.DivideByZero, "AVERAGE requires at least one numeric value.");
        return DataFormulaValue.Number(values.Average());
    }
    private static DataFormulaValue CountBlank(IReadOnlyList<DataFormulaValue> args)
    {
        if (args.Count != 1) return ValueError("COUNTBLANK expects one range or value.");
        var count = DataFormulaConversions.Flatten(args).Count(value => !value.IsError && (value.Scalar is null || value.Scalar is string text && text.Length == 0));
        return DataFormulaValue.Number(count);
    }
    private static DataFormulaValue Aggregate(IReadOnlyList<DataFormulaValue> args, Func<List<double>, double> operation) { var values = Numbers(args); var result = operation(values); return double.IsFinite(result) ? DataFormulaValue.Number(result) : DataFormulaValue.Error(DataFormulaErrorCode.Number, "The aggregate produced an invalid number."); }
    private static DataFormulaValue UnaryNumber(IReadOnlyList<DataFormulaValue> args, Func<double, double> operation) { if (args.Count != 1 || !DataFormulaConversions.TryNumber(args[0], out var value)) return ValueError("This function expects one numeric argument."); var result = operation(value); return double.IsFinite(result) ? DataFormulaValue.Number(result) : DataFormulaValue.Error(DataFormulaErrorCode.Number, "The numeric result is outside the supported domain."); }
    private static DataFormulaValue Median(IReadOnlyList<DataFormulaValue> args) { var values = Numbers(args); if (values.Count == 0) return DataFormulaValue.Error(DataFormulaErrorCode.Value, "MEDIAN requires at least one numeric value."); values.Sort(); var middle = values.Count / 2; return DataFormulaValue.Number(values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2); }
    private static DataFormulaValue SampleStatistic(IReadOnlyList<DataFormulaValue> args, bool standardDeviation) { var values = Numbers(args); if (values.Count < 2) return DataFormulaValue.Error(DataFormulaErrorCode.DivideByZero, "Sample statistics require at least two numeric values."); var average = values.Average(); var variance = values.Sum(value => Math.Pow(value - average, 2)) / (values.Count - 1); return DataFormulaValue.Number(standardDeviation ? Math.Sqrt(variance) : variance); }
    private static DataFormulaValue Round(IReadOnlyList<DataFormulaValue> args, MidpointRounding mode) { if (args.Count is < 1 or > 2 || !DataFormulaConversions.TryNumber(args[0], out var value) || (args.Count == 2 && !DataFormulaConversions.TryNumber(args[1], out _))) return ValueError("ROUND expects a number and optional digits."); var digits = args.Count == 2 && DataFormulaConversions.TryNumber(args[1], out var d) ? (int)Math.Clamp(Math.Truncate(d), -15d, 15d) : 0; if (digits >= 0) return DataFormulaValue.Number(Math.Round(value, digits, mode)); var scale = Math.Pow(10, -digits); return DataFormulaValue.Number(Math.Round(value / scale, 0, mode) * scale); }
    private static DataFormulaValue RoundDirectional(IReadOnlyList<DataFormulaValue> args, bool up)
    {
        if (args.Count is < 1 or > 2 || !DataFormulaConversions.TryNumber(args[0], out var value)) return ValueError("ROUNDUP/ROUNDDOWN expect a number and optional digits.");
        var digits = 0;
        if (args.Count == 2)
        {
            if (!DataFormulaConversions.TryNumber(args[1], out var requestedDigits)) return ValueError("ROUNDUP/ROUNDDOWN expect numeric digits.");
            digits = (int)Math.Clamp(Math.Truncate(requestedDigits), -15d, 15d);
        }
        var scale = Math.Pow(10, digits); var scaled = value * scale; var rounded = up ? (value >= 0 ? Math.Ceiling(scaled) : Math.Floor(scaled)) : (value >= 0 ? Math.Floor(scaled) : Math.Ceiling(scaled)); return DataFormulaValue.Number(rounded / scale);
    }
    private static DataFormulaValue Mod(IReadOnlyList<DataFormulaValue> args) { if (args.Count != 2 || !DataFormulaConversions.TryNumber(args[0], out var left) || !DataFormulaConversions.TryNumber(args[1], out var right)) return ValueError("MOD expects two numbers."); if (Math.Abs(right) < double.Epsilon) return DataFormulaValue.Error(DataFormulaErrorCode.DivideByZero, "MOD divisor cannot be zero."); return DataFormulaValue.Number(left - right * Math.Floor(left / right)); }
    private static DataFormulaValue Power(IReadOnlyList<DataFormulaValue> args) { if (args.Count != 2 || !DataFormulaConversions.TryNumber(args[0], out var left) || !DataFormulaConversions.TryNumber(args[1], out var right)) return ValueError("POWER expects two numbers."); return DataFormulaValue.Number(Math.Pow(left, right)); }
    private static DataFormulaValue Logical(IReadOnlyList<DataFormulaValue> args, bool and) { if (args.Count == 0) return ValueError("Logical functions require arguments."); var values = new List<bool>(); foreach (var item in DataFormulaConversions.Flatten(args)) { if (!DataFormulaConversions.TryBoolean(item, out var value)) return ValueError("Logical arguments must be boolean-compatible."); values.Add(value); } return DataFormulaValue.Boolean(and ? values.All(value => value) : values.Any(value => value)); }
    private static DataFormulaValue Not(IReadOnlyList<DataFormulaValue> args) => args.Count == 1 && DataFormulaConversions.TryBoolean(args[0], out var value) ? DataFormulaValue.Boolean(!value) : ValueError("NOT expects one boolean-compatible argument.");
    private static DataFormulaValue IsError(IReadOnlyList<DataFormulaValue> args, bool includeNotAvailable)
    {
        if (args.Count != 1) return ValueError("ISERROR/ISERR expects one argument.");
        var value = DataFormulaConversions.First(args[0]);
        return DataFormulaValue.Boolean(value.IsError && (includeNotAvailable || value.ErrorCode != DataFormulaErrorCode.NotAvailable));
    }
    private static DataFormulaValue IsBlank(IReadOnlyList<DataFormulaValue> args)
    {
        if (args.Count != 1) return ValueError("ISBLANK expects one argument.");
        var value = DataFormulaConversions.First(args[0]);
        return DataFormulaValue.Boolean(!value.IsError && value.Scalar is null);
    }
    private static DataFormulaValue IsNotAvailable(IReadOnlyList<DataFormulaValue> args)
    {
        if (args.Count != 1) return ValueError("ISNA expects one argument.");
        return DataFormulaValue.Boolean(DataFormulaConversions.First(args[0]).ErrorCode == DataFormulaErrorCode.NotAvailable);
    }
    private static DataFormulaValue IsNumber(IReadOnlyList<DataFormulaValue> args)
    {
        if (args.Count != 1) return ValueError("ISNUMBER expects one argument.");
        var value = DataFormulaConversions.First(args[0]);
        return DataFormulaValue.Boolean(value.Scalar is double || value.Scalar is DateTimeOffset);
    }
    private static DataFormulaValue IsText(IReadOnlyList<DataFormulaValue> args)
    {
        if (args.Count != 1) return ValueError("ISTEXT expects one argument.");
        return DataFormulaValue.Boolean(DataFormulaConversions.First(args[0]).Scalar is string);
    }
    private static DataFormulaValue IsLogical(IReadOnlyList<DataFormulaValue> args)
    {
        if (args.Count != 1) return ValueError("ISLOGICAL expects one argument.");
        return DataFormulaValue.Boolean(DataFormulaConversions.First(args[0]).Scalar is bool);
    }
    private static DataFormulaValue IsNonText(IReadOnlyList<DataFormulaValue> args)
    {
        if (args.Count != 1) return ValueError("ISNONTEXT expects one argument.");
        return DataFormulaValue.Boolean(DataFormulaConversions.First(args[0]).Scalar is not string);
    }
    private static DataFormulaValue LeftRight(IReadOnlyList<DataFormulaValue> args, bool left)
    {
        if (args.Count is < 1 or > 2) return ValueError("LEFT/RIGHT expect text and optional character count.");
        var codePoints = UnicodeCodePoints(DataFormulaConversions.Text(args[0])); var requestedCount = 1d;
        if (args.Count == 2 && !DataFormulaConversions.TryNumber(args[1], out requestedCount)) return ValueError("LEFT/RIGHT character count must be numeric.");
        if (requestedCount < 0) return ValueError("Character count cannot be negative.");
        var count = (int)Math.Min(Math.Truncate(requestedCount), codePoints.Length);
        return DataFormulaValue.Text(left ? string.Concat(codePoints.Take(count)) : string.Concat(codePoints.Skip(codePoints.Length - count)));
    }

    private static string[] UnicodeCodePoints(string text)
    {
        var result = new List<string>(text.Length);
        foreach (var rune in text.EnumerateRunes()) result.Add(rune.ToString());
        return result.ToArray();
    }
    private static DataFormulaValue Repeat(IReadOnlyList<DataFormulaValue> args)
    {
        if (args.Count != 2 || !DataFormulaConversions.TryNumber(args[1], out var requestedCount)) return ValueError("REPT expects text and a numeric repetition count.");
        var text = DataFormulaConversions.Text(args[0]); var count = Math.Truncate(requestedCount);
        if (count < 0) return ValueError("REPT repetition count cannot be negative.");
        if (text.Length == 0 || count == 0) return DataFormulaValue.Text(string.Empty);
        if (count > 32767d / text.Length) return ValueError("REPT result exceeds the supported cell text limit.");
        return DataFormulaValue.Text(string.Concat(Enumerable.Repeat(text, (int)count)));
    }
    private static DataFormulaValue Mid(IReadOnlyList<DataFormulaValue> args)
    {
        if (args.Count != 3 || !DataFormulaConversions.TryNumber(args[1], out var start) || !DataFormulaConversions.TryNumber(args[2], out var length)) return ValueError("MID expects text, start and length.");
        if (start < 1 || length < 0) return ValueError("MID start must be at least 1 and character count cannot be negative.");
        var codePoints = UnicodeCodePoints(DataFormulaConversions.Text(args[0])); var truncatedStart = Math.Truncate(start);
        if (truncatedStart > codePoints.Length) return DataFormulaValue.Text(string.Empty);
        var offset = (int)truncatedStart - 1; var count = (int)Math.Min(Math.Truncate(length), codePoints.Length - offset);
        return DataFormulaValue.Text(string.Concat(codePoints.Skip(offset).Take(count)));
    }
    private static DataFormulaValue Substitute(IReadOnlyList<DataFormulaValue> args)
    {
        if (args.Count is < 3 or > 4) return ValueError("SUBSTITUTE expects text, old text, new text and optional instance number.");
        var text = DataFormulaConversions.Text(args[0]); var oldText = DataFormulaConversions.Text(args[1]); var newText = DataFormulaConversions.Text(args[2]);
        if (args.Count == 3) return DataFormulaValue.Text(text.Replace(oldText, newText, StringComparison.Ordinal));
        if (!DataFormulaConversions.TryNumber(args[3], out var instanceValue)) return ValueError("SUBSTITUTE instance number must be numeric.");
        var instance = Math.Truncate(instanceValue);
        if (instance < 1) return ValueError("SUBSTITUTE instance number must be at least one.");
        if (oldText.Length == 0 || instance > int.MaxValue) return DataFormulaValue.Text(text);
        var index = -1; var searchFrom = 0;
        for (var occurrence = 0; occurrence < (int)instance; occurrence++)
        {
            index = text.IndexOf(oldText, searchFrom, StringComparison.Ordinal);
            if (index < 0) return DataFormulaValue.Text(text);
            searchFrom = index + oldText.Length;
        }
        return DataFormulaValue.Text(text[..index] + newText + text[(index + oldText.Length)..]);
    }
    private static DataFormulaValue Replace(IReadOnlyList<DataFormulaValue> args)
    {
        if (args.Count != 4 || !DataFormulaConversions.TryNumber(args[1], out var start) || !DataFormulaConversions.TryNumber(args[2], out var length)) return ValueError("REPLACE expects text, numeric start, character count and replacement text.");
        if (start < 1 || length < 0) return ValueError("REPLACE start must be at least 1 and character count cannot be negative.");
        var codePoints = UnicodeCodePoints(DataFormulaConversions.Text(args[0])); var replacement = DataFormulaConversions.Text(args[3]); var truncatedStart = Math.Truncate(start);
        var offset = truncatedStart > codePoints.Length ? codePoints.Length : (int)truncatedStart - 1;
        var count = (int)Math.Min(Math.Truncate(length), codePoints.Length - offset);
        return DataFormulaValue.Text(string.Concat(codePoints.Take(offset)) + replacement + string.Concat(codePoints.Skip(offset + count)));
    }
    private static DataFormulaValue Date(IReadOnlyList<DataFormulaValue> args)
    {
        if (args.Count != 3 || !DataFormulaConversions.TryNumber(args[0], out var year) || !DataFormulaConversions.TryNumber(args[1], out var month) || !DataFormulaConversions.TryNumber(args[2], out var day)) return ValueError("DATE expects year, month and day.");
        var normalizedYear = Math.Truncate(year); var normalizedMonth = Math.Truncate(month); var normalizedDay = Math.Truncate(day);
        if (normalizedYear < 0 || normalizedYear > 9999 || normalizedMonth < int.MinValue + 1d || normalizedMonth > int.MaxValue || !double.IsFinite(normalizedDay)) return ValueError("DATE arguments are outside the supported calendar.");
        var yearNumber = (int)normalizedYear;
        if (yearNumber < 100)
        {
            // Match the pinned Calc default; its configurable pivot is not
            // exposed by this scoped formula runtime.
            var century = DefaultTwoDigitYearStart / 100;
            yearNumber += yearNumber < DefaultTwoDigitYearStart % 100 ? (century + 1) * 100 : century * 100;
        }
        try
        {
            var date = new DateTime(yearNumber, 1, 1).AddMonths((int)normalizedMonth - 1).AddDays(normalizedDay - 1);
            return DataFormulaValue.Date(new DateTimeOffset(date, TimeSpan.Zero));
        }
        catch (ArgumentOutOfRangeException) { return ValueError("DATE arguments are outside the supported calendar."); }
        catch (OverflowException) { return ValueError("DATE arguments are outside the supported calendar."); }
    }
    private static DataFormulaValue DatePart(IReadOnlyList<DataFormulaValue> args, Func<DateTimeOffset, int> selector) { if (args.Count != 1) return ValueError("Date-part functions expect one value."); var value = DataFormulaConversions.First(args[0]); if (value.Scalar is DateTimeOffset date) return DataFormulaValue.Number(selector(date)); if (value.Scalar is string text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date)) return DataFormulaValue.Number(selector(date)); if (DataFormulaConversions.TryNumber(value, out var serial)) { try { return DataFormulaValue.Number(selector(new DateTimeOffset(DateTime.FromOADate(serial), TimeSpan.Zero))); } catch (ArgumentException) { } } return ValueError("The value is not a recognised date."); }
    private static DataFormulaValue RangeDimension(IReadOnlyList<DataFormulaValue> args, bool rows)
    {
        if (args.Count != 1) return ValueError("ROWS/COLUMNS expects one range or value.");
        if (args[0].IsError) return args[0];
        return DataFormulaValue.Number(rows ? args[0].Rows : args[0].Columns);
    }
    private static DataFormulaValue Match(IReadOnlyList<DataFormulaValue> args)
    {
        if (args.Count is < 2 or > 3) return ValueError("MATCH expects a lookup value and one-dimensional range.");
        if (args[1].IsError) return args[1];
        var range = AsRange(args[1]);
        if (!range.IsRange || (range.Rows != 1 && range.Columns != 1)) return ValueError("MATCH expects a lookup value and one-dimensional range.");
        var matchType = 1;
        if (args.Count == 3)
        {
            if (!DataFormulaConversions.TryNumber(args[2], out var requestedType)) return ValueError("MATCH match_type must be -1, 0 or 1.");
            matchType = (int)requestedType;
            if (matchType is not (-1 or 0 or 1)) return ValueError("MATCH match_type must be -1, 0 or 1.");
        }
        var items = range.Items!; var candidate = -1;
        for (var index = 0; index < items.Count; index++)
        {
            if (!TryCompareLookupValues(items[index], args[0], out var comparison)) continue;
            if (comparison == 0) return DataFormulaValue.Number(index + 1);
            if (matchType == 1 && comparison < 0 || matchType == -1 && comparison > 0) candidate = index;
        }
        if (matchType != 0 && candidate >= 0) return DataFormulaValue.Number(candidate + 1);
        return DataFormulaValue.Error(DataFormulaErrorCode.NotAvailable, "MATCH did not find the lookup value or a permitted approximate match.");
    }

    private static bool TryCompareLookupValues(DataFormulaValue left, DataFormulaValue right, out int comparison)
    {
        left = DataFormulaConversions.First(left); right = DataFormulaConversions.First(right); comparison = 0;
        if (left.IsError || right.IsError) return false;
        // Preserve Calc's distinction between text keys and numeric keys; do not
        // coerce a numeric-looking text cell into a number during lookup.
        if (left.Scalar is string || right.Scalar is string)
        {
            if (left.Scalar is string leftText && right.Scalar is string rightText)
            {
                comparison = StringComparer.OrdinalIgnoreCase.Compare(leftText, rightText);
                return true;
            }
            return false;
        }
        if (!DataFormulaConversions.TryNumber(left, out var leftNumber) || !DataFormulaConversions.TryNumber(right, out var rightNumber)) return false;
        comparison = leftNumber.CompareTo(rightNumber);
        return true;
    }
    private static DataFormulaValue Lookup(IReadOnlyList<DataFormulaValue> args)
    {
        if (args.Count is < 2 or > 3) return ValueError("LOOKUP expects a value and one-dimensional lookup vector.");
        if (args[0].IsError) return args[0];
        if (args[1].IsError) return args[1];
        if (args.Count == 3 && args[2].IsError) return args[2];
        var lookupRange = AsRange(args[1]); var resultRange = args.Count == 3 ? AsRange(args[2]) : lookupRange;
        if (!lookupRange.IsRange || (lookupRange.Rows != 1 && lookupRange.Columns != 1)) return ValueError("LOOKUP expects a value and one-dimensional lookup vector.");
        if (args.Count == 3 && (!resultRange.IsRange || (resultRange.Rows != 1 && resultRange.Columns != 1) || resultRange.Items!.Count != lookupRange.Items!.Count)) return ValueError("LOOKUP result vector must be one-dimensional and match the lookup vector size.");
        var lookup = lookupRange.Items!; var result = resultRange.Items!; var candidate = -1;
        for (var index = 0; index < lookup.Count; index++)
        {
            if (lookup[index].IsError) return lookup[index];
            if (!TryCompareLookupValues(lookup[index], args[0], out var comparison)) continue;
            if (comparison <= 0) candidate = index;
            else break;
        }
        return candidate >= 0 ? result[candidate] : DataFormulaValue.Error(DataFormulaErrorCode.NotAvailable, "LOOKUP found no value less than or equal to the lookup value.");
    }
    private static DataFormulaValue Index(IReadOnlyList<DataFormulaValue> args)
    {
        if (args.Count is < 2 or > 3) return ValueError("INDEX expects a range and numeric row and optional column numbers.");
        if (args[0].IsError) return args[0];
        if (args[1].IsError) return args[1];
        if (args.Count == 3 && args[2].IsError) return args[2];
        var range = AsRange(args[0]);
        if (!range.IsRange || !DataFormulaConversions.TryNumber(args[1], out var rowValue)) return ValueError("INDEX expects a range and numeric row and optional column numbers.");
        var columnValue = 1d;
        if (args.Count == 3 && !DataFormulaConversions.TryNumber(args[2], out columnValue)) return ValueError("INDEX expects a range and numeric row and optional column numbers.");
        var row = (int)rowValue - 1; var column = (int)columnValue - 1;
        if (row < 0 || column < 0 || row >= range.Rows || column >= range.Columns) return DataFormulaValue.Error(DataFormulaErrorCode.Reference, "INDEX points outside the supplied range.");
        return range.Items![row * range.Columns + column];
    }
    private static DataFormulaValue XLookup(IReadOnlyList<DataFormulaValue> args)
    {
        if (args.Count is < 3 or > 4) return ValueError("XLOOKUP expects lookup value, lookup range and return range.");
        if (args[0].IsError) return WithoutLookupMissMarker(args[0]);
        if (args[1].IsError) return WithoutLookupMissMarker(args[1]);
        if (args[2].IsError) return WithoutLookupMissMarker(args[2]);
        var lookupRange = AsRange(args[1]); var returnRange = AsRange(args[2]);
        if (!lookupRange.IsRange || !returnRange.IsRange) return ValueError("XLOOKUP expects lookup value, lookup range and return range.");
        var lookup = lookupRange.Items!; var returns = returnRange.Items!;
        if ((lookupRange.Rows != 1 && lookupRange.Columns != 1) || (returnRange.Rows != 1 && returnRange.Columns != 1)) return ValueError("XLOOKUP requires one-dimensional lookup and return ranges in the scalar formula engine.");
        if (lookup.Count != returns.Count) return ValueError("XLOOKUP ranges must contain the same number of cells.");
        for (var i = 0; i < lookup.Count; i++)
        {
            if (lookup[i].IsError) return WithoutLookupMissMarker(lookup[i]);
            if (TryCompareLookupValues(lookup[i], args[0], out var comparison) && comparison == 0) return WithoutLookupMissMarker(returns[i]);
        }
        if (args.Count == 4) return WithoutLookupMissMarker(DataFormulaConversions.First(args[3]));
        return DataFormulaValue.Error(DataFormulaErrorCode.NotAvailable, "XLOOKUP did not find the lookup value.") with { IsLookupNotFound = true };
    }

    private static DataFormulaValue WithoutLookupMissMarker(DataFormulaValue value) => value.IsLookupNotFound ? value with { IsLookupNotFound = false } : value;
    private static DataFormulaValue VLookup(IReadOnlyList<DataFormulaValue> args)
    {
        if (args.Count is < 3 or > 4) return ValueError("VLOOKUP expects lookup value, table range and column index.");
        if (args[0].IsError) return args[0];
        if (args[1].IsError) return args[1];
        if (args[2].IsError) return args[2];
        if (args.Count == 4 && args[3].IsError) return args[3];
        var table = AsRange(args[1]);
        if (!table.IsRange || !DataFormulaConversions.TryNumber(args[2], out var columnValue)) return ValueError("VLOOKUP expects lookup value, table range and column index.");
        var approximate = true;
        if (args.Count == 4 && !DataFormulaConversions.TryBoolean(args[3], out approximate)) return ValueError("VLOOKUP range_lookup must be boolean-compatible.");
        var column = (int)columnValue - 1;
        if (column < 0 || column >= table.Columns) return DataFormulaValue.Error(DataFormulaErrorCode.Reference, "VLOOKUP column index is outside the table.");
        var candidate = -1;
        for (var row = 0; row < table.Rows; row++)
        {
            var key = table.Items![row * table.Columns]; if (key.IsError) return key;
            if (!TryCompareLookupValues(key, args[0], out var comparison)) continue;
            if (comparison == 0) return table.Items[row * table.Columns + column];
            if (approximate && comparison < 0) candidate = row;
        }
        if (approximate && candidate >= 0) return table.Items![candidate * table.Columns + column];
        return DataFormulaValue.Error(DataFormulaErrorCode.NotAvailable, "VLOOKUP did not find the lookup value or an eligible approximate match.");
    }
    private static DataFormulaValue HLookup(IReadOnlyList<DataFormulaValue> args)
    {
        if (args.Count is < 3 or > 4) return ValueError("HLOOKUP expects lookup value, table range and row index.");
        if (args[0].IsError) return args[0];
        if (args[1].IsError) return args[1];
        if (args[2].IsError) return args[2];
        if (args.Count == 4 && args[3].IsError) return args[3];
        var table = AsRange(args[1]);
        if (!table.IsRange || !DataFormulaConversions.TryNumber(args[2], out var rowValue)) return ValueError("HLOOKUP expects lookup value, table range and row index.");
        var approximate = true;
        if (args.Count == 4 && !DataFormulaConversions.TryBoolean(args[3], out approximate)) return ValueError("HLOOKUP range_lookup must be boolean-compatible.");
        var row = (int)rowValue - 1;
        if (row < 0 || row >= table.Rows) return DataFormulaValue.Error(DataFormulaErrorCode.Reference, "HLOOKUP row index is outside the table.");
        var candidate = -1;
        for (var column = 0; column < table.Columns; column++)
        {
            var key = table.Items![column]; if (key.IsError) return key;
            if (!TryCompareLookupValues(key, args[0], out var comparison)) continue;
            if (comparison == 0) return table.Items[row * table.Columns + column];
            if (approximate && comparison < 0) candidate = column;
        }
        if (approximate && candidate >= 0) return table.Items![row * table.Columns + candidate];
        return DataFormulaValue.Error(DataFormulaErrorCode.NotAvailable, "HLOOKUP did not find the lookup value or an eligible approximate match.");
    }
    private static DataFormulaValue ValueError(string message) => DataFormulaValue.Error(DataFormulaErrorCode.Value, message);
}
