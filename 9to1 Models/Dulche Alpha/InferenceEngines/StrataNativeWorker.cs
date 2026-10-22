using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using System.Collections.Frozen;
using Haven.Application;

namespace Dulche.Runtime;

/// <summary>A trusted artifact owner supplies a held SAME model binding, not a path-as-permission fallback.</summary>
public abstract class StrataOriginalModelLease : IAsyncDisposable
{
    public abstract InferenceModelRequirements Requirements { get; }
    public abstract string OriginalCheckpointDirectory { get; }
    public abstract IReadOnlyList<int> ActualCudaDeviceIndices { get; }
    /// <summary>Held fd-derived path of the SAME single model checkpoint file (the candidate GGUF).
    /// Null when the holder does not expose exactly one held model file; candidate load must fail
    /// closed rather than scan unheld files.</summary>
    public virtual string? OriginalCheckpointFileFdPath => null;
    /// <summary>Same independently observed build metadata; absent never grants support.</summary>
    public virtual InferenceEngineSupport? OriginalBuildSupport => null;
    public abstract void DemandCurrentOriginalBinding();
    public abstract ValueTask DisposeAsync();
}
public sealed record StrataBundledWorker(string ExecutablePath, string ExpectedSha256, string? PythonInterpreterPath = null,
    string? EnginePath = null, string? EngineSha256 = null, string? EngineFdPath = null,
    IReadOnlyList<string>? HelperPaths = null, IReadOnlyList<string>? HelperSha256 = null,
    IReadOnlyList<string>? HelperFdPaths = null,
    // Held /proc/<pid>/fd/<root> directory descriptor containing the pinned helper member. The
    // helper is executed as <dir>/strata_tokenizer_helper.py so sibling imports travel through the
    // SAME held directory (never an ordinary resolved path). Produced only by the trusted
    // installation source; the host clears the child environment so ambient paths cannot override it.
    string? HelperDirectoryFdPath = null,
    // Held fd-derived /proc handle of the K-quant MMQ DSO (libstrata_kq.so) beside the engine, with
    // its pinned digest, so the candidate worker can re-verify the exact bytes before load. The
    // engine itself dlopens the DSO from its own folder (own_folder() + STRATA_KQ_LIB_NAME) only
    // when its optional gate is on; whether that ordinary sibling path resolves to this SAME held
    // descriptor is a native-boundary question managed custody does not overclaim — the package
    // closure is proven and held, but "library executed via the held descriptor" is NOT claimed.
    string? NativeLibraryFdPath = null, string? NativeLibrarySha256 = null);
public sealed record StrataBuildObservation(string OriginCommit, bool CudaBuilt, bool NcclBuilt, IReadOnlyList<string> Registrations);
public sealed record StrataGenerationObservation(long PromptTokens, long PrefillTokens, long DecodeTokens,
    double PrefillSeconds, double DecodeSeconds, long? ReusedPromptTokens, bool? IncrementalKvContinuation, bool Stopped);

/// <summary>Optional genuine held-model tokenizer identity (the canonical hash of the registered
/// tokenizer vocabulary/merges/chat template). When the held model lease does not supply one, the
/// candidate worker verifies identity from the pack's own extracted tokenizer directory and the
/// expected identity is left empty (comparison then happens only at the native helper boundary).</summary>
public interface IStrataOriginalTokenizerIdentitySource
{ string OriginalTokenizerIdentity { get; } }

/// <summary>Common actual-worker surface shared by the V1 legacy (`StrataNativeWorker`) and guarded
/// V2 candidate (`StrataCandidateWorker`) paths so the same raw provider/factory composition can
/// own either. This is a transport contract only; it never grants tools or fallback authority.</summary>
public interface IStrataNativeWorker : IAsyncDisposable
{
    InferenceModelRequirements Model { get; }
    bool OriginalStructuredToolsAvailable { get; }
    Task<OperationResult<Unit>> ObserveOriginalInitializedModelAsync(ModelIdentity sameModel, CancellationToken cancellationToken);
    Task<Haven.Core.ProviderHealthStatus> CheckOriginalHealthAsync(string providerId, CancellationToken cancellationToken);
    IAsyncEnumerable<string> StreamOriginalAsync(OllamaChatRequest request, CancellationToken cancellationToken);
    Task<string> CompleteOriginalAsync(OllamaChatRequest request, CancellationToken cancellationToken);
    Task<OllamaToolResponse> ToolsOriginalAsync(OllamaToolRequest request, CancellationToken cancellationToken);
    /// <summary>Structured-tool probe. Legacy V1 performs a real same-model correlated proposal probe;
    /// V2 candidate is text-only and fails closed (NotSupportedException) without advertising Tools.</summary>
    Task<bool> ProbeOriginalStructuredToolsAsync(CancellationToken cancellationToken);
    /// <summary>Guard against an external join into an encompassing native close/cleanup phase.</summary>
    void DemandExternalOriginalJoin();
}

/// <summary>Actual bundled native process, one retained session/model. Process termination is cancellation
/// during load/prefill; it requires reinitialization, never pretends to preserve destroyed KV state.</summary>
public sealed class StrataNativeWorker : IAsyncDisposable, IOriginalInferenceCalibrationSource, IStrataNativeWorker
{
    private const int FrameLimit = 16 * 1024 * 1024;
    private const string Origin = "015b075079c51a7aec670ee24924f920f5e7bb2b";
    private readonly object _gate = new();
    private readonly SemaphoreSlim _serial = new(1,1);
    private readonly StrataOriginalModelLease _model;
    private readonly InferenceModelRequirements _requirements;
    private readonly StrataBundledWorker _binary;
    private readonly CancellationTokenSource _retirement = new();
    private readonly List<Task> _drivers = [];
    private readonly List<Task> _raw = [];
    private readonly AsyncLocal<Phase?> _executing = new();
    [ThreadStatic] private static List<StrataNativeWorker>? _physical;
    private Process? _process;
    private Task? _processExit;
    private Task? _stderr;
    private Task? _close;
    private Task? _modelClose;
    private int _rawReservations;
    private bool _terminationRequested;
    private bool _shutdownRequested;
    private bool _sealed;
    private bool _loaded;
    private bool _invalidated;
    private InferenceCalibrationReading? _lastCalibration;
    private Task? _lastCalibrationOriginal;
    private Task? _originalToolProbe;
    private bool _toolsProven;
    public string RuntimeFingerprint=>"strata/"+Origin+"/abi1/"+_binary.ExpectedSha256.ToLowerInvariant();
    public StrataBuildObservation? Build { get; private set; }
    public StrataGenerationObservation? LastGeneration { get; private set; }
    public InferenceModelRequirements Model => _requirements;

    public Task<OperationResult<Unit>> ObserveOriginalInitializedModelAsync(ModelIdentity sameModel,CancellationToken cancellationToken)
        =>Publish(()=> {
            cancellationToken.ThrowIfCancellationRequested();
            Physical(()=> { _model.DemandCurrentOriginalBinding(); return 0; });
            Process? process; bool initialized;
            lock(_gate) { process=_process; initialized=_loaded&&!_invalidated&&!_sealed&&sameModel==Model.Model; }
            var live=initialized&&process is not null&&!process.HasExited;
            Physical(()=> { _model.DemandCurrentOriginalBinding(); return 0; });
            lock(_gate) live=live&&_loaded&&!_invalidated&&!_sealed;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(live ? OperationResult<Unit>.Success(Unit.Value)
                :OperationResult<Unit>.Failure(new(DulcheErrorCode.ModelLoadFailed,"No current initialized original native model is available.",sameModel.StableKey,true)));
        });

    public bool TryObserveOriginalCalibration(ModelIdentity sameModel,out InferenceCalibrationReading? reading)
    {
        reading=null;
        lock(_gate) if(_sealed||_invalidated||!_loaded||_lastCalibration is null||_lastCalibrationOriginal?.IsCompletedSuccessfully!=true||sameModel!=Model.Model) return false;
        if(_model is not IStrataOriginalHardwareBinding hardware) return false;
        Physical(()=> { _model.DemandCurrentOriginalBinding(); return 0; });
        var current=Physical(()=>hardware.OriginalHardwareObservation);
        lock(_gate) {
            if(_sealed||_invalidated||!_loaded||_lastCalibration is null||_lastCalibrationOriginal?.IsCompletedSuccessfully!=true
                ||sameModel!=Model.Model||_lastCalibration.Key.HardwareFingerprint!=current.Fingerprint) return false;
            reading=_lastCalibration; return true;
        }
    }
    public bool IsIssuedOriginalCalibration(InferenceCalibrationReading sameOriginal)
    {
        lock(_gate) return !_sealed&&!_invalidated&&_loaded&&ReferenceEquals(sameOriginal,_lastCalibration)
            &&_lastCalibrationOriginal?.IsCompletedSuccessfully==true;
    }

    public Task<Haven.Core.ProviderHealthStatus> CheckOriginalHealthAsync(string providerId,CancellationToken cancellationToken)
        => Publish(() => {
            cancellationToken.ThrowIfCancellationRequested();
            Physical(() => { _model.DemandCurrentOriginalBinding(); return 0; });
            var clock=Stopwatch.StartNew(); Process? process; bool healthy;
            lock(_gate) { process=_process; healthy=_loaded&&!_invalidated&&!_sealed; }
            healthy=healthy&&process is not null&&!process.HasExited;
            return Task.FromResult(new Haven.Core.ProviderHealthStatus(providerId,healthy,
                healthy ? "Actual bundled worker is alive after successful native model validation." : "Native worker is unavailable or requires reload.",clock.Elapsed,DateTimeOffset.UtcNow));
        });

    private StrataNativeWorker(StrataBundledWorker binary, StrataOriginalModelLease model)
    { _binary=binary; _model=model; var requirements=model.Requirements; _requirements=requirements with {
        RequiredFeatures=requirements.RequiredFeatures.ToFrozenSet(StringComparer.Ordinal), PreferredEngines=requirements.PreferredEngines?.ToArray() }; }
    /// <summary>The returned actual initialization driver must be owned before its publication gate opens.</summary>
    public static Task<StrataNativeWorker> CreateAfterPublicationAsync(Task originalStart, StrataBundledWorker binary,
        StrataOriginalModelLease model, CancellationToken cancellationToken)
    {
        var owner = new StrataNativeWorker(binary,model);
        return owner.Publish(async () =>
        {
            await originalStart.ConfigureAwait(false);
            try { await owner.InitializeAsync(cancellationToken).ConfigureAwait(false); return owner; }
            catch (Exception primary)
            {
                var errors = new List<Exception>(); Add(errors,primary);
                // Startup owns the object before public disclosure; cleanup omits this encompassing driver.
                await owner.CleanupResourcesAsync(errors).ConfigureAwait(false); Throw(errors); throw;
            }
        });
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            throw new NotSupportedException("This fork package currently supports Linux x64 only; other platforms require an actual port.");
        Physical(() => { _model.DemandCurrentOriginalBinding(); return 0; });
        if (Model.WeightFormat != "Safetensors" || Model.Topology != "centralized"
            || Model.ContextTokens <= 0 || Model.NativeRegistration is null)
            throw new NotSupportedException("This worker preserves the original Safetensors registration and centralized topology only.");
        // Actual file observation; protected installed-file and held checkpoint custody remain supplied by the genuine owners.
        var executable = new FileStream(_binary.ExecutablePath,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous);
        try
        {
            var hash = await Own(() => SHA256.HashDataAsync(executable,cancellationToken).AsTask()).ConfigureAwait(false);
            if (!Convert.ToHexString(hash).Equals(_binary.ExpectedSha256,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Bundled Strata executable digest mismatch.");
        }
        finally { await Own(() => executable.DisposeAsync().AsTask()).ConfigureAwait(false); }
        var start = new ProcessStartInfo(_binary.ExecutablePath) { UseShellExecute=false, RedirectStandardInput=true, RedirectStandardOutput=true,
            RedirectStandardError=true, CreateNoWindow=true };
        start.Environment.Clear(); start.Environment["LANG"]="C.UTF-8";
        var process = new Process { StartInfo=start, EnableRaisingEvents=true };
        _process=process;
        if (!Physical(process.Start)) throw new InvalidOperationException("The actual bundled worker did not start.");
        _processExit = Retain(Physical(() => process.WaitForExitAsync(CancellationToken.None)));
        _stderr = Retain(DrainStderrAsync(process.StandardError.BaseStream));
        var hello = await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
        if (hello.Type != 0) throw new InvalidDataException("Missing native worker handshake.");
        using (var input = hello.Reader())
        {
            var version=input.ReadUInt32(); var origin=Text(input); var cuda=input.ReadUInt32()!=0; var nccl=input.ReadUInt32()!=0;
            var registrations=Text(input).Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries); End(input);
            if (version!=1 || origin!=Origin) throw new InvalidDataException("Native worker ABI/source mismatch.");
            Build=new(origin,cuda,nccl,registrations);
            if (!cuda || !registrations.Contains(Model.NativeRegistration,StringComparer.Ordinal))
                throw new NotSupportedException("No actual CUDA build or exact model registration exists in this worker.");
        }
        Physical(() => { _model.DemandCurrentOriginalBinding(); return 0; });
        await WriteAsync(Command(output => {
            output.Write(1u); Text(output,_model.OriginalCheckpointDirectory); Text(output,Model.NativeRegistration);
            output.Write(checked((uint)Model.ContextTokens)); output.Write(checked((uint)_model.ActualCudaDeviceIndices.Count));
            foreach(var device in _model.ActualCudaDeviceIndices) output.Write(checked((uint)device));
        }),cancellationToken).ConfigureAwait(false);
        var loaded=await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
        if (loaded.Type!=4) throw new InvalidDataException("The original native load has no terminal response.");
        using(var input=loaded.Reader()) { NativeErrors(input); End(input); }
        Physical(() => { _model.DemandCurrentOriginalBinding(); return 0; });
        _loaded=true;
    }

    public IAsyncEnumerable<string> StreamOriginalAsync(OllamaChatRequest sameRequest, CancellationToken cancellationToken)
        => StreamAsync(sameRequest,cancellationToken);
    public Task<string> CompleteOriginalAsync(OllamaChatRequest sameRequest, CancellationToken cancellationToken)
        => Publish(async () => { var output = new StringBuilder(); await foreach(var token in StreamAsync(sameRequest,cancellationToken).ConfigureAwait(false)) output.Append(token); return output.ToString(); });

    /// <summary>Actual protected worker capability only after a successful same-worker model probe.
    /// A signed inventory alone, implementation presence or requested feature never supplies this.</summary>
    public bool OriginalStructuredToolsAvailable
    {
        get
        {
            lock (_gate) if (_sealed || _invalidated || !_loaded) return false;
            var eligible = IndependentToolInventory();
            lock (_gate) return eligible && !_sealed && !_invalidated && _loaded && _toolsProven &&
                _originalToolProbe?.IsCompletedSuccessfully == true;
        }
    }
    private bool IndependentToolInventory() => Physical(() =>
    {
        _model.DemandCurrentOriginalBinding();
        var support = _model.OriginalBuildSupport;
        var build = Build;
        return support is { Engine: InferenceEngine.Strata, Available: true, RequiresCuda: true } &&
            support.RuntimeBuild == RuntimeFingerprint && support.Features.Contains("Tools") &&
            build is { CudaBuilt: true } && support.NcclBuilt == build.NcclBuilt &&
            support.NativeRegistrations is { } registrations && registrations.Contains(Model.NativeRegistration!);
    });

    /// <summary>Harmless local model proposal probe inside this genuine approved-load owner.
    /// It neither runs the proposed function nor issues Task/Run or tool authority.</summary>
    public Task<bool> ProbeOriginalStructuredToolsAsync(CancellationToken cancellationToken) => Publish(async () =>
    {
        if (!IndependentToolInventory()) throw new NotSupportedException("No same-worker independent Tools inventory exists.");
        lock (_gate) { _toolsProven = false; _originalToolProbe = null; }
        var nonce = Guid.NewGuid().ToString("N");
        var definition = new OllamaToolDefinition("dulche_transport_probe", "Return the exact supplied nonce; the host does not execute this probe.",
            new Dictionary<string, object> { ["nonce"] = new { type = "string" } }, ["nonce"]);
        var request = new OllamaToolRequest(Model.Model.ModelId,
            [new("user", "Propose exactly one dulche_transport_probe call with nonce " + nonce + ".")],
            [definition], Haven.Core.EffortLevel.Medium, Options: new(0.7, Model.ContextTokens, 24));
        var actual = RunStructuredToolsAsync(request, cancellationToken, probe: true);
        var result = await actual.ConfigureAwait(false);
        Physical(() => { _model.DemandCurrentOriginalBinding(); return 0; });
        if (result.ToolCalls.Count != 1 || result.ToolCalls[0].Name != definition.Name ||
            result.ToolCalls[0].Arguments.Count != 1 || !result.ToolCalls[0].Arguments.TryGetValue("nonce", out var value) ||
            value.ValueKind != System.Text.Json.JsonValueKind.String || value.GetString() != nonce)
            throw new NotSupportedException("The actual model did not produce the correlated structured tool probe.");
        lock (_gate)
        {
            if (_sealed || _invalidated || !_loaded) throw new InvalidOperationException("The original model retired during its tool probe.");
            _toolsProven = true; _originalToolProbe = _executing.Value?.Original
                ?? throw new InvalidOperationException("No original probe driver is in custody.");
        }
        return true;
    });
    public Task<OllamaToolResponse> ToolsOriginalAsync(OllamaToolRequest sameRequest, CancellationToken cancellationToken)
        => RunStructuredToolsAsync(sameRequest, cancellationToken, probe: false);
    private Task<OllamaToolResponse> RunStructuredToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken, bool probe)
        => Publish(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (probe ? !IndependentToolInventory() : !OriginalStructuredToolsAvailable)
                throw new NotSupportedException("No actual same-model structured tool support has been observed.");
            var captured = Physical(() => StrataStructuredToolCodec.Capture(request));
            var output = new StringBuilder(); var terminal = new StructuredTerminal();
            await foreach (var text in StreamAsync(captured.Chat, cancellationToken, structuredTools: true,
                structuredNames: captured.MessageNames, structuredTerminal: terminal).ConfigureAwait(false)) output.Append(text);
            var result = Physical(() => StrataStructuredToolCodec.ParseResponse(captured, output.ToString(),
                terminal.Reasoning ?? throw new InvalidDataException("The actual native terminal has no loaded-registration reasoning metadata.")));
            Physical(() => { _model.DemandCurrentOriginalBinding(); return 0; });
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        });

    private sealed class StructuredTerminal { public StrataDeclaredReasoningFormat? Reasoning; }
    private async IAsyncEnumerable<string> StreamAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken,
        bool structuredTools = false, IReadOnlyList<string>? structuredNames = null, StructuredTerminal? structuredTerminal = null)
    {
        request=request with { Messages=request.Messages.Select(message=>message with { Images=message.Images?.ToArray() }).ToArray() };
        if (structuredTools && (structuredTerminal is null || structuredNames is null || structuredNames.Count != request.Messages.Count))
            throw new InvalidDataException("Structured native message names must match the frozen original history.");
        structuredNames = structuredNames?.ToArray();
        var output=Channel.CreateBounded<string>(new BoundedChannelOptions(8) { SingleWriter=true,SingleReader=true,FullMode=BoundedChannelFullMode.Wait });
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,_retirement.Token);
        var actual=Publish(async () =>
        {
            var acquired=false; var commandStarted=false; var errors=new List<Exception>();
            var decoder=new TokenDecoder();
            var structuredTokens = structuredTools ? new StringBuilder() : null;
            var structuredBytes = 0;
            CancellationTokenRegistration cancellation=default;
            try
            {
                cancellation=linked.Token.Register(static state => ((StrataNativeWorker)state!).RequestOriginalTermination(),this);
                await Own(() => _serial.WaitAsync(linked.Token)).ConfigureAwait(false); acquired=true;
                lock(_gate) if (!_loaded || _invalidated) throw new InvalidOperationException("The original native session must be reinitialized.");
                Physical(() => { _model.DemandCurrentOriginalBinding(); return 0; });
                if (request.Model!=Model.Model.ModelId || request.EnableTools || request.Messages.Any(message => message.Images?.Count>0))
                    throw new NotSupportedException("This raw provider admits the SAME text model only; tools/images require their actual supported adapter.");
                if(request.Options is { } options && options.ContextLimit>Model.ContextTokens) throw new NotSupportedException("Requested context exceeds the loaded native context.");
                // The current raw provider does not infer a model-specific reasoning budget from generic EffortLevel.
                if(request.Effort!=Haven.Core.EffortLevel.Medium) throw new NotSupportedException("A nondefault effort requires a genuine native budget mapping.");
                var messages=request.SystemPrompt is null ? request.Messages.ToArray() : new[] { new OllamaMessage("system",request.SystemPrompt) }.Concat(request.Messages).ToArray();
                var names = structuredNames is null ? null : request.SystemPrompt is null
                    ? structuredNames.ToArray() : new[] { "" }.Concat(structuredNames).ToArray();
                var command=Command(writer => {
                    writer.Write(structuredTools ? 4u : 2u);
                    if (structuredTools) writer.Write(1u); // Explicit tool-wire version, not a text flag.
                    writer.Write(checked((uint)messages.Length));
                    for (var index = 0; index < messages.Length; index++) { var message = messages[index];
                        writer.Write(Role(message.Role)); Text(writer,message.Content); Text(writer,names is null ? "" : names[index]); writer.Write(0u); }
                    writer.Write(256u); writer.Write(request.Options?.Temperature??0.7); writer.Write(1.0); writer.Write(0u); writer.Write(33377335UL); writer.Write(0u); Text(writer,"");
                });
                // Even a partial command write makes this session unusable after failure.
                commandStarted=true;
                await WriteAsync(command,linked.Token).ConfigureAwait(false);
                while(true)
                {
                    var frame=await ReadFrameAsync(linked.Token).ConfigureAwait(false);
                    using var input=frame.Reader();
                    if(frame.Type==1) {
                        _=input.ReadUInt32(); var bytes=Bytes(input); End(input);
                        var token=decoder.Feed(bytes,false);
                        if (structuredTokens is not null) {
                            structuredTokens.Append(token);
                            structuredBytes = checked(structuredBytes + Encoding.UTF8.GetByteCount(token));
                            if (structuredBytes > StrataStructuredToolCodec.MaximumBytes)
                                throw new InvalidDataException("The actual structured tool generation exceeds its finite limit.");
                        }
                        else if(token.Length!=0) await output.Writer.WriteAsync(token,linked.Token).ConfigureAwait(false);
                    }
                    else if(frame.Type==(structuredTools ? 7u : 2u))
                    {
                        if (structuredTools) {
                            if (input.ReadUInt32() != 1) throw new InvalidDataException("Unexpected structured tool terminal version.");
                            var open = Text(input); var close = Text(input); var opened = input.ReadUInt32();
                            if (opened > 1 || Encoding.UTF8.GetByteCount(open) > 4096 || Encoding.UTF8.GetByteCount(close) > 4096)
                                throw new InvalidDataException("The actual loaded registration reasoning metadata is not bounded.");
                            structuredTerminal!.Reasoning = new(open, close, opened == 1);
                        }
                        NativeErrors(input); var terminalText=Text(input); var prompt=checked((long)input.ReadUInt64()); var prefill=checked((long)input.ReadUInt64()); var decode=checked((long)input.ReadUInt64());
                        var prefillSeconds=input.ReadDouble(); var decodeSeconds=input.ReadDouble();
                        long? reused=input.ReadUInt32()==0 ? null : checked((long)input.ReadUInt64());
                        bool? kv=input.ReadUInt32()==0 ? null : input.ReadUInt32()!=0; var stopped=input.ReadUInt32()!=0; End(input);
                        if (!double.IsFinite(prefillSeconds)||!double.IsFinite(decodeSeconds)||prefillSeconds<0||decodeSeconds<0) throw new InvalidDataException("Native telemetry is invalid.");
                        // Native callback slices can split UTF8 scalars. Only the real terminal flush
                        // may establish that all streamed bytes ended at a valid scalar boundary.
                        var tail=decoder.Feed([],true);
                        if (structuredTokens is not null) {
                            structuredTokens.Append(tail);
                            if (!string.Equals(structuredTokens.ToString(), terminalText, StringComparison.Ordinal))
                                throw new InvalidDataException("The actual structured terminal does not match the native token stream.");
                            await output.Writer.WriteAsync(terminalText, linked.Token).ConfigureAwait(false);
                        }
                        else if(tail.Length!=0) await output.Writer.WriteAsync(tail,linked.Token).ConfigureAwait(false);
                        Physical(() => { _model.DemandCurrentOriginalBinding(); return 0; });
                        var measured=new StrataGenerationObservation(prompt,prefill,decode,prefillSeconds,decodeSeconds,reused,kv,stopped);
                        var hardware=_model is IStrataOriginalHardwareBinding binding ? Physical(()=>binding.OriginalHardwareObservation) : null;
                        lock(_gate) {
                            if(_sealed||_invalidated||!_loaded) throw new InvalidOperationException("The original native session retired before terminal disclosure.");
                            LastGeneration=measured;
                            _lastCalibration=null; _lastCalibrationOriginal=null;
                            if(!structuredTools&&hardware is not null&&(request.Options?.Temperature??0.7)==0.7) {
                                _lastCalibration=new(new(InferenceEngine.Strata,Model.Model,Model.ArtifactFingerprint,hardware.Fingerprint,
                                    RuntimeFingerprint,Model.ContextTokens,InferenceCalibrationCache.DefaultSettings),measured);
                                _lastCalibrationOriginal=_executing.Value?.Original;
                            }
                        }
                        break;
                    }
                    else { if(frame.Type==5) NativeErrors(input); throw new InvalidDataException("Native generation has no valid terminal protocol record."); }
                }
            }
            catch(Exception error)
            {
                Add(errors,error);
                if(commandStarted) {
                    // Do not allow a later request to consume unread frames from this failed command.
                    // Keep the same exit/stderr Tasks and every original body/termination cause.
                    try { Physical(() => { RequestOriginalTermination(); return 0; }); } catch(Exception termination) { Add(errors,termination); }
                    if(_processExit is not null) await Join(_processExit,errors).ConfigureAwait(false);
                    if(_stderr is not null) await Join(_stderr,errors).ConfigureAwait(false);
                }
            }
            finally
            {
                try { cancellation.Dispose(); } catch(Exception error) { Add(errors,error); }
                if(acquired) _serial.Release();
                output.Writer.TryComplete(errors.Count==0 ? null : errors.Count==1 ? errors[0] : new AggregateException(errors));
            }
            Throw(errors); return 0;
        });
        try { await foreach(var token in output.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) yield return token; }
        finally
        {
            var errors=new List<Exception>();
            if(!actual.IsCompleted) { try { linked.Cancel(); } catch(Exception error) { Add(errors,error); } }
            await Join(actual,errors).ConfigureAwait(false); Throw(errors);
        }
    }

    public void RequestOriginalTermination()
    {
        Process? process; lock(_gate) { _invalidated=true; process=_process; }
        if(process is not null && !process.HasExited) { lock(_gate) _terminationRequested=true; process.Kill(entireProcessTree:true); }
    }
    public ValueTask DisposeAsync()
    {
        DemandExternalJoin();
        lock(_gate)
        {
            if(_close is not null) return new(_close);
            _sealed=true; var start=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close=CloseAsync(start.Task); start.SetResult(); return new(_close);
        }
    }
    private async Task CloseAsync(Task start)
    {
        await start.ConfigureAwait(false); var errors=new List<Exception>();
        Task[] drivers; lock(_gate) drivers=_drivers.ToArray();
        var idle=drivers.All(driver=>driver.IsCompleted)&&_process is { HasExited:false };
        if(idle)
            try {
                _shutdownRequested=true;
                using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await WriteAsync(Command(output=>output.Write(3u)),deadline.Token).ConfigureAwait(false);
                var stopped=await ReadFrameAsync(deadline.Token).ConfigureAwait(false);
                if(stopped.Type!=3||stopped.Body.Length!=0) throw new InvalidDataException("Native shutdown did not acknowledge the same session.");
            } catch(Exception error) { Add(errors,error); try { RequestOriginalTermination(); } catch(Exception closeError) { Add(errors,closeError); } }
        try { _retirement.Cancel(); } catch(Exception error) { Add(errors,error); }
        if(!idle) try { RequestOriginalTermination(); } catch(Exception error) { Add(errors,error); }
        foreach(var driver in drivers) await Join(driver,errors).ConfigureAwait(false);
        await CleanupResourcesAsync(errors).ConfigureAwait(false); Throw(errors);
    }
    private async Task CleanupResourcesAsync(List<Exception> errors)
    {
        if(!_shutdownRequested) try { RequestOriginalTermination(); } catch(Exception error) { Add(errors,error); }
        if(_processExit is not null) await Join(_processExit,errors).ConfigureAwait(false);
        if(_process is { HasExited:true } process && !_terminationRequested && (!_shutdownRequested||process.ExitCode!=0))
            Add(errors,new InferenceEngineException(new(DulcheErrorCode.ProviderUnavailable,"The bundled Strata worker exited unexpectedly.",Model.Model.StableKey,true,
                Details:new Dictionary<string,string>{{"nativeExitCode",process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}})));
        if(_stderr is not null) await Join(_stderr,errors).ConfigureAwait(false);
        Task[] raw; lock(_gate) raw=_raw.ToArray(); foreach(var task in raw) await Join(task,errors).ConfigureAwait(false);
        try { _modelClose??=Physical(() => _model.DisposeAsync().AsTask()); } catch(Exception error) { Add(errors,error); }
        if(_modelClose is not null) await Join(_modelClose,errors).ConfigureAwait(false);
        try { _process?.Dispose(); } catch(Exception error) { Add(errors,error); }
    }
    private async Task DrainStderrAsync(Stream stream)
    { var buffer=new byte[4096]; while(await Own(() => stream.ReadAsync(buffer,CancellationToken.None).AsTask()).ConfigureAwait(false)!=0) { } }
    private async Task WriteAsync(byte[] bytes,CancellationToken cancellationToken)
    {
        if(bytes.Length>FrameLimit) throw new InvalidDataException("Native command exceeds the finite frame limit.");
        var stream=_process!.StandardInput.BaseStream; var length=BitConverter.GetBytes(bytes.Length);
        await Own(() => stream.WriteAsync(length,cancellationToken).AsTask()).ConfigureAwait(false);
        await Own(() => stream.WriteAsync(bytes,cancellationToken).AsTask()).ConfigureAwait(false);
        await Own(() => stream.FlushAsync(cancellationToken)).ConfigureAwait(false);
    }
    private async Task<Frame> ReadFrameAsync(CancellationToken cancellationToken)
    {
        try {
            var stream=_process!.StandardOutput.BaseStream; var header=new byte[8];
            await Own(() => stream.ReadExactlyAsync(header,cancellationToken).AsTask()).ConfigureAwait(false);
            var type=BitConverter.ToUInt32(header,0); var length=BitConverter.ToInt32(header,4);
            if(length<0||length>FrameLimit) throw new InvalidDataException("Native output exceeds the finite frame limit.");
            var body=new byte[length]; await Own(() => stream.ReadExactlyAsync(body,cancellationToken).AsTask()).ConfigureAwait(false); return new(type,body);
        } catch(EndOfStreamException cause) {
            throw new InferenceEngineException(new(DulcheErrorCode.ProviderUnavailable,
                "The actual native pipe ended before its terminal protocol record; crash/OOM status is unknown.",Model.Model.StableKey,true),cause);
        }
    }
    private static byte[] Command(Action<BinaryWriter> body)
    { using var memory=new MemoryStream(); using(var output=new BinaryWriter(memory,new UTF8Encoding(false,true),true)) body(output); if(memory.Length>FrameLimit) throw new InvalidDataException("Native command is too large."); return memory.ToArray(); }
    private static void Text(BinaryWriter output,string value)
    { var bytes=new UTF8Encoding(false,true).GetBytes(value); if(bytes.Length>FrameLimit) throw new InvalidDataException("Native text is too large."); output.Write(checked((uint)bytes.Length)); output.Write(bytes); }
    private static byte[] Bytes(BinaryReader input)
    { var length=input.ReadUInt32(); if(length>FrameLimit||length>input.BaseStream.Length-input.BaseStream.Position) throw new InvalidDataException("Truncated native text."); return input.ReadBytes(checked((int)length)); }
    private static string Text(BinaryReader input)=>new UTF8Encoding(false,true).GetString(Bytes(input));
    private sealed class TokenDecoder
    {
        private readonly Decoder _decoder=new UTF8Encoding(false,true).GetDecoder();
        private bool _terminal;
        public string Feed(byte[] bytes,bool terminal)
        {
            if(_terminal) throw new InvalidOperationException("The original token decoder is already terminal.");
            // Leave room for a buffered scalar completed by this frame; never use replacement decoding.
            var chars=new char[checked(bytes.Length+2)];
            var count=_decoder.GetChars(bytes,0,bytes.Length,chars,0,terminal);
            if(terminal) _terminal=true;
            return new string(chars,0,count);
        }
    }
    private static void End(BinaryReader input) { if(input.BaseStream.Position!=input.BaseStream.Length) throw new InvalidDataException("Native frame has an unexpected suffix."); }
    private static uint Role(string role) => role switch { "system"=>0, "user"=>1, "assistant"=>2, "tool"=>3, _=>throw new NotSupportedException("Unknown native chat role.") };
    private static void NativeErrors(BinaryReader input)
    {
        var code=input.ReadUInt32(); var count=input.ReadUInt32(); if(count>4096) throw new InvalidDataException("Too many native errors.");
        var errors=new List<string>(); for(uint index=0;index<count;index++) errors.Add(Text(input));
        if(code!=0) throw new InferenceEngineException(new(code==2 ? DulcheErrorCode.OutOfMemory : code==1 ? DulcheErrorCode.ModelLoadFailed : DulcheErrorCode.ProviderUnavailable,
            "Strata native failure: "+string.Join("; ",errors),"Strata",code==2||code==3,
            Details:new Dictionary<string,string>{{"nativeCode",code.ToString(System.Globalization.CultureInfo.InvariantCulture)}}));
        if(count!=0) throw new InvalidDataException("A successful native response contains error causes.");
    }
    private Task<T> Publish<T>(Func<Task<T>> body)
    {
        lock(_gate)
        {
            if(_sealed||_drivers.Count>=128) throw new InvalidOperationException("Native original driver admission is sealed or full.");
            var start=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<T>? actual=null;
            actual=DriveAsync(start.Task,body,()=>actual!); _drivers.Add(actual); start.SetResult(); return actual;
        }
    }
    private async Task<T> DriveAsync<T>(Task start,Func<Task<T>> body,Func<Task> samePublishedOriginal)
    {
        await start.ConfigureAwait(false); var prior=_executing.Value; var phase=new Phase(prior) { Original=samePublishedOriginal() }; _executing.Value=phase;
        try { return await body().ConfigureAwait(false); } finally { phase.Live=false; _executing.Value=prior; }
    }
    private Task Retain(Task task) { lock(_gate) { _raw.Add(task); return task; } }
    private Task<T> Retain<T>(Task<T> task) { lock(_gate) { _raw.Add(task); return task; } }
    private async Task<T> Own<T>(Func<Task<T>> factory)
    {
        ReserveRaw();
        var actual=Retain(Physical(factory));
        try { return await actual.ConfigureAwait(false); }
        catch(Exception error) { var errors=new List<Exception>(); Add(errors,error); if(actual.Exception is { } group) { foreach(var cause in group.InnerExceptions) Add(errors,cause); if(group.InnerExceptions.Count==1 && group.InnerExceptions[0] is OperationCanceledException) Add(errors,group); } Throw(errors); throw; }
    }
    private async Task Own(Func<Task> factory)
    { ReserveRaw(); var actual=Retain(Physical(factory)); var errors=new List<Exception>(); await Join(actual,errors).ConfigureAwait(false); Throw(errors); }
    private void ReserveRaw()
    { lock(_gate) if(++_rawReservations>131072) throw new InvalidOperationException("The bounded actual native source inventory is full; reinitialize after retirement."); }
    private T Physical<T>(Func<T> factory)
    {
        var values=_physical??=[]; values.Add(this);
        try { return factory(); } catch(OperationCanceledException error) { throw new AggregateException("Native source callback faulted synchronously.",error); }
        finally { values.RemoveAt(values.Count-1); }
    }
    public void DemandExternalOriginalJoin()=>DemandExternalJoin();
    private void DemandExternalJoin()
    {
        for(var phase=_executing.Value;phase is not null;phase=phase.Parent) if(phase.Live) throw new InvalidOperationException("Native original cannot join its own worker.");
        if(_physical?.Contains(this)==true) throw new InvalidOperationException("Physical native source callback cannot join its own worker.");
    }
    private static void Add(List<Exception> errors,Exception error) { if(!errors.Any(value=>ReferenceEquals(value,error))) errors.Add(error); }
    private static async Task Join(Task task,List<Exception> errors)
    { try { await task.ConfigureAwait(false); } catch(Exception error) { Add(errors,error); } if(task.Exception is { } group) { foreach(var cause in group.InnerExceptions) Add(errors,cause); if(group.InnerExceptions.Count==1 && group.InnerExceptions[0] is OperationCanceledException) Add(errors,group); } }
    private static void Throw(List<Exception> errors) { if(errors.Count==1) ExceptionDispatchInfo.Capture(errors[0]).Throw(); if(errors.Count>1) throw new AggregateException(errors); }
    private sealed class Phase(Phase? parent) { public Phase? Parent { get; }=parent; public volatile bool Live=true; public Task? Original; }
    private sealed record Frame(uint Type,byte[] Body) { public BinaryReader Reader()=>new(new MemoryStream(Body,false),new UTF8Encoding(false,true),false); }
}

/// <summary>Truthful capability-dispatch registry shared by the candidate worker and managed side
/// (Thunder pass 9). Mirrors `resolve_capability_tolerant` in `dulche_strata_candidate_worker.cpp`.
///
/// Owner-confirmed models (2026-10-21, independent official metadata) both verify to the engine's
/// registered GGUF family `qwen35moe`:
///   - ornith-ai/Ornith-1.5-122B (MIT) and InternScience/Sage-A1.5 (Apache-2.0).
/// Only EXACT verified spellings are accepted (engine-native family, HF model_type/text_config, or
/// the confirmed model identities); a verified family spelling may carry a genuine quant/format
/// suffix (e.g. "qwen35moe-Q4_K_M"). Manual no-fallback: unknown registrations are refused, never
/// silently downgraded. Tools/vision/MTP are never advertised from an architecture string or card
/// claim — the candidate relay is text-only.</summary>
public static class StrataCapabilityRegistry
{
    private static readonly string[] VerifiedSpellings =
    {
        "qwen35moe",                 // engine-native GGUF family
        "qwen3_5_moe",               // verified HF model_type (both confirmed models)
        "qwen3_5_moe_text",          // verified HF text_config.model_type
        "ornith-ai/Ornith-1.5-122B", // owner-confirmed model identity
        "InternScience/Sage-A1.5",   // owner-confirmed model identity
    };

    /// <summary>True when the registration is a verified qwen35moe-family spelling (or a verified
    /// family spelling carrying a genuine quant/format suffix). The resolved family is emitted via
    /// the out parameter.</summary>
    public static bool Resolve(string registration, out string? family)
    {
        family = null;
        if (string.IsNullOrWhiteSpace(registration)) return false;
        foreach (var spelling in VerifiedSpellings)
        {
            if (registration == spelling) { family = "qwen35moe"; return true; }
            if (registration.Length > spelling.Length && registration.StartsWith(spelling, StringComparison.Ordinal))
            {
                var next = registration[spelling.Length];
                if (next == '-' || next == '_' || next == '/') { family = "qwen35moe"; return true; }
            }
        }
        return false;
    }
}

/// <summary>Guarded CANDIDATE (v0.2.2) native worker path (Thunder pass-4).
///
/// This is the managed sibling of the candidate relay worker
/// (`dulche_strata_candidate_worker.cpp`). It speaks the V2 candidate ABI:
///
///   out hello (type 0): version u32=2, origin text, cuda u32, nccl u32, family text
///   in  load  (op 1):  directory text, registration text, context u32,
///                       expected_tokenizer_identity text, count u32, devices[count] u32
///   out loaded (type 4): NativeErrors(0,0)
///   in  text generate (op 2): message_count u32, messages{role u32, content text, name text, images u32},
///                       max_new u32, temperature f64, top_p f64, top_k u32, seed u64,
///                       stop_count u32, stops[stop_count] text, effort text
///   out token (type 1): byte_len u32 + utf-8 bytes
///   out text terminal (type 2): NativeErrors(0,0), terminalText text, prompt u64, prefill u64,
///                       decode u64, prefillSeconds f64, decodeSeconds f64, reused flag u32(+u64),
///                       kv flag u32(+u32), stopped u32
///   out failure (type 5): NativeErrors(code,count,text...)
///   in  cancel (op 5): stop the in-flight generate (forwarded as ENGINE STOP)
///   in  stop  (op 3): clean shutdown (QUIT), ack (type 3), exit 0
///
/// STATUS: AUTHORED, NOT WIRED INTO PRODUCTION COMPOSITION and NOT accepted in this source-only
/// checkpoint (no native/managed build slot, no model/GPU execution). It preserves the V1 legacy
/// worker byte-for-byte and is a self-contained sibling that can be composition-selected only after
/// the coordinator allocates the candidate build + acceptance slot. Structured tools remain
/// unavailable here with the exact dependency report (no version>=1 acceptance).
/// </summary>
public sealed class StrataCandidateWorker : IAsyncDisposable, IOriginalInferenceCalibrationSource, IStrataNativeWorker
{
    private const int FrameLimit = 16 * 1024 * 1024;
    private const string Origin = "011c9e600a93bbc9fd451c563501b5831c32bbca";
    /// <summary>Known verified candidate source identity (Niko1221/Strata v0.2.2 commit). The
    /// production factory selects the V2 worker only when the trusted installed profile's source
    /// identity equals this exact pinned commit — never a filename or loose registration guess.</summary>
    public const string VerifiedCandidateSourceCommit = "011c9e600a93bbc9fd451c563501b5831c32bbca";
    private const uint AbiVersion = 2;
    private const uint OpLoad = 1;
    private const uint OpGenerate = 2;
    private const uint OpStop = 3;
    private const uint OpStructuredGenerate = 4;
    private const uint OpCancel = 5;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _serial = new(1,1);
    private readonly StrataOriginalModelLease _model;
    private readonly InferenceModelRequirements _requirements;
    private readonly StrataBundledWorker _binary;
    private readonly CancellationTokenSource _retirement = new();
    private readonly List<Task> _drivers = [];
    private readonly List<Task> _raw = [];
    private readonly AsyncLocal<Phase?> _executing = new();
    [ThreadStatic] private static List<StrataCandidateWorker>? _physical;
    private Process? _process;
    private Task? _processExit;
    private Task? _stderr;
    private Task? _close;
    private Task? _modelClose;
    private int _rawReservations;
    private bool _terminationRequested;
    private bool _shutdownRequested;
    private bool _sealed;
    private bool _loaded;
    private bool _invalidated;
    private InferenceCalibrationReading? _lastCalibration;
    private Task? _lastCalibrationOriginal;
    public string RuntimeFingerprint=>"strata/"+Origin+"/abi2/"+_binary.ExpectedSha256.ToLowerInvariant();
    public StrataBuildObservation? Build { get; private set; }
    public StrataGenerationObservation? LastGeneration { get; private set; }
    public InferenceModelRequirements Model => _requirements;

    public Task<OperationResult<Unit>> ObserveOriginalInitializedModelAsync(ModelIdentity sameModel,CancellationToken cancellationToken)
        =>Publish(()=> {
            cancellationToken.ThrowIfCancellationRequested();
            Physical(()=> { _model.DemandCurrentOriginalBinding(); return 0; });
            Process? process; bool initialized;
            lock(_gate) { process=_process; initialized=_loaded&&!_invalidated&&!_sealed&&sameModel==Model.Model; }
            var live=initialized&&process is not null&&!process.HasExited;
            Physical(()=> { _model.DemandCurrentOriginalBinding(); return 0; });
            lock(_gate) live=live&&_loaded&&!_invalidated&&!_sealed;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(live ? OperationResult<Unit>.Success(Unit.Value)
                :OperationResult<Unit>.Failure(new(DulcheErrorCode.ModelLoadFailed,"No current initialized candidate native model is available.",sameModel.StableKey,true)));
        });

    public bool TryObserveOriginalCalibration(ModelIdentity sameModel,out InferenceCalibrationReading? reading)
    {
        reading=null;
        lock(_gate) if(_sealed||_invalidated||!_loaded||_lastCalibration is null||_lastCalibrationOriginal?.IsCompletedSuccessfully!=true||sameModel!=Model.Model) return false;
        if(_model is not IStrataOriginalHardwareBinding hardware) return false;
        Physical(()=> { _model.DemandCurrentOriginalBinding(); return 0; });
        var current=Physical(()=>hardware.OriginalHardwareObservation);
        lock(_gate) {
            if(_sealed||_invalidated||!_loaded||_lastCalibration is null||_lastCalibrationOriginal?.IsCompletedSuccessfully!=true
                ||sameModel!=Model.Model||_lastCalibration.Key.HardwareFingerprint!=current.Fingerprint) return false;
            reading=_lastCalibration; return true;
        }
    }
    public bool IsIssuedOriginalCalibration(InferenceCalibrationReading sameOriginal)
    {
        lock(_gate) return !_sealed&&!_invalidated&&_loaded&&ReferenceEquals(sameOriginal,_lastCalibration)
            &&_lastCalibrationOriginal?.IsCompletedSuccessfully==true;
    }

    public Task<Haven.Core.ProviderHealthStatus> CheckOriginalHealthAsync(string providerId,CancellationToken cancellationToken)
        => Publish(() => {
            cancellationToken.ThrowIfCancellationRequested();
            Physical(() => { _model.DemandCurrentOriginalBinding(); return 0; });
            var clock=Stopwatch.StartNew(); Process? process; bool healthy;
            lock(_gate) { process=_process; healthy=_loaded&&!_invalidated&&!_sealed; }
            healthy=healthy&&process is not null&&!process.HasExited;
            return Task.FromResult(new Haven.Core.ProviderHealthStatus(providerId,healthy,
                healthy ? "Actual candidate worker is alive after successful native model validation." : "Native worker is unavailable or requires reload.",clock.Elapsed,DateTimeOffset.UtcNow));
        });

    private StrataCandidateWorker(StrataBundledWorker binary, StrataOriginalModelLease model)
    { _binary=binary; _model=model; var requirements=model.Requirements; _requirements=requirements with {
        RequiredFeatures=requirements.RequiredFeatures.ToFrozenSet(StringComparer.Ordinal), PreferredEngines=requirements.PreferredEngines?.ToArray() }; }
    /// <summary>Bind the approved tokenizer interpreter without inheriting ambient secrets. The
    /// worker environment is deliberately cleared (never forwarding PATH or ambient secrets); we set
    /// STRATA_PYTHON only when the binary/model owner supplies a validated absolute interpreter path.
    /// Otherwise generation is refused precisely rather than silently falling back to an ambiguous
    /// PATH scan.</summary>
    private void BindApprovedPython(ProcessStartInfo start)
    {
        var approved = _binary.PythonInterpreterPath;
        if (string.IsNullOrWhiteSpace(approved))
            throw new NotSupportedException(
                "This candidate worker requires an approved python3 interpreter for its bundled "
                + "tokenizer helper; no STRATA_PYTHON path was supplied by the original worker owner. "
                + "Generation is refused (precise missing-dependency refusal).");
        if (!File.Exists(approved))
            throw new InvalidDataException(
                "Approved tokenizer interpreter path does not exist: " + approved);
        start.Environment["STRATA_PYTHON"] = Path.GetFullPath(approved);
    }

    /// <summary>Candidate HARD REQUIRES the complete held engine/helper/native-library runtime closure:
    /// the engine sibling binary, the four pinned helper text-path members, and the K-quant MMQ DSO
    /// beside the engine. Each is re-verified by digest from its held fd-derived handle before any
    /// candidate load/use. Missing/mismatched/unknown closure fails closed — never a silent V1
    /// fallback. (The engine's own dlopen of the DSO from its folder is governed by its optional gate;
    /// this verifies the HELD descriptor bytes, not that the engine executed that exact descriptor.)</summary>
    private async Task VerifyBundleClosureAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_binary.EngineFdPath) || string.IsNullOrWhiteSpace(_binary.EngineSha256) ||
            _binary.EnginePath is not { } ||
            _binary.HelperFdPaths is not { Count: 4 } helperFds || _binary.HelperSha256 is not { Count: 4 } helperDigests ||
            _binary.HelperPaths is not { Count: 4 } ||
            string.IsNullOrWhiteSpace(_binary.HelperDirectoryFdPath) ||
            string.IsNullOrWhiteSpace(_binary.NativeLibraryFdPath) || string.IsNullOrWhiteSpace(_binary.NativeLibrarySha256))
            throw new InvalidDataException(
                "The candidate worker requires its complete held engine/helper/native-library runtime closure before use; "
                + "generation/load is refused (precise missing-closure refusal).");
        await VerifyHeldFileDigestAsync(_binary.EngineFdPath, _binary.EngineSha256!, cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < helperFds.Count; i++)
            await VerifyHeldFileDigestAsync(helperFds[i], helperDigests[i], cancellationToken).ConfigureAwait(false);
        await VerifyHeldFileDigestAsync(_binary.NativeLibraryFdPath, _binary.NativeLibrarySha256!, cancellationToken).ConfigureAwait(false);
    }
    private async Task VerifyHeldFileDigestAsync(string fdPath, string expectedSha256, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(fdPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        var hash = await Own(() => SHA256.HashDataAsync(stream, cancellationToken).AsTask()).ConfigureAwait(false);
        if (!Convert.ToHexString(hash).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A held candidate bundle-closure member digest mismatch.");
    }

    /// <summary>The returned actual initialization driver must be owned before its publication gate opens.</summary>
    public static Task<StrataCandidateWorker> CreateCandidateAfterPublicationAsync(Task originalStart, StrataBundledWorker binary,
        StrataOriginalModelLease model, CancellationToken cancellationToken)
    {
        var owner = new StrataCandidateWorker(binary,model);
        return owner.Publish(async () =>
        {
            await originalStart.ConfigureAwait(false);
            try { await owner.InitializeAsync(cancellationToken).ConfigureAwait(false); return owner; }
            catch (Exception primary)
            {
                var errors = new List<Exception>(); Add(errors,primary);
                await owner.CleanupResourcesAsync(errors).ConfigureAwait(false); Throw(errors); throw;
            }
        });
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            throw new NotSupportedException("This candidate package currently supports Linux x64 only; other platforms require an actual port.");
        Physical(() => { _model.DemandCurrentOriginalBinding(); return 0; });
        // Candidate (V2) format admission is TRUTHFUL to the actual upstream engine/registry: the
        // engine is GGUF-native and its confirmed family is `qwen35moe`. The candidate worker admits
        // ONLY the engine-native GGUF registration (Safetensors V2 loading is NOT verified and is
        // never claimed) with centralized topology; unknown formats/topologies fail closed. The
        // legacy V1 worker below keeps its Safetensors-only guard unchanged.
        if (Model.WeightFormat != "GGUF" || Model.Topology != "centralized"
            || Model.ContextTokens <= 0 || Model.NativeRegistration is null)
            throw new NotSupportedException("This candidate worker admits only the engine-native GGUF (qwen35moe) registration with centralized topology; other formats/topologies are refused closed.");
        var executable = new FileStream(_binary.ExecutablePath,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous);
        try
        {
            var hash = await Own(() => SHA256.HashDataAsync(executable,cancellationToken).AsTask()).ConfigureAwait(false);
            if (!Convert.ToHexString(hash).Equals(_binary.ExpectedSha256,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Bundled Strata executable digest mismatch.");
        }
        finally { await Own(() => executable.DisposeAsync().AsTask()).ConfigureAwait(false); }
        var start = new ProcessStartInfo(_binary.ExecutablePath) { UseShellExecute=false, RedirectStandardInput=true, RedirectStandardOutput=true,
            RedirectStandardError=true, CreateNoWindow=true };
        start.Environment.Clear();
        start.Environment["LANG"]="C.UTF-8";
        BindApprovedPython(start);
        if (_binary.EnginePath is { } enginePath)
            start.Environment["STRATA_ENGINE_PATH"] = enginePath;
        // The helper runs as <held-dir>/strata_tokenizer_helper.py (fixed member). We hand the held
        // directory descriptor, never an individual script path chosen by list position. The native
        // worker resolves the fixed member and keeps sibling imports on the same held directory.
        if (_binary.HelperDirectoryFdPath is { } helperDir)
            start.Environment["STRATA_HELPER_DIR"] = helperDir;
        var modelFilePath = _model.OriginalCheckpointFileFdPath;
        if (modelFilePath is not null) start.Environment["STRATA_MODEL_PATH"] = modelFilePath;
        var process = new Process { StartInfo=start, EnableRaisingEvents=true };
        _process=process;
        if (!Physical(process.Start)) throw new InvalidOperationException("The actual candidate worker did not start.");
        _processExit = Retain(Physical(() => process.WaitForExitAsync(CancellationToken.None)));
        _stderr = Retain(DrainStderrAsync(process.StandardError.BaseStream));
        var hello = await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
        if (hello.Type != 0) throw new InvalidDataException("Missing candidate worker handshake.");
        using (var input = hello.Reader())
        {
            var version=input.ReadUInt32(); var origin=Text(input); var cuda=input.ReadUInt32()!=0; var nccl=input.ReadUInt32()!=0; var family=Text(input); End(input);
            if (version!=AbiVersion || origin!=Origin) throw new InvalidDataException("Native worker ABI/source mismatch.");
            Build=new(origin,cuda,nccl,[family]);
            if (family!="qwen35moe") throw new NotSupportedException("The candidate worker registers the qwen35moe GGUF family only.");
            if (!cuda) throw new NotSupportedException("No actual CUDA build exists in this candidate worker.");
            // Truthful capability dispatch: only verified qwen35moe-family spellings (engine-native,
            // HF model_type/text_config, or the owner-confirmed model identities) are accepted; an
            // unknown registration is refused rather than silently downgraded. Tools/vision/MTP are
            // never advertised.
            if (!StrataCapabilityRegistry.Resolve(Model.NativeRegistration!, out _))
                throw new NotSupportedException("The candidate registration is not a verified qwen35moe-family model (confirmed: Ornith-1.5-122B, Sage-A1.5).");
        }
        // Candidate HARD REQUIRES the complete held engine/helper closure before load/use. This runs
        // only after the truthful hello (so a genuine non-CUDA worker still reports ProviderUnavailable
        // instead of a missing-closure fabrication), and fails closed before OpLoad if any member is
        // missing, mismatched, or tampered.
        await VerifyBundleClosureAsync(cancellationToken).ConfigureAwait(false);
        // The engine is dispatched with the SAME held GGUF model file (no unsafe directory scan).
        if (string.IsNullOrWhiteSpace(modelFilePath))
            throw new InvalidDataException(
                "The candidate worker requires the held GGUF model file descriptor path before load; "
                + "failing closed (no unsafe scan of unheld files).");
        Physical(() => { _model.DemandCurrentOriginalBinding(); return 0; });
        await WriteAsync(Command(output => {
            output.Write(OpLoad);
            Text(output,_model.OriginalCheckpointDirectory);
            Text(output,Model.NativeRegistration!);
            output.Write(checked((uint)Model.ContextTokens));
            var expectedIdentity = _model is IStrataOriginalTokenizerIdentitySource tokens ? tokens.OriginalTokenizerIdentity : "";
            Text(output,expectedIdentity);
            output.Write(checked((uint)_model.ActualCudaDeviceIndices.Count));
            foreach(var device in _model.ActualCudaDeviceIndices) output.Write(checked((uint)device));
        }),cancellationToken).ConfigureAwait(false);
        var loaded=await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
        if (loaded.Type!=4) throw new InvalidDataException("The candidate native load has no terminal response.");
        using(var input=loaded.Reader()) { NativeErrors(input); End(input); }
        Physical(() => { _model.DemandCurrentOriginalBinding(); return 0; });
        _loaded=true;
    }

    public IAsyncEnumerable<string> StreamOriginalAsync(OllamaChatRequest sameRequest, CancellationToken cancellationToken)
        => StreamAsync(sameRequest,cancellationToken);
    public Task<string> CompleteOriginalAsync(OllamaChatRequest sameRequest, CancellationToken cancellationToken)
        => Publish(async () => { var output = new StringBuilder(); await foreach(var token in StreamAsync(sameRequest,cancellationToken).ConfigureAwait(false)) output.Append(token); return output.ToString(); });

    /// <summary>Candidate structured tools remain unavailable: a proven same-model tool probe does not
    /// exist for this registration yet, so no tool capability is ever claimed.</summary>
    public bool OriginalStructuredToolsAvailable => false;
    public Task<bool> ProbeOriginalStructuredToolsAsync(CancellationToken cancellationToken) => Publish<bool>(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException("structured tool request requires a proven same-model tool probe, which is not available for this candidate registration yet; text generation remains available.");
    });
    public Task<OllamaToolResponse> ToolsOriginalAsync(OllamaToolRequest sameRequest, CancellationToken cancellationToken)
        => Publish<OllamaToolResponse>(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotSupportedException("structured tool request requires a proven same-model tool probe, which is not available for this candidate registration yet; text generation remains available.");
        });

    private async IAsyncEnumerable<string> StreamAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        request=request with { Messages=request.Messages.Select(message=>message with { Images=message.Images?.ToArray() }).ToArray() };
        var output=Channel.CreateBounded<string>(new BoundedChannelOptions(8) { SingleWriter=true,SingleReader=true,FullMode=BoundedChannelFullMode.Wait });
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,_retirement.Token);
        var actual=Publish(async () =>
        {
            var acquired=false; var commandStarted=false; var errors=new List<Exception>();
            var decoder=new TokenDecoder();
            CancellationTokenRegistration cancellation=default;
            try
            {
                cancellation=linked.Token.Register(static state => ((StrataCandidateWorker)state!).RequestOriginalTermination(),this);
                await Own(() => _serial.WaitAsync(linked.Token)).ConfigureAwait(false); acquired=true;
                lock(_gate) if (!_loaded || _invalidated) throw new InvalidOperationException("The candidate native session must be reinitialized.");
                Physical(() => { _model.DemandCurrentOriginalBinding(); return 0; });
                if (request.Model!=Model.Model.ModelId || request.EnableTools || request.Messages.Any(message => message.Images?.Count>0))
                    throw new NotSupportedException("This candidate provider admits the SAME text model only; tools/images require their actual supported adapter.");
                if(request.Options is { } options && options.ContextLimit>Model.ContextTokens) throw new NotSupportedException("Requested context exceeds the loaded native context.");
                if (request.Effort!=Haven.Core.EffortLevel.Medium)
                    throw new NotSupportedException("A nondefault effort requires a genuine native budget mapping.");
                var messages=request.SystemPrompt is null ? request.Messages.ToArray() : new[] { new OllamaMessage("system",request.SystemPrompt) }.Concat(request.Messages).ToArray();
                var command=Command(writer => {
                    writer.Write(OpGenerate);
                    writer.Write(checked((uint)messages.Length));
                    for (var index = 0; index < messages.Length; index++) { var message = messages[index];
                        writer.Write(Role(message.Role)); Text(writer,message.Content); Text(writer,""); writer.Write(0u); }
                    writer.Write(256u); writer.Write(request.Options?.Temperature??0.7); writer.Write(1.0); writer.Write(0u); writer.Write(33377335UL);
                    writer.Write(0u); // stop_count: the candidate relay explicitly refuses stop lists
                    Text(writer,"medium");
                });
                commandStarted=true;
                await WriteAsync(command,linked.Token).ConfigureAwait(false);
                while(true)
                {
                    var frame=await ReadFrameAsync(linked.Token).ConfigureAwait(false);
                    using var input=frame.Reader();
                    if(frame.Type==1) {
                        // The candidate relay emits token frames as `[u32 byte_len][raw utf-8 bytes]`;
                        // Bytes(input) consumes exactly that producer form and End(input) rejects any
                        // trailing bytes, so no length is discarded before the read.
                        var bytes=Bytes(input); End(input);
                        var token=decoder.Feed(bytes,false);
                        if(token.Length!=0) await output.Writer.WriteAsync(token,linked.Token).ConfigureAwait(false);
                    }
                    else if(frame.Type==2)
                    {
                        NativeErrors(input); var terminalText=Text(input);
                        var prompt=checked((long)input.ReadUInt64()); var prefill=checked((long)input.ReadUInt64()); var decode=checked((long)input.ReadUInt64());
                        var prefillSeconds=input.ReadDouble(); var decodeSeconds=input.ReadDouble();
                        long? reused=input.ReadUInt32()==0 ? null : checked((long)input.ReadUInt64());
                        bool? kv=input.ReadUInt32()==0 ? null : input.ReadUInt32()!=0; var stopped=input.ReadUInt32()!=0; End(input);
                        if (!double.IsFinite(prefillSeconds)||!double.IsFinite(decodeSeconds)||prefillSeconds<0||decodeSeconds<0) throw new InvalidDataException("Native telemetry is invalid.");
                        var tail=decoder.Feed([],true);
                        if(tail.Length!=0) await output.Writer.WriteAsync(tail, linked.Token).ConfigureAwait(false);
                        Physical(() => { _model.DemandCurrentOriginalBinding(); return 0; });
                        var measured=new StrataGenerationObservation(prompt,prefill,decode,prefillSeconds,decodeSeconds,reused,kv,stopped);
                        var hardware=_model is IStrataOriginalHardwareBinding binding ? Physical(()=>binding.OriginalHardwareObservation) : null;
                        lock(_gate) {
                            if(_sealed||_invalidated||!_loaded) throw new InvalidOperationException("The candidate native session retired before terminal disclosure.");
                            LastGeneration=measured;
                            _lastCalibration=null; _lastCalibrationOriginal=null;
                            if(hardware is not null&&(request.Options?.Temperature??0.7)==0.7) {
                                _lastCalibration=new(new(InferenceEngine.Strata,Model.Model,Model.ArtifactFingerprint,hardware.Fingerprint,
                                    RuntimeFingerprint,Model.ContextTokens,InferenceCalibrationCache.DefaultSettings),measured);
                                _lastCalibrationOriginal=_executing.Value?.Original;
                            }
                        }
                        break;
                    }
                    else { if(frame.Type==5) NativeErrors(input); throw new InvalidDataException("Candidate generation has no valid terminal protocol record."); }
                }
            }
            catch(Exception error)
            {
                Add(errors,error);
                if(commandStarted) {
                    try { Physical(() => { RequestOriginalTermination(); return 0; }); } catch(Exception termination) { Add(errors,termination); }
                    if(_processExit is not null) await Join(_processExit,errors).ConfigureAwait(false);
                    if(_stderr is not null) await Join(_stderr,errors).ConfigureAwait(false);
                }
            }
            finally
            {
                try { cancellation.Dispose(); } catch(Exception error) { Add(errors,error); }
                if(acquired) _serial.Release();
                output.Writer.TryComplete(errors.Count==0 ? null : errors.Count==1 ? errors[0] : new AggregateException(errors));
            }
            Throw(errors); return 0;
        });
        try { await foreach(var token in output.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) yield return token; }
        finally
        {
            var errors=new List<Exception>();
            if(!actual.IsCompleted) { try { linked.Cancel(); } catch(Exception error) { Add(errors,error); } }
            await Join(actual,errors).ConfigureAwait(false); Throw(errors);
        }
    }

    public void RequestOriginalTermination()
    {
        Process? process; lock(_gate) { _invalidated=true; process=_process; }
        if(process is not null && !process.HasExited) { lock(_gate) _terminationRequested=true; process.Kill(entireProcessTree:true); }
    }
    public ValueTask DisposeAsync()
    {
        DemandExternalJoin();
        lock(_gate)
        {
            if(_close is not null) return new(_close);
            _sealed=true; var start=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close=CloseAsync(start.Task); start.SetResult(); return new(_close);
        }
    }
    private async Task CloseAsync(Task start)
    {
        await start.ConfigureAwait(false); var errors=new List<Exception>();
        Task[] drivers; lock(_gate) drivers=_drivers.ToArray();
        var idle=drivers.All(driver=>driver.IsCompleted)&&_process is { HasExited:false };
        if(idle)
            try {
                _shutdownRequested=true;
                using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await WriteAsync(Command(output=>output.Write(OpStop)),deadline.Token).ConfigureAwait(false);
                var stopped=await ReadFrameAsync(deadline.Token).ConfigureAwait(false);
                if(stopped.Type!=3||stopped.Body.Length!=0) throw new InvalidDataException("Native shutdown did not acknowledge the same session.");
            } catch(Exception error) { Add(errors,error); try { RequestOriginalTermination(); } catch(Exception closeError) { Add(errors,closeError); } }
        try { _retirement.Cancel(); } catch(Exception error) { Add(errors,error); }
        if(!idle) try { RequestOriginalTermination(); } catch(Exception error) { Add(errors,error); }
        foreach(var driver in drivers) await Join(driver,errors).ConfigureAwait(false);
        await CleanupResourcesAsync(errors).ConfigureAwait(false); Throw(errors);
    }
    private async Task CleanupResourcesAsync(List<Exception> errors)
    {
        if(!_shutdownRequested) try { RequestOriginalTermination(); } catch(Exception error) { Add(errors,error); }
        if(_processExit is not null) await Join(_processExit,errors).ConfigureAwait(false);
        if(_process is { HasExited:true } process && !_terminationRequested && (!_shutdownRequested||process.ExitCode!=0))
            Add(errors,new InferenceEngineException(new(DulcheErrorCode.ProviderUnavailable,"The bundled Strata worker exited unexpectedly.",Model.Model.StableKey,true,
                Details:new Dictionary<string,string>{{"nativeExitCode",process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}})));
        if(_stderr is not null) await Join(_stderr,errors).ConfigureAwait(false);
        Task[] raw; lock(_gate) raw=_raw.ToArray(); foreach(var task in raw) await Join(task,errors).ConfigureAwait(false);
        try { _modelClose??=Physical(() => _model.DisposeAsync().AsTask()); } catch(Exception error) { Add(errors,error); }
        if(_modelClose is not null) await Join(_modelClose,errors).ConfigureAwait(false);
        try { _process?.Dispose(); } catch(Exception error) { Add(errors,error); }
    }
    private async Task DrainStderrAsync(Stream stream)
    { var buffer=new byte[4096]; while(await Own(() => stream.ReadAsync(buffer,CancellationToken.None).AsTask()).ConfigureAwait(false)!=0) { } }
    private async Task WriteAsync(byte[] bytes,CancellationToken cancellationToken)
    {
        if(bytes.Length>FrameLimit) throw new InvalidDataException("Native command exceeds the finite frame limit.");
        var stream=_process!.StandardInput.BaseStream; var length=BitConverter.GetBytes(bytes.Length);
        await Own(() => stream.WriteAsync(length,cancellationToken).AsTask()).ConfigureAwait(false);
        await Own(() => stream.WriteAsync(bytes,cancellationToken).AsTask()).ConfigureAwait(false);
        await Own(() => stream.FlushAsync(cancellationToken)).ConfigureAwait(false);
    }
    private async Task<Frame> ReadFrameAsync(CancellationToken cancellationToken)
    {
        try {
            var stream=_process!.StandardOutput.BaseStream; var header=new byte[8];
            await Own(() => stream.ReadExactlyAsync(header,cancellationToken).AsTask()).ConfigureAwait(false);
            var type=BitConverter.ToUInt32(header,0); var length=BitConverter.ToInt32(header,4);
            if(length<0||length>FrameLimit) throw new InvalidDataException("Native output exceeds the finite frame limit.");
            var body=new byte[length]; await Own(() => stream.ReadExactlyAsync(body,cancellationToken).AsTask()).ConfigureAwait(false); return new(type,body);
        } catch(EndOfStreamException cause) {
            throw new InferenceEngineException(new(DulcheErrorCode.ProviderUnavailable,
                "The actual native pipe ended before its terminal protocol record; crash/OOM status is unknown.",Model.Model.StableKey,true),cause);
        }
    }
    private static byte[] Command(Action<BinaryWriter> body)
    { using var memory=new MemoryStream(); using(var output=new BinaryWriter(memory,new UTF8Encoding(false,true),true)) body(output); if(memory.Length>FrameLimit) throw new InvalidDataException("Native command is too large."); return memory.ToArray(); }
    private static void Text(BinaryWriter output,string value)
    { var bytes=new UTF8Encoding(false,true).GetBytes(value); if(bytes.Length>FrameLimit) throw new InvalidDataException("Native text is too large."); output.Write(checked((uint)bytes.Length)); output.Write(bytes); }
    private static byte[] Bytes(BinaryReader input)
    { var length=input.ReadUInt32(); if(length>FrameLimit||length>input.BaseStream.Length-input.BaseStream.Position) throw new InvalidDataException("Truncated native text."); return input.ReadBytes(checked((int)length)); }
    private static string Text(BinaryReader input)=>new UTF8Encoding(false,true).GetString(Bytes(input));
    private sealed class TokenDecoder
    {
        private readonly Decoder _decoder=new UTF8Encoding(false,true).GetDecoder();
        private bool _terminal;
        public string Feed(byte[] bytes,bool terminal)
        {
            if(_terminal) throw new InvalidOperationException("The original token decoder is already terminal.");
            var chars=new char[checked(bytes.Length+2)];
            var count=_decoder.GetChars(bytes,0,bytes.Length,chars,0,terminal);
            if(terminal) _terminal=true;
            return new string(chars,0,count);
        }
    }
    private static void End(BinaryReader input) { if(input.BaseStream.Position!=input.BaseStream.Length) throw new InvalidDataException("Native frame has an unexpected suffix."); }
    private static uint Role(string role) => role switch { "system"=>0, "user"=>1, "assistant"=>2, "tool"=>3, _=>throw new NotSupportedException("Unknown native chat role.") };
    private static void NativeErrors(BinaryReader input)
    {
        var code=input.ReadUInt32(); var count=input.ReadUInt32(); if(count>4096) throw new InvalidDataException("Too many native errors.");
        var errors=new List<string>(); for(uint index=0;index<count;index++) errors.Add(Text(input));
        if(code!=0) throw new InferenceEngineException(new(code==2 ? DulcheErrorCode.OutOfMemory : code==1 ? DulcheErrorCode.ModelLoadFailed : DulcheErrorCode.ProviderUnavailable,
            "Strata native failure: "+string.Join("; ",errors),"Strata",code==2||code==3,
            Details:new Dictionary<string,string>{{"nativeCode",code.ToString(System.Globalization.CultureInfo.InvariantCulture)}}));
        if(count!=0) throw new InvalidDataException("A successful native response contains error causes.");
    }
    private Task<T> Publish<T>(Func<Task<T>> body)
    {
        lock(_gate)
        {
            if(_sealed||_drivers.Count>=128) throw new InvalidOperationException("Native original driver admission is sealed or full.");
            var start=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<T>? actual=null;
            actual=DriveAsync(start.Task,body,()=>actual!); _drivers.Add(actual); start.SetResult(); return actual;
        }
    }
    private async Task<T> DriveAsync<T>(Task start,Func<Task<T>> body,Func<Task> samePublishedOriginal)
    {
        await start.ConfigureAwait(false); var prior=_executing.Value; var phase=new Phase(prior) { Original=samePublishedOriginal() }; _executing.Value=phase;
        try { return await body().ConfigureAwait(false); } finally { phase.Live=false; _executing.Value=prior; }
    }
    private Task Retain(Task task) { lock(_gate) { _raw.Add(task); return task; } }
    private Task<T> Retain<T>(Task<T> task) { lock(_gate) { _raw.Add(task); return task; } }
    private async Task<T> Own<T>(Func<Task<T>> factory)
    {
        ReserveRaw();
        var actual=Retain(Physical(factory));
        try { return await actual.ConfigureAwait(false); }
        catch(Exception error) { var errors=new List<Exception>(); Add(errors,error); if(actual.Exception is { } group) { foreach(var cause in group.InnerExceptions) Add(errors,cause); if(group.InnerExceptions.Count==1 && group.InnerExceptions[0] is OperationCanceledException) Add(errors,group); } Throw(errors); throw; }
    }
    private async Task Own(Func<Task> factory)
    { ReserveRaw(); var actual=Retain(Physical(factory)); var errors=new List<Exception>(); await Join(actual,errors).ConfigureAwait(false); Throw(errors); }
    private void ReserveRaw()
    { lock(_gate) if(++_rawReservations>131072) throw new InvalidOperationException("The bounded actual native source inventory is full; reinitialize after retirement."); }
    private T Physical<T>(Func<T> factory)
    {
        var values=_physical??=[]; values.Add(this);
        try { return factory(); } catch(OperationCanceledException error) { throw new AggregateException("Native source callback faulted synchronously.",error); }
        finally { values.RemoveAt(values.Count-1); }
    }
    public void DemandExternalOriginalJoin()=>DemandExternalJoin();
    private void DemandExternalJoin()
    {
        for(var phase=_executing.Value;phase is not null;phase=phase.Parent) if(phase.Live) throw new InvalidOperationException("Native original cannot join its own worker.");
        if(_physical?.Contains(this)==true) throw new InvalidOperationException("Physical native source callback cannot join its own worker.");
    }
    private static void Add(List<Exception> errors,Exception error) { if(!errors.Any(value=>ReferenceEquals(value,error))) errors.Add(error); }
    private static async Task Join(Task task,List<Exception> errors)
    { try { await task.ConfigureAwait(false); } catch(Exception error) { Add(errors,error); } if(task.Exception is { } group) { foreach(var cause in group.InnerExceptions) Add(errors,cause); if(group.InnerExceptions.Count==1 && group.InnerExceptions[0] is OperationCanceledException) Add(errors,group); } }
    private static void Throw(List<Exception> errors) { if(errors.Count==1) ExceptionDispatchInfo.Capture(errors[0]).Throw(); if(errors.Count>1) throw new AggregateException(errors); }
    private sealed class Phase(Phase? parent) { public Phase? Parent { get; }=parent; public volatile bool Live=true; public Task? Original; }
    private sealed record Frame(uint Type,byte[] Body) { public BinaryReader Reader()=>new(new MemoryStream(Body,false),new UTF8Encoding(false,true),false); }
}
