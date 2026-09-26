namespace Haven.Application;

/// <summary>Optional device selection supported by live call coordinators.</summary>
public interface ICallDeviceSelection
{
    Task SelectInputDeviceAsync(string? deviceId, CancellationToken cancellationToken);
    Task SelectOutputDeviceAsync(string? deviceId, CancellationToken cancellationToken);
}
