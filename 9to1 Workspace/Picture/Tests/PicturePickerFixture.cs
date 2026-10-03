using System.Reflection;
using Avalonia.Platform.Storage;

namespace HavenOS.Images.Tests;

internal sealed class PicturePickerFixture
{
    public PicturePickerFixture(byte[] bytes, string name = "picked-animation.gif")
    {
        var file = DispatchProxy.Create<IStorageFile, PictureStorageTestProxy>();
        ((PictureStorageTestProxy)(object)file).Handler = (method, _) => method.Name switch
        {
            "get_Name" => name,
            "get_Path" => throw new InvalidOperationException("Import must never query a local path."),
            "OpenReadAsync" => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)),
            "Dispose" => MarkDisposed(),
            _ => throw new NotSupportedException(method.Name)
        };
        Provider = DispatchProxy.Create<IStorageProvider, PictureStorageTestProxy>();
        ((PictureStorageTestProxy)(object)Provider).Handler = (method, args) => method.Name switch
        {
            "get_CanOpen" => true,
            "OpenFilePickerAsync" when args![0] is FilePickerOpenOptions { AllowMultiple: false } =>
                Task.FromResult<IReadOnlyList<IStorageFile>>([file]),
            _ => throw new NotSupportedException(method.Name)
        };
    }
    public IStorageProvider Provider { get; }
    public bool Disposed { get; private set; }
    private object? MarkDisposed() { Disposed = true; return null; }
}

// Avalonia intentionally blocks source implementations of storage interfaces. A test-only
// runtime proxy supplies the selected stream without making a production picker substitute.
public class PictureStorageTestProxy : DispatchProxy
{
    internal Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
}
