namespace lightsaber_assist.Core.Analysis;

public static class GripResolver
{
    public const float TwoHandsRatio = 0.35f;

    public static List<PointF> Resolve(Skeleton skeleton, BoundingBox box)
    {
        var grips = new List<PointF>();
        var l = skeleton[CocoKeypoint.LeftWrist];
        var r = skeleton[CocoKeypoint.RightWrist];
        var lOk = l.Confidence >= FighterMetrics.MinConfidence;
        var rOk = r.Confidence >= FighterMetrics.MinConfidence;

        if (lOk)
        {
            grips.Add(new PointF(l.X, l.Y));
        }

        if (rOk)
        {
            grips.Add(new PointF(r.X, r.Y));
        }

        if (lOk && rOk)
        {
            var d = Math.Sqrt(Math.Pow(l.X - r.X, 2) + Math.Pow(l.Y - r.Y, 2));
            if (d < TwoHandsRatio * box.Height)
            {
                grips.Add(new PointF((l.X + r.X) / 2f, (l.Y + r.Y) / 2f));
            }
        }

        return grips;
    }
}

public static class SaberAssociator
{
    public const double MaxGripDistanceRatio = 0.4;
    public const double MinLengthRatio = 0.25;
    public const double BodyMarginRatio = 0.25;

    public static Dictionary<int, SaberSegment> Associate(
        IReadOnlyList<SaberSegment> segments,
        IReadOnlyList<(int FighterId, BoundingBox Box, IReadOnlyList<PointF> Grips)> fighters)
    {
        var candidates = new List<(int Id, int Seg, double Score, bool Flip)>();
        foreach (var (id, box, grips) in fighters)
        {
            if (grips.Count == 0 || box.Height <= 0)
            {
                continue;
            }

            for (var s = 0; s < segments.Count; s++)
            {
                var seg = segments[s];
                if (seg.Length < MinLengthRatio * box.Height)
                {
                    continue;
                }

                var best = double.MaxValue;
                var flip = false;
                foreach (var g in grips)
                {
                    var dh = FighterMetrics.DistancePixels(g, seg.Hilt);
                    var dt = FighterMetrics.DistancePixels(g, seg.Tip);
                    if (dh < best)
                    {
                        best = dh;
                        flip = false;
                    }

                    if (dt < best)
                    {
                        best = dt;
                        flip = true;
                    }
                }

                if (best <= MaxGripDistanceRatio * box.Height)
                {
                    candidates.Add((id, s, best, flip));
                }
            }
        }

        var result = new Dictionary<int, SaberSegment>();
        var usedSegs = new HashSet<int>();
        foreach (var c in candidates.OrderBy(c => c.Score))
        {
            if (result.ContainsKey(c.Id) || usedSegs.Contains(c.Seg))
            {
                continue;
            }

            var seg = segments[c.Seg];
            result[c.Id] = c.Flip ? seg with { Hilt = seg.Tip, Tip = seg.Hilt } : seg;
            usedSegs.Add(c.Seg);
        }

        FallbackByBody(segments, fighters, result, usedSegs);
        return result;
    }

    /// <summary>
    /// Pour les combattants sans sabre associ\u00e9 (main arm\u00e9e masqu\u00e9e par le corps) :
    /// on retient le segment libre dont une extr\u00e9mit\u00e9 est dans ou contre la bo\u00eete du combattant.
    /// </summary>
    private static void FallbackByBody(
        IReadOnlyList<SaberSegment> segments,
        IReadOnlyList<(int FighterId, BoundingBox Box, IReadOnlyList<PointF> Grips)> fighters,
        Dictionary<int, SaberSegment> result,
        HashSet<int> usedSegs)
    {
        var candidates = new List<(int Id, int Seg, double Score, bool Flip)>();
        foreach (var (id, box, _) in fighters)
        {
            if (result.ContainsKey(id) || box.Height <= 0)
            {
                continue;
            }

            for (var s = 0; s < segments.Count; s++)
            {
                var seg = segments[s];
                if (usedSegs.Contains(s) || seg.Length < MinLengthRatio * box.Height)
                {
                    continue;
                }

                var dh = DistanceToBox(seg.Hilt, box);
                var dt = DistanceToBox(seg.Tip, box);
                var best = Math.Min(dh, dt);
                if (best > BodyMarginRatio * box.Height)
                {
                    continue;
                }

                var center = box.Center;
                var flip = FighterMetrics.DistancePixels(seg.Tip, center) < FighterMetrics.DistancePixels(seg.Hilt, center);
                candidates.Add((id, s, best, flip));
            }
        }

        foreach (var c in candidates.OrderBy(c => c.Score))
        {
            if (result.ContainsKey(c.Id) || usedSegs.Contains(c.Seg))
            {
                continue;
            }

            var seg = segments[c.Seg];
            result[c.Id] = c.Flip ? seg with { Hilt = seg.Tip, Tip = seg.Hilt } : seg;
            usedSegs.Add(c.Seg);
        }
    }

    private static double DistanceToBox(PointF p, BoundingBox box)
    {
        var dx = Math.Max(Math.Max(box.X - p.X, 0), p.X - box.Right);
        var dy = Math.Max(Math.Max(box.Y - p.Y, 0), p.Y - box.Bottom);
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
