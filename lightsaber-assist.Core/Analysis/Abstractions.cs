namespace lightsaber_assist.Core.Analysis;

/// <summary>Frame BGR 8 bits, 3 canaux, ligne par ligne sans padding.</summary>
public sealed record RawFrame(int Index, TimeSpan Timestamp, int Width, int Height, byte[] Bgr);

public sealed record VideoInfo(int Width, int Height, double Fps, int FrameCount, TimeSpan Duration);

public interface IPoseDetector
{
    IReadOnlyList<(BoundingBox Box, Skeleton Skeleton)> Detect(RawFrame frame);
}

public interface ISaberDetector
{
    IReadOnlyList<SaberSegment> DetectSegments(RawFrame frame, IReadOnlyList<BoundingBox> searchAreas);
}

public interface IFrameSource : IDisposable
{
    VideoInfo Info { get; }

    IAsyncEnumerable<RawFrame> ReadFramesAsync(int step, CancellationToken cancellationToken);

    RawFrame? GetFrame(int frameIndex);
}

public interface IAnalysisCache
{
    Task<AnalysisResult?> LoadAsync(string videoPath, string modelVersion, CancellationToken cancellationToken);

    Task SaveAsync(AnalysisResult result, CancellationToken cancellationToken);
}

public interface IAnalysisExporter
{
    Task ExportAsync(AnalysisResult result, string destinationPath, CancellationToken cancellationToken);
}

public interface IAnnotationStore
{
    Task<List<Annotation>> LoadAsync(string videoPath, CancellationToken cancellationToken);

    Task SaveAsync(string videoPath, IReadOnlyList<Annotation> annotations, CancellationToken cancellationToken);
}
