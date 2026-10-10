using FullscreenStart.Services;
using Microsoft.Win32;

namespace FullscreenStart.Tests;

/// <summary>Runs against a throw-away key under HKCU\Software, never the real Run key.</summary>
public sealed class AutostartTests : IDisposable
{
    private readonly string _path = @"Software\FullscreenStart.Tests\" + Guid.NewGuid();
    private readonly RegistryKey _root;

    public AutostartTests() => _root = Registry.CurrentUser.CreateSubKey(_path);

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\FullscreenStart.Tests", throwOnMissingSubKey: false);
    }

    [Fact]
    public void EnableAndDisable()
    {
        var autostart = new Autostart(_root);
        Assert.False(autostart.IsEnabled);

        autostart.Enable(@"C:\Apps\Fullscreen Start\FullscreenStart.exe");
        Assert.True(autostart.IsEnabled);
        Assert.Equal(@"C:\Apps\Fullscreen Start\FullscreenStart.exe", autostart.TargetPath);

        autostart.Disable();
        Assert.False(autostart.IsEnabled);
        Assert.Null(autostart.TargetPath);
    }

    [Fact]
    public void TaskManagerDisableIsRespectedAndClearedOnEnable()
    {
        var autostart = new Autostart(_root);
        autostart.Enable(@"C:\x.exe");
        using (var approved = _root.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"))
            approved.SetValue(Autostart.ValueName, new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);

        Assert.False(autostart.IsEnabled);

        autostart.Enable(@"C:\x.exe");
        Assert.True(autostart.IsEnabled);
    }

    [Fact]
    public void CommandQuotesThePath() =>
        Assert.Equal("\"C:\\A B\\x.exe\" --autostart", Autostart.CommandFor(@"C:\A B\x.exe"));
}
