using System.Text.RegularExpressions;

namespace Bookmarks.Api;

public static partial class SpaceValidator
{
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,38}[a-z0-9])?$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex(@"^[a-f0-9]{64}\.(?:png|jpg|gif|webp|svg|ico)$")]
    private static partial Regex ImagePattern();

    public static bool IsValidSlug(string? slug) => slug is not null && SlugPattern().IsMatch(slug);

    public static bool IsValidImage(string? image) => image is not null && ImagePattern().IsMatch(image);

    /// <returns>An error message, or <c>null</c> when the space is valid.</returns>
    public static string? Validate(Space space)
    {
        if (!IsValidSlug(space.Slug)) return "Invalid slug: use lowercase letters, digits and hyphens.";
        if (string.IsNullOrWhiteSpace(space.Name)) return "A space needs a name.";
        if (space.Tabs is null) return "Missing tabs.";

        var ids = new HashSet<string>(StringComparer.Ordinal);
        string? Id(string? id, string what) =>
            string.IsNullOrWhiteSpace(id) || id.Length > 40 ? $"A {what} has a missing or too long id."
            : !ids.Add(id) ? $"Duplicate id '{id}'."
            : null;

        foreach (var tab in space.Tabs)
        {
            if (tab is null) return "Null tab.";
            if (Id(tab.Id, "tab") is { } e1) return e1;
            if (string.IsNullOrWhiteSpace(tab.Title)) return "A tab needs a title.";
            if (tab.Sections is not { Count: > 0 }) return $"Tab '{tab.Title}' needs at least one section.";

            foreach (var section in tab.Sections)
            {
                if (section is null) return "Null section.";
                if (Id(section.Id, "section") is { } e2) return e2;
                if (section.Tiles is null) return "Missing tiles.";

                foreach (var tile in section.Tiles)
                {
                    if (tile is null) return "Null tile.";
                    if (Id(tile.Id, "tile") is { } e3) return e3;
                    if (string.IsNullOrWhiteSpace(tile.Title)) return "A tile needs a title.";
                    if (!Uri.TryCreate(tile.Url, UriKind.Absolute, out var uri)
                        || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                        return $"Tile '{tile.Title}' needs an http or https URL.";
                    if (tile.Image is not null && !IsValidImage(tile.Image))
                        return $"Tile '{tile.Title}' has an invalid image reference.";
                }
            }
        }

        return null;
    }
}
