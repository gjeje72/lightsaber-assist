using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using lightsaber_assist.Core.Analysis;

namespace lightsaber_assist.Storage;

internal static class StorageJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lightsaber-assist", "cache");

    /// <summary>Clé stable : chemin + taille + date de modification (évite de hacher toute la vidéo).</summary>
    public static string VideoKey(string videoPath)
    {
        var info = new FileInfo(videoPath);
        var raw = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..32];
    }
}

public sealed class JsonAnalysisExporter : IAnalysisExporter
{
    public async Task ExportAsync(AnalysisResult result, string destinationPath, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(destinationPath);
        await JsonSerializer.SerializeAsync(stream, result, StorageJson.Options, cancellationToken);
    }
}

public sealed class JsonAnalysisCache : IAnalysisCache
{
    private readonly string root;

    public JsonAnalysisCache(string? root = null) => this.root = root ?? StorageJson.DefaultRoot;

    public async Task<AnalysisResult?> LoadAsync(string videoPath, string modelVersion, CancellationToken cancellationToken)
    {
        var path = PathFor(videoPath);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        var result = await JsonSerializer.DeserializeAsync<AnalysisResult>(stream, StorageJson.Options, cancellationToken);
        return result?.ModelVersion == modelVersion ? result : null;
    }

    public async Task SaveAsync(AnalysisResult result, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root);
        await using var stream = File.Create(PathFor(result.VideoPath));
        await JsonSerializer.SerializeAsync(stream, result, StorageJson.Options, cancellationToken);
    }

    private string PathFor(string videoPath) => Path.Combine(root, StorageJson.VideoKey(videoPath) + ".analysis.json");
}

public sealed class JsonAnnotationStore : IAnnotationStore
{
    private readonly string root;

    public JsonAnnotationStore(string? root = null) => this.root = root ?? StorageJson.DefaultRoot;

    public async Task<List<Annotation>> LoadAsync(string videoPath, CancellationToken cancellationToken)
    {
        var path = PathFor(videoPath);
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<Annotation>>(stream, StorageJson.Options, cancellationToken) ?? [];
    }

    public async Task SaveAsync(string videoPath, IReadOnlyList<Annotation> annotations, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root);
        await using var stream = File.Create(PathFor(videoPath));
        await JsonSerializer.SerializeAsync(stream, annotations, StorageJson.Options, cancellationToken);
    }

    private string PathFor(string videoPath) => Path.Combine(root, StorageJson.VideoKey(videoPath) + ".annotations.json");
}
