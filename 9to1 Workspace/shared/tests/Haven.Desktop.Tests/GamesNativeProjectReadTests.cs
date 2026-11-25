using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Haven.Application.Games;
using Haven.Application.Files;
using Haven.Desktop.NativeHost;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class GamesNativeProjectReadTests
{
    private sealed class FakeReader : IFilesReader
    {
        public FilesReadResult Result;
        public Task<FilesReadResult> ReadAsync(string appId, string projectId, string structuralRevision, string contentRevision, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result);
        }
    }
    private sealed class FakeCodec : GamesProjectCodec
    {
        public GamesProjectDecoded Decoded;
        public GamesProjectDecoded Decode(byte[] bytes) => Decoded;
    }
    [Fact]
    public async Task ReadAsync_ReturnsCanonicalProjectAndBytes()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var reader = new FakeReader
        {
            Result = new FilesReadResult(FilesReadStatus.Success, "app", "proj", "struct", "content", bytes)
        };
        var codec = new FakeCodec
        {
            Decoded = new GamesProjectDecoded("proj", "scene")
        };
        var readerImpl = new NativeGamesProjectContentReader(reader, codec);
        var result = await readerImpl.ReadAsync("app", "proj", "struct", "content");
        Assert.True(result.IsSuccess);
        Assert.Equal("proj", result.Project.ProjectId);
        Assert.Equal("scene", result.Project.SceneId);
        Assert.Equal("struct", result.Project.StructuralRevision);
        Assert.Equal("content", result.Project.ContentRevision);
        Assert.Equal(bytes, result.Bytes);
    }
    [Fact]
    public async Task ReadAsync_RefusesWrongAppOrType()
    {
        var reader = new FakeReader
        {
            Result = new FilesReadResult(FilesReadStatus.Success, "other", "proj", "struct", "content", new byte[] { 1 })
        };
        var codec = new FakeCodec { Decoded = new GamesProjectDecoded("proj", "scene") };
        var result = await new NativeGamesProjectContentReader(reader, codec).ReadAsync("app", "proj", "struct", "content");
        Assert.False(result.IsSuccess);
        Assert.Equal("wrong-app-or-type", result.Reason);
    }
    [Fact]
    public async Task ReadAsync_RefusesMissingRevision()
    {
        var reader = new FakeReader { Result = new FilesReadResult(FilesReadStatus.Success, "app", "proj", "struct", "content", new byte[] { 1 }) };
        var codec = new FakeCodec { Decoded = new GamesProjectDecoded("proj", "scene") };
        var result = await new NativeGamesProjectContentReader(reader, codec).ReadAsync("app", "proj", "", "content");
        Assert.False(result.IsSuccess);
        Assert.Equal("missing-revision", result.Reason);
    }
    [Fact]
    public async Task ReadAsync_RefusesDeniedAccess()
    {
        var reader = new FakeReader
        {
            Result = new FilesReadResult(FilesReadStatus.Denied, "app", "proj", "struct", "content", null)
        };
        var codec = new FakeCodec { Decoded = new GamesProjectDecoded("proj", "scene") };
        var result = await new NativeGamesProjectContentReader(reader, codec).ReadAsync("app", "proj", "struct", "content");
        Assert.False(result.IsSuccess);
        Assert.Equal("denied-or-missing", result.Reason);
    }
}
