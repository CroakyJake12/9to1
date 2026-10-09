using Haven.Infrastructure.Native.Windows;

// SCM is the supported entry. Neither these arguments nor a console invocation
// supplies publisher enrollment or package/runtime authority. The runtime checks
// the actual protected SCM registration, native process and canonical package store.
if (!OperatingSystem.IsWindows()) return 50;
if (args is not ["--root-service", "--home-machine-state", var machineState] ||
    string.IsNullOrWhiteSpace(machineState) || !Path.IsPathFullyQualified(machineState)) return 87;
return NativeWindowsHomeRootScmEntry.RunOriginalService(machineState);
