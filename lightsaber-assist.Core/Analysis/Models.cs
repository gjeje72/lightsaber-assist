namespace lightsaber_assist.Core.Analysis;

public readonly record struct BoundingBox(float X, float Y, float Width, float Height, float Confidence)
{
    public float Right => X + Width;

    public float Bottom => Y + Height;

    public PointF Center => new(X + Width / 2f, Y + Height / 2f);
}

public readonly record struct PointF(float X, float Y);

public readonly record struct Keypoint(float X, float Y, float Confidence);

public enum CocoKeypoint
{
    Nose = 0,
    LeftEye = 1,
    RightEye = 2,
    LeftEar = 3,
    RightEar = 4,
    LeftShoulder = 5,
    RightShoulder = 6,
    LeftElbow = 7,
    RightElbow = 8,
    LeftWrist = 9,
    RightWrist = 10,
    LeftHip = 11,
    RightHip = 12,
    LeftKnee = 13,
    RightKnee = 14,
    LeftAnkle = 15,
    RightAnkle = 16,
}

public sealed class Skeleton
{
    public const int KeypointCount = 17;

    public static readonly IReadOnlyList<(CocoKeypoint From, CocoKeypoint To)> Bones =
    [
        (CocoKeypoint.Nose, CocoKeypoint.LeftEye), (CocoKeypoint.Nose, CocoKeypoint.RightEye),
        (CocoKeypoint.LeftEye, CocoKeypoint.LeftEar), (CocoKeypoint.RightEye, CocoKeypoint.RightEar),
        (CocoKeypoint.LeftShoulder, CocoKeypoint.RightShoulder),
        (CocoKeypoint.LeftShoulder, CocoKeypoint.LeftElbow), (CocoKeypoint.LeftElbow, CocoKeypoint.LeftWrist),
        (CocoKeypoint.RightShoulder, CocoKeypoint.RightElbow), (CocoKeypoint.RightElbow, CocoKeypoint.RightWrist),
        (CocoKeypoint.LeftShoulder, CocoKeypoint.LeftHip), (CocoKeypoint.RightShoulder, CocoKeypoint.RightHip),
        (CocoKeypoint.LeftHip, CocoKeypoint.RightHip),
        (CocoKeypoint.LeftHip, CocoKeypoint.LeftKnee), (CocoKeypoint.LeftKnee, CocoKeypoint.LeftAnkle),
        (CocoKeypoint.RightHip, CocoKeypoint.RightKnee), (CocoKeypoint.RightKnee, CocoKeypoint.RightAnkle),
    ];

    public Keypoint[] Keypoints { get; set; } = new Keypoint[KeypointCount];

    public Keypoint this[CocoKeypoint k] => Keypoints[(int)k];
}

public sealed class FighterFrame
{
    public int FighterId { get; set; }

    public BoundingBox Box { get; set; }

    public Skeleton Skeleton { get; set; } = new();

    public PointF CenterOfGravity { get; set; }
}

public sealed class FrameAnalysis
{
    public int FrameIndex { get; set; }

    public TimeSpan Timestamp { get; set; }

    public List<FighterFrame> Fighters { get; set; } = [];

    public double? DistancePixels { get; set; }

    public double? DistanceNormalized { get; set; }
}

public sealed class AnalysisResult
{
    public string VideoPath { get; set; } = string.Empty;

    public int Width { get; set; }

    public int Height { get; set; }

    public double Fps { get; set; }

    public string ModelVersion { get; set; } = string.Empty;

    public List<FrameAnalysis> Frames { get; set; } = [];

    public List<Annotation> Annotations { get; set; } = [];
}

public enum AnnotationType
{
    ArmeSimple,
    ArmeEngagement,
    Touche,
}

public sealed class Annotation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public TimeSpan Timestamp { get; set; }

    public int FrameIndex { get; set; }

    public AnnotationType Type { get; set; }

    public string? Comment { get; set; }

    public override string ToString()
    {
        var comment = string.IsNullOrWhiteSpace(Comment) ? string.Empty : " - " + Comment;
        return $"#{FrameIndex} {Timestamp.ToString(@"mm\:ss\.ff")} {Type}{comment}";
    }
}
