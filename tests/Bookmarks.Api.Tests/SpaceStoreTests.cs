namespace Bookmarks.Api.Tests;

public sealed class SpaceStoreTests : IDisposable
{
    private readonly string _dataPath = Path.Combine(Path.GetTempPath(), "bookmarks-tests", Guid.NewGuid().ToString("N"));

    public void Dispose() => Directory.Delete(_dataPath, recursive: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<SpaceStore> Open() => SpaceStore.OpenAsync(_dataPath, Ct);

    private static Space WithTile(Space space, string title) => space with
    {
        Tabs = [space.Tabs[0] with
        {
            Sections = [space.Tabs[0].Sections[0] with { Tiles = [new Tile("t_1", title, "https://example.com/", null)] }],
        }],
    };

    [Fact]
    public async Task Empty_data_folder_is_seeded_with_one_space()
    {
        var store = await Open();

        var only = Assert.Single(store.List());
        Assert.Equal("personal", only.Slug);
        Assert.True(File.Exists(Path.Combine(_dataPath, "spaces", "personal.json")));
    }

    [Fact]
    public async Task Save_bumps_the_version_and_survives_a_reopen()
    {
        var store = await Open();
        var space = store.Get("personal")!;

        var result = await store.SaveAsync(WithTile(space, "Example"), Ct);

        Assert.Equal(SaveStatus.Saved, result.Status);
        Assert.Equal(space.Version + 1, result.Space!.Version);

        var reopened = (await Open()).Get("personal")!;
        Assert.Equal(space.Version + 1, reopened.Version);
        Assert.Equal("Example", reopened.Tabs[0].Sections[0].Tiles[0].Title);
        Assert.Empty(Directory.GetFiles(Path.Combine(_dataPath, "spaces"), "*.tmp"));
    }

    [Fact]
    public async Task Save_based_on_a_stale_version_is_rejected_with_the_current_space()
    {
        var store = await Open();
        var original = store.Get("personal")!;
        await store.SaveAsync(WithTile(original, "First device"), Ct);

        var result = await store.SaveAsync(WithTile(original, "Second device"), Ct);

        Assert.Equal(SaveStatus.Conflict, result.Status);
        Assert.Equal("First device", result.Space!.Tabs[0].Sections[0].Tiles[0].Title);
        Assert.Equal("First device", store.Get("personal")!.Tabs[0].Sections[0].Tiles[0].Title);
    }

    [Fact]
    public async Task Update_applies_to_the_latest_version_so_client_edits_are_kept()
    {
        var store = await Open();
        var original = store.Get("personal")!;
        await store.SaveAsync(WithTile(original, "Edited by a client"), Ct);

        var updated = await store.UpdateAsync("personal", s => s with { Name = "Renamed by the server" }, Ct);

        Assert.Equal("Renamed by the server", updated!.Name);
        Assert.Equal("Edited by a client", updated.Tabs[0].Sections[0].Tiles[0].Title);
        Assert.Equal(original.Version + 2, updated.Version);
    }

    [Fact]
    public async Task Create_keeps_order_and_refuses_a_taken_slug()
    {
        var store = await Open();

        Assert.NotNull(await store.CreateAsync("work", "Work", Ct));
        Assert.Null(await store.CreateAsync("work", "Work again", Ct));

        Assert.Equal(["personal", "work"], (await Open()).List().Select(s => s.Slug));
    }

    [Fact]
    public async Task Delete_removes_the_space_but_keeps_its_file()
    {
        var store = await Open();
        await store.CreateAsync("work", "Work", Ct);

        Assert.True(await store.DeleteAsync("work", Ct));

        Assert.Null(store.Get("work"));
        Assert.Single(Directory.GetFiles(Path.Combine(_dataPath, "spaces"), "work.json.deleted-*"));
        Assert.Equal(["personal"], (await Open()).List().Select(s => s.Slug));
    }
}
