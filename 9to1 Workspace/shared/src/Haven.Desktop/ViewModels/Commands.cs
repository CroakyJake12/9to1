/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/Commands.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns RelayCommand, AsyncRelayCommand. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Windows.Input;
using Haven.Desktop.Services;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents relay command and keeps its related state and behavior together.
/// </summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    /// <summary>
    /// Reports whether execute changed applies to the current state.
    /// </summary>
    public event EventHandler? CanExecuteChanged;
    /// <summary>
    /// Reports whether execute applies to the current state.
    /// </summary>
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    /// <summary>
    /// Runs execute while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    public void Execute(object? parameter) => execute();
    /// <summary>
    /// Performs the raise can execute changed step owned by this component.
    /// </summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Represents relay command and keeps its related state and behavior together.
/// </summary>
public sealed class RelayCommand<T>(Action<T?> execute, Func<T?, bool>? canExecute = null) : ICommand
{
    /// <summary>
    /// Reports whether execute changed applies to the current state.
    /// </summary>
    public event EventHandler? CanExecuteChanged;
    /// <summary>
    /// Reports whether execute applies to the current state.
    /// </summary>
    public bool CanExecute(object? parameter) => canExecute?.Invoke((T?)parameter) ?? true;
    /// <summary>
    /// Runs execute while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    public void Execute(object? parameter) => execute((T?)parameter);
    /// <summary>
    /// Performs the raise can execute changed step owned by this component.
    /// </summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Retains the complete async invocation and its actual delegate task. A host must
/// request retirement, leave an admitted callback, then externally join the same close.
/// </summary>
public sealed class AsyncRelayCommand : ICommand, IAsyncDisposable,
    IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly DesktopOriginalWorkLifetime _originalWork;
    private int _running;
    private Task? _originalExecution;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
        _originalWork = new DesktopOriginalWorkLifetime(StopOriginalAsync, CleanupOriginalAsync);
    }

    public event EventHandler? CanExecuteChanged;

    /// <summary>The latest admitted invocation only; the cohort's drain is OriginalClose.</summary>
    public Task? OriginalExecution => Volatile.Read(ref _originalExecution);
    public Task? OriginalClose => _originalWork.OriginalClose;

    public bool CanExecute(object? parameter)
    {
        if (_originalWork.IsRetiring || Volatile.Read(ref _running) != 0) return false;
        var allowed = false;
        _originalWork.RunSynchronous(original =>
        {
            original.DemandPublication();
            allowed = _canExecute?.Invoke() ?? true;
            // A predicate can synchronously retire or invoke this same command.
            if (_originalWork.IsRetiring || Volatile.Read(ref _running) != 0) allowed = false;
        });
        return allowed;
    }

    // Preserve ICommand's original async-void exception delivery. The owned task
    // below is retained; external async-void subscribers/posts are not drain proof.
    public async void Execute(object? parameter) =>
        await ExecuteAsync().ConfigureAwait(true);

    public Task ExecuteAsync() => AcquireOriginal(ExecuteOriginalAsync);

    private Task AcquireOriginal(Func<DesktopOriginalWorkLifetime.Original, Task> body)
    {
        try
        {
            return _originalWork.RunAsync(body,
                actual => Volatile.Write(ref _originalExecution, actual));
        }
        catch (Exception admissionFailure)
        {
            // The old async Task API also returned a faulted task on refusal. No
            // callback or delegate was acquired; this is not an admitted original.
            return Task.FromException(admissionFailure);
        }
    }

    private async Task ExecuteOriginalAsync(DesktopOriginalWorkLifetime.Original original)
    {
        Task? actualDelegate = null;
        var claimedRunning = false;
        try
        {
            if (Volatile.Read(ref _running) != 0) return;
            original.DemandPublication();
            var allowed = _canExecute?.Invoke() ?? true;
            original.DemandPublication();
            if (!allowed || Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
            claimedRunning = true;
            original.DemandPublication();
            NotifyOriginalCanExecuteChanged();
            original.DemandPublication();
            actualDelegate = _execute();
            await original.AwaitAsync(actualDelegate).ConfigureAwait(true);
        }
        catch (Exception error) { original.Capture(actualDelegate, error); }
        finally
        {
            if (claimedRunning)
            {
                Interlocked.Exchange(ref _running, 0);
                // This source-owned final callback remains part of this exact original
                // even after permanent retirement; failure cannot replace the body cause.
                try { NotifyOriginalCanExecuteChanged(); }
                catch (Exception error) { original.Retain(error); }
            }
        }
        original.ThrowRetained();
    }

    public void RaiseCanExecuteChanged() => _originalWork.RunSynchronous(original =>
    {
        original.DemandPublication();
        NotifyOriginalCanExecuteChanged();
    });

    private void NotifyOriginalCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    private Task StopOriginalAsync()
    {
        // Func<Task> has no cancellation argument. Seal and publish disabled state
        // promptly, then retain/await the actual delegate rather than invent a stop ACK.
        NotifyOriginalCanExecuteChanged();
        return Task.CompletedTask;
    }
    private Task CleanupOriginalAsync()
    {
        CanExecuteChanged = null;
        return Task.CompletedTask;
    }

    public void RequestRetirement() => _originalWork.RequestRetirement();
    public void DemandExternalOriginalRetirementJoin() => _originalWork.DemandExternalClose();
    public Task CloseAndDrainAsync() => _originalWork.CloseAndDrainAsync();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}

/// <summary>
/// Retains the complete async invocation and its actual delegate task. A host must
/// request retirement, leave an admitted callback, then externally join the same close.
/// </summary>
public sealed class AsyncRelayCommand<T> : ICommand, IAsyncDisposable,
    IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    private readonly Func<T?, Task> _execute;
    private readonly Func<T?, bool>? _canExecute;
    private readonly DesktopOriginalWorkLifetime _originalWork;
    private int _running;
    private Task? _originalExecution;

    public AsyncRelayCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
        _originalWork = new DesktopOriginalWorkLifetime(StopOriginalAsync, CleanupOriginalAsync);
    }

    public event EventHandler? CanExecuteChanged;

    /// <summary>The latest admitted invocation only; the cohort's drain is OriginalClose.</summary>
    public Task? OriginalExecution => Volatile.Read(ref _originalExecution);
    public Task? OriginalClose => _originalWork.OriginalClose;

    public bool CanExecute(object? parameter)
    {
        if (_originalWork.IsRetiring || Volatile.Read(ref _running) != 0) return false;
        var allowed = false;
        _originalWork.RunSynchronous(original =>
        {
            original.DemandPublication();
            allowed = _canExecute?.Invoke((T?)parameter) ?? true;
            // A predicate can synchronously retire or invoke this same command.
            if (_originalWork.IsRetiring || Volatile.Read(ref _running) != 0) allowed = false;
        });
        return allowed;
    }

    // Preserve ICommand's original async-void exception delivery. The owned task
    // below is retained; external async-void subscribers/posts are not drain proof.
    public async void Execute(object? parameter) =>
        await AcquireOriginal(original => ExecuteOriginalAsync(original, (T?)parameter)).ConfigureAwait(true);

    public Task ExecuteAsync(T? parameter) => AcquireOriginal(original => ExecuteOriginalAsync(original, parameter));

    private Task AcquireOriginal(Func<DesktopOriginalWorkLifetime.Original, Task> body)
    {
        try
        {
            return _originalWork.RunAsync(body,
                actual => Volatile.Write(ref _originalExecution, actual));
        }
        catch (Exception admissionFailure)
        {
            // The old async Task API also returned a faulted task on refusal. No
            // callback or delegate was acquired; this is not an admitted original.
            return Task.FromException(admissionFailure);
        }
    }

    private async Task ExecuteOriginalAsync(DesktopOriginalWorkLifetime.Original original, T? parameter)
    {
        Task? actualDelegate = null;
        var claimedRunning = false;
        try
        {
            if (Volatile.Read(ref _running) != 0) return;
            original.DemandPublication();
            var allowed = _canExecute?.Invoke(parameter) ?? true;
            original.DemandPublication();
            if (!allowed || Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
            claimedRunning = true;
            original.DemandPublication();
            NotifyOriginalCanExecuteChanged();
            original.DemandPublication();
            actualDelegate = _execute(parameter);
            await original.AwaitAsync(actualDelegate).ConfigureAwait(true);
        }
        catch (Exception error) { original.Capture(actualDelegate, error); }
        finally
        {
            if (claimedRunning)
            {
                Interlocked.Exchange(ref _running, 0);
                // This source-owned final callback remains part of this exact original
                // even after permanent retirement; failure cannot replace the body cause.
                try { NotifyOriginalCanExecuteChanged(); }
                catch (Exception error) { original.Retain(error); }
            }
        }
        original.ThrowRetained();
    }

    public void RaiseCanExecuteChanged() => _originalWork.RunSynchronous(original =>
    {
        original.DemandPublication();
        NotifyOriginalCanExecuteChanged();
    });

    private void NotifyOriginalCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    private Task StopOriginalAsync()
    {
        // Func<Task> has no cancellation argument. Seal and publish disabled state
        // promptly, then retain/await the actual delegate rather than invent a stop ACK.
        NotifyOriginalCanExecuteChanged();
        return Task.CompletedTask;
    }
    private Task CleanupOriginalAsync()
    {
        CanExecuteChanged = null;
        return Task.CompletedTask;
    }

    public void RequestRetirement() => _originalWork.RequestRetirement();
    public void DemandExternalOriginalRetirementJoin() => _originalWork.DemandExternalClose();
    public Task CloseAndDrainAsync() => _originalWork.CloseAndDrainAsync();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
