using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static FullscreenStart.Interop.NativeMethods;

namespace FullscreenStart.Services;

/// <summary>
/// Produces a blurred image of the desktop wallpaper for one monitor, without the windows on top of it.
/// Live wallpapers (Lively, Wallpaper Engine, …) are captured from their window behind the desktop
/// icons; otherwise the static wallpaper file is used. Returns null if neither works.
/// </summary>
public static class WallpaperBackdrop
{
    /// <summary>Downscale factor before blurring; the image is stretched back up when shown.</summary>
    private const int Scale = 8;

    public static BitmapSource? Capture(PixelRect monitor, int blurRadius)
    {
        var sw = Stopwatch.StartNew();
        int w = Math.Max(1, monitor.Width / Scale), h = Math.Max(1, monitor.Height / Scale);

        var pixels = CaptureLiveWallpaper(monitor, w, h) ?? LoadStaticWallpaper(monitor, w, h);
        if (pixels == null) return null;

        ImageBlur.Blur(pixels, w, h, Math.Max(1, blurRadius / Scale / 2), passes: 3);
        var image = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, pixels, w * 4);
        image.Freeze();
        Debug.WriteLine($"[Backdrop] captured {w}x{h} in {sw.ElapsedMilliseconds} ms");
        return image;
    }

    /// <summary>Finds a non-Explorer window hosted behind the desktop icons (Progman/WorkerW).</summary>
    internal static IntPtr FindLiveWallpaperWindow()
    {
        var candidates = new List<IntPtr>();
        var hosts = new List<IntPtr>();
        IntPtr progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return IntPtr.Zero;
        GetWindowThreadProcessId(progman, out var explorerPid);

        // Windows 11 24H2+ hosts the wallpaper under Progman; older builds under a top-level WorkerW.
        hosts.Add(progman);
        EnumWindows((hwnd, _) =>
        {
            var cls = new StringBuilder(32);
            GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString() == "WorkerW" && GetWindowThreadProcessId(hwnd, out var pid) != 0 && pid == explorerPid)
                hosts.Add(hwnd);
            return true;
        }, IntPtr.Zero);

        foreach (var host in hosts)
        {
            EnumChildWindows(host, (child, _) =>
            {
                if (GetParent(child) == host && IsWindowVisible(child))
                {
                    GetWindowThreadProcessId(child, out var pid);
                    if (pid != explorerPid) candidates.Add(child);
                }
                return true;
            }, IntPtr.Zero);
            if (candidates.Count > 0) break;
        }
        return candidates.FirstOrDefault();
    }

    private static byte[]? CaptureLiveWallpaper(PixelRect monitor, int w, int h)
    {
        IntPtr hwnd = FindLiveWallpaperWindow();
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect) || rect.Width <= 0 || rect.Height <= 0) return null;

        IntPtr bigDc = CreateCompatibleDC(IntPtr.Zero);
        IntPtr smallDc = CreateCompatibleDC(IntPtr.Zero);
        var bigHeader = BITMAPINFOHEADER.TopDown32(rect.Width, rect.Height);
        var smallHeader = BITMAPINFOHEADER.TopDown32(w, h);
        IntPtr bigBmp = CreateDIBSection(bigDc, ref bigHeader, DIB_RGB_COLORS, out _, IntPtr.Zero, 0);
        IntPtr smallBmp = CreateDIBSection(smallDc, ref smallHeader, DIB_RGB_COLORS, out _, IntPtr.Zero, 0);
        IntPtr oldBig = SelectObject(bigDc, bigBmp);
        IntPtr oldSmall = SelectObject(smallDc, smallBmp);
        try
        {
            if (!PrintWindow(hwnd, bigDc, PW_RENDERFULLCONTENT)) return null;

            // The wallpaper window may span several monitors: cut out ours while scaling down.
            SetStretchBltMode(smallDc, HALFTONE);
            SetBrushOrgEx(smallDc, 0, 0, IntPtr.Zero);
            StretchBlt(smallDc, 0, 0, w, h, bigDc, monitor.Left - rect.Left, monitor.Top - rect.Top, monitor.Width, monitor.Height, SRCCOPY);

            SelectObject(smallDc, oldSmall);
            oldSmall = IntPtr.Zero;
            var pixels = new byte[w * h * 4];
            if (GetDIBits(smallDc, smallBmp, 0, (uint)h, pixels, ref smallHeader, DIB_RGB_COLORS) == 0) return null;
            return LooksBlank(pixels) ? null : pixels;
        }
        finally
        {
            SelectObject(bigDc, oldBig);
            if (oldSmall != IntPtr.Zero) SelectObject(smallDc, oldSmall);
            DeleteObject(bigBmp);
            DeleteObject(smallBmp);
            DeleteDC(bigDc);
            DeleteDC(smallDc);
        }
    }

    private static byte[]? LoadStaticWallpaper(PixelRect monitor, int w, int h)
    {
        var sb = new StringBuilder(1024);
        SystemParametersInfo(SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0);
        var path = sb.ToString();

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            // Solid colour desktop.
            uint c = GetSysColor(COLOR_DESKTOP);
            var solid = new byte[w * h * 4];
            for (int i = 0; i < solid.Length; i += 4)
            {
                solid[i] = (byte)(c >> 16); solid[i + 1] = (byte)(c >> 8); solid[i + 2] = (byte)c; solid[i + 3] = 255;
            }
            return solid;
        }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = Math.Max(w * 2, 64);
            bmp.EndInit();

            // "Fill": scale to cover the monitor and crop the centre.
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                double scale = Math.Max((double)w / bmp.PixelWidth, (double)h / bmp.PixelHeight);
                double dw = bmp.PixelWidth * scale, dh = bmp.PixelHeight * scale;
                dc.DrawImage(bmp, new Rect((w - dw) / 2, (h - dh) / 2, dw, dh));
            }
            var target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            var pixels = new byte[w * h * 4];
            target.CopyPixels(pixels, w * 4, 0);
            return pixels;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }

    /// <summary>A capture that failed silently comes back (almost) black.</summary>
    internal static bool LooksBlank(byte[] bgra)
    {
        long sum = 0;
        int samples = 0;
        for (int i = 0; i < bgra.Length; i += 4 * 7)
        {
            sum += bgra[i] + bgra[i + 1] + bgra[i + 2];
            samples++;
        }
        return samples == 0 || sum / (samples * 3.0) < 2.0;
    }
}
