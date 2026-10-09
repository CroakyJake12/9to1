using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Core.Media;

namespace HavenOS.Apps.Wave;

/// <summary>Owning quick compile/work control. Every input/click uses the actual
/// native controls and SAME loader pipeline; readiness and local files below
/// are explicit fixtures, never product authentication or Files grants.</summary>
internal static partial class WaveNativeWorkflowTest
{
    private static readonly List<(object Owner, Task? Source, Exception Failure)> RetainedFailures = [];
    public static int Run()
    {
        var directory = Directory.CreateTempSubdirectory("wave-native-evidence-");
        WaveNativeWindow? window = null;
        try
        {
            CuiNativeHost.ConfigureFonts(AppBuilder.Configure<TestApplication>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })).SetupWithoutStarting();
            var source = Path.Combine(directory.FullName, "original.wav"); WriteSamples(source);
            var originalHash = Hash(source); var path = Path.Combine(directory.FullName, "editable.9to1w");
            var project = WaveProjectStore.Create("Voice", 48000, 1);
            project = WaveProjectStore.AddWavClip(project, project.Tracks[0].TrackId, source, 0);
            var hostedFixtureId = Guid.NewGuid();
            project = project with { Tracks = project.Tracks.Select(track => track with
                { Clips = track.Clips.Select(clip => clip with { SourceFileID = hostedFixtureId.ToString("D"), SourceRevisionID = "fixture-revision-1" }).ToList() }).ToList() };
            WaveProjectStore.Save(path, project, -1);
            var session = new WaveEditSession(project, (candidate, expected, token) =>
            { token.ThrowIfCancellationRequested(); WaveProjectStore.Save(path, candidate, expected); return Task.CompletedTask; });
            var actualResolver = new WaveformFixtureResolver(hostedFixtureId, source);
            var previewEngine = new SourcePreviewFixtureEngine();
            var workspace = new WaveCuiWorkspace(session, _ => true, new WaveFilesProjectService(actualResolver), originalMediaEngine: previewEngine);
            window = WaveNativeSurface.CreateWindow(workspace, new FixtureReadiness(), CuiAppearance.Dark);
            var initialization = window.InitializeAsync();
            Pump(initialization, window, "native initialization");
            Require(ReferenceEquals(initialization, window.OriginalInitialization), "The original initialization task was replaced.");
            window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            workspace.SynchronizeOriginalWaveforms();
            foreach (var originalWaveform in workspace.OriginalWaveformSources.ToArray()) Pump(originalWaveform, workspace, "actual source waveform analysis");
            var initialWaveform = workspace.ObserveOriginalWaveform(project.Tracks[0].Clips[0])
                ?? throw new InvalidDataException("The real clip has no actual analysed source waveform.");
            Require(initialWaveform.SourceStartFrame == 0 && initialWaveform.FrameCount == 1024 && initialWaveform.Channels.Count == 1 &&
                initialWaveform.Channels[0].Maximum[0] == 10000 / 32768f, "Actual original waveform does not match PCM source/channel/range.");
            Require(actualResolver.Released > 0, "The SAME actual waveform source lease did not release.");
            Find<ComboBox>(window, "WaveClipSelector").SelectedIndex = 0;
            Require(workspace.SelectedClipId == project.Tracks[0].Clips[0].ClipId, "The actual clip selector did not select the same canonical clip.");
            var releasedBeforePreview = actualResolver.Released;
            Click(window, workspace, "WaveSourcePlayButton");
            Require(previewEngine.OpenedSource?.HostedItemId == hostedFixtureId && previewEngine.OpenedSource.SourceRevisionId == "fixture-revision-1" &&
                previewEngine.Session.State == MediaPlaybackState.Playing, "Actual Play did not consume the canonical Files source and shared playback session.");
            Require(actualResolver.Released == releasedBeforePreview, "The original source lease was released while its preview still owns the audio source.");
            Click(window, workspace, "WaveSourcePauseButton");
            Require(previewEngine.Session.State == MediaPlaybackState.Paused, "Actual Pause did not reach the original playback session.");
            Find<TextBox>(window, "WavePlayheadBox").Text = "128";
            Click(window, workspace, "WaveSourceSeekButton");
            Require(previewEngine.Session.LastSeek == MediaTimebase.SamplesPerSecond(48000).At(128), "Actual source seek did not map the canonical playhead to source sample frames.");
            Click(window, workspace, "WaveSourcePlayButton");
            Require(previewEngine.OpenCount == 1, "Resume opened another source/session instead of the same original preview.");
            Click(window, workspace, "WaveSourceStopButton");
            Require(previewEngine.Session.CloseCalls == 1 && actualResolver.Released == releasedBeforePreview + 1,
                "Actual Stop did not retire the SAME native session before releasing its Files lease.");
            Find<TextBox>(window, "WaveClipStartBox").Text = "100";
            Click(window, workspace, "WaveEndButton");
            Require(Find<TextBox>(window, "WaveClipStartBox").Text == "100", "Unrelated seek erased a pending move field.");
            var originalEnvelope = workspace.ObserveOriginalWaveform(workspace.Project.Tracks[0].Clips[0]);
            var originalSourceReads = actualResolver.ReadCalls;
            Click(window, workspace, "WaveMoveButton");
            Require(ReferenceEquals(originalEnvelope, workspace.ObserveOriginalWaveform(workspace.Project.Tracks[0].Clips[0])) &&
                actualResolver.ReadCalls == originalSourceReads, "Timeline placement refetched or replaced unchanged source waveform data.");
            Require(workspace.Project.Tracks[0].Clips[0].TimelineStartFrame == 100, "Actual Move did not modify canonical clip placement.");
            Find<TextBox>(window, "WavePlayheadBox").Text = "612";
            Click(window, workspace, "WaveSplitButton");
            foreach (var originalWaveform in workspace.OriginalWaveformSources.ToArray()) Pump(originalWaveform, workspace, "split source-range waveform");
            var splitWaveform = workspace.ObserveOriginalWaveform(workspace.Project.Tracks[0].Clips[1]);
            Require(splitWaveform is { SourceStartFrame: 512, FrameCount: 512 }, "Split did not rebind the actual waveform to its canonical source range.");
            Require(workspace.Project.Tracks[0].Clips.Count == 2 && workspace.Project.Tracks[0].Clips[0].FrameCount == 512,
                "Actual Split did not preserve the intended source ranges.");
            Click(window, workspace, "WaveUndoButton");
            Require(workspace.Project.Tracks[0].Clips.Single().FrameCount == 1024, "Actual Undo did not restore the original clip range.");
            Find<TextBox>(window, "WaveClipGainBox").Text = "0.5";
            Click(window, workspace, "WaveProcessingButton");
            Find<TextBox>(window, "WaveTrackGainBox").Text = "0.5";
            Click(window, workspace, "WaveMixerButton");
            Require(workspace.Project.Tracks[0].Gain == .5 && workspace.Project.Tracks[0].Clips[0].Gain == .5,
                "Actual mixer/processing actions did not retain native gain settings.");
            Find<TextBox>(window, "WaveMarkerNameBox").Text = "Edit point";
            Click(window, workspace, "WaveAddMarkerButton");
            Require(workspace.Project.Markers.Single().Frame == 612, "Native marker did not bind the actual playhead.");
            Click(window, workspace, "WaveSaveButton");
            var reopened = WaveProjectStore.Open(path);
            Require(reopened.ProjectId == project.ProjectId && reopened.Tracks[0].TrackId == project.Tracks[0].TrackId,
                "Save/reopen replaced the original project/track identity.");
            var output = Path.Combine(directory.FullName, "rendered.wav");
            Require(WaveProjectExporter.ExportPcm16(reopened, output) == 1124, "The actual PCM renderer lost timeline silence or duration.");
            using (var stream = File.OpenRead(output))
            using (var reader = new BinaryReader(stream))
            {
                stream.Position = 44; Require(reader.ReadInt16() == 0, "Mix did not preserve timeline lead-in silence.");
                stream.Position = 44 + 100 * 2; Require(reader.ReadInt16() == 2500, "Stored native clip/track gain did not affect real exported PCM.");
            }
            Require(Hash(source) == originalHash, "Native edits or rendering changed the original audio bytes.");
            window.UpdateLayout();
            using (var frame = window.CaptureRenderedFrame() ?? throw new InvalidDataException("The actual native Wave window produced no frame."))
                frame.Save(Path.Combine(directory.FullName, "wave-native-dark-desktop.png"));
            window.Width = 430; window.Height = 860; window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var timeline = Find<Control>(window, "WaveTimeline");
            Require(timeline.Bounds.Width > 0 && timeline.Bounds.Width <= 430, "The actual compact timeline did not fit the native window.");
            using (var frame = window.CaptureRenderedFrame() ?? throw new InvalidDataException("The actual compact Wave window produced no frame."))
                frame.Save(Path.Combine(directory.FullName, "wave-native-dark-compact.png"));
            window.Close(); Require(window.OriginalClose is not null, "The actual native close source was not retained.");
            var close = window.OriginalClose!; Pump(close, window, "native retirement");
            Require(ReferenceEquals(close, window.OriginalClose) && close.IsCompletedSuccessfully, "Retirement replaced or failed its actual close.");
            Require(session.OriginalPublications.All(task => task.IsCompletedSuccessfully), "Actual publication sources did not settle successfully.");
            CheckFailedPublisherCustody(project);
            CheckExternalTaskCustody(project);
            CheckLocalValidationDecline(project);
            CheckOriginalSourceAuditionLifetime(project, hostedFixtureId, source);
            Console.WriteLine("Wave native actual source/channel/range waveform, original source play/pause/seek/stop lifetime, CUI select/move/split/Undo/gain/mixer/marker/save/reopen/PCM and source preservation passed.");
            Console.WriteLine("Retained native evidence: " + directory.FullName);
            return 0;
        }
        catch (Exception error)
        {
            if (window is not null) RetainedFailures.Add((window, window.OriginalClose ?? window.OriginalInitialization, error));
            Console.Error.WriteLine("Wave native workflow failed; source and unfinished owners retained: " + directory.FullName);
            Console.Error.WriteLine(error); return 1;
        }
    }
    private static void CheckFailedPublisherCustody(WaveProject project)
    {
        var publication = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new WaveEditSession(project, (_, _, _) => publication.Task);
        var workspace = new WaveCuiWorkspace(session, _ => true);
        var save = session.SaveAsync(CancellationToken.None);
        var fault = new IOException("fixture actual publisher refusal"); publication.SetException(fault);
        try { Pump(save, session, "expected failed actual publisher"); throw new InvalidDataException("The failed original save was converted to success."); }
        catch (IOException error) when (ReferenceEquals(error, fault)) { }
        Require(session.OriginalSaves.Contains(save) && session.OriginalPublications.Contains(publication.Task), "Actual failed save/publication references were discarded.");
        var close = workspace.DisposeAsync().AsTask();
        try { Pump(close, workspace, "expected failed owner retirement"); throw new InvalidDataException("The failed original publisher was waived at retirement."); }
        catch (AggregateException) { }
        Require(ReferenceEquals(close, workspace.OriginalClose) && !close.IsCompletedSuccessfully, "Failed retirement lost its original source.");
        var repeated = workspace.DisposeAsync().AsTask(); Require(ReferenceEquals(close, repeated), "Failed close was retried with another source.");
        RetainedFailures.Add((workspace, close, fault));
    }
    private static void CheckExternalTaskCustody(WaveProject project)
    {
        foreach (var fault in new Exception[] { new ArgumentException("actual picker failure"),
            new NotSupportedException("actual picker failure"), new KeyNotFoundException("actual picker failure") })
        {
            var picker = new TaskCompletionSource<(string FileId, string? Revision)?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resolver = new UnreachedResolver();
            var session = new WaveEditSession(project, (_, _, _) => Task.CompletedTask);
            var workspace = new WaveCuiWorkspace(session, _ => true, new WaveFilesProjectService(resolver), _ => picker.Task);
            var original = workspace.DispatchAsync("9to1.Wave.Import", null).AsTask();
            picker.SetException(fault);
            try { Pump(original, workspace, "expected original picker failure"); throw new InvalidDataException("An external picker fault became a successful UI decline."); }
            catch (AggregateException error) when (error.Flatten().InnerExceptions.Any(cause => ReferenceEquals(cause, fault))) { }
            Require(ReferenceEquals(original, workspace.OriginalCommand) && workspace.OriginalExternalSources.Contains(picker.Task),
                "The SAME original command/picker fault source was lost.");
            Require(session.Project.Revision == project.Revision && !resolver.WasCalled, "A failed picker reached an import or changed the native project.");
            var close = workspace.DisposeAsync().AsTask();
            try { Pump(close, workspace, "expected external source retirement failure"); throw new InvalidDataException("The original external source was waived at retirement."); }
            catch (AggregateException error) when (error.Flatten().InnerExceptions.Any(cause => ReferenceEquals(cause, fault))) { }
            Require(ReferenceEquals(close, workspace.OriginalClose) && ReferenceEquals(close, workspace.DisposeAsync().AsTask()),
                "Retirement replaced the SAME failed original close.");
            RetainedFailures.Add((workspace, close, fault));
        }
    }
    private static void CheckLocalValidationDecline(WaveProject project)
    {
        var session = new WaveEditSession(project, (_, _, _) => Task.CompletedTask);
        var workspace = new WaveCuiWorkspace(session, _ => true);
        Require(workspace.TrySetValue("ClipIndex", 0) && workspace.TrySetValue("ClipStart", "-1"), "The real local draft was not accepted.");
        var original = workspace.DispatchAsync("9to1.Wave.Move", null).AsTask();
        Pump(original, workspace, "local pre-effect validation decline");
        Require(original.IsCompletedSuccessfully && session.Project.Revision == project.Revision &&
            workspace.OriginalExternalSources.Count == 0, "A local invalid frame entered publication or poisoned retirement.");
        Pump(workspace.DisposeAsync().AsTask(), workspace, "local validation owner retirement");
    }
    // Explicit local fixture lease, not product Files authority.
    private sealed class WaveformFixtureResolver(Guid hostedId, string source) : IMediaAssetSourceResolver
    {
        public int Released { get; private set; }
        public int ReadCalls { get; private set; }
        public Task<MediaEngineResult<MediaAssetReadLease>> ResolveAsync(string fileID, MediaAssetId assetID,
            string? expectedRevision, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (fileID != hostedId.ToString("D") || expectedRevision != "fixture-revision-1") throw new InvalidDataException("Foreign waveform fixture identity.");
            ReadCalls++;
            var lease = new MediaAssetReadLease(new(assetID, hostedId, new Uri(source), expectedRevision),
                () => { Released++; return ValueTask.CompletedTask; });
            return Task.FromResult(MediaEngineResult<MediaAssetReadLease>.Success(lease));
        }
    }
    private sealed class UnreachedResolver : IMediaAssetSourceResolver
    {
        public bool WasCalled { get; private set; }
        public Task<MediaEngineResult<MediaAssetReadLease>> ResolveAsync(string fileID, MediaAssetId assetID,
            string? expectedRevision, CancellationToken cancellationToken = default)
        { WasCalled = true; throw new InvalidOperationException("A failed picker must not reach the actual Files source resolver."); }
    }
    private static void Click(WaveNativeWindow window, WaveCuiWorkspace workspace, string id)
    {
        var button = Find<Button>(window, id); Require(button.IsEnabled, "Actual action is disabled: " + id);
        var loader = (CuiControlLoader?)typeof(CuiSceneHost).GetField("_contentLoader", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window.SceneHost)
            ?? throw new InvalidDataException("The SAME actual Wave scene loader is absent.");
        var observed = loader.Inspect(button) ?? throw new InvalidDataException("The actual native action has no loader diagnostic receipt: " + id);
        Require(observed.DispatcherConnected && observed.ActionsWired, "The actual native action is not wired: " + id);
        var prior = workspace.OriginalCommand;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var originalPipeline = loader.WhenActionsIdleAsync(); Pump(originalPipeline, window, "native loader action " + id);
        var actual = workspace.OriginalCommand ?? throw new InvalidDataException("The actual CUI click admitted no Wave command: " + id);
        Require(!ReferenceEquals(prior, actual), "CUI click re-used an earlier raw command: " + id);
        Pump(actual, workspace, "native app command " + id); Require(ReferenceEquals(actual, workspace.OriginalCommand), "The native command source was replaced.");
    }
    private static void Pump(Task original, object owner, string stage)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!original.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(30))
            {
                var error = new TimeoutException("Original source remains unfinished at " + stage + ".");
                RetainedFailures.Add((owner, original, error)); throw error;
            }
        }
        original.GetAwaiter().GetResult();
    }
    private static T Find<T>(WaveNativeWindow window, string id) where T : Control => window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == id);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static string Hash(string path) { using var source = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(source)); }
    private static void WriteSamples(string path)
    {
        using var writer = new BinaryWriter(File.Create(path)); writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + 2048);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)1);
        writer.Write(48000); writer.Write(96000); writer.Write((ushort)2); writer.Write((ushort)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(2048);
        for (var index = 0; index < 1024; index++) writer.Write((short)10000);
    }
    private sealed class TestApplication : Application
    { public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this, "Imagine", CuiAppearance.Dark); }
    private sealed class FixtureReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "fixture-only", "Explicit native UI fixture; no Home authentication")); }
    }
}
