using System.Runtime.InteropServices;

namespace FullscreenStart.Interop;

internal enum SIGDN : uint
{
    NORMALDISPLAY = 0x00000000,
    PARENTRELATIVEPARSING = 0x80018001,
    DESKTOPABSOLUTEPARSING = 0x80028000,
    FILESYSPATH = 0x80058000,
}

[Flags]
internal enum SIIGBF
{
    RESIZETOFIT = 0x00,
    BIGGERSIZEOK = 0x01,
    MEMORYONLY = 0x02,
    ICONONLY = 0x04,
    THUMBNAILONLY = 0x08,
    INCACHEONLY = 0x10,
    SCALEUP = 0x100,
}

[StructLayout(LayoutKind.Sequential)]
internal struct SIZE
{
    public int cx, cy;
    public SIZE(int x, int y) { cx = x; cy = y; }
}

[ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    [return: MarshalAs(UnmanagedType.Interface)]
    object BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, [MarshalAs(UnmanagedType.LPStruct)] Guid riid);

    IShellItem GetParent();

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetDisplayName(SIGDN sigdnName);

    uint GetAttributes(uint sfgaoMask);

    int Compare(IShellItem psi, uint hint);
}

[ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumShellItems
{
    [PreserveSig]
    int Next(uint celt, [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.Interface, SizeParamIndex = 0)] IShellItem[] rgelt, out uint pceltFetched);

    void Skip(uint celt);

    void Reset();

    IEnumShellItems Clone();
}

[ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemImageFactory
{
    [PreserveSig]
    int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
}

internal static class ShellGuids
{
    public static readonly Guid IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    public static readonly Guid IEnumShellItems = new("70629033-e363-4a28-a567-0db78006e6d7");
    public static readonly Guid IShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
    public static readonly Guid BHID_EnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");
}
