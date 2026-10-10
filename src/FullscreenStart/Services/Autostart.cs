using Microsoft.Win32;

namespace FullscreenStart.Services;

/// <summary>
/// "Mit Windows starten" via HKCU\…\Run, with normal user rights (an elevated menu would start every
/// app elevated). Respects the switch in Task Manager › Autostart, which Windows keeps under
/// Explorer\StartupApproved\Run.
/// </summary>
public sealed class Autostart(RegistryKey root)
{
    public const string ValueName = "FullscreenStart";
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public static Autostart ForCurrentUser() => new(Registry.CurrentUser);

    public static string CommandFor(string exePath) => $"\"{exePath}\" --autostart";

    public bool IsEnabled
    {
        get
        {
            using var run = root.OpenSubKey(RunPath);
            if (run?.GetValue(ValueName) is not string) return false;
            using var approved = root.OpenSubKey(ApprovedPath);
            // Task Manager writes 12 bytes; an odd first byte means "disabled".
            return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } flags || (flags[0] & 1) == 0;
        }
    }

    /// <summary>The executable the autostart entry points to, if any.</summary>
    public string? TargetPath
    {
        get
        {
            using var run = root.OpenSubKey(RunPath);
            if (run?.GetValue(ValueName) is not string command) return null;
            var trimmed = command.Trim();
            if (!trimmed.StartsWith('"')) return trimmed.Split(' ')[0];
            int end = trimmed.IndexOf('"', 1);
            return end > 1 ? trimmed[1..end] : null;
        }
    }

    public void Enable(string exePath)
    {
        using (var run = root.CreateSubKey(RunPath))
            run.SetValue(ValueName, CommandFor(exePath), RegistryValueKind.String);
        // Clear a "disabled" mark left by Task Manager so the switch really turns it on.
        using var approved = root.OpenSubKey(ApprovedPath, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public void Disable()
    {
        using var run = root.OpenSubKey(RunPath, writable: true);
        run?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
