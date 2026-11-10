using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace RpgMaker.Setup;

/// <summary>
/// Finds install payload beside the Setup EXE, as payload.zip, or as an embedded resource.
/// Single-file publish uses a temp BaseDirectory — always prefer ProcessPath.
/// </summary>
internal static class PayloadLocator
{
    private const string EmbeddedName = "payload.zip";

    public static string Resolve()
    {
        foreach (var dir in GetSearchDirectories())
        {
            var folder = Path.Combine(dir, "payload");
            if (LooksLikePayload(folder))
                return folder;

            var zip = Path.Combine(dir, "payload.zip");
            if (File.Exists(zip))
                return ExtractZipFile(zip);
        }

        var fromEmbed = TryExtractEmbedded();
        if (fromEmbed is not null)
            return fromEmbed;

        throw new DirectoryNotFoundException(
            "未找到安装数据包。请使用官方发布的 Setup.exe（已内嵌安装包），" +
            "或将 Setup.exe 与 payload\\ / payload.zip 放在同一目录。");
    }

    private static List<string> GetSearchDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();

        void Collect(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            try
            {
                var full = Path.GetFullPath(path);
                if (seen.Add(full))
                    list.Add(full);
            }
            catch
            {
                // ignore invalid paths
            }
        }

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
            Collect(Path.GetDirectoryName(processPath));

        Collect(AppContext.BaseDirectory);
        return list;
    }

    private static bool LooksLikePayload(string folder)
    {
        if (!Directory.Exists(folder))
            return false;

        return File.Exists(Path.Combine(folder, InstallConstants.AppExeName))
               || File.Exists(Path.Combine(folder, "bin", InstallConstants.AppExeName))
               || File.Exists(Path.Combine(folder, "app", InstallConstants.AppExeName));
    }

    private static string? TryExtractEmbedded()
    {
        var asm = Assembly.GetExecutingAssembly();
        var names = asm.GetManifestResourceNames();
        var resourceName = names.FirstOrDefault(n =>
            n.Equals(EmbeddedName, StringComparison.OrdinalIgnoreCase) ||
            n.EndsWith("." + EmbeddedName, StringComparison.OrdinalIgnoreCase) ||
            n.EndsWith(EmbeddedName, StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
            return null;

        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null)
            return null;

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();
        var key = "embedded-" + Convert.ToHexString(SHA256.HashData(bytes))[..16];
        return ExtractZipBytes(bytes, key);
    }

    private static string ExtractZipFile(string zipPath)
    {
        var info = new FileInfo(zipPath);
        var key = $"zip-{info.Length}-{info.LastWriteTimeUtc.Ticks}";
        var bytes = File.ReadAllBytes(zipPath);
        return ExtractZipBytes(bytes, key);
    }

    private static string ExtractZipBytes(byte[] zipBytes, string cacheKey)
    {
        var root = Path.Combine(Path.GetTempPath(), "RpgMakerImageViewer-Setup", Sanitize(cacheKey));
        var marker = Path.Combine(root, ".payload-ready");
        var payloadDir = Path.Combine(root, "payload");

        if (File.Exists(marker) && LooksLikePayload(payloadDir))
            return payloadDir;

        if (Directory.Exists(root))
        {
            try { Directory.Delete(root, recursive: true); }
            catch { /* overwrite below */ }
        }

        Directory.CreateDirectory(payloadDir);

        using (var ms = new MemoryStream(zipBytes))
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Read))
        {
            archive.ExtractToDirectory(payloadDir, overwriteFiles: true);
        }

        // Some zips wrap a top-level folder — unwrap if needed.
        if (!LooksLikePayload(payloadDir))
        {
            var nested = Directory.GetDirectories(payloadDir);
            if (nested.Length == 1 && LooksLikePayload(nested[0]))
                payloadDir = nested[0];
            else if (LooksLikePayload(Path.Combine(payloadDir, "payload")))
                payloadDir = Path.Combine(payloadDir, "payload");
        }

        if (!LooksLikePayload(payloadDir))
            throw new InvalidDataException("payload.zip 内容无效（缺少 RpgMakerImageViewer.exe）。");

        File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
        return payloadDir;
    }

    private static string Sanitize(string key)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            key = key.Replace(c, '_');
        return key.Length > 64 ? key[..64] : key;
    }
}
