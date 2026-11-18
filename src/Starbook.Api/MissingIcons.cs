using System.Collections.Concurrent;

namespace Starbook.Api;

/// <summary>Fills in an icon for every tile in a space that has none.</summary>
public sealed class MissingIcons(SpaceStore store, IconFetcher icons)
{
    private const int Parallelism = 6;

    /// <returns>The saved space, or <c>null</c> when it does not exist.</returns>
    public async Task<Space?> FillAsync(string slug, CancellationToken ct)
    {
        if (store.Get(slug) is not { } space) return null;

        var urls = space.Tabs.SelectMany(t => t.Sections).SelectMany(s => s.Tiles)
            .Where(t => t.Image is null).Select(t => t.Url).Distinct().ToList();
        if (urls.Count == 0) return space;

        // One lookup per URL, done outside the store's lock: this takes seconds to minutes.
        var found = new ConcurrentDictionary<string, string>();
        await Parallel.ForEachAsync(urls, new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct },
            async (url, token) =>
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                try
                {
                    if ((await icons.FetchAsync(new Uri(url), timeout.Token)).Image is { } image) found[url] = image;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                }
            });

        // Apply to the latest version, and only to tiles that still have no image.
        return await store.UpdateAsync(slug, latest => latest with
        {
            Tabs = latest.Tabs.Select(tab => tab with
            {
                Sections = tab.Sections.Select(section => section with
                {
                    Tiles = section.Tiles.Select(tile => tile.Image is null && found.TryGetValue(tile.Url, out var image)
                        ? tile with { Image = image }
                        : tile).ToList(),
                }).ToList(),
            }).ToList(),
        }, ct);
    }
}
