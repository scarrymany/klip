namespace Klip.Models;

public sealed class ClipItem
{
    public long Id { get; set; }
    public long? CollectionId { get; set; }
    public string? Title { get; set; }
    public string Content { get; set; } = "";
    public string Kind { get; set; } = "clip";
    public string Color { get; set; } = "none";
    public bool Pinned { get; set; }
    public int CopyCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastCopiedAt { get; set; }

    public string DisplayTitle =>
        string.IsNullOrWhiteSpace(Title) ? Preview(Content, 48) : Title;

    public string PreviewText => Preview(Content, 180);

    public string KindLabel => Kind switch
    {
        "note" => "Заметка",
        "code" => "Код",
        "link" => "Ссылка",
        _ => "Фрагмент",
    };

    public static string DetectKind(string content)
    {
        var text = content.Trim();
        if (text.Length == 0) return "clip";
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !text.Contains('\n'))
        {
            return "link";
        }

        var lines = text.Split('\n');
        if (lines.Length >= 4 &&
            (text.Contains('{') || text.Contains(';') || text.Contains("function ")
             || text.Contains("const ") || text.Contains("class ") || text.Contains("def ")
             || text.Contains("import ") || text.Contains("fn ")))
        {
            return "code";
        }

        return text.Length > 320 || lines.Length >= 6 ? "note" : "clip";
    }

    public static string Preview(string content, int max)
    {
        var compact = string.Join(' ', content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= max ? compact : compact[..max].TrimEnd() + "…";
    }
}
