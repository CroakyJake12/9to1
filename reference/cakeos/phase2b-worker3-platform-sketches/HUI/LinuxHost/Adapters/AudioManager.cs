using System.Diagnostics;
using System.Text.Json;
using CakeOS.Platform;

namespace CakeOS.HuiLinuxHost.Adapters;

/// <summary>Linux audio manager implementation using PipeWire via pw-cli/pactl.</summary>
public sealed class PipeWireAudioManager : IAudioManager
{
    public async Task<AudioDeviceInfo[]> GetOutputDevicesAsync(CancellationToken cancellationToken = default)
    {
        return await GetDevicesAsync("Source", AudioDeviceType.Output, cancellationToken);
    }

    public async Task<AudioDeviceInfo[]> GetInputDevicesAsync(CancellationToken cancellationToken = default)
    {
        return await GetDevicesAsync("Sink", AudioDeviceType.Input, cancellationToken);
    }

    public async Task<bool> SetDefaultOutputAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        return await SetDefaultDeviceAsync(deviceId, "set-default-sink", cancellationToken);
    }

    public async Task<bool> SetDefaultInputAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        return await SetDefaultDeviceAsync(deviceId, "set-default-source", cancellationToken);
    }

    public async Task<IAudioStream?> CreateOutputStreamAsync(AudioStreamParameters parameters, CancellationToken cancellationToken = default)
    {
        return await CreateStreamAsync(parameters, "playback", cancellationToken);
    }

    public async Task<IAudioStream?> CreateInputStreamAsync(AudioStreamParameters parameters, CancellationToken cancellationToken = default)
    {
        return await CreateStreamAsync(parameters, "capture", cancellationToken);
    }

    private async Task<AudioDeviceInfo[]> GetDevicesAsync(string deviceType, AudioDeviceType type, CancellationToken cancellationToken)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "pactl",
                Arguments = $"list {deviceType.ToLowerInvariant()}s short",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
                return [];

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
                return [];

            return ParseDeviceList(output, type);
        }
        catch
        {
            return [];
        }
    }

    private AudioDeviceInfo[] ParseDeviceList(string output, AudioDeviceType type)
    {
        var devices = new List<AudioDeviceInfo>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var id = parts[0];
                var name = parts[1];
                var description = parts.Length > 2 ? parts[2] : name;
                var isDefault = name.Contains("default", StringComparison.OrdinalIgnoreCase);

                devices.Add(new AudioDeviceInfo(id, name, description, isDefault, type));
            }
        }

        return devices.ToArray();
    }

    private async Task<bool> SetDefaultDeviceAsync(string deviceId, string command, CancellationToken cancellationToken)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "pactl",
                Arguments = $"{command} {deviceId}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
                return false;

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<IAudioStream?> CreateStreamAsync(AudioStreamParameters parameters, string direction, CancellationToken cancellationToken)
    {
        try
        {
            var format = parameters.Format switch
            {
                AudioFormat.Float32 => "f32le",
                AudioFormat.Int16 => "s16le",
                AudioFormat.Int24 => "s24le",
                AudioFormat.Int32 => "s32le",
                _ => "f32le"
            };

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "pw-record",
                Arguments = $"--target={direction} --rate={parameters.SampleRate} --channels={parameters.Channels} --format={format}",
                RedirectStandardInput = direction == "playback",
                RedirectStandardOutput = direction == "capture",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
                return null;

            return new PipeWireAudioStream(process, parameters, direction == "playback");
        }
        catch
        {
            return null;
        }
    }

    private sealed class PipeWireAudioStream : IAudioStream
    {
        private readonly Process _process;
        private readonly AudioStreamParameters _parameters;
        private readonly bool _isOutput;
        private bool _started;

        public PipeWireAudioStream(Process process, AudioStreamParameters parameters, bool isOutput)
        {
            _process = process;
            _parameters = parameters;
            _isOutput = isOutput;
        }

        public AudioStreamParameters Parameters => _parameters;

        public async Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_isOutput || _process.StandardInput.BaseStream.CanWrite == false)
                throw new InvalidOperationException("Not an output stream");

            await _process.StandardInput.BaseStream.WriteAsync(buffer, cancellationToken);
        }

        public async Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_isOutput || _process.StandardOutput.BaseStream.CanRead == false)
                throw new InvalidOperationException("Not an input stream");

            return await _process.StandardOutput.BaseStream.ReadAsync(buffer, cancellationToken);
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (!_started)
            {
                _started = true;
                // Stream starts automatically with pw-record
            }
            await Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (_started && !_process.HasExited)
            {
                _process.Kill();
                await _process.WaitForExitAsync(cancellationToken);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync(CancellationToken.None);
            _process.Dispose();
        }
    }
}

/// <summary>Null implementation for environments without PipeWire.</summary>
public sealed class NullAudioManager : IAudioManager
{
    public Task<AudioDeviceInfo[]> GetOutputDevicesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new[]
        {
            new AudioDeviceInfo("default", "Default Output", "Default output device", true, AudioDeviceType.Output)
        });

    public Task<AudioDeviceInfo[]> GetInputDevicesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new[]
        {
            new AudioDeviceInfo("default", "Default Input", "Default input device", true, AudioDeviceType.Input)
        });

    public Task<bool> SetDefaultOutputAsync(string deviceId, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> SetDefaultInputAsync(string deviceId, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<IAudioStream?> CreateOutputStreamAsync(AudioStreamParameters parameters, CancellationToken cancellationToken = default)
        => Task.FromResult<IAudioStream?>(new NullAudioStream(parameters));

    public Task<IAudioStream?> CreateInputStreamAsync(AudioStreamParameters parameters, CancellationToken cancellationToken = default)
        => Task.FromResult<IAudioStream?>(new NullAudioStream(parameters));

    private sealed class NullAudioStream : IAudioStream
    {
        public AudioStreamParameters Parameters { get; }

        public NullAudioStream(AudioStreamParameters parameters)
        {
            Parameters = parameters;
        }

        public Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task StartAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}