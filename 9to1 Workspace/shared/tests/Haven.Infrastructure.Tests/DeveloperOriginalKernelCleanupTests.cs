using System.Collections;
using System.Reflection;
using Haven.Application;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed partial class DeveloperOriginalKernelCaptureTests
{
    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Every_successful_actual_file_stream_cleanup_task_remains_on_the_same_physical_close_owner()
    {
        var rig = new Rig();
        try
        {
            File.WriteAllText(Path.Combine(rig.Project, "one.cs"), "first genuine retained stream");
            File.WriteAllText(Path.Combine(rig.Project, "two.cs"), "second genuine retained stream");
            var selection = await rig.Select();
            var capture = await rig.Source.CaptureOriginalAsync(selection, rig.Selections.Logical, rig.Reads.Admission, CancellationToken.None);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var originalFiles = ((IEnumerable)capture.GetType().GetProperty("Held", flags)!.GetValue(capture)!).Cast<object>().ToArray();
            Assert.Equal(2, originalFiles.Length);
            var originalStreams = originalFiles.Select(file => Assert.IsType<FileStream>(file.GetType().GetProperty("OriginalStream", flags)!.GetValue(file))).ToArray();
            var originalHandles = originalStreams.Select(stream => stream.SafeFileHandle).ToArray();
            Assert.All(originalHandles, handle => Assert.False(handle.IsClosed));
            var actualClose = selection.CloseAndDrainAsync(); Assert.Same(actualClose, selection.CloseAndDrainAsync());
            await actualClose.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            var cleanupTasks = Assert.IsType<List<Task>>(selection.GetType().GetField("OriginalCleanupTasks", flags)!.GetValue(selection));
            Assert.Equal(originalStreams.Length, cleanupTasks.Count);
            Assert.All(cleanupTasks, task => Assert.True(task.IsCompletedSuccessfully));
            Assert.All(originalHandles, handle => Assert.True(handle.IsClosed));
            Assert.False(rig.Source.IsIssuedOriginalCapture(capture, selection, rig.Selections.Logical));
            await rig.Source.CloseAndDrainOriginalCapturesAsync();
            Assert.Equal(originalStreams.Length, cleanupTasks.Count);
        }
        finally { await rig.DisposeAsync(); }
    }
}
