using System.Diagnostics;
using QuickStacks.Domain;
using Xunit;

namespace QuickStacks.UnitTests;

public class LaunchPlannerTests
{
    [Fact]
    public void BuildPlan_NormalExecutable_SetsFileNameAndNormalWindowStyle()
    {
        var item = MenuItem.CreateShortcut("Notepad", null, MenuItemType.Executable, @"C:\Windows\notepad.exe", 0);
        item.ExecutionMode = ExecutionMode.Normal;

        var plan = LaunchPlanner.BuildPlan(item);

        Assert.Equal(@"C:\Windows\notepad.exe", plan.FileName);
        Assert.Equal(ProcessWindowStyle.Normal, plan.WindowStyle);
        Assert.Null(plan.Verb);
        Assert.True(plan.UseShellExecute);
    }

    [Fact]
    public void BuildPlan_AdministratorMode_SetsVerbRunas()
    {
        var item = MenuItem.CreateShortcut("Powershell Admin", null, MenuItemType.Executable, @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", 0);
        item.ExecutionMode = ExecutionMode.Administrator;

        var plan = LaunchPlanner.BuildPlan(item);

        Assert.Equal("runas", plan.Verb);
        Assert.Equal(ProcessWindowStyle.Normal, plan.WindowStyle);
    }

    [Fact]
    public void BuildPlan_MinimizedMode_SetsWindowStyleMinimized()
    {
        var item = MenuItem.CreateShortcut("Backup Tool", null, MenuItemType.Executable, @"C:\Tools\backup.exe", 0);
        item.ExecutionMode = ExecutionMode.Minimized;

        var plan = LaunchPlanner.BuildPlan(item);

        Assert.Equal(ProcessWindowStyle.Minimized, plan.WindowStyle);
        Assert.Null(plan.Verb);
    }

    [Fact]
    public void BuildPlan_MaximizedMode_SetsWindowStyleMaximized()
    {
        var item = MenuItem.CreateShortcut("IDE", null, MenuItemType.Executable, @"C:\Tools\ide.exe", 0);
        item.ExecutionMode = ExecutionMode.Maximized;

        var plan = LaunchPlanner.BuildPlan(item);

        Assert.Equal(ProcessWindowStyle.Maximized, plan.WindowStyle);
        Assert.Null(plan.Verb);
    }

    [Fact]
    public void BuildPlan_CommandItem_WrapsWithCmdExe()
    {
        var item = MenuItem.CreateShortcut("Ping Gateway", null, MenuItemType.Command, "ping 192.168.1.1", 0);
        item.Arguments = "-t";

        var plan = LaunchPlanner.BuildPlan(item);

        Assert.Equal("cmd.exe", plan.FileName);
        Assert.Equal("/c \"ping 192.168.1.1\" -t", plan.Arguments);
    }

    [Fact]
    public void BuildPlan_ExpandsEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("TEST_QUICKSTACKS_VAR", @"C:\AppFolder");
        try
        {
            var item = MenuItem.CreateShortcut("Env App", null, MenuItemType.Executable, @"%TEST_QUICKSTACKS_VAR%\app.exe", 0);
            item.Arguments = @"--data %TEST_QUICKSTACKS_VAR%\data";
            item.WorkingDirectory = @"%TEST_QUICKSTACKS_VAR%";

            var plan = LaunchPlanner.BuildPlan(item);

            Assert.Equal(@"C:\AppFolder\app.exe", plan.FileName);
            Assert.Equal(@"--data C:\AppFolder\data", plan.Arguments);
            Assert.Equal(@"C:\AppFolder", plan.WorkingDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_QUICKSTACKS_VAR", null);
        }
    }

    [Fact]
    public void ResolveExecutablePath_BareExecutable_FindsCandidateInPath()
    {
        var fakePath = @"C:\bin;D:\other";
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\bin\mytool.exe"
        };

        var resolved = LaunchPlanner.ResolveExecutablePath(
            "mytool",
            pathEnvironment: fakePath,
            fileExists: path => existing.Contains(path),
            directoryExists: _ => false);

        Assert.Equal(@"C:\bin\mytool.exe", resolved);
    }

    [Fact]
    public void BuildPlan_AutoDetectsWorkingDirectory_WhenFileExists()
    {
        var fakeFile = @"C:\MyApp\launcher.exe";
        var item = MenuItem.CreateShortcut("Launcher", null, MenuItemType.Executable, fakeFile, 0);

        var plan = LaunchPlanner.BuildPlan(
            item,
            fileExists: p => p == fakeFile,
            directoryExists: _ => false);

        Assert.Equal(@"C:\MyApp", plan.WorkingDirectory);
    }

    [Fact]
    public void BuildPlan_ModeOverride_PreemptsItemDefaultMode()
    {
        var item = MenuItem.CreateShortcut("Editor", null, MenuItemType.Executable, @"C:\Tools\editor.exe", 0);
        item.ExecutionMode = ExecutionMode.Normal;

        var plan = LaunchPlanner.BuildPlan(item, modeOverride: ExecutionMode.Administrator);

        Assert.Equal("runas", plan.Verb);
    }
}
