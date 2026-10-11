using Microsoft.Win32;

namespace FullscreenStart.Services;

/// <summary>
/// "Mit Windows starten" via HKCU\…\Run, with normal user rights (an elevated menu would start every
/// app elevated). Respects the switch in Task Manager › Autostart, which Windows keeps under
/// Explorer\StartupApproved\Run. Current Windows 11 builds skip Run entries that have no value there at
/// all, so enabling writes an explicit "enabled" mark.
/// </summary>
public sealed class Autostart(RegistryKey root)
{
    public const string ValueName = "FullscreenStart";
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>What Task Manager writes when an entry is switched on: an even first byte, no timestamp.</summary>
    private static readonly byte[] ApprovedOn = { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

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
        // Replaces a "disabled" mark left by Task Manager, so the switch really turns it on.
        using var approved = root.CreateSubKey(ApprovedPath);
        approved.SetValue(ValueName, ApprovedOn, RegistryValueKind.Binary);
    }

    /// <summary>
    /// Adds the "enabled" mark to an entry that has none (written by older versions, which Windows then
    /// skipped at sign-in). An entry switched off in Task Manager stays off. Returns true if it was repaired.
    /// </summary>
    public bool EnsureApproved()
    {
        using (var run = root.OpenSubKey(RunPath))
            if (run?.GetValue(ValueName) is not string) return false;
        using var approved = root.CreateSubKey(ApprovedPath);
        if (approved.GetValue(ValueName) != null) return false;
        approved.SetValue(ValueName, ApprovedOn, RegistryValueKind.Binary);
        return true;
    }

    public void Disable()
    {
        using var run = root.OpenSubKey(RunPath, writable: true);
        run?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
