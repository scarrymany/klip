namespace Klip.Models;

public sealed class CollectionItem
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int Count { get; set; }
}
