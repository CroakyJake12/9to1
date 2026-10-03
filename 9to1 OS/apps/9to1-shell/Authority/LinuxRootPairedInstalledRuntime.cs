using System.Net.Sockets;
using System.Runtime.Versioning;
using HavenOS.Home.Core;
namespace NineToOne.Os.Shell.Authority;

// Explicit isolated/administrator composition. Signing, protected installation and
// target-user setup precede this entry. No public tuple or locator issues authority.
[SupportedOSPlatform("linux")]
internal sealed class LinuxRootPairedInstalledRuntime : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime;
    private readonly CancellationTokenSource _ownerChannelLifetime;
    private readonly string _directory;
    private readonly List<Socket> _sockets = new();
    private readonly List<Task> _channels = new();
    private LinuxRootSupervisedHome? _home;
    private Socket? _canonicalSocket;
    private Socket? _widgetObservationSocket;
    private LinuxRootSupervisedInstalledWidgetOwner? _owner;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;
    private Task? _ownerRetireTask;
    private bool _ownerRetireUsesNativeHelper;
    private LinuxRootPairedInstalledRuntime(string directory, CancellationToken ct)
    { _directory = directory; _lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
      _ownerChannelLifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); }
    internal LinuxRootSupervisedHome OriginalHome => _home ?? throw new InvalidOperationException("Original Home not admitted.");
    internal LinuxRootSupervisedInstalledWidgetOwner OriginalOwner => _owner ?? throw new InvalidOperationException("Original owner not admitted.");
    internal Task OwnerChannel => _channels[1];
    internal Socket OriginalCanonicalTupleSocket => _canonicalSocket ?? throw new InvalidOperationException("Original Home channel not admitted.");
    internal Socket OriginalWidgetObservationSocket => _widgetObservationSocket ?? throw new InvalidOperationException("Original widget observation channel not admitted.");
    internal static async Task<LinuxRootPairedInstalledRuntime> StartAsync(uint uid, uint gid,
        string actualUserHome, string protectedRuntimeDirectory, CancellationToken ct)
    {
        if (await new OperatingSystemPrincipalSource().GetPrincipalAsync(ct) != "unix-euid:0" ||
            !Path.IsPathFullyQualified(protectedRuntimeDirectory) ||
            Path.GetFullPath(protectedRuntimeDirectory) != protectedRuntimeDirectory ||
            !LinuxRootOwnedFiles.DirectoryImmutable(protectedRuntimeDirectory))
            throw new UnauthorizedAccessException("Actual administrator protected runtime required.");
        var homePreparation = await LinuxRootHomeStartPreparation.ReadForAdministratorAsync(ct)
            ?? throw new UnauthorizedAccessException("Actual signed Home installation required.");
        var ownerPreparation = await LinuxRootInstalledWidgetOwnerStartPreparation.ReadForAdministratorAsync(ct)
            ?? throw new UnauthorizedAccessException("Actual independent signed widget owner installation required.");
        var directory = Path.Combine(protectedRuntimeDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        var result = new LinuxRootPairedInstalledRuntime(directory, ct);
        try
        {
            if (!LinuxRootOwnedFiles.DirectoryImmutable(directory)) throw new UnauthorizedAccessException("Original private root runtime unavailable.");
            var homePath = Path.Combine(directory, "home.sock");
            var canonicalPath = Path.Combine(directory, "canonical.sock");
            var ownerPath = Path.Combine(directory, "owner.sock");
            var widgetsPath = Path.Combine(directory, "widgets.sock");
            var homeListener = result.Listen(homePath);
            var canonicalListener = result.Listen(canonicalPath);
            var ownerListener = result.Listen(ownerPath);
            var widgetsListener = result.Listen(widgetsPath);
            result._home = await LinuxRootSupervisedHome.StartWithOriginalObservationChannelsPreparedAsync(homePreparation,
                homeListener, homePath, uid, gid, actualUserHome, canonicalPath, widgetsPath, result._lifetime.Token)
                ?? throw new UnauthorizedAccessException("Actual original Home child admission refused.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(result._lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            var canonical = await canonicalListener.AcceptAsync(deadline.Token);
            result._sockets.Add(canonical); result._canonicalSocket = canonical;
            var route = await LinuxRootOriginalHomeCanonicalReader.ReadOriginalWidgetRouteAsync(canonical,
                result._home, deadline.Token) ?? throw new UnauthorizedAccessException("Actual original Home widget routing observation unavailable.");
            var widgetObservation = await widgetsListener.AcceptAsync(deadline.Token);
            result._sockets.Add(widgetObservation);
            var originalHomeContext = result._home.ObserveOriginalContext();
            if (HomeNativePeerObservation.FromAcceptedUnixSocket(widgetObservation) != originalHomeContext.OriginalHostPeer ||
                !await result._home.IsOriginalIssuedContextCurrentAsync(originalHomeContext, deadline.Token))
                throw new UnauthorizedAccessException("Exact original admitted Home widget observation peer required.");
            result._widgetObservationSocket = widgetObservation;
            result._owner = await LinuxRootSupervisedInstalledWidgetOwner.StartPreparedAsync(ownerPreparation,
                result._home, ownerListener, ownerPath, route.SocketPath, uid, gid, actualUserHome, deadline.Token)
                ?? throw new UnauthorizedAccessException("Actual independent owner child admission refused.");
            // Begin authority acceptance before canonical protected tuple reads: those
            // resource checks still require genuine launch authority from this root issuer.
            result._channels.Add(result.ServeOriginalHomeAuthorityAsync(homeListener));
            result._channels.Add(LinuxAdministratorOriginalOwnerChannel.ServeAsync(result._owner,
                result._home, canonical, homePreparation, ownerPreparation, result._ownerChannelLifetime.Token));
            return result;
        }
        catch (Exception admissionFailure)
        {
            try { await result.DisposeAsync(); }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Paired original admission and exact cleanup both failed.", admissionFailure, cleanupFailure);
            }
            throw;
        }
    }
    internal static async Task<LinuxRootPairedInstalledRuntime> StartNativeAsync(uint uid, uint gid,
        string actualUserHome, string protectedRuntimeDirectory, CancellationToken ct)
    {
        if (await new OperatingSystemPrincipalSource().GetPrincipalAsync(ct) != "unix-euid:0" ||
            !Path.IsPathFullyQualified(protectedRuntimeDirectory) ||
            Path.GetFullPath(protectedRuntimeDirectory) != protectedRuntimeDirectory ||
            !LinuxRootOwnedFiles.DirectoryImmutable(protectedRuntimeDirectory))
            throw new UnauthorizedAccessException("Actual administrator protected runtime required.");
        var helperPreparation = await LinuxRootAtomicSpawnHelperPreparation.ReadForAdministratorAsync(ct)
            ?? throw new UnauthorizedAccessException("Actual signed primary native helper installation required.");
        var homePreparation = await LinuxRootHomeStartPreparation.ReadForAdministratorAsync(ct)
            ?? throw new UnauthorizedAccessException("Actual signed Home installation required.");
        var ownerPreparation = await LinuxRootInstalledWidgetOwnerStartPreparation.ReadForAdministratorAsync(ct)
            ?? throw new UnauthorizedAccessException("Actual independent signed widget owner installation required.");
        if (!await helperPreparation.IsCurrentForAdministratorAsync(ct) ||
            !await homePreparation.IsCurrentForAdministratorAsync(ct) ||
            !await ownerPreparation.IsCurrentForAdministratorAsync(ct))
            throw new UnauthorizedAccessException("All three original signed preparations must share the current protected policy.");
        var directory = Path.Combine(protectedRuntimeDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        var result = new LinuxRootPairedInstalledRuntime(directory, ct);
        try
        {
            if (!LinuxRootOwnedFiles.DirectoryImmutable(directory)) throw new UnauthorizedAccessException("Original private root runtime unavailable.");
            var homePath = Path.Combine(directory, "home.sock");
            var canonicalPath = Path.Combine(directory, "canonical.sock");
            var ownerPath = Path.Combine(directory, "owner.sock");
            var widgetsPath = Path.Combine(directory, "widgets.sock");
            var homeListener = result.Listen(homePath);
            var canonicalListener = result.Listen(canonicalPath);
            var ownerListener = result.Listen(ownerPath);
            var widgetsListener = result.Listen(widgetsPath);
            result._home = await LinuxRootSupervisedHome.StartNativeWithOriginalObservationChannelsPreparedAsync(helperPreparation,
                Path.Combine(directory, "native-home.sock"), homePreparation,
                homeListener, homePath, uid, gid, actualUserHome, canonicalPath, widgetsPath, result._lifetime.Token)
                ?? throw new UnauthorizedAccessException("Actual original Home child admission refused.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(result._lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            var canonical = await canonicalListener.AcceptAsync(deadline.Token);
            result._sockets.Add(canonical); result._canonicalSocket = canonical;
            var route = await LinuxRootOriginalHomeCanonicalReader.ReadOriginalWidgetRouteAsync(canonical,
                result._home, deadline.Token) ?? throw new UnauthorizedAccessException("Actual original Home widget routing observation unavailable.");
            var widgetObservation = await widgetsListener.AcceptAsync(deadline.Token);
            result._sockets.Add(widgetObservation);
            var originalHomeContext = result._home.ObserveOriginalContext();
            if (HomeNativePeerObservation.FromAcceptedUnixSocket(widgetObservation) != originalHomeContext.OriginalHostPeer ||
                !await result._home.IsOriginalIssuedContextCurrentAsync(originalHomeContext, deadline.Token))
                throw new UnauthorizedAccessException("Exact original admitted Home widget observation peer required.");
            result._widgetObservationSocket = widgetObservation;
            result._owner = await LinuxRootSupervisedInstalledWidgetOwner.StartNativePreparedAsync(helperPreparation,
                Path.Combine(directory, "native-owner.sock"), ownerPreparation,
                result._home, ownerListener, ownerPath, route.SocketPath, uid, gid, actualUserHome, deadline.Token)
                ?? throw new UnauthorizedAccessException("Actual independent owner child admission refused.");
            // Begin authority acceptance before canonical protected tuple reads: those
            // resource checks still require genuine launch authority from this root issuer.
            result._channels.Add(result.ServeOriginalHomeAuthorityAsync(homeListener));
            result._channels.Add(LinuxAdministratorOriginalOwnerChannel.ServeAsync(result._owner,
                result._home, canonical, homePreparation, ownerPreparation, result._ownerChannelLifetime.Token));
            return result;
        }
        catch (Exception admissionFailure)
        {
            try { await result.DisposeAsync(); }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Paired original admission and exact cleanup both failed.", admissionFailure, cleanupFailure);
            }
            throw;
        }
    }
    private Socket Listen(string path)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _sockets.Add(socket);
        socket.Bind(new UnixDomainSocketEndPoint(path));
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite |
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite);
        socket.Listen(8); return socket;
    }
    private async Task ServeOriginalHomeAuthorityAsync(Socket listener)
    {
        using var accepted = await listener.AcceptAsync(_lifetime.Token);
        await LinuxAdministratorOriginalHomeAuthorityChannel.ServeAsync(accepted, OriginalHome,
            OriginalOwner, _lifetime.Token);
    }
    internal Task RetireOriginalOwnerAndDrainAsync() => BeginOriginalOwnerRetirement(false);
    internal Task RetireOriginalNativeOwnerHelperAndDrainAsync() => BeginOriginalOwnerRetirement(true);
    private Task BeginOriginalOwnerRetirement(bool nativeHelperDeath)
    {
        TaskCompletionSource? completion = null; Task task;
        lock (_disposeGate)
        {
            if (_disposeTask is not null) throw new InvalidOperationException("Original pair retirement already admitted.");
            if (_ownerRetireTask is null)
            { completion = new(TaskCreationOptions.RunContinuationsAsynchronously); _ownerRetireTask = completion.Task; _ownerRetireUsesNativeHelper = nativeHelperDeath; }
            else if (_ownerRetireUsesNativeHelper != nativeHelperDeath) throw new InvalidOperationException("Original owner retirement mode is already frozen.");
            task = _ownerRetireTask;
        }
        if (completion is not null) _ = DrainOriginalOwnerAsync(completion, nativeHelperDeath);
        return task;
    }
    private async Task DrainOriginalOwnerAsync(TaskCompletionSource completion, bool nativeHelperDeath)
    {
        try
        {
            Exception? first = null;
            // An existing protocol fault remains observable during intentional retirement.
            if (OwnerChannel.IsCompleted)
                try { await OwnerChannel; } catch (Exception error) { first = error; }
            try { _ownerChannelLifetime.Cancel(); } catch (Exception error) { first ??= error; }
            try { await OwnerChannel; }
            catch (OperationCanceledException) when (_ownerChannelLifetime.IsCancellationRequested) { }
            catch (Exception error) { first ??= error; }
            if (nativeHelperDeath)
                try { await OriginalOwner.RetireOriginalNativeHelperAndDrainForAdministratorAsync(_lifetime.Token); }
                catch (Exception error) { first ??= error; }
            try { await OriginalOwner.DisposeAsync(); } catch (Exception error) { first ??= error; }
            if (first is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw();
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? completion = null; Task task;
        lock (_disposeGate)
        {
            if (_disposeTask is null)
            { completion = new(TaskCreationOptions.RunContinuationsAsynchronously); _disposeTask = completion.Task; }
            task = _disposeTask;
        }
        if (completion is not null) _ = DrainAsync(completion);
        return new(task);
    }
    private async Task DrainAsync(TaskCompletionSource completion)
    {
        try { await DrainOwnedAsync(); completion.TrySetResult(); }
        catch (Exception error) { completion.TrySetException(error); }
    }
    private async Task DrainOwnedAsync()
    {
        Exception? first = null;
        try { _lifetime.Cancel(); } catch (Exception error) { first = error; }
        foreach (var channel in _channels)
            try { await channel; }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception error) { first ??= error; }
        foreach (var socket in _sockets)
            try { socket.Dispose(); } catch (Exception error) { first ??= error; }
        Task? originalOwnerRetirement;
        lock (_disposeGate) originalOwnerRetirement = _ownerRetireTask;
        if (originalOwnerRetirement is not null)
            try { await originalOwnerRetirement; } catch (Exception error) { first ??= error; }
        if (_owner is not null)
            try { await _owner.DisposeAsync(); } catch (Exception error) { first ??= error; }
        if (_home is not null)
        {
            try
            { if (!await _home.ShutdownOriginalAndDrainForAdministratorAsync()) throw new IOException("Original Home shutdown not observed."); }
            catch (Exception error) { first ??= error; }
            finally { try { _home.Dispose(); } catch (Exception error) { first ??= error; } }
        }
        foreach (var name in new[] { "home.sock", "canonical.sock", "owner.sock", "widgets.sock" })
            try { var path = Path.Combine(_directory, name); if (File.Exists(path)) File.Delete(path); }
            catch (Exception error) { first ??= error; }
        try { Directory.Delete(_directory); } catch (Exception error) { first ??= error; }
        try { _ownerChannelLifetime.Dispose(); } catch (Exception error) { first ??= error; }
        try { _lifetime.Dispose(); } catch (Exception error) { first ??= error; }
        if (first is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw();
    }
}
