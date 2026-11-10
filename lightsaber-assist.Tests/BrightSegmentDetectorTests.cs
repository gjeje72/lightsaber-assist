using lightsaber_assist.Core.Analysis;
using lightsaber_assist.Vision;

namespace lightsaber_assist.Tests;

public class BrightSegmentDetectorTests
{
    [Fact]
    public void DetectsBrightLine()
    {
        const int w = 400, h = 300;
        var bgr = new byte[w * h * 3];
        for (var x = 100; x <= 300; x++)
        {
            for (var t = -2; t <= 2; t++)
            {
                var i = ((150 + t) * w + x) * 3;
                bgr[i] = bgr[i + 1] = bgr[i + 2] = 255;
            }
        }

        var frame = new RawFrame(0, TimeSpan.Zero, w, h, bgr);
        var segs = new BrightSegmentDetector().DetectSegments(frame, [new BoundingBox(50, 50, 300, 200, 1)]);
        Assert.NotEmpty(segs);
        Assert.True(segs.Max(s => s.Length) > 150);
    }

    private static RawFrame LineFrame(byte b, byte g, byte r, byte coreLevel = 0)
    {
        const int w = 400, h = 300;
        var bgr = new byte[w * h * 3];
        for (var x = 100; x <= 300; x++)
        {
            for (var t = -2; t <= 2; t++)
            {
                var i = ((150 + t) * w + x) * 3;
                bgr[i] = b;
                bgr[i + 1] = g;
                bgr[i + 2] = r;
            }
        }

        return new RawFrame(0, TimeSpan.Zero, w, h, bgr);
    }

    [Theory]
    [InlineData(255, 255, 0)]
    [InlineData(255, 40, 255)]
    [InlineData(40, 255, 60)]
    [InlineData(40, 60, 255)]
    [InlineData(255, 180, 20)]
    public void DetectsGlowingSaberOfAnyHue(byte b, byte g, byte r)
    {
        var segs = new BrightSegmentDetector().DetectSegments(LineFrame(b, g, r), [new BoundingBox(50, 50, 300, 200, 1)]);
        Assert.NotEmpty(segs);
        Assert.True(segs.Max(s => s.Length) > 150);
        Assert.True(segs.Max(s => s.Confidence) is > 0.4f and <= 1f);
    }

    [Fact]
    public void IgnoresDarkSaturatedClothing()
    {
        var segs = new BrightSegmentDetector().DetectSegments(LineFrame(20, 20, 140), [new BoundingBox(50, 50, 300, 200, 1)]);
        Assert.Empty(segs);
    }

    [Fact]
    public void ConfidenceDependsOnLengthRelativeToFighter()
    {
        var frame = LineFrame(255, 255, 255);
        var detector = new BrightSegmentDetector { MinLengthRatio = 0.1 };
        var small = detector.DetectSegments(frame, [new BoundingBox(150, 100, 100, 100, 1)]);
        var large = detector.DetectSegments(frame, [new BoundingBox(50, 0, 300, 300, 1)]);
        Assert.NotEmpty(small);
        Assert.NotEmpty(large);
        Assert.All(large.Concat(small), s => Assert.InRange(s.Confidence, 0f, 1f));
        Assert.True(small.Max(s => s.Confidence) > large.Max(s => s.Confidence));
    }
}
