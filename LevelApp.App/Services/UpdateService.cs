using System.Net.Http;
using System.Text.Json;
using LevelApp.Core;

namespace LevelApp.App.Services;

public sealed class UpdateService : IUpdateService
{
    private const string ReleasesApiUrl =
        "https://api.github.com/repos/soldernerd/LevelApp/releases/latest";

    private static readonly HttpClient _http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"LevelApp/{AppVersion.Full}");
        return client;
    }

    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            var response = await _http.GetAsync(ReleasesApiUrl);
            response.EnsureSuccessStatusCode();

            using var doc  = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            string tagName      = root.GetProperty("tag_name").GetString() ?? "";
            string releaseNotes = root.TryGetProperty("body", out var bodyEl)
                ? (bodyEl.GetString() ?? "") : "";

            // Find the first .zip asset
            string? zipUrl = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (name is not null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        zipUrl = asset.TryGetProperty("browser_download_url", out var u)
                            ? u.GetString() : null;
                        break;
                    }
                }
            }

            if (zipUrl is null) return null;

            // Compare versions numerically ("v0.12.0" → 0.12.0)
            string tagVersion = tagName.TrimStart('v');
            if (!Version.TryParse(tagVersion,    out var remote))  return null;
            if (!Version.TryParse(AppVersion.Full, out var current)) return null;
            if (remote <= current) return null;

            return new UpdateInfo(tagVersion, tagName, zipUrl, releaseNotes);
        }
        catch
        {
            // A failed check must never prevent the app from starting.
            return null;
        }
    }

    public async Task<string> DownloadUpdateAsync(UpdateInfo update, IProgress<double> progress)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"LevelApp-{update.Version}.zip");

        using var response = await _http.GetAsync(
            update.ZipUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        long? totalBytes     = response.Content.Headers.ContentLength;
        long  downloadedBytes = 0;

        using var dest   = File.Create(tempPath);
        using var stream = await response.Content.ReadAsStreamAsync();

        var buffer = new byte[81920]; // 80 KB chunks
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, read));
            downloadedBytes += read;
            if (totalBytes.HasValue)
                progress.Report((double)downloadedBytes / totalBytes.Value);
        }

        progress.Report(1.0);
        return tempPath;
    }
}
