using System.Diagnostics;

namespace HavenOS.Files.CUI.Tests;

internal static class Program
{
	private static async Task<int> Main(string[] args)
	{
		try
		{
            if (args.Length > 0) return await FilesInterprocessTests.RunChildAsync(args);
			await FilesDomainContractTests.RunAllAsync().ConfigureAwait(false);
            await FilesInterprocessTests.RunAsync();
			Console.WriteLine("Files CUI domain contract checks passed, including actual interprocess lease/CAS/cancellation/recovery.");
			return 0;
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(exception);
			return 1;
		}
	}
}

internal static class FilesInterprocessTests
{
    public static async Task<int> RunChildAsync(string[] args)
    {
        if (args.Length < 3 || args[0] != "--files-lock-fixture") return 2;
        var path = args[2];
        if (args[1] == "hold")
        {
            var store = new VersionedJsonStateStore<DurableDriveProvider.State>(path, 1, () => throw new InvalidOperationException());
            await store.UpdateAsync(state =>
            {
                Console.WriteLine("HELD"); Console.Out.Flush();
                if (Console.ReadLine() != "release") throw new InvalidOperationException("Fixture lease was not released.");
                return state;
            });
            return 0;
        }
        if (args[1] != "rename" || args.Length != 7) return 2;
        var provider = new DurableDriveProvider(path, new(Guid.Parse(args[3])), "owner");
        var now = DateTimeOffset.UtcNow;
        Console.WriteLine("STARTED"); Console.Out.Flush();
        var result = await provider.MutateAsync(new FilesOperation(new(Guid.NewGuid()), "owner", new(Guid.Parse(args[4])),
            null, null, "Rename", new(Guid.Parse(args[5])), null, FilesOperationState.Pending, now, now, null, null), args[6], default);
        Console.WriteLine(result.IsSuccess ? "COMMITTED" : result.Error!.Code.ToString());
        return result.IsSuccess || result.Error?.Code == FilesErrorCode.RevisionConflict ? 0 : 3;
    }

    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "astra-files-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var children = new List<Process>();
        try
        {
            var path = Path.Combine(directory, "drive.json");
            var lockPath = path + ".lock";
            await File.WriteAllTextAsync(lockPath, "persistent lease sentinel");
            var location = new FilesLocationId(Guid.NewGuid());
            var provider = new DurableDriveProvider(path, location, "owner");
            var raw = new FilesUploadedContent(HostedItemId.New(), null, "original.bin", "application/octet-stream",
                new(Guid.NewGuid()), null, "owner", DateTimeOffset.UtcNow, 1, new string('a', 64), "original.bin");
            Check.True((await provider.CommitUploadedContentAsync(raw)).IsSuccess);
            var original = await File.ReadAllBytesAsync(path);
            Process Start(params string[] values)
            {
                var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The fixture executable path is unavailable.");
                var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardInput = true,
                    RedirectStandardOutput = true, RedirectStandardError = true };
                if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                    info.ArgumentList.Add(typeof(Program).Assembly.Location);
                info.ArgumentList.Add("--files-lock-fixture");
                foreach (var value in values) info.ArgumentList.Add(value);
                var child = Process.Start(info) ?? throw new InvalidOperationException("Could not start the actual Files fixture process.");
                children.Add(child); return child;
            }
            static async Task<string?> Line(Process process) =>
                await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            static async Task Finished(Process process)
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                if (process.ExitCode != 0) throw new InvalidOperationException(await process.StandardError.ReadToEndAsync());
            }
            var holder = Start("hold", path);
            Check.Equal("HELD", await Line(holder));
            using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200)))
                await Check.ThrowsAsync<OperationCanceledException>(() => provider.GetAsync(raw.FileId, cancellation.Token));
            Check.True(Enumerable.SequenceEqual(original, await File.ReadAllBytesAsync(path)));
            var first = Start("rename", path, location.Value.ToString(), raw.FileId.Value.ToString(), raw.RevisionId.Value.ToString(), "first.bin");
            var second = Start("rename", path, location.Value.ToString(), raw.FileId.Value.ToString(), raw.RevisionId.Value.ToString(), "second.bin");
            Check.Equal("STARTED", await Line(first)); Check.Equal("STARTED", await Line(second));
            await holder.StandardInput.WriteLineAsync("release"); await holder.StandardInput.FlushAsync();
            await Finished(holder); await Finished(first); await Finished(second);
            var outcomes = new[] { await Line(first), await Line(second) };
            Check.Equal(1, outcomes.Count(value => value == "COMMITTED"));
            Check.Equal(1, outcomes.Count(value => value == nameof(FilesErrorCode.RevisionConflict)));
            var stateStore = new VersionedJsonStateStore<DurableDriveProvider.State>(path, 1, () => throw new InvalidOperationException());
            var state = await stateStore.ReadAsync();
            Check.Equal(1, state.Items.Count); Check.Equal(1, state.Revisions.Count); Check.Equal(2, state.Events.Count);
            Check.Equal(raw.RevisionId, state.Revisions[0].Id);
            Check.True(state.Items[0].Metadata.CurrentRevisionId != raw.RevisionId);
            Check.Equal(1, state.Operations.Count); Check.Equal(1, state.UploadedContents.Count);
            Check.True(state.Items[0].Metadata.Name is "first.bin" or "second.bin");
            var crashHolder = Start("hold", path);
            Check.Equal("HELD", await Line(crashHolder));
            crashHolder.Kill(entireProcessTree: true);
            await crashHolder.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            using (var recovered = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                Check.True((await provider.GetAsync(raw.FileId, recovered.Token)).IsSuccess);
            Check.Equal("persistent lease sentinel", await File.ReadAllTextAsync(lockPath));
        }
        finally
        {
            foreach (var child in children)
            {
                if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
                child.Dispose();
            }
            Directory.Delete(directory, true);
        }
    }
}
