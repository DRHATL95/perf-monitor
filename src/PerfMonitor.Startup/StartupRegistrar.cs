using Microsoft.Win32.TaskScheduler;

namespace PerfMonitor.Startup;

public static class StartupRegistrar
{
    private const string TaskName = "PerfMonitor Autostart";

    public static void Register(string exePath)
    {
        using var ts = new TaskService();
        var td = ts.NewTask();
        td.RegistrationInfo.Description = "Launch PerfMonitor at logon";
        td.Principal.RunLevel = TaskRunLevel.LUA;
        td.Triggers.Add(new LogonTrigger { Delay = TimeSpan.FromSeconds(10) });
        td.Actions.Add(new ExecAction(exePath, null, Path.GetDirectoryName(exePath)));
        td.Settings.DisallowStartIfOnBatteries = false;
        td.Settings.StopIfGoingOnBatteries = false;
        ts.RootFolder.RegisterTaskDefinition(TaskName, td);
    }

    public static void Unregister()
    {
        using var ts = new TaskService();
        ts.RootFolder.DeleteTask(TaskName, exceptionOnNotExists: false);
    }

    public static bool IsRegistered()
    {
        using var ts = new TaskService();
        return ts.GetTask(TaskName) is not null;
    }
}
