using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Klip.Models;
using Klip.Services;
using WpfButton = System.Windows.Controls.Button;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace Klip;

public partial class MainWindow : Window
{
    private readonly ClipStore _store;
    private readonly ClipboardWatcher _watcher;
    private readonly List<ClipItem> _all = [];
    private readonly ObservableCollection<ClipItem> _visible = [];
    private readonly DispatcherTimer _toastTimer;
    private readonly Dictionary<string, WpfButton> _nav = [];

    private string _filter = "all";
    private bool _forceClose;
    private bool _syncingUi;
    private ClipItem? _editing;
    private string _editorKind = ClipKinds.Clip;
    private string _editorColor = ClipColors.None;

    public ObservableCollection<ClipItem> VisibleClips => _visible;

    public MainWindow(ClipStore store, ClipboardWatcher watcher)
    {
        _store = store;
        _watcher = watcher;
        InitializeComponent();
        DataContext = this;

        _nav["all"] = NavAll;
        _nav["pinned"] = NavPinned;
        _nav["clip"] = NavClip;
        _nav["note"] = NavNote;
        _nav["code"] = NavCode;
        _nav["link"] = NavLink;

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
        _toastTimer.Tick += (_, _) =>
        {
            ToastHost.Visibility = Visibility.Collapsed;
            _toastTimer.Stop();
        };

        SearchBox.GotFocus += (_, _) => SearchHint.Visibility = Visibility.Collapsed;
        SearchBox.LostFocus += (_, _) =>
            SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        FolderNameBox.GotFocus += (_, _) => FolderHint.Visibility = Visibility.Collapsed;
        FolderNameBox.LostFocus += (_, _) =>
            FolderHint.Visibility = string.IsNullOrEmpty(FolderNameBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        FolderNameBox.TextChanged += (_, _) =>
        {
            if (FolderNameBox.IsKeyboardFocused)
                FolderHint.Visibility = Visibility.Collapsed;
        };

        ClipList.ContextMenuOpening += OnContextMenuOpening;

        _syncingUi = true;
        StartupCheck.IsChecked = StartupService.IsEnabled();
        _syncingUi = false;

        SelectNav("all");
        Reload(keepSelection: false);
    }

    public void Reload(bool keepSelection = true)
    {
        var selectedId = keepSelection ? (ClipList.SelectedItem as ClipItem)?.Id : null;
        _all.Clear();
        _all.AddRange(_store.ListClips());
        var folders = _store.ListCollections();
        FolderList.ItemsSource = folders;

        CountAll.Text = _all.Count.ToString(CultureInfo.InvariantCulture);
        CountPinned.Text = _all.Count(c => c.Pinned).ToString(CultureInfo.InvariantCulture);
        CountClip.Text = _all.Count(c => c.Kind == ClipKinds.Clip).ToString(CultureInfo.InvariantCulture);
        CountNote.Text = _all.Count(c => c.Kind == ClipKinds.Note).ToString(CultureInfo.InvariantCulture);
        CountCode.Text = _all.Count(c => c.Kind == ClipKinds.Code).ToString(CultureInfo.InvariantCulture);
        CountLink.Text = _all.Count(c => c.Kind == ClipKinds.Link).ToString(CultureInfo.InvariantCulture);
        TitleCount.Text = FormatRecords(_all.Count);

        ApplyFilter(selectedId);
        foreach (var folder in folders)
            folder.IsSelected = _filter == $"collection:{folder.Id}";
        RebuildFolderMenu();
    }

    public void Notify(string text)
    {
        ToastText.Text = text;
        ToastHost.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    public void Reveal()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    public void HideToTray() => Hide();

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    private void ApplyFilter(long? selectId)
    {
        IEnumerable<ClipItem> query = _all;
        if (_filter == "pinned")
            query = query.Where(c => c.Pinned);
        else if (_filter is ClipKinds.Clip or ClipKinds.Note or ClipKinds.Code or ClipKinds.Link)
            query = query.Where(c => c.Kind == _filter);
        else if (_filter.StartsWith("collection:", StringComparison.Ordinal))
        {
            var id = long.Parse(_filter["collection:".Length..], CultureInfo.InvariantCulture);
            query = query.Where(c => c.CollectionId == id);
        }

        var q = SearchBox.Text.Trim();
        if (q.Length > 0)
        {
            query = query.Where(c =>
                c.Content.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                (c.Title?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false));
        }

        var items = query.ToList();
        _visible.Clear();
        foreach (var item in items)
            _visible.Add(item);

        EmptyState.Visibility = _visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClipList.Visibility = _visible.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_all.Count == 0)
        {
            EmptyTitle.Text = "Буфер ещё пуст";
            EmptyHint.Text = "Скопируйте текст в любой программе - он появится здесь. Нажмите на карточку, чтобы вернуть его в буфер.";
        }
        else
        {
            EmptyTitle.Text = "Ничего не нашлось";
            EmptyHint.Text = "Смените фильтр или запрос - записи остаются на месте.";
        }

        if (selectId is { } idToSelect)
        {
            var match = _visible.FirstOrDefault(c => c.Id == idToSelect);
            if (match is not null)
                ClipList.SelectedItem = match;
        }
    }

    private void SelectNav(string filter)
    {
        _filter = filter;
        foreach (var (key, button) in _nav)
            button.Tag = key == filter ? "active" : key;
        ApplyFilter((ClipList.SelectedItem as ClipItem)?.Id);
        if (FolderList.ItemsSource is IEnumerable<CollectionItem> folders)
        {
            foreach (var folder in folders)
                folder.IsSelected = false;
        }
    }

    private void OnNavClick(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton button)
        {
            var key = _nav.FirstOrDefault(kv => ReferenceEquals(kv.Value, button)).Key;
            if (!string.IsNullOrEmpty(key))
                SelectNav(key);
        }
    }

    private void OnFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CollectionItem folder })
        {
            foreach (var (key, button) in _nav)
                button.Tag = key;
            _filter = $"collection:{folder.Id}";
            if (FolderList.ItemsSource is IEnumerable<CollectionItem> folders)
            {
                foreach (var item in folders)
                    item.IsSelected = item.Id == folder.Id;
            }

            ApplyFilter(null);
        }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) && !SearchBox.IsKeyboardFocused
            ? Visibility.Visible
            : Visibility.Collapsed;
        ApplyFilter(null);
    }

    private void OnClipClick(object sender, WpfMouseButtonEventArgs e)
    {
        if (FindParent<WpfButton>(e.OriginalSource as DependencyObject) is not null)
            return;
        if (ClipList.SelectedItem is ClipItem item)
            CopyItem(item, hideAfter: true);
    }

    private void OnClipDoubleClick(object sender, WpfMouseButtonEventArgs e)
    {
        if (ClipList.SelectedItem is ClipItem item)
            OpenEditor(item);
    }

    private void CopyItem(ClipItem item, bool hideAfter)
    {
        try
        {
            _watcher.CopyText(item.Content);
            _store.MarkCopied(item.Id);
            item.CopyCount++;
            item.LastCopiedAt = DateTime.UtcNow;
            item.UpdatedAt = DateTime.UtcNow;
            Notify("Скопировано");
            if (hideAfter)
                HideToTray();
        }
        catch (Exception ex)
        {
            Notify(ex.Message);
        }
    }

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: ClipItem item })
            TogglePin(item);
    }

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: ClipItem item })
            OpenEditor(item);
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: ClipItem item })
            DeleteItem(item);
    }

    private void TogglePin(ClipItem item)
    {
        item.Pinned = !item.Pinned;
        _store.SetPinned(item.Id, item.Pinned);
        Reload();
    }

    private void DeleteItem(ClipItem item)
    {
        _store.Delete(item.Id);
        Reload(keepSelection: false);
        Notify("Удалено");
    }

    private void OnNewNote(object sender, RoutedEventArgs e) => OpenEditor(null);

    private void OpenEditor(ClipItem? item)
    {
        _editing = item;
        EditorTitle.Text = item is null ? "Новая запись" : "Запись";
        EditorName.Text = item?.Title ?? "";
        EditorBody.Text = item?.Content ?? "";
        _editorKind = item?.Kind ?? ClipKinds.Clip;
        _editorColor = item?.Color ?? ClipColors.None;
        UpdateKindChips();
        RebuildEditorFolders(item?.CollectionId);
        EditorOverlay.Visibility = Visibility.Visible;
        EditorBody.Focus();
    }

    private void OnEditorCancel(object sender, RoutedEventArgs e)
    {
        EditorOverlay.Visibility = Visibility.Collapsed;
        _editing = null;
    }

    private void OnEditorSave(object sender, RoutedEventArgs e)
    {
        var title = string.IsNullOrWhiteSpace(EditorName.Text) ? null : EditorName.Text.Trim();
        var content = EditorBody.Text.Trim();
        if (content.Length == 0)
        {
            Notify("Введите текст");
            return;
        }

        var kind = _editorKind;
        if (_editing is null && kind == ClipKinds.Clip)
            kind = ClipKinds.Detect(content);

        var collectionId = (EditorFolder.SelectedItem as FolderChoice)?.Id;

        try
        {
            if (_editing is null)
            {
                _store.AddNote(title, content, kind, _editorColor, collectionId);
            }
            else
            {
                _editing.Title = title;
                _editing.Content = content;
                _editing.Kind = kind;
                _editing.Color = _editorColor;
                _editing.CollectionId = collectionId;
                _editing.UpdatedAt = DateTime.UtcNow;
                _store.Update(_editing);
            }

            EditorOverlay.Visibility = Visibility.Collapsed;
            _editing = null;
            Reload();
            Notify("Сохранено");
        }
        catch (Exception ex)
        {
            Notify(ex.Message);
        }
    }

    private void OnKindChip(object sender, RoutedEventArgs e)
    {
        _editorKind = sender switch
        {
            _ when ReferenceEquals(sender, KindNote) => ClipKinds.Note,
            _ when ReferenceEquals(sender, KindCode) => ClipKinds.Code,
            _ when ReferenceEquals(sender, KindLink) => ClipKinds.Link,
            _ => ClipKinds.Clip,
        };
        UpdateKindChips();
    }

    private void UpdateKindChips()
    {
        KindClip.IsChecked = _editorKind == ClipKinds.Clip;
        KindNote.IsChecked = _editorKind == ClipKinds.Note;
        KindCode.IsChecked = _editorKind == ClipKinds.Code;
        KindLink.IsChecked = _editorKind == ClipKinds.Link;
    }

    private void OnColorDot(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string color })
            _editorColor = color;
    }

    private void OnAddFolder(object sender, RoutedEventArgs e) => TryAddFolder();

    private void OnFolderNameKey(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryAddFolder();
            e.Handled = true;
        }
    }

    private void TryAddFolder()
    {
        var name = FolderNameBox.Text?.Trim() ?? "";
        if (name.Length == 0)
            return;
        try
        {
            _store.AddCollection(name);
            FolderNameBox.Clear();
            FolderHint.Visibility = Visibility.Visible;
            Reload();
        }
        catch (Exception ex)
        {
            Notify(ex.Message);
        }
    }

    private void OnDeleteFolder(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: CollectionItem folder })
        {
            _store.DeleteCollection(folder.Id);
            if (_filter == $"collection:{folder.Id}")
                SelectNav("all");
            else
                Reload();
        }
    }

    private void OnStartupChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingUi)
            return;
        StartupService.SetEnabled(StartupCheck.IsChecked == true);
    }

    private ClipItem? Selected => ClipList.SelectedItem as ClipItem;

    private void OnCopyMenu(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item)
            CopyItem(item, hideAfter: false);
    }

    private void OnPinMenu(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item)
            TogglePin(item);
    }

    private void OnEditMenu(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item)
            OpenEditor(item);
    }

    private void OnDeleteMenu(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item)
            DeleteItem(item);
    }

    private void OnColorMenu(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string color } && Selected is { } item)
        {
            item.Color = color;
            _store.SetColor(item.Id, color);
        }
    }

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        RebuildFolderMenu();
        if (ClipList.ContextMenu?.Items.OfType<MenuItem>().FirstOrDefault(i =>
                i.Header as string is "Закрепить" or "Открепить") is { } pin
            && Selected is { } item)
        {
            pin.Header = item.Pinned ? "Открепить" : "Закрепить";
        }
    }

    private void RebuildFolderMenu()
    {
        if (FolderMenu is null)
            return;

        FolderMenu.Items.Clear();
        var none = new MenuItem { Header = "Без папки" };
        none.Click += (_, _) => MoveSelectedToFolder(null);
        FolderMenu.Items.Add(none);
        if (FolderList.ItemsSource is IEnumerable<CollectionItem> folders)
        {
            foreach (var folder in folders)
            {
                var item = new MenuItem { Header = folder.Name, Tag = folder.Id };
                item.Click += (_, _) => MoveSelectedToFolder(folder.Id);
                FolderMenu.Items.Add(item);
            }
        }
    }

    private void MoveSelectedToFolder(long? collectionId)
    {
        if (Selected is not { } item)
            return;
        item.CollectionId = collectionId;
        _store.SetCollection(item.Id, collectionId);
        Reload();
    }

    private void RebuildEditorFolders(long? selected)
    {
        var choices = new List<FolderChoice> { new(null, "Без папки") };
        if (FolderList.ItemsSource is IEnumerable<CollectionItem> folders)
            choices.AddRange(folders.Select(f => new FolderChoice(f.Id, f.Name)));
        EditorFolder.ItemsSource = choices;
        EditorFolder.DisplayMemberPath = nameof(FolderChoice.Name);
        EditorFolder.SelectedItem = choices.FirstOrDefault(c => c.Id == selected) ?? choices[0];
    }

    private void OnListKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (Selected is not { } item)
            return;

        if (e.Key == Key.Enter)
        {
            CopyItem(item, hideAfter: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            DeleteItem(item);
            e.Handled = true;
        }
    }

    private void OnPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (EditorOverlay.Visibility == Visibility.Visible)
                EditorOverlay.Visibility = Visibility.Collapsed;
            else
                HideToTray();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
        {
            OpenEditor(null);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (!AcrylicHelper.Apply(this))
            WindowFrame.Background = AcrylicHelper.SolidFallbackBrush;
        else
            WindowFrame.Background = new SolidColorBrush(Color.FromArgb(0x66, 0x0B, 0x0D, 0x11));
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_forceClose)
            return;

        if (System.Windows.Application.Current is App app && app.ShouldCloseToTray)
        {
            e.Cancel = true;
            HideToTray();
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e)
        => SystemCommands.MinimizeWindow(this);

    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
        => HideToTray();

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (MaxIcon is null)
            return;
        MaxIcon.Data = Geometry.Parse(
            WindowState == WindowState.Maximized
                ? "M7,7 H15 V15 H7 Z M5,9 V17 H13"
                : "M5,5 H15 V15 H5 Z");
        WindowFrame.CornerRadius = WindowState == WindowState.Maximized
            ? new CornerRadius(0)
            : new CornerRadius(8);
    }

    private static T? FindParent<T>(DependencyObject? start) where T : DependencyObject
    {
        while (start is not null)
        {
            if (start is T match)
                return match;
            start = VisualTreeHelper.GetParent(start);
        }

        return null;
    }

    private static string FormatRecords(int count)
    {
        var n = Math.Abs(count) % 100;
        var n1 = n % 10;
        if (n > 10 && n < 20)
            return $"{count} записей";
        if (n1 == 1)
            return $"{count} запись";
        if (n1 is >= 2 and <= 4)
            return $"{count} записи";
        return $"{count} записей";
    }

    private sealed record FolderChoice(long? Id, string Name);
}
