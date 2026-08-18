using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Klip.Services;

public sealed record UpdateInfo(
    Version Version,
    string Tag,
    string ReleaseUrl,
    string SetupUrl,
    string PortableUrl,
    string ChecksumsUrl);

public sealed class UpdateService
{
    public const string Owner = "scarrymany";
    public const string Repo = "klip";
    public const string ReleasesLatest = "https://github.com/scarrymany/klip/releases/latest";

    public static Version CurrentVersion { get; } = ReadCurrentVersion();

    private static readonly HttpClient Http = CreateClient();
    private static readonly Uri ApiLatest = new($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");

    private readonly ClipStore _store;

    public UpdateService(ClipStore store) => _store = store;

    public static bool IsPortableInstall() => InstallLayout.IsPortableInstall();

    public bool WasDismissed(Version version)
        => string.Equals(_store.GetSetting("update.dismissed"), version.ToString(), StringComparison.Ordinal);

    public void Dismiss(Version version)
        => _store.SetSetting("update.dismissed", version.ToString());

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync(ApiLatest, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString();
        var releaseUrl = root.GetProperty("html_url").GetString();
        if (string.IsNullOrWhiteSpace(tag) || ParseTag(tag) is not { } latest)
            return null;
        if (latest <= CurrentVersion)
            return null;

        string? setup = null;
        string? portable = null;
        string? sums = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                if (!IsAllowedDownload(url))
                    continue;
                if (Regex.IsMatch(name, @"^Klip-Setup-\d+\.\d+\.\d+\.exe$", RegexOptions.IgnoreCase))
                    setup = url;
                else if (name.Equals("Klip-Portable-win-x64.zip", StringComparison.OrdinalIgnoreCase))
                    portable = url;
                else if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                    sums = url;
            }
        }

        if (string.IsNullOrEmpty(setup) || string.IsNullOrEmpty(portable) || string.IsNullOrEmpty(sums))
            return null;

        return new UpdateInfo(latest, tag, releaseUrl ?? ReleasesLatest, setup, portable, sums);
    }

    public async Task ApplyAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var work = Path.Combine(Path.GetTempPath(), "Klip", "update");
        Directory.CreateDirectory(work);

        if (IsPortableInstall())
        {
            var zip = Path.Combine(work, "Klip-Portable-win-x64.zip");
            await DownloadVerifiedAsync(info.PortableUrl, zip, "Klip-Portable-win-x64.zip", info.ChecksumsUrl, progress, cancellationToken)
                .ConfigureAwait(false);
            var extracted = ExtractPortableExe(zip, work);
            LaunchPortableReplace(extracted);
            return;
        }

        var setupName = $"Klip-Setup-{info.Version}.exe";
        var setupPath = Path.Combine(work, setupName);
        await DownloadVerifiedAsync(info.SetupUrl, setupPath, setupName, info.ChecksumsUrl, progress, cancellationToken)
            .ConfigureAwait(false);
        LaunchInstaller(setupPath);
    }

    private static async Task DownloadVerifiedAsync(
        string url,
        string dest,
        string assetName,
        string checksumsUrl,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var expected = await ReadExpectedHashAsync(checksumsUrl, assetName, cancellationToken).ConfigureAwait(false);
        await DownloadAsync(url, dest, progress, cancellationToken).ConfigureAwait(false);

        string actual;
        await using (var file = new FileStream(dest, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
        {
            actual = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
        }

        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(dest); } catch { /* temp file */ }
            throw new InvalidOperationException("Контрольная сумма обновления не совпала. Файл удалён.");
        }
    }

    private static async Task<string> ReadExpectedHashAsync(string checksumsUrl, string assetName, CancellationToken cancellationToken)
    {
        var text = await Http.GetStringAsync(checksumsUrl, cancellationToken).ConfigureAwait(false);
        return ChecksumFile.RequireHash(text, assetName);
    }

    private static async Task DownloadAsync(string url, string dest, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0L;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, n), cancellationToken).ConfigureAwait(false);
            read += n;
            if (total > 0)
                progress?.Report(Math.Clamp(read / (double)total, 0, 1));
        }

        progress?.Report(1);
    }

    private static string ExtractPortableExe(string zipPath, string work)
    {
        var extractDir = Path.Combine(work, "extracted");
        if (Directory.Exists(extractDir))
            Directory.Delete(extractDir, recursive: true);
        ZipFile.ExtractToDirectory(zipPath, extractDir);

        var exe = Directory.EnumerateFiles(extractDir, "Klip.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (exe is null)
            throw new InvalidOperationException("В архиве нет Klip.exe.");
        return exe;
    }

    private static void LaunchInstaller(string setupPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = setupPath,
            Arguments = "/VERYSILENT /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS /NORESTART /SP- /SUPPRESSMSGBOXES",
            UseShellExecute = true,
            Verb = "runas",
        };

        try
        {
            Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("Обновление отменено.");
        }
    }

    private static void LaunchPortableReplace(string newExePath)
    {
        var current = Environment.ProcessPath
            ?? throw new InvalidOperationException("Не найден путь к Klip.exe.");
        var script = Path.Combine(Path.GetTempPath(), "Klip", "update", "replace.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);

        var body =
            """
            $pidToWait = [int]$args[0]
            $src = [string]$args[1]
            $dst = [string]$args[2]
            while (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue) {
              Start-Sleep -Milliseconds 400
            }
            Copy-Item -LiteralPath $src -Destination $dst -Force
            Start-Process -FilePath $dst
            Remove-Item -LiteralPath $src -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
            """;
        File.WriteAllText(script, body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            ArgumentList =
            {
                "-NoProfile",
                "-ExecutionPolicy", "Bypass",
                "-WindowStyle", "Hidden",
                "-File", script,
                Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
                newExePath,
                current,
            },
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }

    public static Version? ParseTag(string tag)
    {
        var value = tag.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
            value = value[1..];
        return Version.TryParse(value, out var version) ? Normalize(version) : null;
    }

    private static Version ReadCurrentVersion()
    {
        var info = typeof(UpdateService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            if (plus >= 0)
                info = info[..plus];
            if (Version.TryParse(info, out var parsed))
                return Normalize(parsed);
        }

        return Normalize(typeof(UpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0));
    }

    private static Version Normalize(Version version)
        => new(version.Major, version.Minor, Math.Max(version.Build, 0));

    private static bool IsAllowedDownload(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme != Uri.UriSchemeHttps)
            return false;
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return false;
        return uri.AbsolutePath.StartsWith($"/{Owner}/{Repo}/releases/download/", StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(4) };
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            $"Klip/{CurrentVersion} (+https://github.com/scarrymany/klip)");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }
}
