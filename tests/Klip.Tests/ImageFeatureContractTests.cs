namespace Klip.Tests;

public sealed class ImageFeatureContractTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void Clipboard_watcher_prioritizes_images_and_can_copy_them_back()
    {
        var code = Read("src", "Klip", "Services", "ClipboardWatcher.cs");
        var imageCheck = code.IndexOf("Clipboard.ContainsImage()", StringComparison.Ordinal);
        var textCheck = code.IndexOf("Clipboard.ContainsText()", StringComparison.Ordinal);

        Assert.True(imageCheck >= 0, "Clipboard image capture is missing.");
        Assert.True(imageCheck < textCheck, "Images must be checked before text clipboard formats.");
        Assert.Contains("ImageCaptured?.Invoke", code, StringComparison.Ordinal);
        Assert.Contains("public void CopyImage(string path)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void App_persists_captured_images()
    {
        var code = Read("src", "Klip", "App.xaml.cs");

        Assert.Contains("_watcher.ImageCaptured += OnClipboardImage", code, StringComparison.Ordinal);
        Assert.Contains("TryAddImageFromClipboard(image.PngBytes, image.Width, image.Height)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Image_navigation_preview_copy_and_save_actions_are_present()
    {
        var xaml = Read("src", "Klip", "MainWindow.xaml");
        var code = Read("src", "Klip", "MainWindow.xaml.cs");

        Assert.Contains("x:Name=\"NavImage\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ImagePreviewOverlay\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnImageSaveAs\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnImagePreviewCopy\"", xaml, StringComparison.Ordinal);
        Assert.Contains("_watcher.CopyImage(path)", code, StringComparison.Ordinal);
        Assert.Contains("OpenImagePreview(item)", code, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine([Root, .. parts]));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Klip.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
