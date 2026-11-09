public sealed class Combat
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime Date { get; set; }

    public List<CameraRecording> Recordings { get; set; } = [];
}