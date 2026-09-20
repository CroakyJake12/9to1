using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CakeOS.Images.Frontend;

public interface IAiVisionClient
{
    Task<AiVisionAnalysis> AnalyzeAsync(string imagePath, CancellationToken ct = default);
}

public sealed class AiVisionAnalysis
{
    public IReadOnlyList<DetectedObject> Objects { get; init; } = [];
    public IReadOnlyList<TextBlock> TextBlocks { get; init; } = [];
    public string Caption { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags { get; init; } = [];
    public double ProcessingMs { get; init; }
}

public sealed class DetectedObject
{
    public string Label { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public BoundingBox BoundingBox { get; init; } = new();
}

public sealed class TextBlock
{
    public string Text { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public BoundingBox BoundingBox { get; init; } = new();
}

public sealed class BoundingBox
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
}

public sealed class AiVisionClient : IAiVisionClient
{
    // gRPC client would be initialized here
    // private readonly Vision.VisionClient _client;

    public async Task<AiVisionAnalysis> AnalyzeAsync(string imagePath, CancellationToken ct = default)
    {
        // Placeholder implementation - would call gRPC service
        // var request = new AnalyzeRequest
        // {
        //     ImageJpeg = File.ReadAllBytes(imagePath),
        //     Features = { "objects", "text", "caption", "tags" },
        //     Locale = "en-US"
        // };
        // var response = await _client.AnalyzeAsync(request, cancellationToken: ct);
        
        await Task.Delay(100, ct).ConfigureAwait(false); // Simulate network call

        return new AiVisionAnalysis
        {
            Objects = [],
            TextBlocks = [],
            Caption = "AI analysis not yet connected",
            Tags = ["placeholder"],
            ProcessingMs = 100,
        };
    }
}