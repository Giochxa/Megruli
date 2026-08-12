namespace Megruli.Shared;

/// <summary>An auto-sliced candidate clip from a source lesson recording, as produced by Megruli.AudioSlicer.</summary>
public class AudioClip
{
    public string Id { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public int StartMs { get; set; }
    public int EndMs { get; set; }
    public string ClipFile { get; set; } = "";
}

/// <summary>A user-entered label for a clip, stored client-side (localStorage) and merged over the shipped clip list at runtime.</summary>
public class AudioClipLabel
{
    public string ClipId { get; set; } = "";
    public string? Megruli { get; set; }
    public string? Georgian { get; set; }
    public string? LinkedWordId { get; set; }
    public bool Skipped { get; set; }
}
