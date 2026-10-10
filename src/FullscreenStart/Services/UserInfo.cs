using System.IO;
using System.Security.Principal;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace FullscreenStart.Services;

public static class UserInfo
{
    public static string Name => Environment.UserName;

    /// <summary>The account picture Windows shows on the sign-in screen, if one is set.</summary>
    public static ImageSource? Picture()
    {
        try
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value;
            if (sid == null) return null;
            using var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\AccountPicture\Users\{sid}");
            var path = (key?.GetValue("Image96") ?? key?.GetValue("Image64") ?? key?.GetValue("Image192")) as string;
            if (path == null || !File.Exists(path)) return null;

            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 64;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
    }
}
