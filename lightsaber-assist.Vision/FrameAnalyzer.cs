using lightsaber_assist.Core.Analysis;

namespace lightsaber_assist.Vision;

/// <summary>Suivi simple de 2 combattants : identifiants stables par proximité des centres.</summary>
public sealed class FighterTracker
{
    private readonly Dictionary<int, PointF> lastCenters = [];

    public void Reset() => lastCenters.Clear();

    public List<(int Id, BoundingBox Box, Skeleton Skeleton)> Assign(IReadOnlyList<(BoundingBox Box, Skeleton Skeleton)> detections)
    {
        var result = new List<(int, BoundingBox, Skeleton)>();
        if (detections.Count == 0)
        {
            return result;
        }

        var remaining = detections.OrderBy(d => d.Box.Center.X).ToList();

        if (lastCenters.Count == 0)
        {
            for (var i = 0; i < remaining.Count; i++)
            {
                result.Add((i, remaining[i].Box, remaining[i].Skeleton));
                lastCenters[i] = remaining[i].Box.Center;
            }

            return result;
        }

        var pairs = (from id in lastCenters.Keys
                     from d in remaining
                     select (Id: id, Det: d, Dist: FighterMetrics.DistancePixels(lastCenters[id], d.Box.Center)))
            .OrderBy(p => p.Dist)
            .ToList();

        var usedIds = new HashSet<int>();
        var usedDets = new HashSet<(BoundingBox, Skeleton)>();
        foreach (var (id, det, _) in pairs)
        {
            if (usedIds.Contains(id) || usedDets.Contains(det))
            {
                continue;
            }

            usedIds.Add(id);
            usedDets.Add(det);
            result.Add((id, det.Box, det.Skeleton));
            lastCenters[id] = det.Box.Center;
        }

        var nextId = lastCenters.Keys.Max() + 1;
        foreach (var det in remaining.Where(d => !usedDets.Contains(d)))
        {
            result.Add((nextId, det.Box, det.Skeleton));
            lastCenters[nextId] = det.Box.Center;
            nextId++;
        }

        return result.OrderBy(r => r.Item1).ToList();
    }
}

public sealed class FrameAnalyzer
{
    private readonly IPoseDetector detector;
    private readonly FighterTracker tracker = new();
    private readonly ISaberDetector? saberDetector;

    public FrameAnalyzer(IPoseDetector detector, ISaberDetector? saberDetector = null)
    {
        this.detector = detector;
        this.saberDetector = saberDetector;
    }

    public void Reset() => tracker.Reset();

    public FrameAnalysis Analyze(RawFrame frame)
    {
        var analysis = new FrameAnalysis { FrameIndex = frame.Index, Timestamp = frame.Timestamp };
        foreach (var (id, box, skeleton) in tracker.Assign(detector.Detect(frame)))
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
