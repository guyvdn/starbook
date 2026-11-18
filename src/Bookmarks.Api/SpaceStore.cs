using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Bookmarks.Api;

public enum SaveStatus { Saved, NotFound, Conflict }

public sealed record SaveResult(SaveStatus Status, Space? Space);

/// <summary>
/// All spaces are held in memory and written through to one JSON file per space.
/// One writer at a time; reads are lock-free snapshots of immutable references.
/// </summary>
public sealed class SpaceStore
{
    private readonly string _spacesDir;
    private readonly string _indexPath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private volatile Dictionary<string, Space> _spaces = new(StringComparer.Ordinal);
    private volatile List<string> _order = [];

    private SpaceStore(string dataPath)
    {
        _spacesDir = Path.Combine(dataPath, "spaces");
        _indexPath = Path.Combine(dataPath, "spaces.json");
    }

    public static async Task<SpaceStore> OpenAsync(string dataPath, CancellationToken ct = default)
    {
        var store = new SpaceStore(dataPath);
        await store.LoadAsync(ct);
        return store;
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_spacesDir);

        var spaces = new Dictionary<string, Space>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(_spacesDir, "*.json"))
        {
            var space = await ReadAsync(file, FileJsonContext.Default.Space, ct)
                ?? throw new InvalidDataException($"Empty space file: {file}");
            var slug = Path.GetFileNameWithoutExtension(file);
            if (space.Slug != slug)
                throw new InvalidDataException($"Space file {file} has slug '{space.Slug}'.");
            spaces[slug] = space;
        }

        var stored = File.Exists(_indexPath)
            ? (await ReadAsync(_indexPath, FileJsonContext.Default.SpaceIndex, ct))?.Order ?? []
            : [];
        // Keep the stored order for known spaces, then append any space file the index does not list.
        var order = stored.Where(spaces.ContainsKey).Distinct()
            .Concat(spaces.Keys.Where(k => !stored.Contains(k)).Order(StringComparer.Ordinal))
            .ToList();

        _spaces = spaces;
        _order = order;

        if (spaces.Count == 0)
            await CreateAsync("personal", "Personal", ct);
        else if (!order.SequenceEqual(stored))
            await WriteAsync(_indexPath, new SpaceIndex(order), FileJsonContext.Default.SpaceIndex, ct);
    }

    public IReadOnlyList<SpaceSummary> List()
    {
        var spaces = _spaces;
        return _order.Select(slug => new SpaceSummary(slug, spaces[slug].Name)).ToList();
    }

    public string? FirstSlug() => _order.FirstOrDefault();

    public Space? Get(string slug) => _spaces.GetValueOrDefault(slug);

    /// <summary>Saves a space if <paramref name="incoming"/>'s version matches the stored one.</summary>
    public async Task<SaveResult> SaveAsync(Space incoming, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            if (_spaces.GetValueOrDefault(incoming.Slug) is not { } current)
                return new SaveResult(SaveStatus.NotFound, null);
            if (current.Version != incoming.Version)
                return new SaveResult(SaveStatus.Conflict, current);

            var saved = incoming with { Version = current.Version + 1 };
            await WriteSpaceAsync(saved, ct);
            _spaces = new Dictionary<string, Space>(_spaces, StringComparer.Ordinal) { [saved.Slug] = saved };
            return new SaveResult(SaveStatus.Saved, saved);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <returns>The new space, or <c>null</c> when the slug is taken.</returns>
    public async Task<Space?> CreateAsync(string slug, string name, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            if (_spaces.ContainsKey(slug)) return null;

            var space = new Space(slug, name, 1,
                [new Tab(NewId("b"), "Home", [new Section(NewId("s"), null, [])])]);
            await WriteSpaceAsync(space, ct);

            var order = new List<string>(_order) { slug };
            await WriteAsync(_indexPath, new SpaceIndex(order), FileJsonContext.Default.SpaceIndex, ct);

            _spaces = new Dictionary<string, Space>(_spaces, StringComparer.Ordinal) { [slug] = space };
            _order = order;
            return space;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Removes a space from the app. The file is renamed, not deleted, so a mistaken delete can be undone by
    /// renaming it back.
    /// </summary>
    public async Task<bool> DeleteAsync(string slug, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            if (!_spaces.ContainsKey(slug)) return false;

            var order = _order.Where(s => s != slug).ToList();
            await WriteAsync(_indexPath, new SpaceIndex(order), FileJsonContext.Default.SpaceIndex, ct);
            File.Move(SpacePath(slug), $"{SpacePath(slug)}.deleted-{DateTime.UtcNow:yyyyMMddHHmmss}");

            var spaces = new Dictionary<string, Space>(_spaces, StringComparer.Ordinal);
            spaces.Remove(slug);
            _spaces = spaces;
            _order = order;
            return true;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private string SpacePath(string slug) => Path.Combine(_spacesDir, slug + ".json");

    private Task WriteSpaceAsync(Space space, CancellationToken ct) =>
        WriteAsync(SpacePath(space.Slug), space, FileJsonContext.Default.Space, ct);

    private static async Task<T?> ReadAsync<T>(string path, JsonTypeInfo<T> type, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(stream, type, ct);
    }

    /// <summary>Write to a temp file in the same folder, then rename over the target: never a half-written file.</summary>
    private static async Task WriteAsync<T>(string path, T value, JsonTypeInfo<T> type, CancellationToken ct)
    {
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, type, ct);
            }
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }
    }

    private static string NewId(string prefix) => $"{prefix}_{Guid.NewGuid().ToString("N")[..8]}";
}
