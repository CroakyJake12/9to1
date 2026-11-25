using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Dev.Tests;

public sealed class DeveloperTrxIncompleteResultTests
{
    [Fact]
    public void Parse_PreservesPassedButRunAbortedAndFilterRevealsIt()
    {
        var runId = Guid.NewGuid();
        var testId1 = Guid.NewGuid();
        var execId1 = Guid.NewGuid();
        var testId2 = Guid.NewGuid();
        var execId2 = Guid.NewGuid();

        var trx = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<TestRun id=""{runId}"" name=""Test Run"" xmlns=""http://microsoft.com/schemas/VisualStudio/TeamTest/2010"">
  <Times start=""2023-01-01T00:00:00.0000000+00:00"" finish=""2023-01-01T00:01:00.0000000+00:00"" />
  <TestDefinitions>
    <UnitTest id=""{testId1}"">
      <TestMethod className=""MyNamespace.MyClass"" name=""IncompleteTest"" codeBase=""MyAssembly.dll"" />
      <Execution id=""{execId1}"" />
    </UnitTest>
    <UnitTest id=""{testId2}"">
      <TestMethod className=""MyNamespace.MyClass"" name=""PassedTest"" codeBase=""MyAssembly.dll"" />
      <Execution id=""{execId2}"" />
    </UnitTest>
  </TestDefinitions>
  <TestEntries>
    <TestEntry testId=""{testId1}"" executionId=""{execId1}"" testListId=""00000000-0000-0000-0000-000000000000"" />
    <TestEntry testId=""{testId2}"" executionId=""{execId2}"" testListId=""00000000-0000-0000-0000-000000000000"" />
  </TestEntries>
  <Results>
    <UnitTestResult testName=""IncompleteTest"" outcome=""PassedButRunAborted"" testId=""{testId1}"" executionId=""{execId1}"" duration=""00:00:01"" />
    <UnitTestResult testName=""PassedTest"" outcome=""Passed"" testId=""{testId2}"" executionId=""{execId2}"" duration=""00:00:02"" />
  </Results>
  <ResultSummary outcome=""Completed"">
    <Counters total=""2"" passed=""1"" failed=""0"" error=""0"" />
  </ResultSummary>
</TestRun>"";

        var result = DeveloperTrxTestResults.Parse(trx);
        Assert.True(result.Succeeded);
        var report = result.Value!;

        Assert.Equal(2, report.Tests.Count);

        var incomplete = report.Tests.Single(t => t.Name == "IncompleteTest");
        Assert.Equal(testId1, incomplete.TestId);
        Assert.Equal(execId1, incomplete.ExecutionId);
        Assert.Equal("PassedButRunAborted", incomplete.RawOutcome);
        Assert.Equal(DeveloperRecordedTestOutcome.PassedButRunAborted, incomplete.Outcome);
        Assert.True(incomplete.IsFailure);

        var passed = report.Tests.Single(t => t.Name == "PassedTest");
        Assert.Equal(testId2, passed.TestId);
        Assert.Equal(execId2, passed.ExecutionId);
        Assert.Equal("Passed", passed.RawOutcome);
        Assert.Equal(DeveloperRecordedTestOutcome.Passed, passed.Outcome);
        Assert.False(passed.IsFailure);

        var filtered = DeveloperTrxTestResults.Filter(report, "", true);
        Assert.Single(filtered);
        Assert.Equal("IncompleteTest", filtered[0].Name);
        Assert.DoesNotContain(filtered, t => t.Name == "PassedTest");
    }
}
