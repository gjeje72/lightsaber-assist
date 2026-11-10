using lightsaber_assist.Core.Analysis;
using lightsaber_assist.Vision;

namespace lightsaber_assist.Video;

public sealed class VideoPreAnalyzer
{
    private readonly IFrameSource source;
    private readonly IPoseDetector detector;
    private readonly ISaberDetector? saberDetector;
    private readonly FightZone? zone;

    public VideoPreAnalyzer(IFrameSource source, IPoseDetector detector, ISaberDetector? saberDetector = null, FightZone? zone = null)
    {
        this.source = source;
        this.detector = detector;
        this.saberDetector = saberDetector;
        this.zone = zone;
    }

    public async Task<AnalysisResult> AnalyzeAsync(
        string videoPath, string modelVersion, int step, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var info = source.Info;
        var result = new AnalysisResult
        {
            VideoPath = videoPath,
            Width = info.Width,
            Height = info.Height,
            Fps = info.Fps,
            ModelVersion = modelVersion,
        };

        var analyzer = new FrameAnalyzer(detector, saberDetector, zone);
        await foreach (var frame in source.ReadFramesAsync(step, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Frames.Add(await Task.Run(() => analyzer.Analyze(frame), cancellationToken));
            progress?.Report(info.FrameCount > 0 ? Math.Min(1.0, (double)frame.Index / info.FrameCount) : 0);
        }

        progress?.Report(1.0);
        return result;
    }
}
