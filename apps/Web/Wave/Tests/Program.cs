using System.Security.Cryptography;
using System.Text.Json;
using NineToOne.Web.Wave;
using NineToOne.Web.Media;

// Actual owner model, PCM codec/exporter, session persistence and command routing.
// The controlled device boundary is not browser IndexedDB, playback or full donor acceptance.
if (args.Length != 1) throw new ArgumentException("Pass the real stereo 48kHz WAV fixture path.");
var source = File.ReadAllBytes(args[0]);
if (Convert.ToHexString(SHA256.HashData(source)) != "1E70E39D474CBD12D2F160A806C41CFF9D5B17E286E833F277A168DD50555CAC") throw new InvalidDataException("The reviewed WAV fixture changed.");
var checks = 0;
void Check(bool valid, string message) { checks++; if (!valid) throw new InvalidOperationException(message); }
object? Value(WaveBrowserSession session, string name) => session.TryGetValue(name, out var value) ? value : null;
var media = new ControlledWave(source);
using var session = new WaveBrowserSession(media);
session.TrySetValue("NewTitle", "Workspace regression");
Check(await session.CreateAsync(), "Actual canonical project creation succeeds.");
var projectId = session.Project!.ProjectId;
session.TrySetValue("NewTitle", "Cancelled draft name");
await session.DispatchAsync("ToggleNew", null); await session.DispatchAsync("ToggleNew", null);
Check(Equals(Value(session, "WorkspaceTitle"), "Workspace regression"), "New-project draft cannot rename the open project.");
Check(await session.ImportAsync(), "Real PCM source imports through the unchanged owner codec.");
var sourceClip = session.Project.Tracks[0].Clips.Single(); var originalHash = SHA256.HashData(File.ReadAllBytes(sourceClip.SourcePath));
Check(session.TrySetValue("Gain", "0.5") && session.HasPendingInput, "Typed processing remains pending until explicitly applied.");
await session.SeekTimelineAsync(.25);
session.SelectTimelineClip(session.Project.Tracks[0].TrackId, sourceClip.ClipId);
Check(session.HasPendingInput && Equals(Value(session, "Gain"), "0.5") && session.Position == .25, "Seek/selection preserve pending processing input.");
Check(!(await session.PrepareToCloseAsync()).Succeeded, "Close cannot drop unapplied input.");
await session.DispatchAsync("Processing", null);
Check(!session.HasPendingInput && !session.IsDirty && session.Project.Tracks[0].Clips.Single().Gain == .5, "Canonical clip gain commits exactly once and consumes input.");
session.TrySetValue("TrackGain", "0.8"); await session.DispatchAsync("Mixer", null);
Check(session.Project.Tracks[0].Gain == .8 && session.Project.Tracks[0].Clips.Single().Gain == .5, "Track gain does not overwrite clip gain.");
Check(await session.ExportAsync(true) && media.PlayCalls == 1 && media.PlayPosition == .25, "Play sends the actual current playhead and real owner-rendered audio to its device boundary.");
var releases = media.ReleaseCalls;
session.TrySetValue("Start", "0.4"); await session.DispatchAsync("Move", null);
Check(!session.HasPendingInput && media.ReleaseCalls == releases + 1 && session.Project.Tracks[0].Clips.Single().TimelineStartFrame == 19200, "Moving a loaded project retires stale playback before acknowledging new placement.");
var revision = session.Project.Revision;
Check(await session.CloseAsync() && await session.OpenAsync(projectId.ToString()), "Actual native serializer/local receipt close and reopen succeed.");
var reopened = session.Project!.Tracks[0].Clips.Single();
Check(session.Project.ProjectId == projectId && session.Project.Revision == revision && reopened.ClipId == sourceClip.ClipId && reopened.SourceReferenceId == sourceClip.SourceReferenceId && reopened.Gain == .5 && reopened.TimelineStartFrame == 19200, "Project/clip/source identities, revision, placement and gain survive reopen.");
Check(await session.UndoAsync() && session.Project.Revision == revision + 1 && session.Project.Tracks[0].Clips.Single().TimelineStartFrame == 0, "Undo restores placement without rewinding canonical revision.");
Check(await session.ExportAsync() && media.Downloads.Single().Length > 44 && media.Downloads.Single().AsSpan(0, 4).SequenceEqual("RIFF"u8), "Export delivers actual owner-rendered PCM WAV bytes.");
Check(SHA256.HashData(File.ReadAllBytes(sourceClip.SourcePath)).SequenceEqual(originalHash), "Mix/edit/history/export leave original audio bytes intact.");
session.TrySetValue("Seek", "invalid time"); await session.DispatchAsync("Seek", null);
Check(session.ErrorCode == "InvalidArgument" && session.Project.ProjectId == projectId, "Invalid seek is a typed refusal, not an uncaught action failure.");
session.TrySetValue("Gain", "broken"); await session.DispatchAsync("Processing", null);
Check(session.HasPendingInput && session.ErrorCode == "InvalidArgument", "Invalid processing retains the draft.");
await session.DispatchAsync("ResetInputs", null);
Check(!session.HasPendingInput && Equals(Value(session, "Gain"), "0.5") && (await session.PrepareToCloseAsync()).Succeeded, "Explicit restore recovers applied fields and unblocks close.");
Console.WriteLine($"PASS {checks} owner/session workflow assertions; browser storage/playback and donor acceptance NOT RUN.");

sealed class ControlledWave(byte[] source) : IWaveBrowserMedia
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public Dictionary<string, JsonElement> Saved { get; } = [];
    public List<byte[]> Downloads { get; } = [];
    public int PlayCalls, ReleaseCalls;
    public double PlayPosition;
    private static string Ok(object value) => JsonSerializer.Serialize(new { ok = true, value }, Json);
    public Task<string> InvokeAsync(string action, string argumentsJson, CancellationToken cancellationToken = default)
    {
        using var parsed = JsonDocument.Parse(argumentsJson); var args = parsed.RootElement;
        var response = action switch
        {
            "list" => Ok(Saved.Values.Select(bundle => new { projectId = bundle.GetProperty("projectId").GetString(), name = bundle.GetProperty("name").GetString(), revision = bundle.GetProperty("revision").GetInt64() }).ToArray()),
            "pick" => Ok(new { name = "tone-48000-stereo.wav", base64 = Convert.ToBase64String(source) }),
            "open" => Ok(Saved[args.GetProperty("projectId").GetString()!]),
            "commit" => Commit(args),
            "download" => Download(args),
            "play" => Play(args),
            "unload" or "pause" or "resume" or "stop" or "seek" or "loop" => Ok(new { }),
            "state" => Ok(new { position = PlayPosition, paused = false }),
            _ => throw new InvalidOperationException("Unexpected device boundary operation: " + action)
        };
        return Task.FromResult(response);
    }
    private string Commit(JsonElement args)
    {
        var bundle = args.GetProperty("bundle").Clone(); var id = bundle.GetProperty("projectId").GetString()!;
        var prior = Saved.TryGetValue(id, out var old) ? old.GetProperty("revision").GetInt64() : -1;
        if (prior != args.GetProperty("expectedRevision").GetInt64()) return JsonSerializer.Serialize(new { ok = false, code = "RevisionConflict", message = "Controlled conflicting revision." }, Json);
        Saved[id] = bundle; return Ok(new { revision = bundle.GetProperty("revision").GetInt64() });
    }
    private string Download(JsonElement args) { Downloads.Add(Convert.FromBase64String(args.GetProperty("base64").GetString()!)); return Ok(new { initiated = true }); }
    private string Play(JsonElement args) { PlayCalls++; PlayPosition = args.GetProperty("seconds").GetDouble(); return Ok(new { playing = true }); }
    public void SetDirty(bool dirty) { }
    public void Release() { ReleaseCalls++; }
}
