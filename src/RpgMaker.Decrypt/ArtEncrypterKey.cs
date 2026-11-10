using System.Security.Cryptography;
using System.Text;

namespace RpgMaker.Decrypt;

/// <summary>
/// Art_Decrypterator3000 / Arthran key transform:
/// MD5(hexKeyString) → 16 bytes, then append reversed copy → 32-byte XOR key.
/// </summary>
public static class ArtEncrypterKey
{
    public static byte[] TransformFromHexKey(string encryptionKeyHex)
    {
        if (string.IsNullOrWhiteSpace(encryptionKeyHex))
            throw new ArgumentException("encryptionKey is required.", nameof(encryptionKeyHex));

        // SparkMD5.hash(string) hashes the UTF-8 characters of the hex string itself.
        var md5Hex = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(encryptionKeyHex)))
            .ToLowerInvariant();

        if (md5Hex.Length != 32)
            throw new InvalidOperationException("MD5 hex length must be 32.");

        var parts = new List<byte>(32);
        for (var i = 0; i < 32; i += 2)
            parts.Add(Convert.ToByte(md5Hex.Substring(i, 2), 16));

        for (var t = parts.Count - 1; t >= 0; t--)
            parts.Add(parts[t]);

        return parts.ToArray();
    }
}
