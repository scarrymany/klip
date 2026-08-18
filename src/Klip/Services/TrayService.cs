using System.Drawing;
using System.IO;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace Klip.Services;

public sealed class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly Icon _icon;

    public event EventHandler? ShowRequested;
    public event EventHandler? ExitRequested;

    public TrayService()
    {
        _icon = LoadIcon();

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Показать", null, (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon = new WinForms.NotifyIcon
        {
            Text = "Клип",
            Visible = true,
            Icon = _icon,
            ContextMenuStrip = menu,
        };
        _notifyIcon.MouseClick += OnMouseClick;
    }

    private void OnMouseClick(object? sender, WinForms.MouseEventArgs e)
    {
        if (e.Button == WinForms.MouseButtons.Left)
            ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    private static Icon LoadIcon()
    {
        try
        {
            var stream = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/klip.ico"));
            if (stream?.Stream is { } resource)
            {
                using (resource)
                {
                    using var copy = new MemoryStream();
                    resource.CopyTo(copy);
                    copy.Position = 0;
                    using var tmp = new Icon(copy);
                    return (Icon)tmp.Clone();
                }
            }
        }
        catch
        {
            // Fall through to the executable icon.
        }

        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                var extracted = Icon.ExtractAssociatedIcon(path);
                if (extracted is not null)
                    return extracted;
            }
        }
        catch
        {
            // Fall through to the stock application icon.
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.MouseClick -= OnMouseClick;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }
}
