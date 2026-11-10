using System.Runtime.InteropServices;

namespace RpgMaker.ThumbnailProvider;

internal enum WTS_ALPHATYPE
{
    WTSAT_UNKNOWN = 0,
    WTSAT_RGB = 1,
    WTSAT_ARGB = 2
}

internal enum SIGDN : uint
{
    SIGDN_FILESYSPATH = 0x80058000
}

[ComImport]
[Guid("e357fccd-a995-4576-b01f-234630154e96")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IThumbnailProvider
{
    void GetThumbnail(uint cx, out IntPtr phbmp, out WTS_ALPHATYPE pdwAlpha);
}

[ComImport]
[Guid("7F73BE3F-FB79-493C-A09C-C95F7B4D00CC")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInitializeWithItem
{
    void Initialize(IShellItem psi, uint grfMode);
}

[ComImport]
[Guid("b7d14566-0509-4cce-a71f-0a554233bd9b")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInitializeWithFile
{
    void Initialize([MarshalAs(UnmanagedType.LPWStr)] string pszFilePath, uint grfMode);
}

[ComImport]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    void BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
    void GetParent(out IShellItem ppsi);
    void GetDisplayName(SIGDN sigdnName, out IntPtr ppszName);
    void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
    void Compare(IShellItem psi, uint hint, out int piOrder);
}

internal static class HResults
{
    public const int S_OK = 0;
    public const int E_FAIL = unchecked((int)0x80004005);
}

internal static class NativeMethods
{
    [DllImport("ole32.dll")]
    public static extern void CoTaskMemFree(IntPtr pv);
}
