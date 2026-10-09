using System.Text.Json;
using HavenOS.Apps.Motion;

const string name = "original-motion-project-cli-real-file-workflow";
if (args.Length != 1) return 2;
var reportPath = Path.GetFullPath(args[0]);
if (File.Exists(reportPath)) return 2;
Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
int? originalReturnCode = null;
string? failure = null;
Console.WriteLine("START " + name);
try
{
    // This is the unchanged whole original owner test, not a recreated oracle.
    originalReturnCode = MotionProjectWorkflowTest.Run();
    if (originalReturnCode != 0)
        failure = "Original MotionProjectWorkflowTest.Run returned " + originalReturnCode;
}
catch (Exception exception)
{
    var text = exception.ToString();
    failure = text.Length <= 32768 ? text : text[..32768] + " [bounded diagnostic truncated]";
}
var passed = originalReturnCode == 0 && failure is null;
var report = new
{
    scope = "Unchanged original Motion CLI/model real local-filesystem workflow only; no browser, Files resolution, source decode, donor render, provider, crash injection or full Motion acceptance",
    discovered = 1, executed = 1, passed = passed ? 1 : 0, failed = passed ? 0 : 1, notRun = 0,
    name, originalReturnCode, failure,
    assertions = (int?)null, assertionCountQualification = "Original owner test is not instrumented; its return-code checkpoints are not separate discovered tests or measured assertion counts"
};
File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine((passed ? "PASS " : "FAIL ") + name);
return passed ? 0 : 1;
