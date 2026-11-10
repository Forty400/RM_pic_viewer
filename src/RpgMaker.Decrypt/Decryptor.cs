namespace RpgMaker.Decrypt;

public sealed class Decryptor
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47];

    /// <summary>
    /// First 16 bytes of a standard PNG (signature + IHDR length/type prefix),
    /// used to derive the XOR key when System.json is unavailable (RPGMV / legacy Art).
    /// </summary>
    private static readonly byte[] PngHeaderPrefix =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52
    ];

    private readonly KeyResolver _keyResolver;

    public Decryptor(KeyResolver? keyResolver = null)
    {
        _keyResolver = keyResolver ?? new KeyResolver();
    }

    public byte[] LoadImageBytes(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Image file not found.", fullPath);

        var ext = Path.GetExtension(fullPath);
        var bytes = File.ReadAllBytes(fullPath);

        if (IsPlainPngExtension(ext))
        {
            EnsurePngSignature(bytes);
            return bytes;
        }

        if (!ImageFormats.IsEncryptedImageExtension(ext))
            throw new NotSupportedException($"Unsupported image extension: {ext}");

        return DecryptWithKeyFallback(bytes, fullPath);
    }

    public byte[] DecryptWithKeyFallback(ReadOnlySpan<byte> encrypted, string? imagePathForKeyLookup = null)
    {
        var kind = EncryptionHeader.Detect(encrypted);
        if (kind == EncryptionHeaderKind.Unknown)
            throw new InvalidDataException(
                "Not a valid encrypted image header (expected RPGMV... or ART ENCRYPTER...).");

        // Art_Decrypterator3000: 32-byte FREE VERSION header + MD5-transformed 32-byte key.
        // PNG-header derive cannot recover the full key (XOR covers 32 bytes including IHDR data).
        if (kind == EncryptionHeaderKind.ArtEncrypterFree)
            return DecryptArtEncrypterFree(encrypted, imagePathForKeyLookup);

        var candidates = new List<byte[]>();
        Exception? lastError = null;

        try
        {
            candidates.Add(DeriveKeyFromPngPrefix(encrypted));
        }
        catch (Exception ex)
        {
            lastError = ex;
        }

        if (!string.IsNullOrWhiteSpace(imagePathForKeyLookup))
        {
            try
            {
                var fromJson = _keyResolver.ResolveFromImagePath(imagePathForKeyLookup);
                if (candidates.All(k => !k.AsSpan().SequenceEqual(fromJson)))
                    candidates.Add(fromJson);
            }
            catch (Exception ex)
            {
                lastError ??= ex;
            }
        }

        foreach (var key in candidates)
        {
            try
            {
                return Decrypt(encrypted, key);
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new InvalidDataException(
            "解密失败：PNG 头还原与 System.json 密钥均无效。" +
            (lastError is null ? "" : $" 详情：{lastError.Message}"),
            lastError);
    }

    private byte[] DecryptArtEncrypterFree(ReadOnlySpan<byte> encrypted, string? imagePathForKeyLookup)
    {
        if (string.IsNullOrWhiteSpace(imagePathForKeyLookup))
            throw new InvalidDataException(
                "Art Encrypter FREE VERSION 需要同游戏的 System.json（encryptionKey），无法仅凭 PNG 头还原。");

        try
        {
            var hex = _keyResolver.ResolveHexFromImagePath(imagePathForKeyLookup);
            var key = ArtEncrypterKey.TransformFromHexKey(hex);
            return Decrypt(encrypted, key);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                "解密失败：Art Encrypter FREE VERSION 需要有效的 System.json encryptionKey。" +
                $" 详情：{ex.Message}",
                ex);
        }
    }

    public byte[] Decrypt(ReadOnlySpan<byte> encrypted, ReadOnlySpan<byte> key)
    {
        var kind = EncryptionHeader.Detect(encrypted);
        if (kind == EncryptionHeaderKind.Unknown)
            throw new InvalidDataException(
                "Not a valid encrypted image header (expected RPGMV... or ART ENCRYPTER...).");

        var headerSize = EncryptionHeader.GetHeaderSize(kind);
        var xorLen = EncryptionHeader.GetXorLength(kind);

        if (key.Length != xorLen)
            throw new ArgumentException($"Key must be {xorLen} bytes for {kind}.", nameof(key));

        var payloadLength = encrypted.Length - headerSize;
        if (payloadLength < xorLen)
            throw new InvalidDataException("Encrypted payload is too short.");

        var output = new byte[payloadLength];
        encrypted.Slice(headerSize).CopyTo(output);

        for (var i = 0; i < xorLen; i++)
            output[i] ^= key[i];

        EnsurePngHeader(output);
        return output;
    }

    /// <summary>
    /// Petschko RPG-Maker-MV-Decrypter style "restore image without key":
    /// derive the 16-byte XOR key from the known PNG header prefix.
    /// Only valid for RPGMV / legacy 16-byte Art Encrypter (not FREE VERSION).
    /// </summary>
    public static byte[] DeriveKeyFromPngPrefix(ReadOnlySpan<byte> encrypted)
    {
        var kind = EncryptionHeader.Detect(encrypted);
        if (kind is EncryptionHeaderKind.Unknown or EncryptionHeaderKind.ArtEncrypterFree)
            throw new InvalidDataException("Cannot derive key: unsupported or Art Encrypter FREE VERSION header.");

        var headerSize = EncryptionHeader.GetHeaderSize(kind);
        if (encrypted.Length < headerSize + 16)
            throw new InvalidDataException("Cannot derive key: file too short.");

        var key = new byte[16];
        var payload = encrypted.Slice(headerSize);
        for (var i = 0; i < 16; i++)
            key[i] = (byte)(payload[i] ^ PngHeaderPrefix[i]);

        // Verify: restored prefix must match known PNG header (tautological for derive,
        // but catches truncated / non-PNG payloads after a second check below).
        Span<byte> check = stackalloc byte[16];
        for (var i = 0; i < 16; i++)
            check[i] = (byte)(payload[i] ^ key[i]);

        if (!check.StartsWith(PngSignature) || !check.Slice(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("Derived key does not produce a valid PNG header.");

        // Sanity: next bytes should look like IHDR data (reasonable width/height).
        if (payload.Length >= 24)
        {
            var width = (payload[16] << 24) | (payload[17] << 16) | (payload[18] << 8) | payload[19];
            var height = (payload[20] << 24) | (payload[21] << 16) | (payload[22] << 8) | payload[23];
            // After derive, bytes 16+ are NOT xor'd — they must already be plaintext IHDR fields.
            if (width <= 0 || height <= 0 || width > 100_000 || height > 100_000)
                throw new InvalidDataException("Derived key leaves invalid PNG IHDR dimensions (wrong format).");
        }

        return key;
    }

    public static byte[] EncryptForTests(ReadOnlySpan<byte> pngBytes, ReadOnlySpan<byte> key)
    {
        if (key.Length != 16)
            throw new ArgumentException("Key must be 16 bytes.", nameof(key));
        if (pngBytes.Length < 16)
            throw new ArgumentException("PNG too short.", nameof(pngBytes));

        var output = new byte[EncryptionHeader.RpgMvSize + pngBytes.Length];
        ReadOnlySpan<byte> header =
        [
            (byte)'R', (byte)'P', (byte)'G', (byte)'M', (byte)'V',
            0x00, 0x00, 0x00, 0x00, 0x03, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00
        ];
        header.CopyTo(output);
        pngBytes.CopyTo(output.AsSpan(EncryptionHeader.RpgMvSize));
        for (var i = 0; i < 16; i++)
            output[EncryptionHeader.RpgMvSize + i] ^= key[i];

        return output;
    }

    private static bool IsPlainPngExtension(string ext) =>
        ext.Equals(".png", StringComparison.OrdinalIgnoreCase);

    private static void EnsurePngSignature(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || !data.StartsWith(PngSignature))
            throw new InvalidDataException("Decrypted data is not a PNG (missing 89 50 4E 47 signature).");
    }

    private static void EnsurePngHeader(ReadOnlySpan<byte> data)
    {
        EnsurePngSignature(data);
        if (data.Length < 24 || !data.Slice(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("Decrypted data is not a PNG (missing IHDR).");

        var width = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
        var height = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
        if (width <= 0 || height <= 0 || width > 100_000 || height > 100_000)
            throw new InvalidDataException($"Decrypted PNG has invalid dimensions ({width}x{height}).");
    }
}
