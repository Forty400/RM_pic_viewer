namespace RpgMaker.Decrypt;

public static class ImageFormats
{
    public static readonly string[] ViewerExtensions = [".rpgmvp", ".png_", ".png"];
    public static readonly string[] EncryptedExtensions = [".rpgmvp", ".png_"];

    public static bool IsEncryptedImageExtension(string extension) =>
        EncryptedExtensions.Any(e => e.Equals(extension, StringComparison.OrdinalIgnoreCase));

    public static bool IsViewerImageExtension(string extension) =>
        ViewerExtensions.Any(e => e.Equals(extension, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> ListSiblingImages(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Cannot resolve directory.");

        return ListImagesInDirectory(directory);
    }

    public static IReadOnlyList<string> ListImagesInDirectory(string directory)
    {
        var fullDir = Path.GetFullPath(directory);
        return Directory.EnumerateFiles(fullDir)
            .Where(p => IsViewerImageExtension(Path.GetExtension(p)))
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
