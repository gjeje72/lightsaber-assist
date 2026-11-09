using lightsaber_assist.Core.Analysis;

namespace lightsaber_assist.Tests;

public class ThrustDetectionTests
{
    private static FighterFrame Fighter(int id, float cx, PointF hilt, PointF tip)
    {
        var s = new Skeleton();
        s.Keypoints[(int)CocoKeypoint.LeftShoulder] = new Keypoint(cx - 20, 100, 0.9f);
        s.Keypoints[(int)CocoKeypoint.RightShoulder] = new Keypoint(cx + 20, 100, 0.9f);
        s.Keypoints[(int)CocoKeypoint.LeftHip] = new Keypoint(cx - 15, 200, 0.9f);
        s.Keypoints[(int)CocoKeypoint.RightHip] = new Keypoint(cx + 15, 200, 0.9f);
        return new FighterFrame
        {
            FighterId = id,
            Box = new BoundingBox(cx - 40, 50, 80, 200, 1),
            Skeleton = s,
            CenterOfGravity = new PointF(cx, 200),
            Saber = new SaberSegment(hilt, tip, 1f),
        };
    }

    [Fact]
    public void TipBeyondBodyPlane_IsSimple_NotEngagement()
    {
        var a = Fighter(0, 100, new PointF(60, 150), new PointF(160, 150));
        var b = Fighter(1, 300, new PointF(0, 0), new PointF(1, 1));
        var r = ThrustDetector.Evaluate(a, b)!;
        Assert.True(r.Simple < 0.01f);

        var a2 = Fighter(0, 100, new PointF(60, 150), new PointF(20, 150));
        var r2 = ThrustDetector.Evaluate(a2, b)!;
        Assert.True(r2.Simple > 0.5f);
        Assert.True(r2.Engagement < r2.Simple + 0.001f);
    }

    [Fact]
    public void WholeBladeBeyondPlane_IsEngagement()
    {
        var a = Fighter(0, 100, new PointF(40, 150), new PointF(-40, 150));
        var b = Fighter(1, 300, new PointF(0, 0), new PointF(1, 1));
        var r = ThrustDetector.Evaluate(a, b)!;
        Assert.True(r.Engagement > 0.5f);
    }

    [Fact]
    public void EventBuilder_MergesConsecutiveFrames()
    {
        FrameAnalysis Frame(int i, float simple) => new()
        {
            FrameIndex = i,
            Timestamp = TimeSpan.FromMilliseconds(i * 40),
            Fighters = [new FighterFrame { FighterId = 0, Thrust = new ThrustScores { Simple = simple } }],
        };

        var ev = ThrustEventBuilder.Build([Frame(1, 0.8f), Frame(2, 0.9f), Frame(3, 0.1f), Frame(10, 0.7f)], 0.6, 1);
        Assert.Equal(2, ev.Count);
        Assert.Equal(2, ev[0].EndFrame);
        Assert.Equal(0.9f, ev[0].Confidence);
    }
}
