namespace lightsaber_assist.Core.Analysis;

public static class FighterMetrics
{
    public const float MinConfidence = 0.3f;

    /// <summary>Milieu des hanches; repli sur les épaules, puis sur le centre de la boîte.</summary>
    public static PointF CenterOfGravity(Skeleton skeleton, BoundingBox box)
    {
        if (TryMidpoint(skeleton[CocoKeypoint.LeftHip], skeleton[CocoKeypoint.RightHip], out var hips))
        {
            return hips;
        }

        if (TryMidpoint(skeleton[CocoKeypoint.LeftShoulder], skeleton[CocoKeypoint.RightShoulder], out var shoulders))
        {
            return shoulders;
        }

        return box.Center;
    }

    public static double DistancePixels(PointF a, PointF b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    /// <summary>Distance divisée par la hauteur moyenne des boîtes; null si hauteur nulle.</summary>
    public static double? DistanceNormalized(double distancePixels, BoundingBox a, BoundingBox b)
    {
        var meanHeight = (a.Height + b.Height) / 2.0;
        return meanHeight > 0 ? distancePixels / meanHeight : null;
    }

    public static void ComputeDistance(FrameAnalysis frame)
    {
        frame.DistancePixels = null;
        frame.DistanceNormalized = null;
        if (frame.Fighters.Count < 2)
        {
            return;
        }

        var a = frame.Fighters[0];
        var b = frame.Fighters[1];
        var d = DistancePixels(a.CenterOfGravity, b.CenterOfGravity);
        frame.DistancePixels = d;
        frame.DistanceNormalized = DistanceNormalized(d, a.Box, b.Box);
    }

    private static bool TryMidpoint(Keypoint a, Keypoint b, out PointF mid)
    {
        if (a.Confidence >= MinConfidence && b.Confidence >= MinConfidence)
        {
            mid = new PointF((a.X + b.X) / 2f, (a.Y + b.Y) / 2f);
            return true;
        }

        mid = default;
        return false;
    }
}
