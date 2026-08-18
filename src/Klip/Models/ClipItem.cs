using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace Klip.Models;

public static class ClipKinds
{
    public const string Clip = "clip";
    public const string Note = "note";
    public const string Code = "code";
    public const string Link = "link";

    public static readonly string[] All = [Clip, Note, Code, Link];

    public static string Detect(string content)
    {
        var text = content.Trim();
        if (text.Length == 0)
            return Clip;

        if (Regex.IsMatch(text, @"^https?://\S+$", RegexOptions.IgnoreCase) && !text.Contains('\n'))
            return Link;

        var lines = text.Split('\n');
        if (lines.Length >= 4 &&
            Regex.IsMatch(text, @"[{};=<>]|function |const |let |class |def |import |#include |fn |pub "))
        {
            return Code;
        }

        if (text.Length > 320 || lines.Length >= 6)
            return Note;

        return Clip;
    }

    public static string Label(string kind) => kind switch
    {
        Note => "Заметка",
        Code => "Код",
        Link => "Ссылка",
        _ => "Фрагмент",
    };
}

public static class ClipColors
{
    public const string None = "none";
    public const string Sky = "sky";
    public const string Sage = "sage";
    public const string Clay = "clay";
    public const string Sand = "sand";

    public static readonly string[] All = [None, Sky, Sage, Clay, Sand];

    public static readonly Color SkyColor = Color.FromRgb(0x7A, 0xA2, 0xC4);
    public static readonly Color SageColor = Color.FromRgb(0x7A, 0x9A, 0x86);
    public static readonly Color ClayColor = Color.FromRgb(0xC4, 0x8A, 0x7A);
    public static readonly Color SandColor = Color.FromRgb(0xC4, 0xB4, 0x9A);
    public static readonly Color NoneColor = Color.FromArgb(0x55, 0xEE, 0xF1, 0xF5);

    public static readonly Brush SkyBrush = Freeze(SkyColor);
    public static readonly Brush SageBrush = Freeze(SageColor);
    public static readonly Brush ClayBrush = Freeze(ClayColor);
    public static readonly Brush SandBrush = Freeze(SandColor);
    public static readonly Brush NoneBrush = Freeze(NoneColor);

    public static Brush BrushFor(string color) => color switch
    {
        Sky => SkyBrush,
        Sage => SageBrush,
        Clay => ClayBrush,
        Sand => SandBrush,
        _ => NoneBrush,
    };

    public static string Label(string color) => color switch
    {
        Sky => "Небо",
        Sage => "Шалфей",
        Clay => "Глина",
        Sand => "Песок",
        _ => "Без метки",
    };

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

public sealed class ClipItem : INotifyPropertyChanged
{
    private string? _title;
    private string _content = "";
    private string _kind = ClipKinds.Clip;
    private string _color = ClipColors.None;
    private bool _pinned;
    private int _copyCount;
    private DateTime? _lastCopiedAt;
    private DateTime _updatedAt;
    private long? _collectionId;

    public event PropertyChangedEventHandler? PropertyChanged;

    public long Id { get; set; }

    public long? CollectionId
    {
        get => _collectionId;
        set => Set(ref _collectionId, value);
    }

    public string? Title
    {
        get => _title;
        set
        {
            if (Set(ref _title, value))
                OnPropertyChanged(nameof(DisplayTitle));
        }
    }

    public string Content
    {
        get => _content;
        set
        {
            if (Set(ref _content, value))
            {
                OnPropertyChanged(nameof(DisplayTitle));
                OnPropertyChanged(nameof(Preview));
                OnPropertyChanged(nameof(IsCode));
            }
        }
    }

    public string Kind
    {
        get => _kind;
        set
        {
            if (Set(ref _kind, value))
            {
                OnPropertyChanged(nameof(KindLabel));
                OnPropertyChanged(nameof(IsCode));
            }
        }
    }

    public string Color
    {
        get => _color;
        set
        {
            if (Set(ref _color, value))
                OnPropertyChanged(nameof(MarkBrush));
        }
    }

    public bool Pinned
    {
        get => _pinned;
        set => Set(ref _pinned, value);
    }

    public int CopyCount
    {
        get => _copyCount;
        set
        {
            if (Set(ref _copyCount, value))
                OnPropertyChanged(nameof(MetaLabel));
        }
    }

    public DateTime? LastCopiedAt
    {
        get => _lastCopiedAt;
        set => Set(ref _lastCopiedAt, value);
    }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt
    {
        get => _updatedAt;
        set
        {
            if (Set(ref _updatedAt, value))
                OnPropertyChanged(nameof(MetaLabel));
        }
    }

    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title))
                return Title.Trim();
            return PreviewText(Content, 48);
        }
    }

    public string Preview => PreviewText(Content, 220);

    public string KindLabel => ClipKinds.Label(Kind);

    public bool IsCode => Kind == ClipKinds.Code;

    public Brush MarkBrush => ClipColors.BrushFor(Color);

    public string MetaLabel
    {
        get
        {
            var time = FormatRelative(UpdatedAt);
            if (CopyCount <= 0)
                return time;
            return $"{time}  ·  {FormatCopies(CopyCount)}";
        }
    }

    public static string PreviewText(string content, int max)
    {
        var compact = Regex.Replace(content, @"\s+", " ").Trim();
        if (compact.Length <= max)
            return compact;
        return compact[..max].TrimEnd() + "…";
    }

    public static string FormatRelative(DateTime utc)
    {
        var local = utc.Kind == DateTimeKind.Utc ? utc.ToLocalTime() : utc;
        var now = DateTime.Now;
        var span = now - local;
        if (span.TotalSeconds < 45)
            return "только что";
        if (span.TotalMinutes < 60)
            return $"{Math.Max(1, (int)span.TotalMinutes)} мин. назад";
        if (span.TotalHours < 24)
            return $"{Math.Max(1, (int)span.TotalHours)} ч. назад";
        if (local.Date == now.Date.AddDays(-1))
            return "вчера";
        var ru = CultureInfo.GetCultureInfo("ru-RU");
        if (local.Year == now.Year)
            return local.ToString("d MMM", ru);
        return local.ToString("d MMM yyyy", ru);
    }

    public static string FormatCopies(int count)
    {
        var n = Math.Abs(count) % 100;
        var n1 = n % 10;
        if (n > 10 && n < 20)
            return $"{count} копий";
        if (n1 == 1)
            return $"{count} копия";
        if (n1 is >= 2 and <= 4)
            return $"{count} копии";
        return $"{count} копий";
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
