namespace lightsaber_assist.Core.Analysis;

/// <summary>Scores de confiance (0..1) pour les armés d'un combattant sur une frame.</summary>
public sealed class ThrustScores
{
    public float Simple { get; set; }

    public float Engagement { get; set; }
}

public sealed record ThrustEvent(
    int FighterId,
    AnnotationType Type,
    int StartFrame,
    int EndFrame,
    TimeSpan Start,
    TimeSpan End,
    float Confidence);

/// <summary>
/// Le buste de l'attaquant est assimilé à un plan vertical (droite épaules-hanches dans l'image).
/// Armé simple : la pointe est de l'autre côté du plan par rapport à l'adversaire.
/// Armé d'engagement : la lame complète (garde et pointe) est de l'autre côté du plan.
/// </summary>
public static class ThrustDetector
{
    public static ThrustScores? Evaluate(FighterFrame attacker, FighterFrame opponent)
    {
        if (attacker.Saber is not { } saber)
        {
            return null;
        }

        var sk = attacker.Skeleton;
        var ls = sk[CocoKeypoint.LeftShoulder];
        var rs = sk[CocoKeypoint.RightShoulder];
        var lh = sk[CocoKeypoint.LeftHip];
        var rh = sk[CocoKeypoint.RightHip];
        var min = FighterMetrics.MinConfidence;
        var shoulders = ls.Confidence >= min && rs.Confidence >= min;
        var hips = lh.Confidence >= min && rh.Confidence >= min;

        var box = attacker.Box;
        var top = shoulders
            ? new PointF((ls.X + rs.X) / 2f, (ls.Y + rs.Y) / 2f)
            : new PointF(box.Center.X, box.Y);
        var bottom = hips
            ? new PointF((lh.X + rh.X) / 2f, (lh.Y + rh.Y) / 2f)
            : new PointF(top.X, box.Bottom);

        var dx = bottom.X - top.X;
        var dy = bottom.Y - top.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-3)
        {
            return null;
        }

        double Signed(PointF p) => (dx * (p.Y - top.Y) - dy * (p.X - top.X)) / len;

        var oppSide = Math.Sign(Signed(opponent.CenterOfGravity));
        if (oppSide == 0)
        {
            return null;
        }

        var halfWidth = shoulders
            ? Math.Sqrt(Math.Pow(ls.X - rs.X, 2) + Math.Pow(ls.Y - rs.Y, 2)) / 2
            : box.Width / 4.0;
        halfWidth = Math.Max(halfWidth, 1);

        double Beyond(PointF p) => Math.Clamp(-Signed(p) * oppSide / halfWidth, 0, 1);

        var reliability = new[] { shoulders ? Math.Min(ls.Confidence, rs.Confidence) : 0.5f, hips ? Math.Min(lh.Confidence, rh.Confidence) : 0.5f }
            .Average();
        var factor = Math.Clamp(reliability, 0, 1) * Math.Clamp(saber.Confidence, 0, 1);

        var tip = Beyond(saber.Tip);
        var hilt = Beyond(saber.Hilt);
        return new ThrustScores
        {
            Simple = (float)(tip * factor),
            Engagement = (float)(Math.Min(tip, hilt) * factor),
        };
    }

    public static void Apply(FrameAnalysis analysis)
    {
        foreach (var f in analysis.Fighters)
        {
            f.Thrust = null;
        }

        if (analysis.Fighters.Count < 2)
        {
            return;
        }

        foreach (var f in analysis.Fighters)
        {
            var other = analysis.Fighters.FirstOrDefault(o => o.FighterId != f.FighterId);
            if (other is not null)
            {
                f.Thrust = Evaluate(f, other);
            }
        }
    }
}

public static class ThrustEventBuilder
{
    public static List<ThrustEvent> Build(IEnumerable<FrameAnalysis> frames, double threshold, int maxGapFrames)
    {
        var events = new List<ThrustEvent>();
        var open = new Dictionary<int, ThrustEvent>();
        foreach (var frame in frames.OrderBy(f => f.FrameIndex))
        {
            foreach (var fighter in frame.Fighters)
            {
                var s = fighter.Thrust;
                AnnotationType? type = null;
                float conf = 0;
                if (s is not null)
                {
                    if (s.Engagement >= threshold)
                    {
                        type = AnnotationType.ArmeEngagement;
                        conf = s.Engagement;
                    }
                    else if (s.Simple >= threshold)
                    {
                        type = AnnotationType.ArmeSimple;
                        conf = s.Simple;
                    }
                }

                if (type is null)
                {
                    continue;
                }

                if (open.TryGetValue(fighter.FighterId, out var cur)
                    && cur.Type == type
                    && frame.FrameIndex - cur.EndFrame <= maxGapFrames)
                {
                    open[fighter.FighterId] = cur with
                    {
                        EndFrame = frame.FrameIndex,
                        End = frame.Timestamp,
                        Confidence = Math.Max(cur.Confidence, conf),
                    };
                }
                else
                {
                    if (cur is not null)
                    {
                        events.Add(cur);
                    }

                    open[fighter.FighterId] = new ThrustEvent(
                        fighter.FighterId, type.Value, frame.FrameIndex, frame.FrameIndex, frame.Timestamp, frame.Timestamp, conf);
                }
            }
        }

        events.AddRange(open.Values);
        return events.OrderBy(e => e.StartFrame).ToList();
    }
}
