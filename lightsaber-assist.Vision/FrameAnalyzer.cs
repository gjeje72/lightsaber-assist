using lightsaber_assist.Core.Analysis;

namespace lightsaber_assist.Vision;

public sealed class FrameAnalyzer
{
    private readonly IPoseDetector detector;
    private readonly FighterTracker tracker = new();
    private readonly ISaberDetector? saberDetector;

    private readonly FightZone? zone;

    public FrameAnalyzer(IPoseDetector detector, ISaberDetector? saberDetector = null, FightZone? zone = null)
    {
        this.detector = detector;
        this.saberDetector = saberDetector;
        this.zone = zone;
        ApplySeeds();
    }

    public void Reset()
    {
        tracker.Reset();
        ApplySeeds();
    }

    private void ApplySeeds()
    {
        if (zone is { FighterSeeds.Count: > 0 })
        {
            tracker.Seed(zone.FighterSeeds);
        }
    }

    public FrameAnalysis Analyze(RawFrame frame)
    {
        var analysis = new FrameAnalysis { FrameIndex = frame.Index, Timestamp = frame.Timestamp };
        IReadOnlyList<(BoundingBox Box, Skeleton Skeleton)> detections = detector.Detect(frame);
        // La zone ne sert qu'à créer ou reprendre une piste : un combattant peut se tenir sur la ligne du ring ou au-delà.
        Func<BoundingBox, bool>? canStartTrack = zone is null ? null : b => zone.ContainsFeet(b, FightZone.TrackingMargin);

        foreach (var (id, box, skeleton) in tracker.Assign(detections, canStartTrack))
        {
            analysis.Fighters.Add(new FighterFrame
            {
                FighterId = id,
                Box = box,
                Skeleton = skeleton,
                CenterOfGravity = FighterMetrics.CenterOfGravity(skeleton, box),
            });
        }

        FighterMetrics.ComputeDistance(analysis);

        if (saberDetector is not null && analysis.Fighters.Count > 0)
        {
            var segments = saberDetector.DetectSegments(frame, analysis.Fighters.Select(f => f.Box).ToList());
            if (zone is not null)
            {
                segments = segments.Where(s => !zone.IsAlongRing(s)).ToList();
            }
            var inputs = analysis.Fighters
                .Select(f => (f.FighterId, f.Box, (IReadOnlyList<PointF>)GripResolver.Resolve(f.Skeleton, f.Box)))
                .ToList();
            var sabers = SaberAssociator.Associate(segments, inputs);
            foreach (var f in analysis.Fighters)
            {
                f.Saber = sabers.TryGetValue(f.FighterId, out var s) ? s : null;
            }

            ThrustDetector.Apply(analysis);
        }

        return analysis;
    }
}
