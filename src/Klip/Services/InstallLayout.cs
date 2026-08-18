using System.IO;

namespace Klip.Services;

public static class InstallLayout
{
    public const string MarkerFileName = "Klip.installed";
    public const string InnoUninstaller = "unins000.exe";

    public static bool IsPortableInstall()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
            return true;

        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir))
            return true;

        if (File.Exists(Path.Combine(dir, MarkerFileName)))
            return false;

        if (File.Exists(Path.Combine(dir, InnoUninstaller)))
            return false;

        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
        {
            if (!string.IsNullOrEmpty(root) &&
                dir.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsPortableDirectory(string directory)
    {
        if (string.IsNullOrEmpty(directory))
            return true;
        if (File.Exists(Path.Combine(directory, MarkerFileName)))
            return false;
        if (File.Exists(Path.Combine(directory, InnoUninstaller)))
            return false;
        return true;
    }
}
