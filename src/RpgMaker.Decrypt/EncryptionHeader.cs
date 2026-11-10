namespace RpgMaker.Decrypt;

public enum EncryptionHeaderKind
{
    Unknown = 0,
    RpgMv = 1,
    /// <summary>Legacy 16-byte ART\0ENCRYPTER… wrapper (XOR 16 like RPGMV).</summary>
    ArtEncrypter = 2,
    /// <summary>Art_Decrypterator3000 / FREE VERSION — 32-byte header, MD5 key, XOR 32.</summary>
    ArtEncrypterFree = 3
}

public static class EncryptionHeader
{
    public const int RpgMvSize = 16;
    public const int ArtFreeSize = 32;

    public static ReadOnlySpan<byte> RpgMvMagic => "RPGMV"u8;
    public static ReadOnlySpan<byte> ArtPrefix => "ART"u8;
    public static ReadOnlySpan<byte> ArtMarker => "ENCRYPTER"u8;

    /// <summary>ART\0ENCRYPTER100FREE\0VERSION\0\0\0\0</summary>
    public static ReadOnlySpan<byte> ArtFreeHeader =>
    [
        0x41, 0x52, 0x54, 0x00, 0x45, 0x4E, 0x43, 0x52,
        0x59, 0x50, 0x54, 0x45, 0x52, 0x31, 0x30, 0x30,
        0x46, 0x52, 0x45, 0x45, 0x00, 0x56, 0x45, 0x52,
        0x53, 0x49, 0x4F, 0x4E, 0x00, 0x00, 0x00, 0x00
    ];

    public static EncryptionHeaderKind Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length < RpgMvSize)
            return EncryptionHeaderKind.Unknown;

        if (data.StartsWith(RpgMvMagic))
            return EncryptionHeaderKind.RpgMv;

        if (data.Length >= ArtFreeSize && data.StartsWith(ArtFreeHeader))
            return EncryptionHeaderKind.ArtEncrypterFree;

        // ART\0ENCRYPTER… without FREE VERSION trailer
        if (data.StartsWith(ArtPrefix) && data.Length >= 13 && data.Slice(4, 9).SequenceEqual(ArtMarker))
            return EncryptionHeaderKind.ArtEncrypter;

        return EncryptionHeaderKind.Unknown;
    }

    public static bool IsValid(ReadOnlySpan<byte> data) =>
        Detect(data) != EncryptionHeaderKind.Unknown;

    public static int GetHeaderSize(EncryptionHeaderKind kind) => kind switch
    {
        EncryptionHeaderKind.RpgMv => RpgMvSize,
        EncryptionHeaderKind.ArtEncrypter => RpgMvSize,
        EncryptionHeaderKind.ArtEncrypterFree => ArtFreeSize,
        _ => 0
    };

    /// <summary>Bytes XOR'd after stripping the fake header.</summary>
    public static int GetXorLength(EncryptionHeaderKind kind) => kind switch
    {
        EncryptionHeaderKind.RpgMv => 16,
        EncryptionHeaderKind.ArtEncrypter => 16,
        EncryptionHeaderKind.ArtEncrypterFree => 32,
        _ => 0
    };
}
