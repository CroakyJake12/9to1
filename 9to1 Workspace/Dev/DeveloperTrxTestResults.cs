using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace HavenOS.Apps.Dev;

public enum DeveloperRecordedTestOutcome
{
    Unknown, Passed, Failed, Error, Timeout, Aborted, NotExecuted, Inconclusive,
    Warning, Pending, NotRunnable, Disconnected, Completed, InProgress, PassedButRunAborted
}
public sealed record DeveloperRecordedTest(Guid TestId, Guid ExecutionId, string Name, string RawOutcome,
    DeveloperRecordedTestOutcome Outcome, TimeSpan? Duration, string? ClassName, string? MethodName, string? Assembly,
    string Message, string StackTrace, string StandardOutput, string StandardError, bool DetailTruncated)
{
    public bool IsFailure => Outcome is DeveloperRecordedTestOutcome.Failed or DeveloperRecordedTestOutcome.Error or
        DeveloperRecordedTestOutcome.Timeout or DeveloperRecordedTestOutcome.Aborted or DeveloperRecordedTestOutcome.NotRunnable or DeveloperRecordedTestOutcome.Disconnected;
}
public sealed record DeveloperTrxTestReport(Guid RunId, string Name, string ReportedRunOutcome,
    DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, IReadOnlyList<DeveloperRecordedTest> Tests);

/// <summary>Pure bounded decoding of the standard TRX 2010 data format using System.Xml.
/// IDs/outcomes come from the saved report only. No test discovery, compiler/process, assembly
/// loading, attachment access, source identity or permission is inferred from this document.</summary>
public static class DeveloperTrxTestResults
{
    public const int MaximumCharacters = 2_000_000, MaximumTests = 5000;
    private const int MaximumDetailCharacters = 32_000;
    private static readonly XNamespace Trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
    private static XmlReaderSettings Settings() => new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
        MaxCharactersInDocument = MaximumCharacters, MaxCharactersFromEntities = 0 };

    public static bool IsReportPath(string path) => !string.IsNullOrWhiteSpace(path) && path.Length <= 4096 &&
        !path.Any(char.IsControl) && !Path.IsPathRooted(path) && !path.StartsWith('/') && !path.StartsWith('\\') &&
        !(path.Length > 1 && path[1] == ':') && !path.Contains('\\') &&
        path.Split('/').All(part => part is not ("" or "." or "..")) && path.EndsWith(".trx", StringComparison.OrdinalIgnoreCase);

    public static DeveloperOperationResult<DeveloperTrxTestReport> Parse(string source, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source); token.ThrowIfCancellationRequested();
        if (source.Length > MaximumCharacters) return Invalid("This report exceeds the 2,000,000-character limit. The previous report is kept.");
        try
        {
            // Bound nesting before constructing a DOM. Both passes use the maintained XML
            // reader; no DTD, resolver, attachment, CodeBase or external URI is followed.
            using (var input = new StringReader(source))
            using (var reader = XmlReader.Create(input, Settings()))
            {
                var elements = 0;
                while (reader.Read())
                {
                    token.ThrowIfCancellationRequested();
                    if (reader.Depth > 32 || reader.NodeType == XmlNodeType.Element && ++elements > 100_000)
                        return Invalid("This report exceeds the supported XML depth/element bounds. The previous report is kept.");
                }
            }
            XDocument document;
            using (var input = new StringReader(source))
            using (var reader = XmlReader.Create(input, Settings())) document = XDocument.Load(reader);
            var root = document.Root;
            if (root?.Name != Trx + "TestRun") return Invalid("Choose a standard TRX 2010 TestRun report. Other result formats are not supported here.");
            var runId = RequiredGuid(root, "id");
            var results = OptionalSingle(root, "Results"); var definitions = OptionalSingle(root, "TestDefinitions");
            var entries = OptionalSingle(root, "TestEntries"); var summary = OptionalSingle(root, "ResultSummary"); var times = OptionalSingle(root, "Times");
            var methods = new Dictionary<Guid, (Guid? Execution, string? Class, string? Method, string? Assembly)>();
            if (definitions is not null)
                foreach (var definition in definitions.Elements())
                {
                    token.ThrowIfCancellationRequested();
                    if (definition.Name != Trx + "UnitTest" || methods.Count >= MaximumTests) throw new InvalidDataException("This report has unsupported or excessive test definitions.");
                    var id = RequiredGuid(definition, "id"); var method = OptionalSingle(definition, "TestMethod"); var execution = OptionalSingle(definition, "Execution");
                    if (!methods.TryAdd(id, (execution is null ? null : RequiredGuid(execution, "id"),
                        Attribute(method, "className", 4096), Attribute(method, "name", 4096), Attribute(method, "codeBase", 4096))))
                        throw new InvalidDataException("Duplicate test definitions make this report ambiguous.");
                }
            var entryMap = new Dictionary<Guid, Guid>();
            if (entries is not null)
                foreach (var entry in entries.Elements())
                {
                    token.ThrowIfCancellationRequested();
                    if (entry.Name != Trx + "TestEntry" || entryMap.Count >= MaximumTests ||
                        !entryMap.TryAdd(RequiredGuid(entry, "executionId"), RequiredGuid(entry, "testId")))
                        throw new InvalidDataException("This report has unsupported or ambiguous execution entries.");
                }
            var rows = new List<DeveloperRecordedTest>(); var executions = new HashSet<Guid>();
            if (results is not null)
                foreach (var row in results.Elements())
                {
                    token.ThrowIfCancellationRequested();
                    if (row.Name != Trx + "UnitTestResult" || rows.Count >= MaximumTests || row.Element(Trx + "InnerResults") is not null)
                        throw new InvalidDataException("This viewer supports up to 5,000 individual UnitTestResult rows. Aggregate/inner-result formats stay in their test provider.");
                    var testId = RequiredGuid(row, "testId"); var executionId = RequiredGuid(row, "executionId");
                    if (!executions.Add(executionId)) throw new InvalidDataException("Duplicate execution IDs make this report ambiguous.");
                    if (definitions is not null && !methods.ContainsKey(testId) || entries is not null &&
                        (!entryMap.TryGetValue(executionId, out var entryTest) || entryTest != testId))
                        throw new InvalidDataException("A test result contradicts its saved definition or execution entry.");
                    methods.TryGetValue(testId, out var definition);
                    if (definition.Execution is { } declaredExecution && declaredExecution != executionId)
                        throw new InvalidDataException("A test result contradicts its definition's execution ID.");
                    var name = Attribute(row, "testName", 4096); var outcomeText = Attribute(row, "outcome", 128);
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(outcomeText)) throw new InvalidDataException("Every test result needs its recorded name and outcome.");
                    var outcome = Enum.TryParse<DeveloperRecordedTestOutcome>(outcomeText, false, out var parsed) && Enum.IsDefined(parsed) &&
                        !int.TryParse(outcomeText, out _) ? parsed : DeveloperRecordedTestOutcome.Unknown;
                    var output = OptionalSingle(row, "Output"); var error = output is null ? null : OptionalSingle(output, "ErrorInfo");
                    var truncated = false;
                    string Detail(XElement? parent, string element)
                    {
                        var text = parent is null ? "" : OptionalSingle(parent, element)?.Value ?? "";
                        if (text.Length <= MaximumDetailCharacters) return text;
                        truncated = true; return text[..MaximumDetailCharacters] + "\n[detail truncated]";
                    }
                    var message = Detail(error, "Message"); var stack = Detail(error, "StackTrace");
                    var stdout = Detail(output, "StdOut"); var stderr = Detail(output, "StdErr");
                    rows.Add(new(testId, executionId, name, outcomeText, outcome, Duration(row), definition.Class, definition.Method,
                        definition.Assembly, message, stack, stdout, stderr, truncated));
                }
            if (entries is not null && (entryMap.Count != rows.Count || entryMap.Keys.Any(id => !executions.Contains(id))))
                throw new InvalidDataException("Execution entries and result rows differ. Partial/aggregate reports are not adopted.");
            var counters = summary is null ? null : OptionalSingle(summary, "Counters");
            CheckCounter(counters, "total", rows.Count);
            CheckCounter(counters, "passed", rows.Count(row => row.Outcome == DeveloperRecordedTestOutcome.Passed));
            CheckCounter(counters, "failed", rows.Count(row => row.Outcome == DeveloperRecordedTestOutcome.Failed));
            CheckCounter(counters, "error", rows.Count(row => row.Outcome == DeveloperRecordedTestOutcome.Error));
            token.ThrowIfCancellationRequested();
            return DeveloperOperationResult<DeveloperTrxTestReport>.Success(new(runId, Attribute(root, "name", 4096) ?? "",
                Attribute(summary, "outcome", 128) ?? "Not reported", Timestamp(times, "start"), Timestamp(times, "finish"), Array.AsReadOnly(rows.ToArray())));
        }
        catch (Exception error) when (error is XmlException or InvalidDataException or FormatException or ArgumentException or OverflowException)
        { return Invalid("The saved report could not be verified: " + error.Message + " The previous report is kept."); }
    }

    public static IReadOnlyList<DeveloperRecordedTest> Filter(DeveloperTrxTestReport report, string query, bool failuresOnly, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(report); ArgumentNullException.ThrowIfNull(query);
        if (query.Length > 256 || query.Any(char.IsControl)) throw new ArgumentException("Use a literal test name up to 256 characters.", nameof(query));
        var rows = new List<DeveloperRecordedTest>();
        foreach (var row in report.Tests)
        {
            token.ThrowIfCancellationRequested();
            if ((!failuresOnly || row.IsFailure) && row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) rows.Add(row);
        }
        return Array.AsReadOnly(rows.ToArray());
    }
    private static XElement? OptionalSingle(XElement parent, string name)
    {
        var nodes = parent.Elements(Trx + name).Take(2).ToArray();
        return nodes.Length <= 1 ? nodes.SingleOrDefault() : throw new InvalidDataException("Repeated " + name + " sections are ambiguous.");
    }
    private static string? Attribute(XElement? element, string name, int maximum)
    {
        var value = element?.Attribute(name)?.Value;
        if (value is not null && value.Length > maximum) throw new InvalidDataException("The report's " + name + " field exceeds its bound.");
        return value;
    }
    private static Guid RequiredGuid(XElement element, string name) =>
        Guid.TryParse(Attribute(element, name, 128), out var id) && id != Guid.Empty ? id : throw new InvalidDataException("Missing or invalid " + name + ". No identity was generated.");
    private static TimeSpan? Duration(XElement element)
    {
        var value = Attribute(element, "duration", 128);
        if (value is null) return null;
        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var duration) && duration >= TimeSpan.Zero
            ? duration : throw new InvalidDataException("The report contains an invalid test duration.");
    }
    private static DateTimeOffset? Timestamp(XElement? element, string name)
    {
        var value = Attribute(element, name, 128);
        if (value is null) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp)
            ? timestamp : throw new InvalidDataException("The report contains an invalid " + name + " timestamp.");
    }
    private static void CheckCounter(XElement? counters, string name, int actual)
    {
        var value = Attribute(counters, name, 128);
        if (value is not null && (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count != actual))
            throw new InvalidDataException("The reported " + name + " counter contradicts the individual result rows.");
    }
    private static DeveloperOperationResult<DeveloperTrxTestReport> Invalid(string message) =>
        DeveloperOperationResult<DeveloperTrxTestReport>.Failure(DeveloperOperationErrorCode.InvalidStoredData, message, "test-report");
}
