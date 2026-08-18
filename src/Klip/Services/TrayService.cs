using System.Drawing;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace Klip.Services;

public sealed class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;

    public event Action? ShowClicked;
    public event Action? ExitClicked;

    public TrayService()
    {
        _icon = new WinForms.NotifyIcon
        {
            Text = "Клип — буфер обмена",
            Visible = true,
            Icon = LoadIcon(),
        };
        _icon.DoubleClick += (_, _) => ShowClicked?.Invoke();
        _icon.ContextMenuStrip = new WinForms.ContextMenuStrip();
        _icon.ContextMenuStrip.Items.Add("Открыть", null, (_, _) => ShowClicked?.Invoke());
        _icon.ContextMenuStrip.Items.Add("Выход", null, (_, _) => ExitClicked?.Invoke());
    }

    public void Balloon(string text)
    {
        _icon.BalloonTipTitle = "Клип";
        _icon.BalloonTipText = text;
        _icon.ShowBalloonTip(1600);
    }

    private static Icon LoadIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/klip.ico");
            var stream = Application.GetResourceStream(uri)?.Stream;
            if (stream is not null) return new Icon(stream);
        }
        catch
        {
            // fallback
        }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
