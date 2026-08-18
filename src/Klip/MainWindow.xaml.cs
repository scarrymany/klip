using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Klip.Models;
using Klip.Services;

namespace Klip;

public partial class MainWindow : Window
{
    private readonly ClipStore _store = new();
    private readonly ClipboardWatcher _clipboard = new();
    private readonly HotkeyService _hotkey = new();
    private string _filter = "all";
    private bool _exit;
    private bool _startupHandlerReady;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Closing += (_, e) =>
        {
            if (_exit) return;
            e.Cancel = true;
            Hide();
        };
        Loaded += (_, _) =>
        {
            StartupBox.IsChecked = StartupService.IsEnabled();
            _startupHandlerReady = true;
            Reload();
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        AcrylicHelper.TryApply(this);
        if (AcrylicHelper.IsWindows11())
            Shell.Background = System.Windows.Media.Brushes.Transparent;

        var hwnd = new WindowInteropHelper(this).Handle;
        _clipboard.Start(hwnd);
        _clipboard.TextCaptured += text => Dispatcher.Invoke(() =>
        {
            try
            {
                _store.Add(text);
                Reload();
            }
            catch
            {
                // ignore empty / duplicate failures
            }
        });
        _hotkey.Register(hwnd);
        _hotkey.Pressed += () => Dispatcher.Invoke(Reveal);
    }

    public void Reveal()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        SearchBox.Focus();
    }

    public void PrepareExit()
    {
        _exit = true;
        _clipboard.Dispose();
        _hotkey.Dispose();
        _store.Dispose();
    }

    private void Reload()
    {
        var q = SearchBox.Text;
        string? kind = _filter is "clip" or "note" or "code" or "link" ? _filter : null;
        var pinned = _filter == "pinned";
        long? folder = _filter.StartsWith("folder:", StringComparison.Ordinal)
            ? long.Parse(_filter["folder:".Length..])
            : null;
        ClipList.ItemsSource = _store.List(q, kind, pinned, folder);
        var c = _store.Counts();
        CountLabel.Text = $"{c.All} записей";
        FolderList.ItemsSource = _store.ListCollections();
    }

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: CollectionItem item })
        {
            _filter = $"folder:{item.Id}";
            Reload();
        }
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
        {
            _filter = tag;
            Reload();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Reload();

    private void SaveDraft_Click(object sender, RoutedEventArgs e)
    {
        var text = DraftBox.Text.Trim();
        if (text.Length == 0) return;
        _store.Add(text);
        DraftBox.Clear();
        Reload();
    }

    private void ClipList_DoubleClick(object sender, MouseButtonEventArgs e) => CopySelected();

    private void ClipList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CopySelected();
        if (e.Key == Key.Delete && ClipList.SelectedItem is ClipItem item)
        {
            _store.Delete(item.Id);
            Reload();
        }
    }

    private void CopySelected()
    {
        if (ClipList.SelectedItem is not ClipItem item) return;
        _clipboard.IgnoreNext();
        System.Windows.Clipboard.SetText(item.Content);
        _store.MarkCopied(item.Id);
        Reload();
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: long id }) return;
        var item = _store.Get(id);
        if (item is null) return;
        item.Pinned = !item.Pinned;
        _store.Update(item);
        Reload();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: long id }) return;
        _store.Delete(id);
        Reload();
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _store.AddCollection(FolderNameBox.Text);
            FolderNameBox.Clear();
            Reload();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Клип");
        }
    }

    private void StartupBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_startupHandlerReady) return;
        StartupService.SetEnabled(StartupBox.IsChecked == true);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private void Title_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
