using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bookmarks.Api.Tests;

public sealed class IconFetcherTests : IDisposable
{
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] Ico = [0, 0, 1, 0, 1, 0, 16, 16];

    private readonly string _dataPath = Path.Combine(Path.GetTempPath(), "bookmarks-tests", Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, (string Type, byte[] Body)> _site = new();
    private readonly List<string> _requested = [];

    public void Dispose()
    {
        if (Directory.Exists(_dataPath)) Directory.Delete(_dataPath, recursive: true);
    }

    private IconFetcher Fetcher() => new(new HttpClient(new FakeSite(_site, _requested)), new ImageStore(_dataPath),
        NullLogger<IconFetcher>.Instance);

    private void Page(string html) => _site["https://example.com/"] = ("text/html", Encoding.UTF8.GetBytes(html));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Prefers_the_largest_icon_and_returns_the_page_title()
    {
        Page("""
             <html><head><title> Example &amp; Co </title>
             <link rel="icon" href="/small.png" sizes="16x16">
             <link rel=apple-touch-icon href='/touch.png'>
             </head></html>
             """);
        _site["https://example.com/small.png"] = ("image/png", [.. Png, 1]);
        _site["https://example.com/touch.png"] = ("image/png", [.. Png, 2]);

        var result = await Fetcher().FetchAsync(new Uri("https://example.com/"), Ct);

        Assert.Equal("Example & Co", result.Title);
        Assert.NotNull(result.Image);
        Assert.Equal([.. Png, 2], File.ReadAllBytes(Path.Combine(_dataPath, "images", result.Image)));
    }

    [Fact]
    public async Task Uses_manifest_icons_but_skips_maskable_ones()
    {
        Page("""<link rel="manifest" href="/app.webmanifest">""");
        _site["https://example.com/app.webmanifest"] = ("application/manifest+json", Encoding.UTF8.GetBytes("""
            { "icons": [
                { "src": "/mask.png", "sizes": "512x512", "purpose": "maskable" },
                { "src": "/any.png", "sizes": "192x192" } ] }
            """));
        _site["https://example.com/mask.png"] = ("image/png", [.. Png, 9]);
        _site["https://example.com/any.png"] = ("image/png", [.. Png, 7]);

        var result = await Fetcher().FetchAsync(new Uri("https://example.com/"), Ct);

        Assert.Equal([.. Png, 7], File.ReadAllBytes(Path.Combine(_dataPath, "images", result.Image!)));
        Assert.DoesNotContain("https://example.com/mask.png", _requested);
    }

    [Fact]
    public async Task Falls_back_to_favicon_ico_when_a_candidate_is_not_an_image()
    {
        Page("""<link rel="icon" href="/broken.png" sizes="64x64">""");
        _site["https://example.com/broken.png"] = ("text/html", "<html>not found</html>"u8.ToArray());
        _site["https://example.com/favicon.ico"] = ("image/x-icon", Ico);

        var result = await Fetcher().FetchAsync(new Uri("https://example.com/"), Ct);

        Assert.EndsWith(".ico", result.Image);
    }

    [Fact]
    public async Task Unreachable_site_yields_no_image_and_no_title()
    {
        var result = await Fetcher().FetchAsync(new Uri("https://example.com/"), Ct);

        Assert.Equal(new IconResult(null, null), result);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "jpg")]
    [InlineData(new byte[] { 0, 0, 1, 0, 1, 0 }, "ico")]
    [InlineData(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' }, "gif")]
    [InlineData(new byte[] { (byte)'<', (byte)'h', (byte)'t', (byte)'m', (byte)'l', (byte)'>' }, null)]
    public void Sniffs_formats_from_bytes(byte[] bytes, string? ext) => Assert.Equal(ext, ImageStore.Sniff(bytes));

    [Fact]
    public void Sniffs_svg_after_an_xml_declaration() =>
        Assert.Equal("svg", ImageStore.Sniff("""<?xml version="1.0"?><svg xmlns="http://www.w3.org/2000/svg"/>"""u8));

    private sealed class FakeSite(Dictionary<string, (string Type, byte[] Body)> site, List<string> requested)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            requested.Add(url);
            if (!site.TryGetValue(url, out var page))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });

            var content = new ByteArrayContent(page.Body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(page.Type);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
        }
    }
}
