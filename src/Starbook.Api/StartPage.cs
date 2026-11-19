using System.Text.Json;

namespace Starbook.Api;

/// <summary>
/// Serves the built <c>index.html</c> with the space's data embedded, so a new tab renders from one response.
/// </summary>
public sealed class StartPage(IWebHostEnvironment env)
{
    private const string Marker = "<!--space-data-->";
    private const string ThemeMarker = "<!--space-theme-->";

    private readonly Lazy<string?> _template = new(() =>
    {
        var file = env.WebRootFileProvider.GetFileInfo("index.html");
        if (!file.Exists) return null;
        using var reader = new StreamReader(file.CreateReadStream());
        var html = reader.ReadToEnd();
        foreach (var marker in new[] { Marker, ThemeMarker })
            if (!html.Contains(marker, StringComparison.Ordinal))
                throw new InvalidOperationException($"index.html has no {marker} marker.");
        return html;
    });

    public IResult Render(StartPageData data)
    {
        if (_template.Value is not { } template)
            return Results.Problem("The frontend is not built. Run `npm run build` in web/, or use the Vite dev server.",
                statusCode: StatusCodes.Status503ServiceUnavailable);

        // The default encoder escapes <, > and &, so the JSON cannot close the script element.
        var json = JsonSerializer.Serialize(data, AppJsonContext.Default.StartPageData);
        // The accent is set before first paint, so a themed space never flashes the default colours. Only a
        // palette colour is ever embedded, so a hand-edited file cannot inject anything.
        var theme = SpaceValidator.IsValidColor(data.Space.Color)
            ? $"<script>document.documentElement.dataset.themed='';document.documentElement.style.setProperty('--accent','{data.Space.Color}')</script>"
            : "";
        var html = template
            .Replace(ThemeMarker, theme, StringComparison.Ordinal)
            .Replace(Marker, $"<script id=\"space-data\" type=\"application/json\">{json}</script>", StringComparison.Ordinal);
        return Results.Content(html, "text/html; charset=utf-8");
    }
}
