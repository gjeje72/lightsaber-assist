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
}
