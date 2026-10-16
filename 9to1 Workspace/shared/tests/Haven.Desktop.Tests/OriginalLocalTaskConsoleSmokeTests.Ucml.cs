using System.Text.Json;
using Haven.Desktop.Services;
using HavenOS.Home.Core;
using NineToOne.Dulche.Context;

namespace Haven.Desktop.Tests;

public sealed partial class OriginalLocalTaskConsoleSmokeTests
{
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 1)]
    [InlineData(true, 40)]
    public async Task Actual_linux_ucml_console_reviews_exact_files_and_obeys_individual_Home_decision(bool accept, int recordCount)
    {
        if (!OperatingSystem.IsLinux()) return;
        var token = TestContext.Current.CancellationToken;
        await ConsoleOwnership.WaitAsync(token);
        var previousInput = Console.In; var previousOutput = Console.Out;
        var input = new ReviewedSetupInput(); var output = new ReviewedSetupOutput(boundedPermissionHistory: true);
        var failures = new List<Exception>(); var sources = new List<Task>();
        string? root = null; string? filesRoot = null;
        Task<Task<int>>? launch = null; Task<int>? actualRun = null; Task<int>? wholeDriver = null;
        var quitRequested = false;
        try
        {
            root = Directory.CreateTempSubdirectory("haven-actual-ucml-console-").FullName;
            PrepareOriginalPrivateHomeDirectory(root);
            filesRoot = Directory.CreateTempSubdirectory("haven-actual-ucml-files-").FullName;
            var documents = Enumerable.Range(0, recordCount).Select(_ => UcmlConsoleFixtureDocument()).ToArray();
            var original = documents[0];
            var importPath = Path.Combine(filesRoot, "original-0000.ucml"); var exportPath = Path.Combine(filesRoot, "exported.ucml");
            for (var index = 0; index < documents.Length; index++)
            {
                var path = Path.Combine(filesRoot, $"original-{index:D4}.ucml");
                var write = File.WriteAllTextAsync(path, UcmlCodec.Serialize(documents[index]), token); sources.Add(write); await write;
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            Console.SetIn(input); Console.SetOut(output);
            var actualDataRoot = root;
            launch = Task.Factory.StartNew(() => OriginalLocalTaskConsole.RunAsync(["--data-directory", actualDataRoot], token),
                CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
            wholeDriver = launch.Unwrap();
            await output.NextAsync(value => value.TryGetProperty("localHomeStarted", out _), token);

            input.Supply("assistant-setup");
            var setup = await output.NextAsync(value => value.TryGetProperty("CanBindEmpty", out _), token);
            Assert.False(setup.GetProperty("IsOwned").GetBoolean());
            if (setup.GetProperty("CanBindEmpty").GetBoolean()) input.Supply("assistant-store-bind-empty");
            else
            {
                input.Supply("assistant-store-request");
                var ownershipRequest = await output.NextAsync(value => value.TryGetProperty("RequestId", out _) && value.TryGetProperty("Code", out _), token);
                var ownershipReview = await ReviewActualUcmlRequestAsync(input, output, token,
                    "home.profile.importStore", "9to1.home.local-profile");
                Assert.Equal(ownershipRequest.GetProperty("RequestId").GetString(), ownershipReview.GetProperty("RequestId").GetString());
                input.Supply("home-accept " + ownershipReview.GetProperty("RequestId").GetString());
                await output.NextAsync(value => value.TryGetProperty("Code", out var code) && code.GetString() == "HOME_PERMISSION_ACCEPTED", token);
                input.Supply("assistant-store-complete");
            }
            var owned = await output.NextAsync(value => value.TryGetProperty("IsOwned", out var valueOwned) && valueOwned.GetBoolean(), token);
            Assert.Equal(setup.GetProperty("StoreId").GetGuid(), owned.GetProperty("StoreId").GetGuid());
            input.Supply("assistant-create UCML fixture helper");
            var created = await output.NextAsync(value => value.TryGetProperty("definition", out _), token);
            var configuration = created.GetProperty("definition").GetProperty("Configuration");
            Assert.Empty(configuration.GetProperty("Memory").GetProperty("UcmlCategories").EnumerateArray());
            Assert.False(configuration.GetProperty("Model").GetProperty("AllowCloud").GetBoolean());

            if (recordCount > 1)
            {
                // Exercise the real migration's 215 prepared originals while keeping
                // this regression's individually approved writes bounded at forty.
                // Re-reviewing a smaller batch does not erase the first source-owned
                // preparations or grant permission to commit them.
                var largeReviewRoot = Directory.CreateDirectory(Path.Combine(filesRoot, "large-review")).FullName;
                for (var index = 0; index < 215; index++)
                {
                    var prepared = index < documents.Length ? documents[index] : UcmlConsoleFixtureDocument();
                    var path = Path.Combine(largeReviewRoot, $"prepared-{index:D4}.ucml");
                    var write = File.WriteAllTextAsync(path, UcmlCodec.Serialize(prepared), token); sources.Add(write); await write;
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                input.Supply("ucml-review " + largeReviewRoot);
                var largeReview = await output.NextAsync(value => value.TryGetProperty("records", out _), token, timeoutSeconds: 120);
                Assert.Equal(215, largeReview.GetProperty("count").GetInt32());
            }

            input.Supply("ucml-review " + (recordCount == 1 ? importPath : filesRoot));
            var reviewed = await output.NextAsync(value => value.TryGetProperty("records", out _), token);
            var batch = reviewed.GetProperty("batch").GetGuid(); Assert.Equal(recordCount, reviewed.GetProperty("count").GetInt32());
            var reviewedRecords = reviewed.GetProperty("records").EnumerateArray().ToArray(); Assert.Equal(recordCount, reviewedRecords.Length);
            Assert.Equal(documents.Select(document => document.Metadata.Id), reviewedRecords.Select(row => row.GetProperty("Metadata").GetProperty("Id").GetGuid()));
            var reviewedRecord = reviewedRecords[0];
            Assert.Equal(original.Metadata.Id, reviewedRecord.GetProperty("Metadata").GetProperty("Id").GetGuid());
            Assert.Equal(original.Markdown, reviewedRecord.GetProperty("markdown").GetString());
            Assert.Equal("local-model", reviewedRecord.GetProperty("Metadata").GetProperty("Consent").GetProperty("Recipients")[0].GetString());
            Assert.Equal(original.Metadata.Sources[0].Id, reviewedRecord.GetProperty("Metadata").GetProperty("Sources")[0].GetProperty("Id").GetString());
            // The actual review owns frozen bytes. A changed source file must not silently
            // replace the record subsequently presented to Home or persisted/exported.
            var replacement = original with { Markdown = "Different bytes after review must never be imported." };
            var changed = File.WriteAllTextAsync(importPath, UcmlCodec.Serialize(replacement), token); sources.Add(changed); await changed;
            input.Supply("ucml-commit " + Guid.NewGuid());
            var refused = await output.NextAsync(value => value.TryGetProperty("commandFailed", out _), token, allowCommandFailure: true);
            Assert.Contains("exact displayed batch", refused.GetProperty("cause").GetString());
            input.Supply("ucml-commit " + batch);
            await output.NextAsync(value => value.TryGetProperty("started", out var started) && started.GetBoolean(), token);
            for (var index = 0; index < documents.Length; index++)
            {
                var document = documents[index];
                var request = await ReviewActualUcmlRequestAsync(input, output, token);
                input.Supply((accept ? "home-accept " : "home-decline ") + request.GetProperty("RequestId").GetString());
                JsonElement outcome;
                if (recordCount == 1)
                    outcome = await output.NextAsync(value => value.TryGetProperty("Saved", out _) && value.TryGetProperty("id", out _), token);
                else
                {
                    // The real migration driver queries Home and batch status while
                    // the accepted write advances. Exercise that concurrent reader
                    // pressure beyond the previously observed 37th-write stall.
                    var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
                    while (true)
                    {
                        input.Supply("home-requests");
                        await output.NextAsync(value => value.TryGetProperty("PendingRequests", out _), token);
                        input.Supply("ucml-status");
                        var progress = await output.NextAsync(value => value.TryGetProperty("outcomes", out _), token);
                        Assert.NotEqual("Faulted", progress.GetProperty("state").GetString());
                        if (progress.GetProperty("outcomes").GetArrayLength() > index)
                        { outcome = progress.GetProperty("outcomes")[index]; break; }
                        if (DateTimeOffset.UtcNow >= deadline)
                            throw new TimeoutException($"Actual approved UCML batch write {index + 1}/{recordCount} did not settle.");
                        await Task.Delay(1000, token);
                    }
                }
                Assert.Equal(document.Metadata.Id, outcome.GetProperty("id").GetGuid()); Assert.Equal(accept, outcome.GetProperty("Saved").GetBoolean());
            }

            JsonElement status;
            do
            {
                input.Supply("ucml-status");
                status = await output.NextAsync(value => value.TryGetProperty("outcomes", out _), token);
                Assert.NotEqual("Faulted", status.GetProperty("state").GetString());
                if (status.GetProperty("state").GetString() != "RanToCompletion") await Task.Delay(25, token);
            } while (status.GetProperty("state").GetString() != "RanToCompletion");
            Assert.Equal(batch, status.GetProperty("batch").GetGuid()); Assert.Equal(recordCount, status.GetProperty("outcomes").GetArrayLength());
            input.Supply("ucml-commit " + batch);
            var replay = await output.NextAsync(value => value.TryGetProperty("commandFailed", out _), token, allowCommandFailure: true);
            Assert.Contains("already submitted", replay.GetProperty("cause").GetString());
            input.Supply("ucml-list"); input.Supply("ucml-status");
            await output.NextAsync(value => value.TryGetProperty("outcomes", out _), token);
            var listed = ReadUcmlConsoleListedRecords(output.ToString());
            Assert.Equal(accept, listed.Any(value => value.GetProperty("Id").GetGuid() == original.Metadata.Id));
            Assert.Equal(accept ? recordCount : 0, listed.Count);
            if (accept)
            {
                input.Supply("ucml-export " + original.Metadata.Id + " " + exportPath);
                var exported = await output.NextAsync(value => value.TryGetProperty("exported", out _), token);
                Assert.Equal(original.Metadata.Id, exported.GetProperty("exported").GetGuid());
                var read = File.ReadAllTextAsync(exportPath, token); sources.Add(read);
                Assert.Equal(UcmlCodec.Serialize(original), UcmlCodec.Serialize(UcmlCodec.Parse(await read)));
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(exportPath));
                Assert.Empty(Directory.EnumerateFiles(filesRoot, ".ucml-export-*"));
                input.Supply("ucml-export " + original.Metadata.Id + " " + exportPath);
                var overwrite = await output.NextAsync(value => value.TryGetProperty("commandFailed", out _), token, allowCommandFailure: true);
                Assert.Contains("never overwritten", overwrite.GetProperty("cause").GetString());
            }
            else Assert.False(File.Exists(exportPath));

            input.Supply("quit"); quitRequested = true;
            Assert.True(await wholeDriver == 0, output.ToString()); actualRun = await launch;
            var recoveryRead = File.ReadAllTextAsync(Path.Combine(root, "startup-recovery.json"), token); sources.Add(recoveryRead);
            using var recovery = JsonDocument.Parse(await recoveryRead);
            Assert.True(recovery.RootElement.GetProperty("currentRun").GetProperty("cleanShutdown").GetBoolean());
            Assert.Contains("\"modelInitialized\":false", output.ToString());

            // Reopen the real public driver against the same private Home/Den/SQLite
            // stores after its first close has joined every original owner. No test
            // object, prior view, write preview, or approval is reused by this process.
            sources.Add(launch); sources.Add(actualRun); sources.Add(wholeDriver);
            var restartOutputOffset = output.ToString().Length;
            launch = Task.Factory.StartNew(() => OriginalLocalTaskConsole.RunAsync(["--data-directory", actualDataRoot], token),
                CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
            wholeDriver = launch.Unwrap(); actualRun = null; quitRequested = false;
            await output.NextAsync(value => value.TryGetProperty("localHomeStarted", out _), token);
            input.Supply("assistant-setup");
            var reopenedStore = await output.NextAsync(value => value.TryGetProperty("IsOwned", out _), token);
            Assert.True(reopenedStore.GetProperty("IsOwned").GetBoolean());
            Assert.Equal(owned.GetProperty("StoreId").GetGuid(), reopenedStore.GetProperty("StoreId").GetGuid());
            input.Supply("assistant-open " + created.GetProperty("definition").GetProperty("Identity").GetProperty("DefinitionId").GetString()
                + " " + created.GetProperty("conversation").GetGuid());
            var reopened = await output.NextAsync(value => value.TryGetProperty("definition", out _), token);
            Assert.Equal(created.GetProperty("definition").GetProperty("Identity").GetRawText(),
                reopened.GetProperty("definition").GetProperty("Identity").GetRawText());
            Assert.Equal(created.GetProperty("conversation").GetGuid(), reopened.GetProperty("conversation").GetGuid());
            Assert.Empty(reopened.GetProperty("definition").GetProperty("Configuration").GetProperty("Memory").GetProperty("UcmlCategories").EnumerateArray());
            input.Supply("ucml-commit " + batch);
            var staleReview = await output.NextAsync(value => value.TryGetProperty("commandFailed", out _), token, allowCommandFailure: true);
            Assert.Contains("Review the selected files first", staleReview.GetProperty("cause").GetString());
            input.Supply("ucml-list"); input.Supply("ucml-status");
            await output.NextAsync(value => value.TryGetProperty("outcomes", out _), token);
            var restartedRows = ReadUcmlConsoleListedRecords(output.ToString()[restartOutputOffset..]);
            Assert.Equal(accept, restartedRows.Any(value => value.GetProperty("Id").GetGuid() == original.Metadata.Id));
            Assert.Equal(accept ? recordCount : 0, restartedRows.Count);
            if (accept)
            {
                var restartedExportPath = Path.Combine(filesRoot, "exported-after-restart.ucml");
                input.Supply("ucml-export " + original.Metadata.Id + " " + restartedExportPath);
                await output.NextAsync(value => value.TryGetProperty("exported", out _), token);
                var read = File.ReadAllTextAsync(restartedExportPath, token); sources.Add(read);
                Assert.Equal(UcmlCodec.Serialize(original), UcmlCodec.Serialize(UcmlCodec.Parse(await read)));
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(restartedExportPath));
                var restoreSource = File.WriteAllTextAsync(importPath, UcmlCodec.Serialize(original), token); sources.Add(restoreSource); await restoreSource;
                input.Supply("ucml-review " + importPath);
                var alreadyImported = await output.NextAsync(value => value.TryGetProperty("alreadyImported", out _), token);
                Assert.Equal(original.Metadata.Id, alreadyImported.GetProperty("Id").GetGuid());
                var unchangedReview = await output.NextAsync(value => value.TryGetProperty("records", out _), token);
                Assert.Equal(0, unchangedReview.GetProperty("count").GetInt32());
            }
            input.Supply("home-requests");
            var restartPermissions = await output.NextAsync(value => value.TryGetProperty("PendingRequests", out _), token);
            Assert.Empty(restartPermissions.GetProperty("PendingRequests").EnumerateArray());
            input.Supply("quit"); quitRequested = true;
            Assert.True(await wholeDriver == 0, output.ToString()); actualRun = await launch;
            var restartedRecoveryRead = File.ReadAllTextAsync(Path.Combine(root, "startup-recovery.json"), token); sources.Add(restartedRecoveryRead);
            using var restartedRecovery = JsonDocument.Parse(await restartedRecoveryRead);
            Assert.True(restartedRecovery.RootElement.GetProperty("currentRun").GetProperty("cleanShutdown").GetBoolean());
        }
        catch (Exception cause) { Capture(failures, null, cause); }
        finally
        {
            if (launch is not null && !quitRequested)
                try { input.Supply("quit"); } catch (Exception cause) { Capture(failures, null, cause); }
            if (launch is not null)
                try { actualRun = await launch; } catch (Exception cause) { Capture(failures, launch, cause); }
            if (actualRun is not null)
                try { await actualRun; } catch (Exception cause) { Capture(failures, actualRun, cause); }
            if (wholeDriver is not null)
                try { await wholeDriver; } catch (Exception cause) { Capture(failures, wholeDriver, cause); }
            foreach (var original in sources.Concat(output.OriginalReads).Concat(input.OriginalReads))
                try { await original; } catch (Exception cause) { Capture(failures, original, cause); }
            try { Console.SetIn(previousInput); } catch (Exception cause) { Capture(failures, null, cause); }
            try { Console.SetOut(previousOutput); } catch (Exception cause) { Capture(failures, null, cause); }
            if (root is not null)
                try { await File.WriteAllTextAsync(Path.Combine(root, "actual-ucml-console.log"), output.ToString(), CancellationToken.None); }
                catch (Exception cause) { Capture(failures, null, cause); }
            try { input.Dispose(); } catch (Exception cause) { Capture(failures, null, cause); }
            try { output.Dispose(); } catch (Exception cause) { Capture(failures, null, cause); }
            ConsoleOwnership.Release(); // Retain this test's private source/export/evidence for inspection.
        }
        if (failures.Count != 0)
            throw new AggregateException($"Actual UCML console workflow failed; retained private data: {root}; files: {filesRoot}.", failures);
    }

    private static async Task<JsonElement> ReviewActualUcmlRequestAsync(ReviewedSetupInput input,
        ReviewedSetupOutput output, CancellationToken token,
        string action = HomeCanonicalAssistantMemoryWriteSource.WriteAction, string app = "assistants")
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            input.Supply("home-requests");
            var snapshot = await output.NextAsync(value => value.TryGetProperty("PendingRequests", out _), token);
            var pending = snapshot.GetProperty("PendingRequests").EnumerateArray().Where(row =>
                row.GetProperty("Scope").GetProperty("ActionName").GetString() == action).ToArray();
            if (pending.Length == 0)
            {
                if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException("The actual UCML Home write request was not published.");
                await Task.Delay(1000, token); continue;
            }
            var request = Assert.Single(pending); var id = request.GetProperty("RequestId").GetString();
            input.Supply("home-display " + id);
            var displayed = await output.NextAsync(value => value.TryGetProperty("RequestId", out var current) && current.GetString() == id, token);
            Assert.Equal(request.GetRawText(), displayed.GetRawText());
            Assert.Equal(app, displayed.GetProperty("Scope").GetProperty("TargetAppId").GetString());
            Assert.False(displayed.GetProperty("Scope").GetProperty("IncludesAllObjects").GetBoolean());
            Assert.True(displayed.GetProperty("Caller").GetProperty("IsVerified").GetBoolean());
            Assert.True(displayed.GetProperty("Policy").GetProperty("RequiresPerActionApproval").GetBoolean());
            Assert.False(displayed.GetProperty("Policy").GetProperty("HasExternalSideEffects").GetBoolean());
            Assert.Equal(JsonValueKind.Null, displayed.GetProperty("AppliedGrantId").ValueKind);
            return displayed;
        }
    }

    private static IReadOnlyList<JsonElement> ReadUcmlConsoleListedRecords(string text)
    {
        var rows = new List<JsonElement>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Array) continue;
            rows.AddRange(document.RootElement.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.Object &&
                value.TryGetProperty("Id", out _) && value.TryGetProperty("Title", out _) && value.TryGetProperty("Confidence", out _)).Select(value => value.Clone()));
        }
        return rows;
    }

    private static UcmlDocument UcmlConsoleFixtureDocument()
    {
        var now = DateTimeOffset.UtcNow;
        return new(new UcmlMetadata { SchemaVersion = 1, Id = Guid.NewGuid(), Revision = 1, Title = "Console fixture café",
            OwnerId = "fixture-owner", Scope = new("personal", "fixture-owner"), Category = "communication", Classification = "private",
            CreatedAt = now, UpdatedAt = now, Retention = "until-revoked", Confidence = .91,
            Consent = new("explicit", ["9to1.assistants"], ["personalization"], ["local-model"]),
            Sources = [new("original-user-source", "Fixture user statement", "user-message", null, now)] },
            "Exact source-backed fixture memory. Preserve this qualification and provenance.");
    }
}
