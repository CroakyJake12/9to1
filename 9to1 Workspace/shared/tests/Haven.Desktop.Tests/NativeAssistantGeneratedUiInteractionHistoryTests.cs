#if !ANDROID
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public async Task Two_individually_reviewed_saves_refuse_foreign_history_receipts_operations_and_same_instance_before_row_edge()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(Haven.Desktop.Tests.TestAppBuilder));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            foreach (var corruption in new[] { "descriptor", "operation", "before-edge" })
            {
                var rig = new Rig(originalAdditionalPolicy: new HavenOS.Home.Core.HomeCanonicalGeneratedUiInteractionActionPolicySource());
                ProtectedGeneratedGraph? graph = null; var originals = new List<Task>(); var causes = new List<Exception>(); var errors = new List<Exception>();
                try
                {
                    await rig.InitializeAsync(true, importMemory: false);
                    var binding = await rig.CreateAsync(new() { Name = "Fictional original history witness", Memory = new(false) });
                    await AddProtectedGeneratedMessage(rig, binding);
                    var actual = graph = new ProtectedGeneratedGraph(rig);
                    ICanonicalGeneratedUiOriginalObservation? observed = null;
                    await WithProtectedGeneratedView(rig, actual, async (window, surface, controller, host) =>
                    {
                        await OpenActualSavedMemoryConversation(surface, binding.Definition.Identity, binding.Conversation.Id, window);
                        await AwaitOriginalGeneratedTree(window, surface, () => window.GetVisualDescendants().OfType<GenerativeUiSurface>().Any());
                        var generated = Assert.Single(window.GetVisualDescendants().OfType<GenerativeUiSurface>());
                        for (var index = 0; index != 2; index++)
                        {
                            var completed = new TaskCompletionSource<GenUiActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                            void Capture(object? sender, GenUiActionResult result) => completed.TrySetResult(result);
                            generated.ActionCompleted += Capture;
                            try
                            {
                                Assert.Single(generated.GetVisualDescendants().OfType<TextBox>()).Text = index == 0 ? "2 + 3" : "5 + 7";
                                var calculate = Assert.Single(generated.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Calculate"));
                                calculate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                var result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
                                Assert.Equal(GenUiActionStatus.Completed, result.Status);
                                Assert.True(actual.Instances.TryObserveOriginalMutation(result, out var mutation)); Assert.NotNull(mutation);
                            }
                            finally { generated.ActionCompleted -= Capture; }
                            await FlushNativeMemoryUi(window);
                            var save = ClickProtectedGeneratedControl(window, host, "Save generated interaction 1"); originals.Add(save);
                            await DecideProtectedGeneratedSave(rig, save, HomeApprovalChoice.Accept); await save;
                            var review = ClickProtectedGeneratedControl(window, host, "Review generated interaction 1"); originals.Add(review); await review;
                            observed = Assert.Single(host.OriginalInteractionObservations);
                            Assert.Equal(CanonicalGeneratedUiOriginalReadState.Restorable, observed.State);
                        }
                    });
                    var current = Assert.IsAssignableFrom<ICanonicalGeneratedUiOriginalObservation>(observed);
                    Assert.Equal(2L, await CountProtectedGeneratedOperations(rig)); Assert.Equal(1L, await CountProtectedGeneratedRows(rig, "genui_apps"));
                    var rowBefore = Assert.IsType<string>(await GeneratedHistoryScalar(rig, "SELECT definition_json FROM genui_apps;"));
                    await CorruptGeneratedHistory(rig, corruption);
                    Assert.Equal(rowBefore, await GeneratedHistoryScalar(rig, "SELECT definition_json FROM genui_apps;"));
                    var inspection = actual.Writer.RevalidateOriginalObservationWithinSourceAsync(current, Scope, actual.Retain, Token);
                    originals.Add(inspection); actual.Retain(inspection);
                    Assert.NotNull(await Record.ExceptionAsync(() => inspection)); Assert.True(inspection.IsFaulted); Assert.False(inspection.IsCanceled);
                    var expectedCorruption = corruption switch
                    {
                        "descriptor" => "The stored descriptor/receipt does not belong to the same original message history.",
                        "operation" => "The exact durable operation/descriptor/receipt history is incoherent.",
                        "before-edge" => "The exact saved descriptor predecessor does not belong to this original history.",
                        _ => throw new InvalidOperationException("Unknown original history corruption control.")
                    };
                    AssertOriginalHistoryInspectionCorruption(inspection.Exception!, expectedCorruption);
                    // Register exact references only after the entire SAME initial
                    // inspection graph passes this specific corruption proof.
                    causes.AddRange(ProtectedGeneratedCauses(inspection.Exception!));
                    Assert.Equal(rowBefore, await GeneratedHistoryScalar(rig, "SELECT definition_json FROM genui_apps;"));
                    Assert.Equal(2L, await CountProtectedGeneratedOperations(rig));
                    var close = actual.CloseAsync(); originals.Add(close);
                    Assert.NotNull(await Record.ExceptionAsync(() => close)); Assert.True(close.IsFaulted); Assert.False(close.IsCanceled);
                    Assert.Same(close, actual.CloseAsync()); AssertKnownGeneratedCauseGraph(close.Exception!, causes);
                    Assert.Null(actual.Origins.OriginalClose); Assert.Null(actual.Home.OriginalClose); Assert.Null(rig.Store.OriginalClose);
                }
                catch (Exception failure) { errors.Add(failure); }
                finally
                {
                    // Acquire/root actual cached close before any cleanup cause check, even after a body assertion fails.
                    try { if (graph is not null) originals.Add(graph.CloseAsync()); }
                    catch (Exception failure) { errors.Add(failure); }
                    // The actual failed READ close keeps process origins/Home/Den/store dependencies alive.
                    lock (FailedProtectedGeneratedUiOwners) FailedProtectedGeneratedUiOwners.Add([rig, graph!, originals, causes, errors]);
                    foreach (var original in originals.Distinct<Task>(ReferenceEqualityComparer.Instance))
                    {
                        try { await original; }
                        catch (Exception failure)
                        {
                            try { AssertKnownGeneratedCauseGraph(original.Exception ?? failure, causes); }
                            catch (Exception unknown) { errors.Add(original.Exception ?? failure); errors.Add(unknown); }
                        }
                    }
                    if (errors.Count != 0) throw new AggregateException("Unknown original history-control cleanup remains retained.", errors);
                }
            }
            return true;
        }, Token));
    }

    private static void AssertOriginalHistoryInspectionCorruption(Exception actual, string expectedMessage)
    {
        if (actual.GetType() == typeof(AggregateException))
        {
            var combined = Assert.IsType<AggregateException>(actual);
            Assert.NotEmpty(combined.InnerExceptions);
            foreach (var original in combined.InnerExceptions)
                AssertOriginalHistoryInspectionCorruption(original, expectedMessage);
            return;
        }
        var corruption = Assert.IsType<InvalidDataException>(actual);
        Assert.Null(corruption.InnerException);
        Assert.Equal(expectedMessage, corruption.Message);
    }

    private static async Task CorruptGeneratedHistory(Rig rig, string corruption)
    {
        const string prefix = "canonical.genui.interaction.v1.%";
        var latestKey = Assert.IsType<string>(await GeneratedHistoryScalar(rig, "SELECT key FROM settings WHERE key LIKE $prefix ORDER BY key DESC LIMIT 1;", ("$prefix", prefix)));
        var previousKey = Assert.IsType<string>(await GeneratedHistoryScalar(rig, "SELECT key FROM settings WHERE key LIKE $prefix ORDER BY key DESC LIMIT 1 OFFSET 1;", ("$prefix", prefix)));
        async Task<string> Read(string key) => Assert.IsType<string>(await GeneratedHistoryScalar(rig, "SELECT value FROM settings WHERE key=$key;", ("$key", key)));
        async Task Write(string key, string value) => Assert.Equal(1L, Convert.ToInt64(await GeneratedHistoryScalar(rig,
            "UPDATE settings SET value=$value WHERE key=$key RETURNING 1;", ("$key", key), ("$value", value))));
        var previous = JsonNode.Parse(await Read(previousKey))!;
        if (corruption == "descriptor")
        {
            // Keep the previous JSON hash and latest durable-operation descriptor coherent.
            // Only the previous receipt's real message tuple becomes foreign.
            previous["Receipt"]!["MessageId"] = JsonValue.Create(Guid.NewGuid());
            var json = previous.ToJsonString(); await Write(previousKey, json);
            var latest = JsonNode.Parse(await Read(latestKey))!;
            latest["PreviousDescriptorSha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
            await Write(latestKey, latest.ToJsonString());
            var key = "canonical.genui.operation.v1." + latest["Receipt"]!["OperationId"]!.GetValue<Guid>().ToString("N");
            var operation = JsonNode.Parse(await Read(key))!; operation["Descriptor"] = latest.DeepClone();
            await Write(key, operation.ToJsonString());
        }
        else if (corruption == "operation")
        {
            // The immutable descriptor/hash pair stays exact; its original durable operation is foreign.
            var key = "canonical.genui.operation.v1." + previous["Receipt"]!["OperationId"]!.GetValue<Guid>().ToString("N");
            var operation = JsonNode.Parse(await Read(key))!; operation["StoreId"] = JsonValue.Create(Guid.NewGuid());
            await Write(key, operation.ToJsonString());
        }
        else
        {
            // Preserve the latest row, previous descriptor/hash and complete operation agreement.
            // Only the claimed SAME-instance predecessor row becomes different from the actual prior row.
            var latest = JsonNode.Parse(await Read(latestKey))!;
            latest["Receipt"]!["BeforeRowSha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("Fictional foreign predecessor row")));
            await Write(latestKey, latest.ToJsonString());
            var key = "canonical.genui.operation.v1." + latest["Receipt"]!["OperationId"]!.GetValue<Guid>().ToString("N");
            var operation = JsonNode.Parse(await Read(key))!;
            operation["BeforeRowSha256"] = latest["Receipt"]!["BeforeRowSha256"]!.DeepClone();
            operation["Descriptor"] = latest.DeepClone(); await Write(key, operation.ToJsonString());
        }
    }
    private static async Task<object?> GeneratedHistoryScalar(Rig rig, string sql, params (string Name, string Value)[] arguments)
    {
        // Fixture corruption/observations only, over the SAME actual canonical database; no production path adopts these values.
        var opened = rig.Database.OpenAsync(Token); rig.Retain(opened); var connection = await opened;
        var command = connection.CreateCommand(); var errors = new List<Exception>(); object? result = null;
        try
        {
            command.CommandText = sql; foreach (var argument in arguments) command.Parameters.AddWithValue(argument.Name, argument.Value);
            var raw = command.ExecuteScalarAsync(Token); rig.Retain(raw);
            try { result = await raw; } catch (Exception failure) { errors.Add(raw.Exception ?? failure); }
        }
        catch (Exception failure) { errors.Add(failure); }
        var commandClose = command.DisposeAsync().AsTask(); rig.Retain(commandClose);
        try { await commandClose; } catch (Exception failure) { errors.Add(commandClose.Exception ?? failure); }
        if (commandClose.IsCompletedSuccessfully)
        {
            var close = connection.DisposeAsync().AsTask(); rig.Retain(close);
            try { await close; } catch (Exception failure) { errors.Add(close.Exception ?? failure); }
        }
        if (errors.Count != 0)
        {
            lock (FailedProtectedGeneratedUiOwners) FailedProtectedGeneratedUiOwners.Add([rig, connection, command, commandClose, errors]);
            throw new AggregateException("Actual history fixture SQL and cached cleanup causes.", errors);
        }
        return result;
    }
}
#endif
