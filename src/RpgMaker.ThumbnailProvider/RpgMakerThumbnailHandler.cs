using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using RpgMaker.Decrypt;

namespace RpgMaker.ThumbnailProvider;

/// <summary>
/// Explorer thumbnail handler. Uses IInitializeWithItem/File so encryptionKey can be resolved from System.json.
/// Stream-only init cannot locate the game key; those calls fail cleanly.
/// </summary>
[ComVisible(true)]
[Guid(Clsid)]
[ClassInterface(ClassInterfaceType.None)]
[ProgId("RpgMaker.ThumbnailProvider")]
public sealed class RpgMakerThumbnailHandler : IThumbnailProvider, IInitializeWithItem, IInitializeWithFile
{
    public const string Clsid = "A7B3C1D2-4E5F-6789-ABCD-EF0123456789";

    private string? _filePath;
    private readonly Decryptor _decryptor = new();

    void IInitializeWithItem.Initialize(IShellItem item, uint grfMode)
    {
        ArgumentNullException.ThrowIfNull(item);

        item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var namePtr);
        try
        {
            _filePath = Marshal.PtrToStringUni(namePtr)
                ?? throw new InvalidOperationException("Empty shell item path.");
        }
        finally
        {
            NativeMethods.CoTaskMemFree(namePtr);
        }
    }

    void IInitializeWithFile.Initialize(string pszFilePath, uint grfMode)
    {
        if (string.IsNullOrWhiteSpace(pszFilePath))
            throw new ArgumentException("File path is required.", nameof(pszFilePath));

        _filePath = pszFilePath;
    }

    void IThumbnailProvider.GetThumbnail(uint cx, out IntPtr phbmp, out WTS_ALPHATYPE pdwAlpha)
    {
        phbmp = IntPtr.Zero;
        pdwAlpha = WTS_ALPHATYPE.WTSAT_ARGB;

        if (string.IsNullOrWhiteSpace(_filePath) || cx == 0)
            Marshal.ThrowExceptionForHR(HResults.E_FAIL);

        var path = _filePath!;

        try
        {
            var pngBytes = _decryptor.LoadImageBytes(path);
            using var input = new MemoryStream(pngBytes);
            using var original = Image.FromStream(input);
            using var thumb = RenderThumbnail(original, (int)cx);

            phbmp = thumb.GetHbitmap();
        }
        catch
        {
            Marshal.ThrowExceptionForHR(HResults.E_FAIL);
        }
    }

    private static Bitmap RenderThumbnail(Image source, int cx)
    {
        var scale = Math.Min(cx / (double)source.Width, cx / (double)source.Height);
        if (scale > 1)
            scale = 1;

        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(source, 0, 0, width, height);
        return bmp;
    }
}
