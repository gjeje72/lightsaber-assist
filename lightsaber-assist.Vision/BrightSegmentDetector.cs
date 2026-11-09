using lightsaber_assist.Core.Analysis;
using OpenCvSharp;
using CorePoint = lightsaber_assist.Core.Analysis.PointF;

namespace lightsaber_assist.Vision;

/// <summary>Détecte les segments très lumineux (sabres) dans la zone des combattants.</summary>
public sealed class BrightSegmentDetector : ISaberDetector
{
    public int BrightnessThreshold { get; set; } = 230;

    public double RoiMargin { get; set; } = 0.3;

    public double MinLengthRatio { get; set; } = 0.2;

    public double MergeAngleDegrees { get; set; } = 5;

    public double MergeDistance { get; set; } = 6;

    public IReadOnlyList<SaberSegment> DetectSegments(RawFrame frame, IReadOnlyList<BoundingBox> searchAreas)
    {
        var result = new List<SaberSegment>();
        using var image = Mat.FromPixelData(frame.Height, frame.Width, MatType.CV_8UC3, frame.Bgr);

        var areas = searchAreas.Count > 0
            ? searchAreas
            : [new BoundingBox(0, 0, frame.Width, frame.Height, 1)];

        foreach (var area in areas)
        {
            var mx = (int)(area.Width * RoiMargin);
            var my = (int)(area.Height * RoiMargin);
            var x0 = Math.Clamp((int)area.X - mx, 0, frame.Width - 1);
            var y0 = Math.Clamp((int)area.Y - my, 0, frame.Height - 1);
            var x1 = Math.Clamp((int)area.Right + mx, x0 + 1, frame.Width);
            var y1 = Math.Clamp((int)area.Bottom + my, y0 + 1, frame.Height);
            var roi = new Rect(x0, y0, x1 - x0, y1 - y0);

            using var sub = new Mat(image, roi);
            using var hsv = new Mat();
            using var mask = new Mat();
            Cv2.CvtColor(sub, hsv, ColorConversionCodes.BGR2HSV);
            Cv2.InRange(hsv, new Scalar(0, 0, BrightnessThreshold), new Scalar(180, 255, 255), mask);
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
            Cv2.MorphologyEx(mask, mask, MorphTypes.Open, kernel);
            Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);

            var minLength = Math.Max(10, MinLengthRatio * area.Height);
            var lines = Cv2.HoughLinesP(mask, 1, Math.PI / 180, 40, minLength, 10);
            var segs = lines
                .Select(l => (A: new CorePoint(l.P1.X + x0, l.P1.Y + y0), B: new CorePoint(l.P2.X + x0, l.P2.Y + y0)))
                .ToList();

            foreach (var (a, b) in Merge(segs))
            {
                result.Add(new SaberSegment(a, b, 1f));
            }
        }

        return result;
    }

    private List<(CorePoint A, CorePoint B)> Merge(List<(CorePoint A, CorePoint B)> segs)
    {
        var list = segs.OrderByDescending(s => Len(s.A, s.B)).ToList();
        var merged = new List<(CorePoint A, CorePoint B)>();
        foreach (var s in list)
        {
            var found = false;
            for (var i = 0; i < merged.Count; i++)
            {
                if (Collinear(merged[i], s))
                {
                    merged[i] = Extend(merged[i], s);
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                merged.Add(s);
            }
        }

        return merged;
    }

    private static double Len(CorePoint a, CorePoint b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private static double Angle(CorePoint a, CorePoint b) => Math.Atan2(b.Y - a.Y, b.X - a.X);

    private bool Collinear((CorePoint A, CorePoint B) m, (CorePoint A, CorePoint B) s)
    {
        var diff = Math.Abs(Angle(m.A, m.B) - Angle(s.A, s.B)) * 180 / Math.PI % 180;
        if (Math.Min(diff, 180 - diff) > MergeAngleDegrees)
        {
            return false;
        }

        return PerpDistance(m, s.A) <= MergeDistance && PerpDistance(m, s.B) <= MergeDistance;
    }

    private static double PerpDistance((CorePoint A, CorePoint B) l, CorePoint p)
    {
        var dx = l.B.X - l.A.X;
        var dy = l.B.Y - l.A.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        return len < 1e-6 ? Len(l.A, p) : Math.Abs(dy * (p.X - l.A.X) - dx * (p.Y - l.A.Y)) / len;
    }

    private static (CorePoint A, CorePoint B) Extend((CorePoint A, CorePoint B) m, (CorePoint A, CorePoint B) s)
    {
        var dx = m.B.X - m.A.X;
        var dy = m.B.Y - m.A.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6)
        {
            return m;
        }

        double T(CorePoint p) => ((p.X - m.A.X) * dx + (p.Y - m.A.Y) * dy) / len;
        var pts = new[] { m.A, m.B, s.A, s.B };
        var min = pts.MinBy(T);
        var max = pts.MaxBy(T);
        return (min, max);
    }
}
