using System.IO;
using System.Xml.Linq;
using Microsoft.Win32;

namespace FullscreenStart.Services;

/// <summary>
/// "Mit Windows starten" via a Task Scheduler task that runs at the user's sign-in, with normal user
/// rights (an elevated menu would start every app elevated).
/// Earlier versions used HKCU\…\Run. Written from inside a packaged (MSIX) app, such as a terminal in a
/// packaged IDE or agent host, that write lands in the package's private registry and Windows never
/// sees it. A task is registered through the Task Scheduler service and is always real.
/// An existing Run entry is converted to the task.
/// </summary>
public sealed class Autostart(ILogonTaskStore tasks, RegistryKey root)
{
    public const string TaskName = "FullscreenStart";
    public const string Arguments = "--autostart";
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public static Autostart ForCurrentUser() => new(new TaskSchedulerStore(), Registry.CurrentUser);

    public bool IsEnabled => tasks.IsEnabled(TaskName);

    /// <summary>The executable the autostart task starts, if there is one.</summary>
    public string? TargetPath => tasks.CommandOf(TaskName);

    public void Enable(string exePath)
    {
        tasks.Register(TaskName, TaskXml(exePath, $@"{Environment.UserDomainName}\{Environment.UserName}"));
        RemoveRunEntry();
    }

    public void Disable()
    {
        tasks.Delete(TaskName);
        RemoveRunEntry();
    }

    /// <summary>
    /// Turns an autostart entry of an earlier version (Run key) into the task. An entry switched off in
    /// Task Manager is just removed. Returns true if something was converted.
    /// </summary>
    public bool MigrateRunEntry(string exePath)
    {
        using (var run = root.OpenSubKey(RunPath))
            if (run?.GetValue(TaskName) is not string) return false;

        using (var approved = root.OpenSubKey(ApprovedPath))
        {
            // Task Manager marks a switched-off entry with an odd first byte.
            bool switchedOff = approved?.GetValue(TaskName) is byte[] { Length: > 0 } flags && (flags[0] & 1) == 1;
            if (switchedOff) RemoveRunEntry();
            else Enable(exePath);
        }
        return true;
    }

    private void RemoveRunEntry()
    {
        using (var run = root.OpenSubKey(RunPath, writable: true))
            run?.DeleteValue(TaskName, throwOnMissingValue: false);
        using var approved = root.OpenSubKey(ApprovedPath, writable: true);
        approved?.DeleteValue(TaskName, throwOnMissingValue: false);
    }

    /// <summary>Task definition: start at this user's sign-in, normal rights and priority, no time limit.</summary>
    public static string TaskXml(string exePath, string user)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var task = new XElement(ns + "Task", new XAttribute("version", "1.2"),
            new XElement(ns + "RegistrationInfo",
                new XElement(ns + "Description", "Startet Fullscreen Start bei der Anmeldung.")),
            new XElement(ns + "Triggers",
                new XElement(ns + "LogonTrigger",
                    new XElement(ns + "Enabled", "true"),
                    new XElement(ns + "UserId", user),
                    // Give Explorer a moment to create the taskbar and the notification area.
                    new XElement(ns + "Delay", "PT3S"))),
            new XElement(ns + "Principals",
                new XElement(ns + "Principal", new XAttribute("id", "Author"),
                    new XElement(ns + "UserId", user),
                    new XElement(ns + "LogonType", "InteractiveToken"),
                    new XElement(ns + "RunLevel", "LeastPrivilege"))),
            new XElement(ns + "Settings",
                new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                new XElement(ns + "StopIfGoingOnBatteries", "false"),
                new XElement(ns + "AllowHardTerminate", "true"),
                new XElement(ns + "StartWhenAvailable", "false"),
                new XElement(ns + "RunOnlyIfNetworkAvailable", "false"),
                new XElement(ns + "IdleSettings",
                    new XElement(ns + "StopOnIdleEnd", "false"),
                    new XElement(ns + "RestartOnIdle", "false")),
                new XElement(ns + "AllowStartOnDemand", "true"),
                new XElement(ns + "Enabled", "true"),
                new XElement(ns + "Hidden", "false"),
                new XElement(ns + "RunOnlyIfIdle", "false"),
                new XElement(ns + "WakeToRun", "false"),
                // Runs as long as the session; the default limit is 3 days.
                new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                // The default (7) would start the menu below normal priority.
                new XElement(ns + "Priority", "4")),
            new XElement(ns + "Actions", new XAttribute("Context", "Author"),
                new XElement(ns + "Exec",
                    new XElement(ns + "Command", exePath),
                    new XElement(ns + "Arguments", Arguments),
                    new XElement(ns + "WorkingDirectory", Path.GetDirectoryName(exePath) ?? ""))));
        return task.ToString();
    }
}

/// <summary>Access to scheduled tasks in the root folder of the Task Scheduler.</summary>
public interface ILogonTaskStore
{
    bool IsEnabled(string name);
    string? CommandOf(string name);
    void Register(string name, string xml);
    void Delete(string name);
}

/// <summary>Task Scheduler via its COM API; registering a task for one's own sign-in needs no admin rights.</summary>
public sealed class TaskSchedulerStore : ILogonTaskStore
{
    private const int CreateOrUpdate = 6;
    private const int LogonInteractiveToken = 3;

    public bool IsEnabled(string name) => Find(name) is { } task && (bool)task.Enabled;

    public string? CommandOf(string name)
    {
        if (Find(name) is not { } task) return null;
        foreach (dynamic action in task.Definition.Actions)
            return (string)action.Path; // the first (only) exec action
        return null;
    }

    public void Register(string name, string xml) =>
        RootFolder().RegisterTask(name, xml, CreateOrUpdate, null, null, LogonInteractiveToken, null);

    public void Delete(string name)
    {
        if (Find(name) != null) RootFolder().DeleteTask(name, 0);
    }

    private static dynamic? Find(string name)
    {
        try
        {
            return RootFolder().GetTask(name);
        }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80070002))
        {
            return null; // no such task
        }
    }

    private static dynamic RootFolder()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("Task Scheduler not available");
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        return service.GetFolder(@"\");
    }
}
