using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace BatteryTray;

internal sealed record BatteryTrayRelease(string Tag, Version Version, Uri DownloadUri);

internal static class UpdateService
{
    private const string ReleasesApi = "https://api.github.com/repos/beemoon23/BatteryTray/releases?per_page=20";
    private const string AssetName = "BatteryTray-win-x64.zip";
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BatteryTray", Application.ProductVersion));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static async Task<BatteryTrayRelease?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(ReleasesApi, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var current = GetCurrentVersion();
        foreach (var release in json.RootElement.EnumerateArray()
                     .Where(x => x.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())
                     .OrderByDescending(x => x.TryGetProperty("published_at", out var date) ? date.GetString() : string.Empty))
        {
            if (!release.TryGetProperty("tag_name", out var tagElement)) continue;
            var tag = tagElement.GetString() ?? string.Empty;
            var versionText = tag.TrimStart('v', 'V');
            if (!Version.TryParse(versionText, out var version) || version <= current) continue;

            if (!release.TryGetProperty("assets", out var assets)) continue;
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != AssetName) continue;
                var url = asset.GetProperty("browser_download_url").GetString();
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                    return new BatteryTrayRelease(tag, version, uri);
            }
        }

        return null;
    }

    public static async Task InstallAsync(BatteryTrayRelease release, CancellationToken cancellationToken = default)
    {
        var appPath = Application.ExecutablePath;
        var appDirectory = Path.GetDirectoryName(appPath) ?? throw new InvalidOperationException("Pasta do aplicativo não encontrada.");
        var tempDirectory = Path.Combine(Path.GetTempPath(), "BatteryTrayUpdate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var zipPath = Path.Combine(tempDirectory, AssetName);

        try
        {
            using (var response = await Client.GetAsync(release.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var destination = File.Create(zipPath);
                await source.CopyToAsync(destination, cancellationToken);
            }

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    var target = Path.GetFullPath(Path.Combine(tempDirectory, "verify", entry.FullName));
                    var root = Path.GetFullPath(Path.Combine(tempDirectory, "verify")) + Path.DirectorySeparatorChar;
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("O pacote de atualização contém um caminho inválido.");
                }
            }

            var scriptPath = Path.Combine(tempDirectory, "apply-update.ps1");
            var script = $@"$ErrorActionPreference = 'Stop'
$processId = {Environment.ProcessId}
$zip = '{EscapePowerShell(zipPath)}'
$target = '{EscapePowerShell(appDirectory)}'
$exe = '{EscapePowerShell(appPath)}'
$temp = Join-Path $env:TEMP ('BatteryTrayExtract-' + [guid]::NewGuid().ToString('N'))
try {{
    Wait-Process -Id $processId -ErrorAction SilentlyContinue
    Expand-Archive -LiteralPath $zip -DestinationPath $temp -Force
    Copy-Item -Path (Join-Path $temp '*') -Destination $target -Recurse -Force
    Start-Process -FilePath $exe -WorkingDirectory $target
}} catch {{
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show('Não foi possível concluir a atualização do BatteryTray: ' + $_.Exception.Message, 'BatteryTray')
}} finally {{
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Split-Path -Parent $zip) -Recurse -Force -ErrorAction SilentlyContinue
}}";
            await File.WriteAllTextAsync(scriptPath, script, cancellationToken);

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }) ?? throw new InvalidOperationException("Não foi possível iniciar o instalador da atualização.");

            Application.Exit();
        }
        catch
        {
            try { Directory.Delete(tempDirectory, true); } catch { }
            throw;
        }
    }

    private static Version GetCurrentVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version ?? new Version(0, 0, 0);
    }

    private static string EscapePowerShell(string value) => value.Replace("'", "''");
}
