using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using lightsaber_assist.Core.Analysis;

namespace lightsaber_assist.Vision;

/// <summary>
/// Détecteur YOLO11-pose ONNX (sortie [1, 56, N] : cx, cy, w, h, score, 17 x (x, y, conf)).
/// </summary>
public sealed class YoloPoseDetector : IPoseDetector, IDisposable
{
    public const int InputSize = 640;

    private readonly InferenceSession session;
    private readonly string inputName;
    private readonly float scoreThreshold;
    private readonly float nmsThreshold;
    private readonly int maxFighters;

    public YoloPoseDetector(string modelPath, float scoreThreshold = 0.25f, float nmsThreshold = 0.5f, int maxFighters = 10)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"Modèle YOLO11-pose ONNX introuvable : {modelPath}", modelPath);
        }

        session = new InferenceSession(modelPath);
        inputName = session.InputMetadata.Keys.First();
        this.scoreThreshold = scoreThreshold;
        this.nmsThreshold = nmsThreshold;
        this.maxFighters = maxFighters;
    }

    public IReadOnlyList<(BoundingBox Box, Skeleton Skeleton)> Detect(RawFrame frame)
    {
        var (input, scale, padX, padY) = Preprocess(frame);
        using var results = session.Run([NamedOnnxValue.CreateFromTensor(inputName, input)]);
        var output = results.First().AsTensor<float>();
        return Decode(output.ToArray(), output.Dimensions[1], output.Dimensions[2], scale, padX, padY,
            frame.Width, frame.Height, scoreThreshold, nmsThreshold, maxFighters);
    }

    public void Dispose() => session.Dispose();

    internal static (DenseTensor<float> Tensor, float Scale, float PadX, float PadY) Preprocess(RawFrame frame)
    {
        var scale = Math.Min((float)InputSize / frame.Width, (float)InputSize / frame.Height);
        var newW = (int)Math.Round(frame.Width * scale);
        var newH = (int)Math.Round(frame.Height * scale);
        var padX = (InputSize - newW) / 2f;
        var padY = (InputSize - newH) / 2f;
        var offX = (int)padX;
        var offY = (int)padY;

        var tensor = new DenseTensor<float>([1, 3, InputSize, InputSize]);
        var span = tensor.Buffer.Span;
        span.Fill(114f / 255f);
        var plane = InputSize * InputSize;

        for (var y = 0; y < newH; y++)
        {
            var sy = Math.Clamp((y + 0.5f) / scale - 0.5f, 0, frame.Height - 1);
            var y0 = (int)sy;
            var y1 = Math.Min(y0 + 1, frame.Height - 1);
            var fy = sy - y0;
            for (var x = 0; x < newW; x++)
            {
                var sx = Math.Clamp((x + 0.5f) / scale - 0.5f, 0, frame.Width - 1);
                var x0 = (int)sx;
                var x1 = Math.Min(x0 + 1, frame.Width - 1);
                var fx = sx - x0;
                var dst = (y + offY) * InputSize + x + offX;
                for (var c = 0; c < 3; c++)
                {
                    var top = Lerp(frame.Bgr[(y0 * frame.Width + x0) * 3 + c], frame.Bgr[(y0 * frame.Width + x1) * 3 + c], fx);
                    var bottom = Lerp(frame.Bgr[(y1 * frame.Width + x0) * 3 + c], frame.Bgr[(y1 * frame.Width + x1) * 3 + c], fx);
                    // BGR -> RGB
                    span[(2 - c) * plane + dst] = Lerp(top, bottom, fy) / 255f;
                }
            }
        }

        return (tensor, scale, padX, padY);
    }

    internal static IReadOnlyList<(BoundingBox, Skeleton)> Decode(
        float[] data, int channels, int count, float scale, float padX, float padY,
        int imgW, int imgH, float scoreThreshold, float nmsThreshold, int maxFighters)
    {
        var candidates = new List<(BoundingBox Box, Skeleton Skeleton)>();
        for (var i = 0; i < count; i++)
        {
            var score = data[4 * count + i];
            if (score < scoreThreshold)
            {
                continue;
            }

            var cx = (data[i] - padX) / scale;
            var cy = (data[count + i] - padY) / scale;
            var w = data[2 * count + i] / scale;
            var h = data[3 * count + i] / scale;
            var box = new BoundingBox(
                Math.Clamp(cx - w / 2, 0, imgW), Math.Clamp(cy - h / 2, 0, imgH), w, h, score);

            var skeleton = new Skeleton();
            for (var k = 0; k < Skeleton.KeypointCount; k++)
            {
                var kx = (data[(5 + k * 3) * count + i] - padX) / scale;
                var ky = (data[(6 + k * 3) * count + i] - padY) / scale;
                var kc = data[(7 + k * 3) * count + i];
                skeleton.Keypoints[k] = new Keypoint(kx, ky, kc);
            }

            candidates.Add((box, skeleton));
        }

        var kept = new List<(BoundingBox Box, Skeleton Skeleton)>();
        foreach (var c in candidates.OrderByDescending(c => c.Box.Confidence))
        {
            if (kept.All(k => IoU(k.Box, c.Box) < nmsThreshold))
            {
                kept.Add(c);
                if (kept.Count == maxFighters)
                {
                    break;
                }
            }
        }

        return kept;
    }

    internal static float IoU(BoundingBox a, BoundingBox b)
    {
        var ix = Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X));
        var iy = Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y));
        var inter = ix * iy;
        var union = a.Width * a.Height + b.Width * b.Height - inter;
        return union <= 0 ? 0 : inter / union;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
