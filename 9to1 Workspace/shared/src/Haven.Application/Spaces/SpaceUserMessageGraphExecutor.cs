using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Haven.Application.Automations;

namespace Haven.Application;

/// <summary>Per-request pure nodes in the existing Automation executor. Input is held by
/// this actual execution instance, never read from editable node configuration.</summary>
internal sealed class SpaceUserMessageGraphExecutor(string originalInput) : IAutomationGraphNodeExecutor
{
    private const int MaximumResults = 512;
    private static readonly JsonSerializerOptions OutputJson = new(JsonSerializerDefaults.Web);
    private static readonly Regex NumericCandidates = new(@"[-+]?[0-9]+(?:[.,][0-9]+)*(?:[eE][-+]?[0-9]+)?",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));
    private static readonly Regex FrenchNumericCandidates = new(@"[-+]?[0-9]+(?:(?:[.,][0-9]+)|(?:[ \u00A0\u202F][0-9]{3}\b))*(?:[eE][-+]?[0-9]+)?",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));
    public bool CanExecute(AutomationGraphNodeDefinition node) => node.Category == SpaceUserMessageGraph.Category &&
        node.ActionKey is SpaceUserMessageGraph.SourceType or SpaceUserMessageGraph.TruncateType or SpaceUserMessageGraph.OutputType or
            SpaceUserMessageGraph.ExtractNumbersType or SpaceUserMessageGraph.NumbersToTextType or SpaceUserMessageGraph.SearchWordsType or SpaceUserMessageGraph.MatchesToTextType or
            SpaceUserMessageGraph.ParseJsonType or SpaceUserMessageGraph.SelectJsonType or SpaceUserMessageGraph.RenderJsonType or SpaceUserMessageGraph.JsonStringType or SpaceUserMessageGraph.RouteWordsType or SpaceUserMessageGraph.MergeType;
    public Task<AutomationGraphNodeExecutionResult> ExecuteAsync(AutomationGraphNodeExecutionContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!CanExecute(context.Node)) throw new InvalidOperationException("No Space message executor owns this node.");
        if (context.Node.ActionKey == SpaceUserMessageGraph.SourceType) return Task.FromResult(Result(originalInput, "Read original user-message input."));
        if (context.TypedInputs is null || !context.TypedInputs.TryGetValue("in", out var inputs) || inputs.Count != 1)
            return Task.FromResult(new AutomationGraphNodeExecutionResult(false, "Exactly one present typed input is required."));
        if (context.Node.ActionKey is SpaceUserMessageGraph.SelectJsonType or SpaceUserMessageGraph.RenderJsonType or SpaceUserMessageGraph.JsonStringType)
            return Task.FromResult(UseJsonValue(inputs[0], context.Node.ActionKey, context.Node.Parameters, token));
        if (context.Node.ActionKey is SpaceUserMessageGraph.NumbersToTextType or SpaceUserMessageGraph.MatchesToTextType)
        {
            var expected = context.Node.ActionKey == SpaceUserMessageGraph.NumbersToTextType ? SpaceUserMessageGraph.NumbersDataType : SpaceUserMessageGraph.MatchesDataType;
            if (inputs[0].DataType != expected || inputs[0].State != AutomationGraphValueState.Value || inputs[0].JsonValue is not { ValueKind: JsonValueKind.Object } typed)
                return Task.FromResult(new AutomationGraphNodeExecutionResult(false, "The renderer requires its declared structured extraction value."));
            var rendered = typed.GetRawText();
            return Task.FromResult(rendered.Length <= 131072 ? Result(rendered, "Rendered the typed derived value with its explicit semantics and omissions.") :
                new AutomationGraphNodeExecutionResult(false, "The rendered derived value exceeds the supported 131072-character bound."));
        }
        if (!inputs[0].TryGetString(out var text) || text is null)
            return Task.FromResult(new AutomationGraphNodeExecutionResult(false, "Exactly one present string input is required."));
        if (text.Length > 131072) return Task.FromResult(new AutomationGraphNodeExecutionResult(false, "The derived string input exceeds its supported bound."));
        if (context.Node.ActionKey == SpaceUserMessageGraph.ParseJsonType)
            return Task.FromResult(ParseJson(text, token));
        if (context.Node.ActionKey == SpaceUserMessageGraph.ExtractNumbersType)
            return Task.FromResult(ExtractNumbers(text, context.Node.Parameters["locale"], token));
        if (context.Node.ActionKey == SpaceUserMessageGraph.SearchWordsType)
            return Task.FromResult(SearchWords(text, context.Node.Parameters, token));
        if (context.Node.ActionKey == SpaceUserMessageGraph.RouteWordsType)
        {
            var branch = FindWord(text, context.Node.Parameters, 0, token) >= 0 ? "true" : "false";
            return Task.FromResult(new AutomationGraphNodeExecutionResult(true,
                "Selected one branch using explicit literal word/phrase matching semantics.", text, branch,
                new Dictionary<string, AutomationGraphValue> { [branch] = AutomationGraphValue.FromString(text) }));
        }
        if (context.Node.ActionKey == SpaceUserMessageGraph.MergeType)
            return Task.FromResult(Result(text, "Joined exactly one active typed branch value."));
        if (context.Node.ActionKey == SpaceUserMessageGraph.TruncateType)
        {
            var maximum = int.Parse(context.Node.Parameters["maximumTextElements"], CultureInfo.InvariantCulture);
            var offsets = StringInfo.ParseCombiningCharacters(text);
            text = offsets.Length <= maximum ? text : text[..offsets[maximum]];
            return Task.FromResult(Result(text, "Applied the configured Unicode text-element limit."));
        }
        return Task.FromResult(new AutomationGraphNodeExecutionResult(true, "Produced derived request text.", text));
    }
    private static AutomationGraphNodeExecutionResult Result(string text, string message) => new(true, message, text,
        TypedOutputs: new Dictionary<string, AutomationGraphValue> { ["out"] = AutomationGraphValue.FromString(text) });

    private static AutomationGraphNodeExecutionResult Structured(string type, object value, string message)
    {
        var json = JsonSerializer.SerializeToElement(value, OutputJson);
        if (Encoding.UTF8.GetByteCount(json.GetRawText()) > 262144)
            return new(false, "The structured derived value exceeds its supported 262144-byte UTF-8 bound.");
        return new(true, message, json.GetRawText(), TypedOutputs: new Dictionary<string, AutomationGraphValue>
            { ["out"] = AutomationGraphValue.FromJson(type, json) });
    }
    private static AutomationGraphNodeExecutionResult ParseJson(string text, CancellationToken token)
    {
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions
                { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 32 });
            if (!UniqueJsonObjectKeys(document.RootElement, token))
                return new(false, "JSON object property names must be unique after decoding; duplicate fields are ambiguous.");
            // An envelope distinguishes present JSON null from absent graph input and
            // prevents user property names from becoming graph omission/uncertainty flags.
            return Structured(SpaceUserMessageGraph.JsonDataType, new { value = document.RootElement },
                "Parsed strict JSON with unique ordinal property names and maximum container depth 32.");
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException)
        { return new(false, "The derived input is not strict JSON within the supported depth of 32. Comments, trailing commas and malformed Unicode are unsupported."); }
    }
    private static bool UniqueJsonObjectKeys(JsonElement value, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in value.EnumerateObject())
                if (!names.Add(field.Name) || !UniqueJsonObjectKeys(field.Value, token)) return false;
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) if (!UniqueJsonObjectKeys(item, token)) return false;
        else if (value.ValueKind == JsonValueKind.String)
            _ = value.GetString(); // Demand actual valid decoded text, not an invalid escaped surrogate.
        return true;
    }
    private static AutomationGraphNodeExecutionResult UseJsonValue(AutomationGraphValue input, string action,
        IReadOnlyDictionary<string, string> configuration, CancellationToken token)
    {
        if (input.DataType != SpaceUserMessageGraph.JsonDataType || input.State != AutomationGraphValueState.Value ||
            input.JsonValue is not { ValueKind: JsonValueKind.Object } envelope || !envelope.TryGetProperty("value", out var value))
            return new(false, "Exactly one present typed JSON value is required.");
        token.ThrowIfCancellationRequested();
        if (action == SpaceUserMessageGraph.SelectJsonType)
        {
            var pointer = configuration["pointer"];
            if (!SpaceUserMessageGraph.IsSupportedJsonPointer(pointer)) return new(false, "The configured JSON Pointer is invalid.");
            if (pointer.Length != 0)
                foreach (var encoded in pointer.Split('/').Skip(1))
                {
                    token.ThrowIfCancellationRequested();
                    var part = encoded.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
                    if (value.ValueKind == JsonValueKind.Object)
                    {
                        if (!value.TryGetProperty(part, out value)) return new(false, "The JSON Pointer did not identify one value.");
                    }
                    else if (value.ValueKind == JsonValueKind.Array)
                    {
                        if (part.Length == 0 || part.Length > 1 && part[0] == '0' || !part.All(char.IsAsciiDigit) ||
                            !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index >= value.GetArrayLength())
                            return new(false, "The JSON Pointer requires an existing array index written without leading zeros.");
                        value = value[index];
                    }
                    else return new(false, "The JSON Pointer cannot traverse a scalar JSON value.");
                }
            return Structured(SpaceUserMessageGraph.JsonDataType, new { value },
                "Selected exactly one JSON value using ordinal RFC 6901 field names and zero-based array indices.");
        }
        if (action == SpaceUserMessageGraph.JsonStringType && value.ValueKind != JsonValueKind.String)
            return new(false, "Read JSON string requires an actual string; numbers, objects, arrays, booleans and null are not coerced.");
        var rendered = action == SpaceUserMessageGraph.JsonStringType ? value.GetString()! : JsonSerializer.Serialize(value, OutputJson);
        return rendered.Length <= 131072 ? Result(rendered, action == SpaceUserMessageGraph.JsonStringType
            ? "Read the selected JSON string without coercion." : "Rendered the selected JSON value as compact JSON.")
            : new(false, "The rendered JSON value exceeds the supported 131072-character bound.");
    }

    private static AutomationGraphNodeExecutionResult ExtractNumbers(string text, string locale, CancellationToken token)
    {
        var results = new List<SpaceExtractedNumber>();
        var more = false;
        foreach (Match match in (locale == "fr-FR" ? FrenchNumericCandidates : NumericCandidates).Matches(text))
        {
            token.ThrowIfCancellationRequested();
            if (results.Count == MaximumResults) { more = true; break; }
            var value = ParseNumber(match.Value, locale, out var issue);
            if (match.Value[0] is '+' or '-' && match.Index > 0 && char.IsAsciiDigit(text[match.Index - 1]))
            { value = null; issue = "A sign adjoining another number may be a date/range delimiter; no structured date or range parsing is performed."; }
            if (value is not null)
            {
                if (match.Value.Any(character => character is ' ' or '\u00A0' or '\u202F'))
                    issue = "Whitespace grouping may instead separate numeric tokens; the selected French grouping interpretation was used.";
                foreach (var other in new[] { "invariant", "en-US", "de-DE", "fr-FR" }.Where(other => other != locale))
                {
                    var alternate = ParseNumber(match.Value, other, out _);
                    if (alternate is not null && !decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)
                        .Equals(decimal.Parse(alternate, NumberStyles.Float, CultureInfo.InvariantCulture)))
                    { issue = "This punctuation has a different numeric interpretation in another supported locale; the selected locale was used."; break; }
                }
            }
            results.Add(new(match.Index, match.Length, match.Value, value, issue));
        }
        return Structured(SpaceUserMessageGraph.NumbersDataType,
            new SpaceNumberExtraction(locale,
                "ASCII signed decimal/exponent candidates, including embedded digit runs; invariant forbids grouping, en-US/de-DE require 3-digit groups, fr-FR permits 3-digit space/NBSP/narrow-NBSP groups. At most 28 significant digits; unsupported/ambiguous formats retain raw text and uncertainty. Spans are UTF-16 offsets in this node's input; no date, fraction, currency or non-ASCII numeral parser is implied.",
                results, results.Any(item => item.Uncertainty is not null), more), "Extracted numeric candidates under the explicit locale.");
    }
    private static string? ParseNumber(string raw, string locale, out string? issue)
    {
        issue = null;
        var exponentAt = raw.IndexOfAny(['e', 'E']);
        var coefficient = exponentAt < 0 ? raw : raw[..exponentAt];
        var exponent = exponentAt < 0 ? "" : raw[exponentAt..];
        var sign = coefficient[0] is '+' or '-' ? coefficient[..1] : "";
        coefficient = coefficient[sign.Length..];
        var separator = locale is "de-DE" or "fr-FR" ? ',' : '.';
        var split = coefficient.Split(separator);
        if (split.Length > 2 || split.Any(part => part.Length == 0)) return Refuse("The candidate does not have one valid decimal separator for the selected locale.", out issue);
        var whole = split[0];
        var group = locale switch { "en-US" => @",", "de-DE" => @"\.", "fr-FR" => @"[ \u00A0\u202F]", _ => null };
        if (!whole.All(char.IsAsciiDigit) && (group is null || !Regex.IsMatch(whole, @"\A[0-9]{1,3}(?:" + group + @"[0-9]{3})+\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)))
            return Refuse("The grouping or decimal punctuation is unsupported for the selected locale.", out issue);
        if (split.Length == 2 && !split[1].All(char.IsAsciiDigit)) return Refuse("A fraction contains unsupported punctuation.", out issue);
        var digits = new string(whole.Where(char.IsAsciiDigit).ToArray());
        var significant = (digits + (split.Length == 2 ? split[1] : "")).TrimStart('0').Length;
        if (significant > 28) return Refuse("The candidate exceeds the exact supported 28-significant-digit precision; raw text was retained.", out issue);
        if (exponent.Length != 0 && !int.TryParse(exponent[1..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
            return Refuse("The exponent is outside the supported numeric format.", out issue);
        var exponentValue = exponent.Length == 0 ? 0 : int.Parse(exponent[1..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        if ((long)(split.Length == 2 ? split[1].Length : 0) - exponentValue > 28)
            return Refuse("The candidate exceeds the exact supported decimal scale; raw text was retained.", out issue);
        var canonical = sign + digits + (split.Length == 2 ? "." + split[1] : "") + exponent;
        if (!decimal.TryParse(canonical, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture, out var number)) return Refuse("The candidate is outside the exact supported decimal range.", out issue);
        // Decimal can round very small exponents to zero; never report that loss as exact.
        if (number == 0 && significant != 0) return Refuse("The exponent is below the supported decimal range; raw text was retained.", out issue);
        return number.ToString("G29", CultureInfo.InvariantCulture);
    }
    private static string? Refuse(string reason, out string? issue) { issue = reason; return null; }

    private static AutomationGraphNodeExecutionResult SearchWords(string text, IReadOnlyDictionary<string, string> configuration, CancellationToken token)
    {
        var query = configuration["query"];
        var matches = new List<SpacePhraseMatch>();
        var more = false;
        var offset = 0;
        while (offset <= text.Length - query.Length)
        {
            var found = FindWord(text, configuration, offset, token);
            if (found < 0) break;
            if (matches.Count == MaximumResults) { more = true; break; }
            matches.Add(new(found, query.Length, text.Substring(found, query.Length)));
            offset = found + query.Length; // Explicitly non-overlapping matches.
        }
        return Structured(SpaceUserMessageGraph.MatchesDataType,
            new SpacePhraseMatches(query, configuration["locale"], configuration["comparison"], configuration["boundary"],
                "Literal non-overlapping ordinal Unicode matching; whitespace and diacritics are significant, no regex/stemming/normalisation or locale-specific collation. Whole-word edges exclude Unicode letters, numbers, connector punctuation and combining marks. Spans are UTF-16 offsets in this node's input.",
                matches, more), "Matched literal words or phrases under the explicit comparison and boundary semantics.");
    }
    private static int FindWord(string text, IReadOnlyDictionary<string, string> configuration, int offset, CancellationToken token)
    {
        var query = configuration["query"];
        var comparison = configuration["comparison"] == "ordinal" ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var wholeWord = configuration["boundary"] == "whole-word";
        while (offset <= text.Length - query.Length)
        {
            token.ThrowIfCancellationRequested();
            var found = text.IndexOf(query, offset, comparison);
            if (found < 0) return -1;
            if (!wholeWord || !IsWordBefore(text, found) && !IsWordAfter(text, found + query.Length)) return found;
            offset = found + 1;
        }
        return -1;
    }
    private static bool IsWordBefore(string text, int offset) => offset > 0 &&
        Rune.DecodeLastFromUtf16(text.AsSpan(0, offset), out var value, out _) == System.Buffers.OperationStatus.Done && IsWord(value);
    private static bool IsWordAfter(string text, int offset) => offset < text.Length &&
        Rune.DecodeFromUtf16(text.AsSpan(offset), out var value, out _) == System.Buffers.OperationStatus.Done && IsWord(value);
    private static bool IsWord(Rune value) => Rune.GetUnicodeCategory(value) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or
        UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.DecimalDigitNumber or
        UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber or UnicodeCategory.ConnectorPunctuation or UnicodeCategory.NonSpacingMark or
        UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
}

public sealed record SpaceExtractedNumber(int StartUtf16, int LengthUtf16, string RawText, string? InvariantValue, string? Uncertainty);
public sealed record SpaceNumberExtraction(string Locale, string Semantics, IReadOnlyList<SpaceExtractedNumber> Numbers, bool HasUncertainty, bool MoreResultsOmitted);
public sealed record SpacePhraseMatch(int StartUtf16, int LengthUtf16, string Text);
public sealed record SpacePhraseMatches(string Query, string Locale, string Comparison, string Boundary, string Semantics, IReadOnlyList<SpacePhraseMatch> Matches, bool MoreResultsOmitted);
