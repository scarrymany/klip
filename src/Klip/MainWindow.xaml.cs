using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    private readonly UpdateService _updates;
    private readonly UiTheme _theme;
    private readonly List<ClipItem> _all = [];
    private readonly ObservableCollection<ClipItem> _visible = [];
    private readonly DispatcherTimer _toastTimer;
    private readonly DispatcherTimer _updateTimer;
    private readonly Dictionary<string, WpfButton> _nav = [];

    private string _filter = "all";
    private bool _forceClose;
    private bool _syncingUi;
    private bool _updating;
    private ClipItem? _editing;
    private UpdateInfo? _pendingUpdate;
    private CancellationTokenSource? _updateCts;
    private string _editorKind = ClipKinds.Clip;
    private string _editorColor = ClipColors.None;
    private string? _wallpaperLoaded;
    private bool _appearanceReady;
    private bool _overlayClosing;
    private bool _navReady;
    private DispatcherTimer? _overlayHide;
    private readonly DispatcherTimer _themeSaveTimer;
    private readonly DispatcherTimer _themeApplyTimer;

    private static readonly string[] NavOrder = ["all", "pinned", "clip", "note", "code", "link"];

    public ObservableCollection<ClipItem> VisibleClips => _visible;

    public MainWindow(ClipStore store, ClipboardWatcher watcher)
    {
        _store = store;
        _watcher = watcher;
        _updates = new UpdateService(store);
        _theme = UiTheme.Load(store);
        InitializeComponent();
        DataContext = this;

        VersionLabel.Text = $"Версия {UpdateService.CurrentVersion}";
        SyncSettingsControls();

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
        _navReady = true;

        Loaded += (_, _) =>
        {
            SmoothScroll.Attach(SettingsScroll);
            SmoothScroll.Attach(SideScroll);
            if (FindVisualChild<ScrollViewer>(ClipList) is { } listScroll)
                SmoothScroll.Attach(listScroll);
        };

        _themeSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _themeSaveTimer.Tick += (_, _) =>
        {
            _themeSaveTimer.Stop();
            _theme.Save(_store);
        };
        _themeApplyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _themeApplyTimer.Tick += (_, _) =>
        {
            _themeApplyTimer.Stop();
            if (_appearanceReady)
                ApplyTheme();
        };

        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(4) };
        _updateTimer.Tick += (_, _) => _ = CheckUpdatesAsync(manual: false);
        _updateTimer.Start();

        Loaded += (_, _) =>
        {
            var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
            delay.Tick += (_, _) =>
            {
                delay.Stop();
                _ = CheckUpdatesAsync(manual: false);
            };
            delay.Start();
        };
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

    public void AddCaptured(ClipItem item)
    {
        var existing = _all.FindIndex(c => c.Id == item.Id);
        if (existing >= 0)
            _all.RemoveAt(existing);
        _all.Add(item);
        _all.Sort(CompareClips);
        DropTrimmedCaptures();

        CountAll.Text = _all.Count.ToString(CultureInfo.InvariantCulture);
        CountPinned.Text = _all.Count(c => c.Pinned).ToString(CultureInfo.InvariantCulture);
        CountClip.Text = _all.Count(c => c.Kind == ClipKinds.Clip).ToString(CultureInfo.InvariantCulture);
        CountNote.Text = _all.Count(c => c.Kind == ClipKinds.Note).ToString(CultureInfo.InvariantCulture);
        CountCode.Text = _all.Count(c => c.Kind == ClipKinds.Code).ToString(CultureInfo.InvariantCulture);
        CountLink.Text = _all.Count(c => c.Kind == ClipKinds.Link).ToString(CultureInfo.InvariantCulture);
        TitleCount.Text = FormatRecords(_all.Count);
        ApplyFilter(item.Id);
    }

    private void DropTrimmedCaptures()
    {
        if (_all.Count <= ClipStore.HistoryLimit)
            return;

        var extra = _all.Count - ClipStore.HistoryLimit;
        var victims = _all
            .Where(c => !c.Pinned && c.Source == ClipSources.Clipboard)
            .OrderBy(c => c.UpdatedAt)
            .ThenBy(c => c.Id)
            .Take(extra)
            .Select(c => c.Id)
            .ToHashSet();
        if (victims.Count == 0)
            return;
        _all.RemoveAll(c => victims.Contains(c.Id));
    }

    private static int CompareClips(ClipItem a, ClipItem b)
    {
        var pin = b.Pinned.CompareTo(a.Pinned);
        if (pin != 0)
            return pin;
        var updated = b.UpdatedAt.CompareTo(a.UpdatedAt);
        if (updated != 0)
            return updated;
        return b.Id.CompareTo(a.Id);
    }

    private ClipItem Hydrate(ClipItem item)
    {
        if (item.HasFullContent)
            return item;

        var full = _store.GetById(item.Id);
        if (full is null)
            return item;

        item.Content = full.Content;
        item.HasFullContent = true;
        return item;
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
        FlushTheme();
        _updateCts?.Cancel();
        _forceClose = true;
        Close();
    }

    private async Task CheckUpdatesAsync(bool manual)
    {
        try
        {
            var info = await _updates.CheckAsync();
            if (info is null)
            {
                if (manual)
                    Notify("У вас актуальная версия");
                return;
            }

            if (!manual && _updates.WasDismissed(info.Version))
                return;

            ShowUpdateCard(info);
        }
        catch (Exception ex)
        {
            if (manual)
                Notify(ex.Message);
        }
    }

    private void ShowUpdateCard(UpdateInfo info)
    {
        _pendingUpdate = info;
        UpdateTitle.Text = $"Доступна версия {info.Version}";
        UpdateHint.Text = UpdateService.IsPortableInstall()
            ? "Клип скачает файл, заменит себя и откроется снова."
            : "Клип обновится и откроется снова. Может появиться запрос прав Windows.";
        UpdateButton.Content = "Обновить";
        UpdateButton.IsEnabled = true;

        if (UpdateCard.Visibility == Visibility.Visible)
            return;

        UpdateCard.Visibility = Visibility.Visible;
        var slide = new TranslateTransform(24, 0);
        UpdateCard.RenderTransform = slide;
        UpdateCard.Opacity = 0;
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        slide.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
        UpdateCard.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
    }

    private void OnCheckUpdates(object sender, RoutedEventArgs e)
        => _ = CheckUpdatesAsync(manual: true);

    private void OnUpdateDismiss(object sender, RoutedEventArgs e)
    {
        if (_updating)
            return;
        if (_pendingUpdate is { } info)
            _updates.Dismiss(info.Version);
        PlayFade(UpdateCard, show: false, () => UpdateCard.Visibility = Visibility.Collapsed);
    }

    private async void OnUpdateApply(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is not { } info || _updating)
            return;

        _updating = true;
        _updateCts?.Cancel();
        _updateCts = new CancellationTokenSource();
        UpdateButton.IsEnabled = false;
        UpdateButton.Content = "Загрузка…";

        var progress = new Progress<double>(value =>
        {
            UpdateButton.Content = value >= 1
                ? "Установка…"
                : $"Загрузка {(int)(value * 100)}%";
        });

        try
        {
            await _updates.ApplyAsync(info, progress, _updateCts.Token);
            Notify("Установка запущена");
            if (System.Windows.Application.Current is App app)
                app.RequestExit();
            else
                ForceClose();
        }
        catch (OperationCanceledException)
        {
            UpdateButton.IsEnabled = true;
            UpdateButton.Content = "Обновить";
        }
        catch (Exception ex)
        {
            UpdateButton.IsEnabled = true;
            UpdateButton.Content = "Обновить";
            Notify(ex.Message);
        }
        finally
        {
            _updating = false;
        }
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
            var matches = _store.SearchIds(q).ToHashSet();
            query = query.Where(c =>
                matches.Contains(c.Id) ||
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
        var previous = _filter;
        _filter = filter;
        foreach (var (key, button) in _nav)
            button.Tag = key == filter ? "active" : key;
        if (FolderList.ItemsSource is IEnumerable<CollectionItem> folders)
        {
            foreach (var folder in folders)
                folder.IsSelected = false;
        }

        if (!_navReady || previous == filter)
        {
            ApplyFilter((ClipList.SelectedItem as ClipItem)?.Id);
            return;
        }

        PlaySection(NavIndex(filter).CompareTo(NavIndex(previous)), () => ApplyFilter(null));
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
            var previous = _filter;
            var next = $"collection:{folder.Id}";
            _filter = next;
            if (FolderList.ItemsSource is IEnumerable<CollectionItem> folders)
            {
                foreach (var item in folders)
                    item.IsSelected = item.Id == folder.Id;
            }

            if (!_navReady || previous == next)
            {
                ApplyFilter(null);
                return;
            }

            PlaySection(NavIndex(next).CompareTo(NavIndex(previous)), () => ApplyFilter(null));
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
            item = Hydrate(item);
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
        if (item is not null)
            item = Hydrate(item);
        _editing = item;
        EditorTitle.Text = item is null ? "Новая запись" : "Запись";
        EditorName.Text = item?.Title ?? "";
        EditorBody.Text = item?.Content ?? "";
        _editorKind = item?.Kind ?? ClipKinds.Clip;
        _editorColor = item?.Color ?? ClipColors.None;
        UpdateKindChips();
        RebuildEditorFolders(item?.CollectionId);
        PlayOverlay(EditorOverlay, EditorCard, show: true);
        EditorBody.Focus();
    }

    private void OnEditorCancel(object sender, RoutedEventArgs e)
    {
        PlayOverlay(EditorOverlay, EditorCard, show: false);
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
                Hydrate(_editing);
                _editing.Title = title;
                _editing.Content = content;
                _editing.Kind = kind;
                _editing.Color = _editorColor;
                _editing.CollectionId = collectionId;
                _editing.UpdatedAt = DateTime.UtcNow;
                _store.Update(_editing);
            }

            PlayOverlay(EditorOverlay, EditorCard, show: false);
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
            if (SettingsOverlay.Visibility == Visibility.Visible)
                CloseSettings();
            else if (EditorOverlay.Visibility == Visibility.Visible)
                PlayOverlay(EditorOverlay, EditorCard, show: false);
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

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        SyncSettingsControls();
        SettingsButton.Tag = "active";
        PlayOverlay(SettingsOverlay, SettingsCard, show: true);
    }

    private void OnCloseSettings(object sender, RoutedEventArgs e) => CloseSettings();

    private void CloseSettings()
    {
        SettingsButton.Tag = null;
        PlayOverlay(SettingsOverlay, SettingsCard, show: false);
    }

    private void PlayOverlay(Grid overlay, FrameworkElement card, bool show)
    {
        if (show)
        {
            _overlayClosing = false;
            _overlayHide?.Stop();
            overlay.Visibility = Visibility.Visible;
        }
        else if (_overlayClosing || overlay.Visibility != Visibility.Visible)
        {
            return;
        }
        else
        {
            _overlayClosing = true;
        }

        card.RenderTransformOrigin = new Point(0.5, 0.42);
        var scale = new ScaleTransform(show ? 0.96 : 1, show ? 0.96 : 1);
        var slide = new TranslateTransform(0, show ? 14 : 0);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(slide);
        card.RenderTransform = group;

        var ease = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn };
        var duration = TimeSpan.FromMilliseconds(show ? 240 : 150);

        overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 0 : 1, show ? 1 : 0, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd,
        });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(show ? 0.96 : 1, show ? 1 : 0.98, duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(show ? 0.96 : 1, show ? 1 : 0.98, duration) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(show ? 14 : 0, show ? 0 : 10, duration) { EasingFunction = ease });

        if (show)
            return;

        _overlayHide?.Stop();
        _overlayHide = new DispatcherTimer { Interval = duration };
        _overlayHide.Tick += (_, _) =>
        {
            _overlayHide.Stop();
            overlay.BeginAnimation(OpacityProperty, null);
            overlay.Opacity = 1;
            overlay.Visibility = Visibility.Collapsed;
            _overlayClosing = false;
        };
        _overlayHide.Start();
    }

    private static void PlayFade(UIElement element, bool show, Action? done = null)
    {
        element.Visibility = Visibility.Visible;
        var anim = new DoubleAnimation(show ? 0 : 1, show ? 1 : 0, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        if (done is not null)
        {
            anim.Completed += (_, _) => done();
        }

        element.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private void OnThemeChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingUi || !IsLoaded)
            return;
        _theme.Acrylic = SettingsAcrylic.IsChecked == true;
        PersistTheme();
    }

    private void OnThemeSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingUi || !IsLoaded || SettingsBlur is null || SettingsDim is null)
            return;
        _theme.Blur = SettingsBlur.Value;
        _theme.Dim = SettingsDim.Value / 100.0;
        SettingsBlurValue.Text = ((int)_theme.Blur).ToString(CultureInfo.InvariantCulture);
        SettingsDimValue.Text = ((int)(_theme.Dim * 100)).ToString(CultureInfo.InvariantCulture) + "%";
        ScheduleThemeApply();
        PersistTheme(immediateSave: false);
    }

    private void OnAccentDot(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string hex })
        {
            _theme.Accent = hex;
            PersistTheme();
        }
    }

    private void OnTintDot(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string hex })
        {
            _theme.Tint = hex;
            PersistTheme();
        }
    }

    private void OnStretchChip(object sender, RoutedEventArgs e)
    {
        _theme.Stretch = sender switch
        {
            _ when ReferenceEquals(sender, StretchFill) => "Fill",
            _ when ReferenceEquals(sender, StretchFit) => "Uniform",
            _ => "UniformToFill",
        };
        SyncStretchChips();
        PersistTheme();
    }

    private void OnPickWallpaper(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Фон окна",
            Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.jfif|Все файлы|*.*",
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var path = _theme.InstallWallpaper(dialog.FileName);
            try
            {
                _theme.Tint = UiTheme.ToHex(UiTheme.SampleAverage(path));
            }
            catch
            {
                // Keep the previous tint if sampling fails.
            }

            PersistTheme();
            SyncSettingsControls();
            Notify("Фон установлен");
        }
        catch (Exception ex)
        {
            Notify(ex.Message);
        }
    }

    private void OnClearWallpaper(object sender, RoutedEventArgs e)
    {
        _theme.ClearWallpaper();
        PersistTheme();
        SyncSettingsControls();
    }

    private void OnSampleWallpaper(object sender, RoutedEventArgs e)
    {
        if (_theme.WallpaperPath is not { } path || !System.IO.File.Exists(path))
        {
            Notify("Сначала выберите фото");
            return;
        }

        try
        {
            _theme.Tint = UiTheme.ToHex(UiTheme.SampleAverage(path));
            PersistTheme();
            Notify("Цвет подложки взят из фото");
        }
        catch (Exception ex)
        {
            Notify(ex.Message);
        }
    }

    private void OnResetTheme(object sender, RoutedEventArgs e)
    {
        _theme.ClearWallpaper();
        _theme.Reset();
        PersistTheme();
        SyncSettingsControls();
        Notify("Оформление сброшено");
    }

    private void PersistTheme(bool immediateSave = true)
    {
        if (_appearanceReady)
            ApplyTheme();
        if (immediateSave)
        {
            _themeSaveTimer.Stop();
            _theme.Save(_store);
            return;
        }

        ScheduleThemeSave();
    }

    private void ScheduleThemeApply()
    {
        _themeApplyTimer.Stop();
        _themeApplyTimer.Start();
    }

    private void ScheduleThemeSave()
    {
        _themeSaveTimer.Stop();
        _themeSaveTimer.Start();
    }

    private void FlushTheme()
    {
        _themeApplyTimer.Stop();
        _themeSaveTimer.Stop();
        _theme.Save(_store);
    }

    private void SyncSettingsControls()
    {
        _syncingUi = true;
        SettingsAcrylic.IsChecked = _theme.Acrylic;
        SettingsBlur.Value = _theme.Blur;
        SettingsDim.Value = _theme.Dim * 100;
        SettingsBlurValue.Text = ((int)_theme.Blur).ToString(CultureInfo.InvariantCulture);
        SettingsDimValue.Text = ((int)(_theme.Dim * 100)).ToString(CultureInfo.InvariantCulture) + "%";
        SettingsPhotoName.Text = _theme.WallpaperFile is { } name ? name : "Фото не выбрано";
        SyncStretchChips();
        _syncingUi = false;
    }

    private void SyncStretchChips()
    {
        StretchCover.IsChecked = _theme.Stretch == "UniformToFill";
        StretchFill.IsChecked = _theme.Stretch == "Fill";
        StretchFit.IsChecked = _theme.Stretch == "Uniform";
    }

    private void ApplyTheme()
    {
        var accent = UiTheme.TryParseHex(_theme.Accent) ?? UiTheme.TryParseHex(UiTheme.DefaultAccent)!.Value;
        var tint = UiTheme.TryParseHex(_theme.Tint) ?? UiTheme.TryParseHex(UiTheme.DefaultTint)!.Value;
        SetBrush("AccentBrush", accent);
        SetBrush("AccentFgBrush", UiTheme.ContrastOn(accent));
        SetBrush("BgBrush", tint);

        var hasPhoto = ApplyWallpaper(tint);
        var useAcrylic = _theme.Acrylic && !hasPhoto;

        AcrylicHelper.RemoveBackdrop(this, tint);
        if (hasPhoto)
            WindowFrame.Background = System.Windows.Media.Brushes.Transparent;
        else if (useAcrylic)
            WindowFrame.Background = new SolidColorBrush(Color.FromArgb(0xCC, tint.R, tint.G, tint.B));
        else
            WindowFrame.Background = new SolidColorBrush(tint);

        ApplyWindowShape();
    }

    private bool ApplyWallpaper(Color tint)
    {
        if (_theme.WallpaperPath is not { } path || !System.IO.File.Exists(path))
        {
            WallpaperImage.Source = null;
            _wallpaperLoaded = null;
            WallpaperImage.Visibility = Visibility.Collapsed;
            WallpaperDim.Visibility = Visibility.Collapsed;
            return false;
        }

        if (_wallpaperLoaded != path)
        {
            WallpaperImage.Source = UiTheme.LoadBitmap(path);
            _wallpaperLoaded = path;
        }
        WallpaperImage.Stretch = UiTheme.ParseStretch(_theme.Stretch);
        WallpaperImage.Visibility = Visibility.Visible;
        WallpaperBlur.Radius = _theme.Blur;
        WallpaperDim.Background = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Clamp((int)(_theme.Dim * 255), 0, 255),
            tint.R, tint.G, tint.B));
        WallpaperDim.Visibility = Visibility.Visible;
        return true;
    }

    private static void SetBrush(string key, Color color)
    {
        if (System.Windows.Application.Current.TryFindResource(key) is SolidColorBrush brush && !brush.IsFrozen)
            brush.Color = color;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ApplyTheme();
        _appearanceReady = true;
        ApplyWindowShape();
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e) => ApplyWindowShape();

    private void OnWindowDpiChanged(object sender, DpiChangedEventArgs e) => ApplyWindowShape();

    private void ApplyWindowShape()
    {
        if (WindowFrame is null)
            return;
        WindowCorners.ClipFrame(WindowFrame, this);
        WindowCorners.Apply(this);
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
        ApplyWindowShape();
    }

    private int NavIndex(string filter)
    {
        var i = Array.IndexOf(NavOrder, filter);
        if (i >= 0)
            return i;
        if (filter.StartsWith("collection:", StringComparison.Ordinal) &&
            FolderList.ItemsSource is IEnumerable<CollectionItem> folders)
        {
            var id = filter["collection:".Length..];
            var index = 0;
            foreach (var folder in folders)
            {
                if (folder.Id.ToString(CultureInfo.InvariantCulture) == id)
                    return NavOrder.Length + index;
                index++;
            }
            return NavOrder.Length;
        }
        return 0;
    }

    private void PlaySection(int direction, Action swap)
    {
        if (direction == 0)
            direction = 1;
        swap();
        if (ClipPane is null)
            return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slide = new TranslateTransform(22 * direction, 12);
        var scale = new ScaleTransform(0.982, 0.982);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(slide);
        ClipPane.RenderTransformOrigin = new Point(0.5, 0.08);
        ClipPane.RenderTransform = group;
        ClipPane.Opacity = 0;

        var time = TimeSpan.FromMilliseconds(320);
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(22 * direction, 0, time) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, time) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.982, 1, time) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.982, 1, time) { EasingFunction = ease });
        ClipPane.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
    }

    private static T? FindVisualChild<T>(DependencyObject? root) where T : DependencyObject
    {
        if (root is null)
            return null;
        if (root is T match)
            return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindVisualChild<T>(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
                return found;
        }
        return null;
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
