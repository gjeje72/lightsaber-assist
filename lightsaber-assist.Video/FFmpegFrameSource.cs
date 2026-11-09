using lightsaber_assist.Core.Analysis;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace lightsaber_assist.Video;

/// <summary>
/// Source de frames basée sur les exécutables FFmpeg (ffmpeg.exe / ffprobe.exe).
/// Les frames sont indexées par numéro exact (index = round(temps * fps)).
/// </summary>
public sealed class FFmpegFrameSource : IFrameSource
{
    private readonly string videoPath;
    private readonly string ffmpegPath;

    public FFmpegFrameSource(string videoPath, string? ffmpegDirectory = null)
    {
        if (!File.Exists(videoPath))
        {
            throw new FileNotFoundException("Vidéo introuvable.", videoPath);
        }

        this.videoPath = videoPath;
        ffmpegPath = Resolve("ffmpeg", ffmpegDirectory);
        Info = Probe(Resolve("ffprobe", ffmpegDirectory), videoPath);
    }

    public VideoInfo Info { get; }

    public static string Resolve(string tool, string? directory)
    {
        var exe = OperatingSystem.IsWindows() ? tool + ".exe" : tool;
        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(directory))
        {
            candidates.Add(Path.Combine(directory, exe));
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "ffmpeg", exe));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "Assets", "ffmpeg", exe));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, exe));
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (!string.IsNullOrWhiteSpace(dir))
            {
                candidates.Add(Path.Combine(dir, exe));
            }
        }

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException(
                $"{exe} est introuvable. Placez les binaires FFmpeg dans le dossier 'ffmpeg' à côté de l'application ou ajoutez-les au PATH.");
    }

    public async IAsyncEnumerable<RawFrame> ReadFramesAsync(
        int step, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        step = Math.Max(1, step);
        var filter = step > 1 ? $"-vf \"select=not(mod(n\\,{step}))\" -fps_mode passthrough " : "-fps_mode passthrough ";
        using var process = Start($"-v error -i \"{videoPath}\" {filter}-f rawvideo -pix_fmt bgr24 -");
        var size = Info.Width * Info.Height * 3;
        var stream = process.StandardOutput.BaseStream;
        var i = 0;
        try
        {
            while (true)
            {
                var buffer = new byte[size];
                if (!await ReadExactlyOrEndAsync(stream, buffer, cancellationToken))
                {
                    yield break;
                }

                var index = i * step;
                yield return new RawFrame(index, TimeSpan.FromSeconds(index / Info.Fps), Info.Width, Info.Height, buffer);
                i++;
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
    }

    public RawFrame? GetFrame(int frameIndex)
    {
        var time = (frameIndex / Info.Fps).ToString("0.######", CultureInfo.InvariantCulture);
        using var process = Start($"-v error -ss {time} -i \"{videoPath}\" -frames:v 1 -f rawvideo -pix_fmt bgr24 -");
        var buffer = new byte[Info.Width * Info.Height * 3];
        var stream = process.StandardOutput.BaseStream;
        var read = 0;
        while (read < buffer.Length)
        {
            var n = stream.Read(buffer, read, buffer.Length - read);
            if (n == 0)
            {
                return null;
            }

            read += n;
        }

        process.WaitForExit();
        return new RawFrame(frameIndex, TimeSpan.FromSeconds(frameIndex / Info.Fps), Info.Width, Info.Height, buffer);
    }

    public void Dispose()
    {
    }

    private Process Start(string arguments)
    {
        var psi = new ProcessStartInfo(ffmpegPath, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var process = Process.Start(psi) ?? throw new InvalidOperationException("Impossible de démarrer ffmpeg.");
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task<bool> ReadExactlyOrEndAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0)
            {
                return false;
            }

            read += n;
        }

        return true;
    }

    private static VideoInfo Probe(string ffprobe, string path)
    {
        var psi = new ProcessStartInfo(ffprobe,
            $"-v error -select_streams v:0 -count_packets -show_entries stream=width,height,r_frame_rate,nb_read_packets,duration -of json \"{path}\"")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Impossible de démarrer ffprobe.");
        var json = p.StandardOutput.ReadToEnd();
        p.WaitForExit();

        using var doc = JsonDocument.Parse(json);
        var s = doc.RootElement.GetProperty("streams")[0];
        var width = s.GetProperty("width").GetInt32();
        var height = s.GetProperty("height").GetInt32();
        var rate = s.GetProperty("r_frame_rate").GetString()!.Split('/');
        var fps = double.Parse(rate[0], CultureInfo.InvariantCulture) / (rate.Length > 1 ? double.Parse(rate[1], CultureInfo.InvariantCulture) : 1);
        var frames = int.Parse(s.GetProperty("nb_read_packets").GetString()!, CultureInfo.InvariantCulture);
        var duration = s.TryGetProperty("duration", out var d) && double.TryParse(d.GetString(), CultureInfo.InvariantCulture, out var sec)
            ? TimeSpan.FromSeconds(sec)
            : TimeSpan.FromSeconds(frames / fps);
        return new VideoInfo(width, height, fps, frames, duration);
    }
}
