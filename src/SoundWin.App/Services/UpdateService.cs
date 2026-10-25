using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SoundWin.App.Services;

public sealed class UpdateAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;
}

public sealed class UpdateInfo
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("assets")]
    public List<UpdateAsset> Assets { get; set; } = new();
}

public static class UpdateService
{
    public const string DefaultCurrentVersion = "v0.1.0-edge.8";
    public static string CurrentVersion { get; } = ResolveCurrentVersion();
    private const string RepoOwner = "djell1l";
    private const string RepoName = "SoundWin";
    private static readonly HttpClient Http = new();

    private static string ResolveCurrentVersion()
    {
        try
        {
            var asm = typeof(UpdateService).Assembly;
            var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(infoVer))
            {
                var clean = infoVer.Split('+')[0].Trim();
                if (!string.IsNullOrWhiteSpace(clean) && clean != "1.0.0" && clean != "1.0.0.0")
                {
                    return clean.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? clean : "v" + clean;
                }
            }

            var ver = asm.GetName().Version;
            if (ver is not null && ver != new Version(1, 0, 0, 0) && ver != new Version(0, 0, 0, 0))
            {
                return $"v{ver.Major}.{ver.Minor}.{ver.Build}";
            }
        }
        catch { }

        return DefaultCurrentVersion;
    }

    public static async Task<UpdateInfo?> CheckForUpdatesAsync(string channel = "Edge")
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases");
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("SCwin", "1.0"));

            var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync();
            var releases = JsonSerializer.Deserialize<List<UpdateInfo>>(json);
            if (releases is null || releases.Count == 0) return null;

            var isEdge = string.Equals(channel, "Edge", StringComparison.OrdinalIgnoreCase);

            UpdateInfo? latest = null;

            if (isEdge)
            {
                latest = releases.FirstOrDefault(r => IsNewerVersion(r.TagName, CurrentVersion));
            }
            else
            {
                latest = releases.FirstOrDefault(r => !r.Prerelease && IsNewerVersion(r.TagName, CurrentVersion));
            }

            return latest;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsNewerVersion(string remoteTag, string localTag)
    {
        try
        {
            var r = ParseVersion(remoteTag);
            var l = ParseVersion(localTag);
            if (r == null || l == null) return false;

            if (r.Value.Major != l.Value.Major) return r.Value.Major > l.Value.Major;
            if (r.Value.Minor != l.Value.Minor) return r.Value.Minor > l.Value.Minor;
            if (r.Value.Patch != l.Value.Patch) return r.Value.Patch > l.Value.Patch;

            // Same major.minor.patch
            if (r.Value.IsEdge && !l.Value.IsEdge) return false; // Stable is newer than Edge of same semver
            if (!r.Value.IsEdge && l.Value.IsEdge) return true;  // Stable release of current edge is newer
            if (r.Value.IsEdge && l.Value.IsEdge) return r.Value.EdgeBuild > l.Value.EdgeBuild;

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static (int Major, int Minor, int Patch, bool IsEdge, int EdgeBuild)? ParseVersion(string tag)
    {
        var clean = tag.Trim().TrimStart('v', 'V');
        var isEdge = clean.Contains("-edge.", StringComparison.OrdinalIgnoreCase);
        int edgeBuild = 0;

        if (isEdge)
        {
            var parts = clean.Split(new[] { "-edge." }, StringSplitOptions.None);
            clean = parts[0];
            if (parts.Length > 1 && int.TryParse(parts[1], out var eb))
            {
                edgeBuild = eb;
            }
        }

        var segs = clean.Split('.');
        if (segs.Length < 2) return null;

        int major = int.TryParse(segs[0], out var maj) ? maj : 0;
        int minor = int.TryParse(segs[1], out var min) ? min : 0;
        int patch = segs.Length > 2 && int.TryParse(segs[2], out var pat) ? pat : 0;

        return (major, minor, patch, isEdge, edgeBuild);
    }
}
