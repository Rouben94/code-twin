using System;
using System.Text.Json;

namespace CodeTwin.Core;

public sealed class AvailableUpdate
{
    public AvailableUpdate(Version version, string releaseUrl, string setupUrl)
    {
        Version = version;
        ReleaseUrl = releaseUrl;
        SetupUrl = setupUrl;
    }

    public Version Version { get; }
    public string ReleaseUrl { get; }
    public string SetupUrl { get; }
}

/// <summary>Reads the GitHub "latest release" response that CI publishes for each manual release.</summary>
public static class ReleaseFeed
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/Rouben94/code-twin/releases/latest";

    /// <summary>The latest release if it is newer than <paramref name="installed"/> and ships a setup, otherwise null.</summary>
    public static AvailableUpdate? FindUpdate(string latestReleaseJson, Version installed)
    {
        using JsonDocument document = JsonDocument.Parse(latestReleaseJson);
        JsonElement release = document.RootElement;
        if (!release.TryGetProperty("tag_name", out JsonElement tag)
            || !Version.TryParse(tag.GetString()?.TrimStart('v'), out Version? latest)
            || latest <= installed
            || !release.TryGetProperty("html_url", out JsonElement releaseUrl)
            || !release.TryGetProperty("assets", out JsonElement assets))
        {
            return null;
        }

        string setupName = $"CodeTwin-Setup-{latest}.exe";
        foreach (JsonElement asset in assets.EnumerateArray())
        {
            if (string.Equals(asset.GetProperty("name").GetString(), setupName, StringComparison.OrdinalIgnoreCase)
                && asset.GetProperty("browser_download_url").GetString() is { } setupUrl)
            {
                return new AvailableUpdate(latest, releaseUrl.GetString() ?? setupUrl, setupUrl);
            }
        }

        return null;
    }
}
