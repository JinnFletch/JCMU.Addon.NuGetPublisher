using JinnDev.JCMU.Addon.NuGetPublisher.Models;
using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.Utilities.Monad;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JinnDev.JCMU.Addon.NuGetPublisher.Services;

public static class NuGetFeedService
{
    private static readonly HttpClient HttpClient = new();

    /// <summary>
    /// Queries the destination source to find the highest published version of the package.
    /// Returns 0.0.0.0 if the package has never been published to this source before.
    /// </summary>
    public static async Task<Maybe<Version>> GetLatestVersionAsync(string packageId, PublishSource source, IHostServices host)
    {
        host.UI.WriteLine($"\n[Server Check] Querying {source.Name} for existing versions of '{packageId}'...", ConsoleColor.Cyan);

        return await Maybe.TryAsync<Version>(async () =>
        {
            Version? highestVersion;

            if (IsLocalFolder(source.Url))
            {
                highestVersion = GetLatestLocalVersion(packageId, source.Url);
            }
            else
            {
                highestVersion = await GetLatestRemoteVersionAsync(packageId, source, host).ConfigureAwait(false);
            }

            if (highestVersion == null)
            {
                host.UI.WriteLine($"  -> Highest published version found: {highestVersion}");
                return new Version(0, 0, 0, 0);
            }

            host.UI.WriteLine($"  -> Highest published version found: {highestVersion}");
            return highestVersion;
        }).ConfigureAwait(false);
    }

    private static bool IsLocalFolder(string url)
    {
        // Simple check: if it's not HTTP/HTTPS, assume it's a local or network file path
        return !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
               !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static Version? GetLatestLocalVersion(string packageId, string folderPath)
    {
        if (!Directory.Exists(folderPath)) return null;

        var files = Directory.GetFiles(folderPath, $"{packageId}.*.nupkg");
        var regex = new Regex($@"{Regex.Escape(packageId)}\.((?:\d+\.)*\d+)(?:-.*)?\.nupkg$", RegexOptions.IgnoreCase);

        var highestVersion = files
            .Select(f => regex.Match(Path.GetFileName(f)))
            .Where(m => m.Success && Version.TryParse(m.Groups[1].Value, out _))
            .Select(m => Version.Parse(m.Groups[1].Value))
            .OrderByDescending(v => v)
            .FirstOrDefault();

        return highestVersion;
    }

    private static async Task<Version?> GetLatestRemoteVersionAsync(string packageId, PublishSource source, IHostServices host)
    {
        try
        {
            // 1. Authenticate if necessary (useful for custom feeds like GitHub Packages or Nexus)
            var request = new HttpRequestMessage(HttpMethod.Get, source.Url);
            if (!string.IsNullOrWhiteSpace(source.ApiKey) && !source.Url.Contains("api.nuget.org"))
            {
                // Note: Standard NuGet.org doesn't need API keys for searching, but private feeds might
                request.Headers.Add("Authorization", $"Bearer {source.ApiKey}");
            }

            // 2. We use the NuGet V3 API to find the package base address or search endpoint.
            // For simplicity and speed in this addon, we query the search endpoint directly 
            // by manipulating the index.json URL to a standard V3 search query.
            string searchUrl = GetSearchUrlFromIndex(source.Url, packageId);
            request.RequestUri = new Uri(searchUrl);

            var response = await HttpClient.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                host.UI.WriteLine($"  -> [Warning] Failed to reach remote feed HTTP {response.StatusCode}. Assuming 0.0.0.", ConsoleColor.Yellow);
                return null;
            }

            var jsonContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(jsonContent);

            // 3. Parse the standard NuGet V3 JSON structure: { "data": [ { "version": "1.0.0" } ] }
            var dataElement = doc.RootElement.GetProperty("data");
            if (dataElement.ValueKind != JsonValueKind.Array || dataElement.GetArrayLength() == 0)
            {
                return null; // Package not found
            }

            // Assuming exact match search, the first element is our package
            var packageData = dataElement[0];
            var versionString = packageData.GetProperty("version").GetString();

            // NuGet versions can include pre-release tags (1.0.0-beta). We strip them to find the core Version object
            if (!string.IsNullOrWhiteSpace(versionString))
            {
                var coreVersionStr = versionString.Split('-')[0];
                if (Version.TryParse(coreVersionStr, out var v)) return v;
            }

            return null;
        }
        catch (Exception ex)
        {
            host.UI.WriteLine($"  -> [Warning] Could not verify remote version ({ex.Message}).", ConsoleColor.Yellow);
            return null;
        }
    }

    private static string GetSearchUrlFromIndex(string indexUrl, string packageId)
    {
        // Most V3 feeds (NuGet.org, GitHub, Azure) expose standard search endpoints.
        // If the URL ends in index.json, we transform it into a search query.
        // A production-grade NuGet client would parse the index.json first to find the `@type: SearchQueryService` URL,
        // but for a lightweight CLI tool, assuming standard V3 routing is usually sufficient.

        var baseUrl = indexUrl.EndsWith("index.json", StringComparison.OrdinalIgnoreCase)
            ? indexUrl.Substring(0, indexUrl.Length - "index.json".Length).TrimEnd('/')
            : indexUrl.TrimEnd('/');

        // Construct standard V3 search query (prerelease=true ensures we see everything)
        return $"{baseUrl}/query?q=packageid:{packageId}&prerelease=true&semVerLevel=2.0.0";
    }
}