using System.Text.Json;

namespace Starbook.Api;

/// <summary>
/// Serves the built <c>index.html</c> with the space's data embedded, so a new tab renders from one response.
/// </summary>
public sealed class StartPage(IWebHostEnvironment env)
{
    private const string Marker = "<!--space-data-->";

    private readonly Lazy<string?> _template = new(() =>
    {
        var file = env.WebRootFileProvider.GetFileInfo("index.html");
        if (!file.Exists) return null;
        using var reader = new StreamReader(file.CreateReadStream());
        var html = reader.ReadToEnd();
        return html.Contains(Marker, StringComparison.Ordinal)
            ? html
            : throw new InvalidOperationException($"index.html has no {Marker} marker.");
    });

    public IResult Render(StartPageData data)
    {
        if (_template.Value is not { } template)
            return Results.Problem("The frontend is not built. Run `npm run build` in web/, or use the Vite dev server.",
                statusCode: StatusCodes.Status503ServiceUnavailable);

        // The default encoder escapes <, > and &, so the JSON cannot close the script element.
        var json = JsonSerializer.Serialize(data, AppJsonContext.Default.StartPageData);
        var html = template.Replace(Marker,
            $"<script id=\"space-data\" type=\"application/json\">{json}</script>", StringComparison.Ordinal);
        return Results.Content(html, "text/html; charset=utf-8");
    }
}
