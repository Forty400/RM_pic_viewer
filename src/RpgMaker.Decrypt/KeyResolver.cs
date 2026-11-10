using System.Collections.Concurrent;
using System.Text.Json;

namespace RpgMaker.Decrypt;

public sealed class KeyResolver
{
    private readonly ConcurrentDictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _hexCache = new(StringComparer.OrdinalIgnoreCase);

    public byte[] ResolveFromImagePath(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            throw new ArgumentException("Image path is required.", nameof(imagePath));

        var directory = Path.GetDirectoryName(Path.GetFullPath(imagePath))
            ?? throw new InvalidOperationException("Cannot resolve directory for image path.");

        return ResolveFromDirectory(directory);
    }

    public string ResolveHexFromImagePath(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            throw new ArgumentException("Image path is required.", nameof(imagePath));

        var directory = Path.GetDirectoryName(Path.GetFullPath(imagePath))
            ?? throw new InvalidOperationException("Cannot resolve directory for image path.");

        return ResolveHexFromDirectory(directory);
    }

    public byte[] ResolveFromDirectory(string startDirectory)
    {
        var hex = ResolveHexFromDirectory(startDirectory);
        return ParseHexKey(hex);
    }

    public string ResolveHexFromDirectory(string startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
            throw new ArgumentException("Directory is required.", nameof(startDirectory));

        var current = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (current is not null)
        {
            foreach (var relative in new[] { Path.Combine("data", "System.json"), Path.Combine("www", "data", "System.json") })
            {
                var candidate = Path.Combine(current.FullName, relative);
                if (!File.Exists(candidate))
                    continue;

                var cacheKey = Path.GetDirectoryName(candidate)!;
                if (_hexCache.TryGetValue(cacheKey, out var cachedHex))
                    return cachedHex;

                var hex = ReadEncryptionKeyHex(candidate);
                _hexCache[cacheKey] = hex;
                _cache[cacheKey] = ParseHexKey(hex);
                return hex;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(
            "Could not find System.json with encryptionKey while walking up from the image directory.");
    }

    public static byte[] ParseHexKey(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex.Length != 32)
            throw new FormatException("encryptionKey must be a 32-character hex string.");

        var bytes = Convert.FromHexString(hex);
        if (bytes.Length != 16)
            throw new FormatException("encryptionKey must decode to 16 bytes.");

        return bytes;
    }

    private static string ReadEncryptionKeyHex(string systemJsonPath)
    {
        using var stream = File.OpenRead(systemJsonPath);
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("encryptionKey", out var keyElement))
            throw new InvalidDataException($"Missing encryptionKey in {systemJsonPath}");

        var hex = keyElement.GetString()
            ?? throw new InvalidDataException($"encryptionKey is null in {systemJsonPath}");

        if (hex.Length != 32)
            throw new FormatException($"encryptionKey in {systemJsonPath} must be a 32-character hex string.");

        return hex;
    }
}
