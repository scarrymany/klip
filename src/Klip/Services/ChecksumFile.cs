namespace Klip.Services;

public static class ChecksumFile
{
    public static string? FindHash(string text, string assetName)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0)
                continue;

            var space = line.IndexOfAny([' ', '\t']);
            if (space <= 0)
                continue;

            var hash = line[..space].Trim();
            var name = line[space..].Trim().TrimStart('*');
            if (name.Equals(assetName, StringComparison.OrdinalIgnoreCase) && hash.Length >= 32)
                return hash;
        }

        return null;
    }

    public static string RequireHash(string text, string assetName)
        => FindHash(text, assetName)
           ?? throw new InvalidOperationException("В SHA256SUMS.txt нет этого файла.");
}
