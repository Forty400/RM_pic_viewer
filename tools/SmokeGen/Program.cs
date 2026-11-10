using RpgMaker.Decrypt;

var dir = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tests", "testImg"));

if (!Directory.Exists(dir))
{
    Console.Error.WriteLine("testImg not found: " + dir);
    return 1;
}

var decryptor = new Decryptor();
var outDir = Path.Combine(dir, "_decrypted");
Directory.CreateDirectory(outDir);

foreach (var path in Directory.EnumerateFiles(dir)
             .Where(f => ImageFormats.IsEncryptedImageExtension(Path.GetExtension(f)))
             .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
{
    var name = Path.GetFileName(path);
    try
    {
        var kind = EncryptionHeader.Detect(File.ReadAllBytes(path));
        var png = decryptor.LoadImageBytes(path);
        var dest = Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".png");
        File.WriteAllBytes(dest, png);
        Console.WriteLine($"OK  {name}  kind={kind}  -> {dest} ({png.Length} bytes)");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL {name}  {ex.Message}");
        return 2;
    }
}

Console.WriteLine("DECRYPT_SMOKE=OK");
return 0;
