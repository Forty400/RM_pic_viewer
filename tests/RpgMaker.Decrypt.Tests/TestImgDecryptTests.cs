using RpgMaker.Decrypt;

namespace RpgMaker.Decrypt.Tests;

public class TestImgDecryptTests
{
    private static string TestImgDir
    {
        get
        {
            // tests/RpgMaker.Decrypt.Tests/bin/Release/net8.0 → ../../../../testImg
            var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "testImg"));
            if (!Directory.Exists(dir))
                dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tests", "testImg"));
            return dir;
        }
    }

    public static IEnumerable<object[]> TestFiles()
    {
        var dir = TestImgDir;
        if (!Directory.Exists(dir))
            yield break;

        foreach (var file in Directory.EnumerateFiles(dir)
                     .Where(f => ImageFormats.IsEncryptedImageExtension(Path.GetExtension(f)))
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            yield return [file];
        }
    }

    [Theory]
    [MemberData(nameof(TestFiles))]
    public void TestImg_LoadImageBytes_ProducesValidPng(string path)
    {
        Assert.True(File.Exists(path), $"Missing fixture: {path} (resolved TestImgDir={TestImgDir})");

        var decryptor = new Decryptor();
        var png = decryptor.LoadImageBytes(path);

        Assert.True(png.Length > 32);
        Assert.Equal(0x89, png[0]);
        Assert.Equal(0x50, png[1]);
        Assert.Equal(0x4E, png[2]);
        Assert.Equal(0x47, png[3]);
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(png, 12, 4));

        var width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        var height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        Assert.InRange(width, 1, 100_000);
        Assert.InRange(height, 1, 100_000);
    }

    [Fact]
    public void TestImg_Directory_HasExpectedSamples()
    {
        Assert.True(Directory.Exists(TestImgDir), $"Expected fixtures at {TestImgDir}");
        var files = Directory.EnumerateFiles(TestImgDir)
            .Where(f => ImageFormats.IsEncryptedImageExtension(Path.GetExtension(f)))
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("birth1.rpgmvp", files);
        Assert.Contains("D_Goblin_11-2.png_", files);
        Assert.Contains("EV_dance08.rpgmvp", files);
    }

    [Fact]
    public void TestImg_Birth1_IsArtEncrypterFree()
    {
        var path = Path.Combine(TestImgDir, "birth1.rpgmvp");
        Assert.True(File.Exists(path));
        var data = File.ReadAllBytes(path);
        Assert.Equal(EncryptionHeaderKind.ArtEncrypterFree, EncryptionHeader.Detect(data));
    }

    [Fact]
    public void TestImg_Goblin_IsRpgMv()
    {
        var path = Path.Combine(TestImgDir, "D_Goblin_11-2.png_");
        Assert.True(File.Exists(path));
        var data = File.ReadAllBytes(path);
        Assert.Equal(EncryptionHeaderKind.RpgMv, EncryptionHeader.Detect(data));
    }

    [Fact]
    public void TestImg_Birth1_RequiresMatchingSystemJson()
    {
        var source = Path.Combine(TestImgDir, "birth1.rpgmvp");
        Assert.True(File.Exists(source));

        var root = Path.Combine(Path.GetTempPath(), "rpgmaker-wrongkey-" + Guid.NewGuid().ToString("N"));
        var dataDir = Path.Combine(root, "www", "data");
        var imgDir = Path.Combine(root, "www", "img");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(imgDir);
        File.WriteAllText(Path.Combine(dataDir, "System.json"),
            """{"encryptionKey":"0123456789abcdeffedcba9876543210"}""");
        var path = Path.Combine(imgDir, "birth1.rpgmvp");
        File.Copy(source, path);

        try
        {
            var ex = Assert.Throws<InvalidDataException>(() => new Decryptor().LoadImageBytes(path));
            Assert.Contains("Art Encrypter", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TestImg_Birth1_DecryptsWithGameSystemJson()
    {
        var path = Path.Combine(TestImgDir, "birth1.rpgmvp");
        var png = new Decryptor().LoadImageBytes(path);
        var width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        var height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        Assert.Equal(1280, width);
        Assert.Equal(720, height);
    }

    [Fact]
    public void ArtEncrypterKey_Transform_Is32Bytes()
    {
        var key = ArtEncrypterKey.TransformFromHexKey("51f4c046df81ee615cb0df8136e18577");
        Assert.Equal(32, key.Length);
        // Second half is reverse of first half
        for (var i = 0; i < 16; i++)
            Assert.Equal(key[i], key[31 - i]);
    }
}
