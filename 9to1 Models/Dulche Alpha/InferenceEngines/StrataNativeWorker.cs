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
    public abstract void DemandCurrentOriginalBinding();
    public abstract ValueTask DisposeAsync();
}
public sealed record StrataBundledWorker(string ExecutablePath, string ExpectedSha256);
public sealed record StrataBuildObservation(string OriginCommit, bool CudaBuilt, bool NcclBuilt, IReadOnlyList<string> Registrations);
public sealed record StrataGenerationObservation(long PromptTokens, long PrefillTokens, long DecodeTokens,
    double PrefillSeconds, double DecodeSeconds, long? ReusedPromptTokens, bool? IncrementalKvContinuation, bool Stopped);

/// <summary>Actual bundled native process, one retained session/model. Process termination is cancellation
/// during load/prefill; it requires reinitialization, never pretends to preserve destroyed KV state.</summary>
public sealed class StrataNativeWorker : IAsyncDisposable
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
                var command=Command(writer => {
                    writer.Write(2u); writer.Write(checked((uint)messages.Length));
                    foreach(var message in messages) { writer.Write(Role(message.Role)); Text(writer,message.Content); Text(writer,""); writer.Write(0u); }
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
                        if(token.Length!=0) await output.Writer.WriteAsync(token,linked.Token).ConfigureAwait(false);
                    }
                    else if(frame.Type==2)
                    {
                        NativeErrors(input); _=Text(input); var prompt=checked((long)input.ReadUInt64()); var prefill=checked((long)input.ReadUInt64()); var decode=checked((long)input.ReadUInt64());
                        var prefillSeconds=input.ReadDouble(); var decodeSeconds=input.ReadDouble();
                        long? reused=input.ReadUInt32()==0 ? null : checked((long)input.ReadUInt64());
                        bool? kv=input.ReadUInt32()==0 ? null : input.ReadUInt32()!=0; var stopped=input.ReadUInt32()!=0; End(input);
                        if (!double.IsFinite(prefillSeconds)||!double.IsFinite(decodeSeconds)||prefillSeconds<0||decodeSeconds<0) throw new InvalidDataException("Native telemetry is invalid.");
                        // Native callback slices can split UTF8 scalars. Only the real terminal flush
                        // may establish that all streamed bytes ended at a valid scalar boundary.
                        var tail=decoder.Feed([],true);
                        if(tail.Length!=0) await output.Writer.WriteAsync(tail,linked.Token).ConfigureAwait(false);
                        Physical(() => { _model.DemandCurrentOriginalBinding(); return 0; });
                        lock(_gate) {
                            if(_sealed||_invalidated||!_loaded) throw new InvalidOperationException("The original native session retired before terminal disclosure.");
                            LastGeneration=new(prompt,prefill,decode,prefillSeconds,decodeSeconds,reused,kv,stopped);
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
            var actual=DriveAsync(start.Task,body); _drivers.Add(actual); start.SetResult(); return actual;
        }
    }
    private async Task<T> DriveAsync<T>(Task start,Func<Task<T>> body)
    {
        await start.ConfigureAwait(false); var prior=_executing.Value; var phase=new Phase(prior); _executing.Value=phase;
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
    private sealed class Phase(Phase? parent) { public Phase? Parent { get; }=parent; public volatile bool Live=true; }
    private sealed record Frame(uint Type,byte[] Body) { public BinaryReader Reader()=>new(new MemoryStream(Body,false),new UTF8Encoding(false,true),false); }
}
