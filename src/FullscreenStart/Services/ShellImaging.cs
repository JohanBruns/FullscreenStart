using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FullscreenStart.Interop;
using static FullscreenStart.Interop.NativeMethods;

namespace FullscreenStart.Services;

/// <summary>Turns GDI bitmaps from the shell into frozen WPF images.</summary>
internal static class ShellImaging
{
    /// <summary>Loads the icon of a shell item given by its absolute PIDL. Must run on an STA thread.</summary>
    public static BitmapSource? LoadIcon(byte[] pidl, int size)
    {
        IntPtr native = Marshal.AllocCoTaskMem(pidl.Length);
        try
        {
            Marshal.Copy(pidl, 0, native, pidl.Length);
            SHCreateItemFromIDList(native, ShellGuids.IShellItemImageFactory, out var obj);
            var factory = (IShellItemImageFactory)obj;
            try
            {
                if (factory.GetImage(new SIZE(size, size), SIIGBF.ICONONLY, out var hbm) != 0 || hbm == IntPtr.Zero)
                    return null;
                try { return FromHBitmap(hbm); }
                finally { DeleteObject(hbm); }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(native);
        }
    }

    /// <summary>Copies a 32-bit HBITMAP into a frozen premultiplied-BGRA BitmapSource.</summary>
    public static BitmapSource? FromHBitmap(IntPtr hbm)
    {
        if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), out var bm) == 0) return null;
        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
        if (w <= 0 || h <= 0) return null;

        var pixels = new byte[w * h * 4];
        var header = BITMAPINFOHEADER.TopDown32(w, h);
        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(dc, hbm, 0, (uint)h, pixels, ref header, DIB_RGB_COLORS) == 0) return null;
        }
        finally
        {
            DeleteDC(dc);
        }

        // Bitmaps without an alpha channel come back fully transparent; make them opaque.
        bool hasAlpha = false;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0) { hasAlpha = true; break; }
        }
        if (!hasAlpha)
        {
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        }

        var source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, w * 4);
        source.Freeze();
        return source;
    }
}
