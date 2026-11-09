using lightsaber_assist.Core.Analysis;

namespace lightsaber_assist.Tests;

public class SaberAssociationTests
{
    private static Skeleton Wrists(float lx, float ly, float lc, float rx, float ry, float rc)
    {
        var s = new Skeleton();
        s.Keypoints[(int)CocoKeypoint.LeftWrist] = new Keypoint(lx, ly, lc);
        s.Keypoints[(int)CocoKeypoint.RightWrist] = new Keypoint(rx, ry, rc);
        return s;
    }

    private static readonly BoundingBox Box = new(0, 0, 200, 400, 1);

    [Fact]
    public void Grip_SingleReliableWrist()
    {
        var g = GripResolver.Resolve(Wrists(10, 10, 0.9f, 0, 0, 0.1f), Box);
        Assert.Single(g);
        Assert.Equal(new PointF(10, 10), g[0]);
    }

    [Fact]
    public void Grip_TwoCloseWrists_AddsMidpoint()
    {
        var g = GripResolver.Resolve(Wrists(100, 100, 0.9f, 120, 100, 0.9f), Box);
        Assert.Equal(3, g.Count);
        Assert.Contains(new PointF(110, 100), g);
    }

    [Fact]
    public void Grip_NoReliableWrist_Empty()
    {
        Assert.Empty(GripResolver.Resolve(Wrists(1, 1, 0.1f, 2, 2, 0.1f), Box));
    }

    [Fact]
    public void Associate_TwoFightersTwoSegments_AndOrientsHiltAtWrist()
    {
        var segs = new[]
        {
            new SaberSegment(new PointF(300, 100), new PointF(100, 100), 1),
            new SaberSegment(new PointF(600, 100), new PointF(800, 100), 1),
        };
        var fighters = new (int, BoundingBox, IReadOnlyList<PointF>)[]
        {
            (0, Box, new[] { new PointF(110, 105) }),
            (1, new BoundingBox(500, 0, 200, 400, 1), new[] { new PointF(790, 100) }),
        };
        var r = SaberAssociator.Associate(segs, fighters);
        Assert.Equal(new PointF(100, 100), r[0].Hilt);
        Assert.Equal(new PointF(800, 100), r[1].Hilt);
    }

    [Fact]
    public void Associate_SingleSegment_GoesToClosestOnly()
    {
        var segs = new[] { new SaberSegment(new PointF(100, 100), new PointF(300, 100), 1) };
        var fighters = new (int, BoundingBox, IReadOnlyList<PointF>)[]
        {
            (0, Box, new[] { new PointF(100, 100) }),
            (1, Box, new[] { new PointF(100, 150) }),
        };
        var r = SaberAssociator.Associate(segs, fighters);
        Assert.Single(r);
        Assert.True(r.ContainsKey(0));
    }

    [Fact]
    public void Associate_FarSegment_Rejected()
    {
        var segs = new[] { new SaberSegment(new PointF(1000, 1000), new PointF(1200, 1000), 1) };
        var fighters = new (int, BoundingBox, IReadOnlyList<PointF>)[] { (0, Box, new[] { new PointF(10, 10) }) };
        Assert.Empty(SaberAssociator.Associate(segs, fighters));
    }
}
