using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Starbook.Api;

public sealed record IconRequest(string Url);

/// <summary>What a URL lookup found. Either field may be <c>null</c>.</summary>
public sealed record IconResult(string? Image, string? Title);

/// <summary>
/// Finds the best icon for a site and stores it. Candidates come from the page's &lt;link&gt; tags, its web app
/// manifest and <c>/favicon.ico</c>; the largest is tried first, and an SVG beats any bitmap.
/// </summary>
public sealed partial class IconFetcher(HttpClient http, ImageStore images, ILogger<IconFetcher> log)
{
    private const int MaxPageBytes = 1024 * 1024;

    public async Task<IconResult> FetchAsync(Uri page, CancellationToken ct)
    {
        string? html = null;
        Uri baseUri = page;
        try
        {
            using var response = await http.GetAsync(page, HttpCompletionOption.ResponseHeadersRead, ct);
            baseUri = response.RequestMessage?.RequestUri ?? page; // after redirects
            if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType is "text/html" or null)
                html = Encoding.UTF8.GetString(await ReadCappedAsync(response, MaxPageBytes, ct) ?? []);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogInformation("Page fetch failed for {Url}: {Error}", page, e.Message);
        }

        var candidates = html is null ? [] : await CandidatesAsync(html, baseUri, ct);
        candidates.Add(new Candidate(new Uri(baseUri, "/favicon.ico"), 16));

        foreach (var candidate in candidates.OrderByDescending(c => c.Score).DistinctBy(c => c.Url))
        {
            if (await DownloadAsync(candidate.Url, ct) is { } image)
                return new IconResult(image, html is null ? null : TitleOf(html));
        }

        return new IconResult(null, html is null ? null : TitleOf(html));
    }

    private sealed record Candidate(Uri Url, int Score);

    private async Task<List<Candidate>> CandidatesAsync(string html, Uri baseUri, CancellationToken ct)
    {
        var result = new List<Candidate>();
        Uri? manifest = null;

        foreach (Match link in LinkTag().Matches(html))
        {
            var attrs = Attributes(link.Value);
            if (!attrs.TryGetValue("rel", out var rel) || !attrs.TryGetValue("href", out var href)) continue;
            if (!Uri.TryCreate(baseUri, WebUtility.HtmlDecode(href), out var url)) continue;

            var rels = rel.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (rels.Contains("manifest")) manifest = url;
            else if (rels.Contains("apple-touch-icon") || rels.Contains("apple-touch-icon-precomposed"))
                result.Add(new Candidate(url, SizeOf(attrs.GetValueOrDefault("sizes")) ?? 180));
            else if (rels.Contains("icon"))
                result.Add(new Candidate(url, IsSvg(url, attrs.GetValueOrDefault("type"))
                    ? 1000
                    : SizeOf(attrs.GetValueOrDefault("sizes")) ?? 32));
        }

        if (manifest is not null) result.AddRange(await ManifestIconsAsync(manifest, ct));
        return result;
    }

    private async Task<IEnumerable<Candidate>> ManifestIconsAsync(Uri manifest, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(manifest, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode || await ReadCappedAsync(response, MaxPageBytes, ct) is not { } bytes)
                return [];

            using var doc = JsonDocument.Parse(bytes);
            if (!doc.RootElement.TryGetProperty("icons", out var icons) || icons.ValueKind != JsonValueKind.Array)
                return [];

            var result = new List<Candidate>();
            foreach (var icon in icons.EnumerateArray())
            {
                if (icon.TryGetProperty("purpose", out var purpose) && purpose.GetString() is { } p
                    && !p.Contains("any", StringComparison.OrdinalIgnoreCase))
                    continue; // maskable/monochrome icons are cropped or single-colour
                if (icon.TryGetProperty("src", out var src) && src.GetString() is { } s
                    && Uri.TryCreate(manifest, s, out var url))
                {
                    var sizes = icon.TryGetProperty("sizes", out var sz) ? sz.GetString() : null;
                    var type = icon.TryGetProperty("type", out var t) ? t.GetString() : null;
                    result.Add(new Candidate(url, IsSvg(url, type) ? 1000 : SizeOf(sizes) ?? 64));
                }
            }
            return result;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException
                                      or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogInformation("Manifest fetch failed for {Url}: {Error}", manifest, e.Message);
            return [];
        }
    }

    private async Task<string?> DownloadAsync(Uri url, CancellationToken ct)
    {
        if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps) return null;
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await ReadCappedAsync(response, ImageStore.MaxBytes, ct) is { } bytes
                ? await images.SaveAsync(bytes, ct)
                : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <returns>The body, or <c>null</c> when it is larger than <paramref name="max"/>.</returns>
    private static async Task<byte[]?> ReadCappedAsync(HttpResponseMessage response, int max, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > max) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > max) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static bool IsSvg(Uri url, string? type) =>
        type?.Contains("svg", StringComparison.OrdinalIgnoreCase) == true
        || url.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);

    /// <summary>Largest dimension in a <c>sizes</c> attribute ("16x16 32x32", "any").</summary>
    private static int? SizeOf(string? sizes)
    {
        if (string.IsNullOrWhiteSpace(sizes)) return null;
        if (sizes.Contains("any", StringComparison.OrdinalIgnoreCase)) return 1000;
        int? best = null;
        foreach (var part in sizes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var x = part.IndexOfAny(['x', 'X']);
            if (x > 0 && int.TryParse(part.AsSpan(0, x), out var w)) best = Math.Max(best ?? 0, w);
        }
        return best;
    }

    internal static string? TitleOf(string html)
    {
        var match = TitleTag().Match(html);
        if (!match.Success) return null;
        var title = Whitespace().Replace(WebUtility.HtmlDecode(match.Groups[1].Value), " ").Trim();
        return title.Length is > 0 and <= 200 ? title : null;
    }

    internal static Dictionary<string, string> Attributes(string tag)
    {
        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Attribute().Matches(tag))
            attrs.TryAdd(m.Groups["name"].Value, m.Groups["dq"].Success ? m.Groups["dq"].Value
                : m.Groups["sq"].Success ? m.Groups["sq"].Value : m.Groups["bare"].Value);
        return attrs;
    }

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTag();

    [GeneratedRegex(@"(?<name>[a-zA-Z-]+)\s*=\s*(?:""(?<dq>[^""]*)""|'(?<sq>[^']*)'|(?<bare>[^\s>]+))")]
    private static partial Regex Attribute();

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
