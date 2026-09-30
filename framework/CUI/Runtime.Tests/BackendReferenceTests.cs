using System.Reflection;
using Avalonia.Controls;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class BackendReferenceTests
{
    [Fact]
    public void Source_backend_cast_clones_share_real_counter_and_dispose_resource_exactly_once()
    {
        var owner = typeof(Avalonia.AvaloniaObject).Assembly.GetType("Avalonia.Utilities.RefCountable", throwOnError: true);
        var resource = new Resource();
        var original = owner!.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!.MakeGenericMethod(typeof(Resource)).Invoke(null, [resource])!;
        var cloneMethod = owner.GetMethod("CloneAs", BindingFlags.Public | BindingFlags.Static)!;
        var clone = cloneMethod.MakeGenericMethod(typeof(object)).Invoke(null, [original])!;
        Assert.Equal(2, Count(original));
        ((IDisposable)original).Dispose();
        Assert.Equal(0, resource.DisposeCount);
        Assert.Same(resource, clone.GetType().GetProperty("Item")!.GetValue(clone));
        ((IDisposable)clone).Dispose();
        ((IDisposable)clone).Dispose();
        Assert.Equal(1, resource.DisposeCount);
        var disposed = Assert.Throws<TargetInvocationException>(() => cloneMethod.MakeGenericMethod(typeof(object)).Invoke(null, [clone]));
        Assert.IsType<ObjectDisposedException>(disposed.InnerException);
    }

    [Fact]
    public void Invalid_cast_does_not_increment_or_dispose_the_existing_counter()
    {
        var owner = typeof(Avalonia.AvaloniaObject).Assembly.GetType("Avalonia.Utilities.RefCountable", throwOnError: true)!;
        var resource = new Resource();
        var original = owner.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!.MakeGenericMethod(typeof(Resource)).Invoke(null, [resource])!;
        var error = Assert.Throws<TargetInvocationException>(() => owner.GetMethod("CloneAs", BindingFlags.Public | BindingFlags.Static)!
            .MakeGenericMethod(typeof(string)).Invoke(null, [original]));
        Assert.IsType<InvalidCastException>(error.InnerException);
        Assert.Equal(1, Count(original));
        ((IDisposable)original).Dispose();
        Assert.Equal(1, resource.DisposeCount);
    }
    private static int Count(object reference) => (int)reference.GetType().GetProperty("RefCount")!.GetValue(reference)!;
    private sealed class Resource : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }
}
