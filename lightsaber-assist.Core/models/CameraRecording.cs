public sealed class CameraRecording
{
    public int CameraNumber { get; set; }

    public string FilePath { get; set; } = string.Empty;

    public TimeSpan Offset { get; set; }
}