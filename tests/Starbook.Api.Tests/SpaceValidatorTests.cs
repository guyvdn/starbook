namespace Starbook.Api.Tests;

public sealed class SpaceValidatorTests
{
    private static Space Valid(params Tile[] tiles) =>
        new("personal", "Personal", 1, [new Tab("b_1", "Home", [new Section("s_1", null, [.. tiles])])]);

    [Fact]
    public void A_palette_colour_or_none_is_valid()
    {
        Assert.Null(SpaceValidator.Validate(Valid() with { Color = "#fc6404" }));
        Assert.Null(SpaceValidator.Validate(Valid() with { Color = null }));
    }

    [Theory]
    [InlineData("#FC6404")]
    [InlineData("#123456")]
    [InlineData("red")]
    [InlineData("")]
    public void A_colour_outside_the_palette_is_rejected(string color) =>
        Assert.Equal("Unknown colour.", SpaceValidator.Validate(Valid() with { Color = color }));

    [Fact]
    public void Valid_space_passes() =>
        Assert.Null(SpaceValidator.Validate(Valid(new Tile("t_1", "Example", "https://example.com/", null))));

    [Theory]
    [InlineData("personal", true)]
    [InlineData("customer-a", true)]
    [InlineData("a", true)]
    [InlineData("Personal", false)]
    [InlineData("-work", false)]
    [InlineData("work-", false)]
    [InlineData("../etc", false)]
    [InlineData("", false)]
    public void Slugs(string slug, bool valid) => Assert.Equal(valid, SpaceValidator.IsValidSlug(slug));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///c:/windows")]
    [InlineData("not a url")]
    public void Only_http_and_https_urls_are_allowed(string url) =>
        Assert.NotNull(SpaceValidator.Validate(Valid(new Tile("t_1", "Bad", url, null))));

    [Fact]
    public void Duplicate_ids_are_rejected() =>
        Assert.NotNull(SpaceValidator.Validate(Valid(
            new Tile("t_1", "A", "https://a.example/", null),
            new Tile("t_1", "B", "https://b.example/", null))));

    [Fact]
    public void A_tab_without_sections_is_rejected() =>
        Assert.NotNull(SpaceValidator.Validate(new Space("personal", "Personal", 1, [new Tab("b_1", "Home", [])])));

    [Theory]
    [InlineData("../../secret.png")]
    [InlineData("abc.png")]
    public void Image_references_must_be_content_hashes(string image) =>
        Assert.NotNull(SpaceValidator.Validate(Valid(new Tile("t_1", "A", "https://a.example/", image))));
}
