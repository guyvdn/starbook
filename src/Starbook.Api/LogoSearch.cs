using System.Text.Json;
using System.Text.RegularExpressions;

namespace Starbook.Api;

/// <summary>A logo found by a search. <paramref name="Id"/> is <c>prefix:name</c>, as the logo source names it.</summary>
public sealed record LogoHit(string Id);

/// <summary>
/// Searches open-licensed brand logo sets (SVG Logos, theSVG, selfh.st icons) through the Iconify API. They are
/// vector with a transparent background and keep the brand's colours. Only the typed query leaves the server,
/// never the bookmarked URL, and a picked logo is stored like any other image.
/// </summary>
public sealed partial class LogoSearch(HttpClient http, ILogger<LogoSearch> log)
{
    private const string Api = "https://api.iconify.design";
    private const string Collections = "logos,thesvg-color,selfhst";
    private const int MaxResults = 48;
    private const int MaxSvgBytes = 256 * 1024;

    public async Task<IReadOnlyList<LogoHit>> SearchAsync(string query, CancellationToken ct)
    {
        query = query.Trim();
        if (query.Length is 0 or > 100) return [];
        try
        {
            var url = $"{Api}/search?query={Uri.EscapeDataString(query)}&limit={MaxResults}&prefixes={Collections}";
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return [];

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("icons", out var icons) || icons.ValueKind != JsonValueKind.Array)
                return [];
            return [.. icons.EnumerateArray()
                .Select(i => i.GetString())
                .Where(IsValidId)
                .Select(i => new LogoHit(i!))];
        }
        catch (Exception e) when (e is HttpRequestException or JsonException
                                      or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogInformation("Logo search failed for '{Query}': {Error}", query, e.Message);
            return [];
        }
    }

    /// <returns>The SVG, or <c>null</c> for an unknown id or a response that is not a small SVG.</returns>
    public async Task<byte[]?> GetSvgAsync(string id, CancellationToken ct)
    {
        if (!IsValidId(id)) return null;
        var (prefix, name) = (id[..id.IndexOf(':')], id[(id.IndexOf(':') + 1)..]);
        try
        {
            using var response = await http.GetAsync($"{Api}/{prefix}/{name}.svg", HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxSvgBytes) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            return bytes.Length <= MaxSvgBytes && ImageStore.Sniff(bytes) == "svg" ? bytes : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogInformation("Logo fetch failed for {Id}: {Error}", id, e.Message);
            return null;
        }
    }

    /// <summary>Guards the id that is spliced into the upstream URL.</summary>
    public static bool IsValidId(string? id) => id is not null && LogoId().IsMatch(id);

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*:[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex LogoId();
}
