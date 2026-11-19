using System.Text.Json;
using System.Text.Json.Serialization;

namespace Starbook.Api;

/// <param name="Color">One of <see cref="SpaceValidator.Colors"/>, or <c>null</c> for the default blue look.</param>
public sealed record Space(
    string Slug,
    string Name,
    int Version,
    List<Tab> Tabs,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Color = null);

public sealed record Tab(string Id, string Title, List<Section> Sections);

public sealed record Section(string Id, string? Title, List<Tile> Tiles);

public sealed record Tile(string Id, string Title, string Url, string? Image);

public sealed record SpaceSummary(string Slug, string Name);

public sealed record CreateSpaceRequest(string Slug, string Name);

/// <summary>Order of the spaces; the space files themselves hold everything else.</summary>
public sealed record SpaceIndex(List<string> Order);

/// <summary>What the start page embeds, so it renders without a second request.</summary>
public sealed record StartPageData(Space Space, IReadOnlyList<SpaceSummary> Spaces);

public sealed record ErrorResponse(string Error);

public sealed record ImageResponse(string Image);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Space))]
[JsonSerializable(typeof(SpaceIndex))]
[JsonSerializable(typeof(StartPageData))]
[JsonSerializable(typeof(CreateSpaceRequest))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(ImageResponse))]
[JsonSerializable(typeof(IconRequest))]
[JsonSerializable(typeof(IconResult))]
[JsonSerializable(typeof(IReadOnlyList<SpaceSummary>))]
[JsonSerializable(typeof(IReadOnlyList<LogoHit>))]
internal partial class AppJsonContext : JsonSerializerContext;

/// <summary>Indented variant for the files on disk, so they stay readable and diffable.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(Space))]
[JsonSerializable(typeof(SpaceIndex))]
internal partial class FileJsonContext : JsonSerializerContext;
