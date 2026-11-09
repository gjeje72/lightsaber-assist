using lightsaber_assist.Core.Analysis;

namespace lightsaber_assist.Tests;

public class FighterMetricsTests
{
    private static Skeleton Make(params (CocoKeypoint K, float X, float Y, float C)[] pts)
    {
        var s = new Skeleton();
        foreach (var (k, x, y, c) in pts)
        {
            s.Keypoints[(int)k] = new Keypoint(x, y, c);
        }

        return s;
    }

    [Fact]
    public void CenterOfGravity_IsHipMidpoint()
    {
        var s = Make((CocoKeypoint.LeftHip, 10, 20, 0.9f), (CocoKeypoint.RightHip, 30, 40, 0.9f));
        var c = FighterMetrics.CenterOfGravity(s, new BoundingBox(0, 0, 100, 100, 1));
        Assert.Equal(new PointF(20, 30), c);
    }

    [Fact]
    public void CenterOfGravity_FallsBackToShoulders()
    {
        var s = Make((CocoKeypoint.LeftShoulder, 0, 0, 0.9f), (CocoKeypoint.RightShoulder, 10, 10, 0.9f));
        var c = FighterMetrics.CenterOfGravity(s, new BoundingBox(0, 0, 100, 100, 1));
        Assert.Equal(new PointF(5, 5), c);
    }

    [Fact]
    public void CenterOfGravity_FallsBackToBoxCenter()
    {
        var c = FighterMetrics.CenterOfGravity(new Skeleton(), new BoundingBox(0, 0, 100, 200, 1));
        Assert.Equal(new PointF(50, 100), c);
    }

    [Fact]
    public void ComputeDistance_PixelsAndNormalized()
    {
        var frame = new FrameAnalysis
        {
            Fighters =
            [
                new FighterFrame { Box = new BoundingBox(0, 0, 50, 200, 1), CenterOfGravity = new PointF(0, 0) },
                new FighterFrame { Box = new BoundingBox(0, 0, 50, 200, 1), CenterOfGravity = new PointF(300, 400) },
            ],
        };
        FighterMetrics.ComputeDistance(frame);
        Assert.Equal(500, frame.DistancePixels!.Value, 6);
        Assert.Equal(2.5, frame.DistanceNormalized!.Value, 6);
    }

    [Fact]
    public void ComputeDistance_NullWithOneFighter()
    {
        var frame = new FrameAnalysis { Fighters = [new FighterFrame()] };
        FighterMetrics.ComputeDistance(frame);
        Assert.Null(frame.DistancePixels);
        Assert.Null(frame.DistanceNormalized);
    }
}
