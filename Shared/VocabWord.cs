namespace Megruli.Shared;

public class VocabWord
{
    public string Id { get; set; } = "";
    public string Megruli { get; set; } = "";
    public string Georgian { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Notes { get; set; }
    public string? AudioClipId { get; set; }
}
