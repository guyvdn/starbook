using System.Security.Cryptography;
using System.Text;

namespace Starbook.Api;

/// <summary>
/// Content-addressed image files: <c>&lt;sha256&gt;.&lt;ext&gt;</c>. The format is sniffed from the bytes, never
/// taken from a file name or a Content-Type header.
/// </summary>
public sealed class ImageStore
{
    public const int MaxBytes = 1024 * 1024;

    public string Directory { get; }

    public ImageStore(string dataPath)
    {
        Directory = Path.Combine(dataPath, "images");
        System.IO.Directory.CreateDirectory(Directory);
    }

    /// <returns>The image reference, or <c>null</c> when the bytes are not a supported image.</returns>
    public async Task<string?> SaveAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct = default)
    {
        if (bytes.Length is 0 or > MaxBytes || Sniff(bytes.Span) is not { } ext) return null;

        var name = $"{Convert.ToHexStringLower(SHA256.HashData(bytes.Span))}.{ext}";
        var path = Path.Combine(Directory, name);
        if (File.Exists(path)) return name;

        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(temp, bytes, ct);
        File.Move(temp, path, overwrite: true);
        return name;
    }

    public static string? Sniff(ReadOnlySpan<byte> b)
    {
        if (b.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G'])) return "png";
        if (b.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF])) return "jpg";
        if (b.StartsWith("GIF8"u8)) return "gif";
        if (b.Length > 12 && b.StartsWith("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8)) return "webp";
        if (b.StartsWith((ReadOnlySpan<byte>)[0, 0, 1, 0])) return "ico";
        if (LooksLikeSvg(b)) return "svg";
        return null;
    }

    private static bool LooksLikeSvg(ReadOnlySpan<byte> b)
    {
        var head = Encoding.UTF8.GetString(b[..Math.Min(b.Length, 1024)]).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return head.StartsWith('<') && head.Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }

    public static string ContentType(string ext) => ext switch
    {
        "png" => "image/png",
        "jpg" => "image/jpeg",
        "gif" => "image/gif",
        "webp" => "image/webp",
        "ico" => "image/x-icon",
        "svg" => "image/svg+xml",
        _ => "application/octet-stream",
    };
}
