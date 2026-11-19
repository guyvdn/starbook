using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace Starbook.Api.Tests;

public sealed class LogoSearchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LogoSearch Search(Func<string, (HttpStatusCode, string)> reply) =>
        new(new HttpClient(new Handler(reply)), NullLogger<LogoSearch>.Instance);

    [Theory]
    [InlineData("logos:google-calendar", true)]
    [InlineData("thesvg-color:google-calendar-2026", true)]
    [InlineData("logos:", false)]
    [InlineData("logos", false)]
    [InlineData("logos:../etc", false)]
    [InlineData("logos:a/b", false)]
    [InlineData("Logos:a", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Validates_logo_ids(string? id, bool valid) => Assert.Equal(valid, LogoSearch.IsValidId(id));

    [Fact]
    public async Task Returns_valid_ids_and_drops_malformed_ones()
    {
        var logos = Search(_ => (HttpStatusCode.OK, """{"icons":["logos:google-calendar","bad id","selfhst:a/b"]}"""));

        var hits = await logos.SearchAsync("google calendar", Ct);

        Assert.Equal([new LogoHit("logos:google-calendar")], hits);
    }

    [Fact]
    public async Task A_failing_source_yields_no_hits() =>
        Assert.Empty(await Search(_ => (HttpStatusCode.BadGateway, "")).SearchAsync("x", Ct));

    [Fact]
    public async Task Rejects_a_download_that_is_not_an_svg()
    {
        Assert.Null(await Search(_ => (HttpStatusCode.OK, "<html>nope</html>")).GetSvgAsync("logos:a", Ct));
        Assert.NotNull(await Search(_ => (HttpStatusCode.OK, "<svg xmlns='http://www.w3.org/2000/svg'/>")).GetSvgAsync("logos:a", Ct));
    }

    [Fact]
    public async Task Never_calls_the_source_for_an_invalid_id()
    {
        var called = false;
        var logos = Search(_ => { called = true; return (HttpStatusCode.OK, "<svg/>"); });

        Assert.Null(await logos.GetSvgAsync("logos:../x", Ct));
        Assert.False(called);
    }

    private sealed class Handler(Func<string, (HttpStatusCode, string)> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var (status, body) = reply(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8) });
        }
    }
}
