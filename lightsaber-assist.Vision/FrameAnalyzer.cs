using lightsaber_assist.Core.Analysis;

namespace lightsaber_assist.Vision;

/// <summary>Suivi simple de 2 combattants : identifiants stables par proximité des centres.</summary>
public sealed class FighterTracker
{
    private readonly Dictionary<int, PointF> lastCenters = [];
    private int? maxIds;

    public void Reset()
    {
        lastCenters.Clear();
        maxIds = null;
    }

    /// <summary>Initialise les combattants à suivre (ids par X croissant) et interdit la création d'autres identifiants.</summary>
    public void Seed(IReadOnlyList<PointF> seeds)
    {
        lastCenters.Clear();
        var ordered = seeds.OrderBy(s => s.X).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            lastCenters[i] = ordered[i];
        }

        maxIds = ordered.Count > 0 ? ordered.Count : null;
    }

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
            if (maxIds is not null && lastCenters.Count >= maxIds)
            {
                break;
            }

            result.Add((nextId, det.Box, det.Skeleton));
            lastCenters[nextId] = det.Box.Center;
            nextId++;
        }

        return result.OrderBy(r => r.Item1).ToList();
    }
}

public sealed class FrameAnalyzer
{
    private const int MaxFighters = 2;
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
        if (zone is not null)
        {
            detections = detections.Where(d => zone.ContainsFeet(d.Box)).ToList();
        }

        if (zone is not { FighterSeeds.Count: > 0 })
        {
            detections = detections.OrderByDescending(d => d.Box.Confidence).Take(MaxFighters).ToList();
        }

        foreach (var (id, box, skeleton) in tracker.Assign(detections))
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
