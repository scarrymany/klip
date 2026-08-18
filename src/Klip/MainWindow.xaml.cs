using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Klip.Models;
using Klip.Services;

namespace Klip;

public partial class MainWindow : Window
{
    private readonly ClipStore _store = new();
    private readonly ClipboardWatcher _clipboard = new();
    private readonly HotkeyService _hotkey = new();
    private readonly DispatcherTimer _toastTimer;
    private readonly ObservableCollection<ClipItem> _visible = [];
    private string _filter = "all";
    private bool _exit;
    private bool _startupReady;
    private ClipItem? _editing;
    private string _editKind = ClipKinds.Clip;
    private string _editColor = ClipColors.None;

    public ObservableCollection<ClipItem> VisibleClips => _visible;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
        _toastTimer.Tick += (_, _) =>
        {
            ToastHost.Visibility = Visibility.Collapsed;
            _toastTimer.Stop();
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        AcrylicHelper.TryApply(this);
        if (AcrylicHelper.IsWindows11())
            WindowFrame.Background = Brushes.Transparent;

        _clipboard.Start();
        if (_clipboard.Source is { } source)
            _hotkey.Attach(source);

        _clipboard.TextCaptured += (_, text) => Dispatcher.Invoke(() =>
        {
            _store.TryAddFromClipboard(text);
            Reload();
            Toast("Сохранено в буфер");
        });
        _hotkey.Activated += (_, _) => Dispatcher.Invoke(Reveal);

        StartupCheck.IsChecked = StartupService.IsEnabled();
        _startupReady = true;
        Reload();
    }

    public void Reveal()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        SearchBox.Focus();
    }

    public void PrepareExit()
    {
        _exit = true;
        _hotkey.Dispose();
        _clipboard.Dispose();
        _store.Dispose();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exit) return;
        e.Cancel = true;
        Hide();
    }

    private void Reload()
    {
        var q = SearchBox.Text.Trim();
        var all = _store.ListClips();
        var filtered = all.Where(clip =>
        {
            if (_filter == "pinned" && !clip.Pinned) return false;
            if (_filter is "clip" or "note" or "code" or "link" && clip.Kind != _filter) return false;
            if (_filter.StartsWith("folder:", StringComparison.Ordinal)
                && clip.CollectionId != long.Parse(_filter["folder:".Length..]))
            {
                return false;
            }
            if (q.Length == 0) return true;
            return clip.Content.Contains(q, StringComparison.OrdinalIgnoreCase)
                   || (clip.Title?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
        }).ToList();

        _visible.Clear();
        foreach (var item in filtered)
            _visible.Add(item);

        TitleCount.Text = $"{all.Count} записей";
        CountAll.Text = all.Count.ToString();
        CountPinned.Text = all.Count(x => x.Pinned).ToString();
        CountClip.Text = all.Count(x => x.Kind == ClipKinds.Clip).ToString();
        CountNote.Text = all.Count(x => x.Kind == ClipKinds.Note).ToString();
        CountCode.Text = all.Count(x => x.Kind == ClipKinds.Code).ToString();
        CountLink.Text = all.Count(x => x.Kind == ClipKinds.Link).ToString();
        FolderList.ItemsSource = _store.ListCollections();
        EmptyState.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = all.Count == 0 ? "Буфер ещё пуст" : "Ничего не нашлось";
        EmptyHint.Text = all.Count == 0
            ? "Скопируйте текст в любой программе — он появится здесь."
            : "Смените фильтр или запрос.";
    }

    private ClipItem? Selected => ClipList.SelectedItem as ClipItem
        ?? (ClipList.SelectedIndex >= 0 && ClipList.SelectedIndex < _visible.Count
            ? _visible[ClipList.SelectedIndex]
            : null);

    private void CopyItem(ClipItem item)
    {
        _clipboard.CopyText(item.Content);
        _store.MarkCopied(item.Id);
        Reload();
        Toast("Скопировано");
    }

    private void Toast(string text)
    {
        ToastText.Text = text;
        ToastHost.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void OnNavClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string tag })
        {
            _filter = tag;
            Reload();
        }
    }

    private void OnFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: long id })
        {
            _filter = $"folder:{id}";
            Reload();
        }
        else if (sender is System.Windows.Controls.Button { DataContext: CollectionItem folder })
        {
            _filter = $"folder:{folder.Id}";
            Reload();
        }
    }

    private void OnDeleteFolder(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CollectionItem folder }) return;
        _store.DeleteCollection(folder.Id);
        if (_filter == $"folder:{folder.Id}") _filter = "all";
        Reload();
    }

    private void OnAddFolder(object sender, RoutedEventArgs e) => AddFolder();

    private void OnFolderNameKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddFolder();
            e.Handled = true;
        }
    }

    private void AddFolder()
    {
        try
        {
            _store.AddCollection(FolderNameBox.Text);
            FolderNameBox.Clear();
            Reload();
        }
        catch (Exception ex)
        {
            Toast(ex.Message);
        }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Reload();

    private void OnNewNote(object sender, RoutedEventArgs e) => OpenEditor(null);

    private void OnClipClick(object sender, MouseButtonEventArgs e)
    {
        if (FindParent<System.Windows.Controls.Button>(e.OriginalSource as DependencyObject) is not null)
            return;
        if (ClipList.SelectedItem is ClipItem item)
            CopyItem(item);
    }

    private void OnClipDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ClipList.SelectedItem is ClipItem item)
            OpenEditor(item);
    }

    private void OnListKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Selected is { } copy)
        {
            CopyItem(copy);
            e.Handled = true;
        }
        if (e.Key == Key.Delete && Selected is { } del)
        {
            _store.Delete(del.Id);
            Reload();
            e.Handled = true;
        }
    }

    private void OnCopyMenu(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item) CopyItem(item);
    }

    private void OnPinMenu(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item)
        {
            _store.SetPinned(item.Id, !item.Pinned);
            Reload();
        }
    }

    private void OnEditMenu(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item) OpenEditor(item);
    }

    private void OnColorMenu(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        if (sender is MenuItem { Tag: string color })
        {
            _store.SetColor(item.Id, color);
            Reload();
        }
    }

    private void OnDeleteMenu(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item)
        {
            _store.Delete(item.Id);
            Reload();
        }
    }

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ClipItem item })
        {
            _store.SetPinned(item.Id, !item.Pinned);
            Reload();
        }
    }

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ClipItem item })
            OpenEditor(item);
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ClipItem item })
        {
            _store.Delete(item.Id);
            Reload();
        }
    }

    private void OpenEditor(ClipItem? item)
    {
        _editing = item;
        EditorTitle.Text = item is null ? "Новая запись" : "Запись";
        EditorName.Text = item?.Title ?? "";
        EditorBody.Text = item?.Content ?? "";
        _editKind = item?.Kind ?? ClipKinds.Clip;
        _editColor = item?.Color ?? ClipColors.None;
        SyncKindChips();
        EditorFolder.ItemsSource = _store.ListCollections();
        EditorFolder.DisplayMemberPath = "Name";
        EditorFolder.SelectedValuePath = "Id";
        EditorFolder.SelectedValue = item?.CollectionId;
        EditorOverlay.Visibility = Visibility.Visible;
        EditorBody.Focus();
    }

    private void SyncKindChips()
    {
        KindClip.IsChecked = _editKind == ClipKinds.Clip;
        KindNote.IsChecked = _editKind == ClipKinds.Note;
        KindCode.IsChecked = _editKind == ClipKinds.Code;
        KindLink.IsChecked = _editKind == ClipKinds.Link;
    }

    private void OnKindChip(object sender, RoutedEventArgs e)
    {
        if (sender == KindClip) _editKind = ClipKinds.Clip;
        else if (sender == KindNote) _editKind = ClipKinds.Note;
        else if (sender == KindCode) _editKind = ClipKinds.Code;
        else if (sender == KindLink) _editKind = ClipKinds.Link;
        SyncKindChips();
    }

    private void OnColorDot(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string color })
            _editColor = color;
    }

    private void OnEditorCancel(object sender, RoutedEventArgs e)
    {
        EditorOverlay.Visibility = Visibility.Collapsed;
        _editing = null;
    }

    private void OnEditorSave(object sender, RoutedEventArgs e)
    {
        var body = EditorBody.Text.Trim();
        if (body.Length == 0)
        {
            Toast("Пустой фрагмент");
            return;
        }

        long? folderId = EditorFolder.SelectedValue is long id ? id : null;
        var title = string.IsNullOrWhiteSpace(EditorName.Text) ? null : EditorName.Text.Trim();
        if (_editing is null)
        {
            _store.AddNote(title, body, _editKind, _editColor, folderId);
        }
        else
        {
            _editing.Title = title;
            _editing.Content = body;
            _editing.Kind = _editKind;
            _editing.Color = _editColor;
            _editing.CollectionId = folderId;
            _store.Update(_editing);
        }
        EditorOverlay.Visibility = Visibility.Collapsed;
        _editing = null;
        Reload();
        Toast("Сохранено");
    }

    private void OnStartupChanged(object sender, RoutedEventArgs e)
    {
        if (!_startupReady) return;
        StartupService.SetEnabled(StartupCheck.IsChecked == true);
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (MaxIcon is not null)
            MaxIcon.Data = Geometry.Parse(
                WindowState == WindowState.Maximized
                    ? "M4,8 H16 V20 H4 Z M8,4 H20 V16"
                    : "M4,4 H20 V20 H4 Z");
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (EditorOverlay.Visibility == Visibility.Visible)
            {
                EditorOverlay.Visibility = Visibility.Collapsed;
                e.Handled = true;
            }
            else
            {
                Hide();
            }
        }
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.N)
        {
            OpenEditor(null);
            e.Handled = true;
        }
    }

    private static T? FindParent<T>(DependencyObject? start) where T : DependencyObject
    {
        while (start is not null)
        {
            if (start is T match) return match;
            start = VisualTreeHelper.GetParent(start);
        }
        return null;
    }
}
