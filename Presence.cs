namespace EasyRPC;

public class Presence
{
    public string? Details { get; set; }
    public string? State { get; set; }
    public string? LargeImageKey { get; set; }
    public string? LargeImageText { get; set; }
    public string? SmallImageKey { get; set; }
    public string? SmallImageText { get; set; }
    public DateTime? StartTimestamp { get; set; }
    public DateTime? EndTimestamp { get; set; }
    public string? PartyId { get; set; }
    public int? PartySize { get; set; }
    public int? PartyMax { get; set; }
    public List<Button>? Buttons { get; set; }
}