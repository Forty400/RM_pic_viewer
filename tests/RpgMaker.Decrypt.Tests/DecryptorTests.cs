using System.Text;
using System.Text.Json;
using RpgMaker.Decrypt;

namespace RpgMaker.Decrypt.Tests;

public class DecryptorTests
{
    // Minimal 1x1 PNG
    private static readonly byte[] MinimalPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
        0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41,
        0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
        0x00, 0x00, 0x03, 0x00, 0x01, 0x00, 0x05, 0xFE,
        0xD4, 0xEF, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45,
        0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
    ];

    private static readonly byte[] TestKey = KeyResolver.ParseHexKey("0123456789abcdeffedcba9876543210");

    [Fact]
    public void Decrypt_RoundTrip_RestoresPngSignatureAndBytes()
    {
        var encrypted = Decryptor.EncryptForTests(MinimalPng, TestKey);
        var decryptor = new Decryptor();
        var decrypted = decryptor.Decrypt(encrypted, TestKey);

        Assert.Equal(MinimalPng, decrypted);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, decrypted.Take(4).ToArray());
    }

    [Fact]
    public void LoadImageBytes_ResolvesKeyFromSystemJson()
    {
        var root = CreateTempGameTree(out var imagePath);
        try
        {
            var decryptor = new Decryptor();
            var bytes = decryptor.LoadImageBytes(imagePath);
            Assert.Equal(MinimalPng, bytes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadImageBytes_PlainPng_DoesNotRequireKey()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rpgmaker-plain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "plain.png");
        File.WriteAllBytes(path, MinimalPng);

        try
        {
            var bytes = new Decryptor().LoadImageBytes(path);
            Assert.Equal(MinimalPng, bytes);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Decrypt_InvalidHeader_Throws()
    {
        var decryptor = new Decryptor();
        var junk = Encoding.ASCII.GetBytes("NOTRPGMAKER!!!!!........");
        var ex = Assert.Throws<InvalidDataException>(() => decryptor.Decrypt(junk, TestKey));
        Assert.Contains("header", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ListSiblingImages_OrdersIgnoreCase()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rpgmaker-sib-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "b.rpgmvp"), [0]);
        File.WriteAllBytes(Path.Combine(dir, "A.png_"), [0]);
        File.WriteAllBytes(Path.Combine(dir, "c.png"), [0]);
        File.WriteAllText(Path.Combine(dir, "ignore.txt"), "x");

        try
        {
            var list = ImageFormats.ListSiblingImages(Path.Combine(dir, "b.rpgmvp"));
            Assert.Equal(3, list.Count);
            Assert.Equal("A.png_", Path.GetFileName(list[0]), ignoreCase: true);
            Assert.Equal("b.rpgmvp", Path.GetFileName(list[1]), ignoreCase: true);
            Assert.Equal("c.png", Path.GetFileName(list[2]), ignoreCase: true);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string CreateTempGameTree(out string imagePath)
    {
        var root = Path.Combine(Path.GetTempPath(), "rpgmaker-game-" + Guid.NewGuid().ToString("N"));
        var data = Path.Combine(root, "www", "data");
        var img = Path.Combine(root, "www", "img", "pictures");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(img);

        var system = new { encryptionKey = "0123456789abcdeffedcba9876543210" };
        File.WriteAllText(Path.Combine(data, "System.json"), JsonSerializer.Serialize(system));

        imagePath = Path.Combine(img, "Actor1.rpgmvp");
        File.WriteAllBytes(imagePath, Decryptor.EncryptForTests(MinimalPng, TestKey));
        return root;
    }
}
