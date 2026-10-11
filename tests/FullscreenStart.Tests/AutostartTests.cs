using System.Xml.Linq;
using FullscreenStart.Services;
using Microsoft.Win32;

namespace FullscreenStart.Tests;

/// <summary>Runs against a fake task store and a throw-away key under HKCU\Software, never the real ones.</summary>
public sealed class AutostartTests : IDisposable
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private readonly string _path = @"Software\FullscreenStart.Tests\" + Guid.NewGuid();
    private readonly RegistryKey _root;
    private readonly FakeTaskStore _tasks = new();

    public AutostartTests() => _root = Registry.CurrentUser.CreateSubKey(_path);

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\FullscreenStart.Tests", throwOnMissingSubKey: false);
    }

    [Fact]
    public void EnableAndDisable()
    {
        var autostart = new Autostart(_tasks, _root);
        Assert.False(autostart.IsEnabled);

        autostart.Enable(@"C:\Apps\Fullscreen Start\FullscreenStart.exe");
        Assert.True(autostart.IsEnabled);
        Assert.Equal(@"C:\Apps\Fullscreen Start\FullscreenStart.exe", autostart.TargetPath);

        autostart.Disable();
        Assert.False(autostart.IsEnabled);
        Assert.Null(autostart.TargetPath);
    }

    [Fact]
    public void OldRunEntryBecomesTask()
    {
        SetRunEntry(approved: null);
        var autostart = new Autostart(_tasks, _root);

        Assert.True(autostart.MigrateRunEntry(@"C:\x.exe"));

        Assert.True(autostart.IsEnabled);
        Assert.Null(_root.OpenSubKey(RunPath)?.GetValue(Autostart.TaskName));
        Assert.Null(_root.OpenSubKey(ApprovedPath)?.GetValue(Autostart.TaskName));
        Assert.False(autostart.MigrateRunEntry(@"C:\x.exe")); // nothing left to convert
    }

    [Fact]
    public void RunEntrySwitchedOffInTaskManagerStaysOff()
    {
        SetRunEntry(approved: new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        var autostart = new Autostart(_tasks, _root);

        Assert.True(autostart.MigrateRunEntry(@"C:\x.exe"));

        Assert.False(autostart.IsEnabled);
        Assert.Null(_root.OpenSubKey(RunPath)?.GetValue(Autostart.TaskName));
    }

    [Fact]
    public void TaskStartsAtSignInWithNormalRightsAndPriority()
    {
        var xml = XDocument.Parse(Autostart.TaskXml(@"C:\A B\x.exe", @"PC\Johan"));
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        string Value(string name) => xml.Descendants(ns + name).Single().Value;

        Assert.Equal(@"PC\Johan", xml.Descendants(ns + "LogonTrigger").Single().Element(ns + "UserId")!.Value);
        Assert.Equal("LeastPrivilege", Value("RunLevel"));
        Assert.Equal("InteractiveToken", Value("LogonType"));
        Assert.Equal("4", Value("Priority"));
        Assert.Equal("PT0S", Value("ExecutionTimeLimit"));
        Assert.Equal("false", Value("DisallowStartIfOnBatteries"));
        Assert.Equal(@"C:\A B\x.exe", Value("Command"));
        Assert.Equal("--autostart", Value("Arguments"));
        Assert.Equal(@"C:\A B", Value("WorkingDirectory"));
    }

    private void SetRunEntry(byte[]? approved)
    {
        using (var run = _root.CreateSubKey(RunPath))
            run.SetValue(Autostart.TaskName, "\"C:\\x.exe\" --autostart");
        if (approved == null) return;
        using var key = _root.CreateSubKey(ApprovedPath);
        key.SetValue(Autostart.TaskName, approved, RegistryValueKind.Binary);
    }

    private sealed class FakeTaskStore : ILogonTaskStore
    {
        private readonly Dictionary<string, string> _tasks = new();

        public bool IsEnabled(string name) => _tasks.ContainsKey(name);

        public string? CommandOf(string name) =>
            _tasks.TryGetValue(name, out var xml)
                ? XDocument.Parse(xml).Descendants().First(e => e.Name.LocalName == "Command").Value
                : null;

        public void Register(string name, string xml) => _tasks[name] = xml;

        public void Delete(string name) => _tasks.Remove(name);
    }
}
