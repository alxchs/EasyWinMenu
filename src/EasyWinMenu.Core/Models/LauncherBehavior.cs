namespace EasyWinMenu.Core.Models;

public sealed class LauncherBehavior
{
    public TrayClickMode ClickMode { get; set; } = TrayClickMode.SingleClick;
    public bool GlobalHotkeyEnabled { get; set; }
    public HotkeyModifiers GlobalHotkeyModifiers { get; set; } = HotkeyModifiers.Control | HotkeyModifiers.Shift;
    public string GlobalHotkeyKey { get; set; } = "Space";

    /// <summary>
    /// A hotkey that un-minimizes every group. Show Desktop (and Win+M) minimizes any
    /// visible top-level window, taskbar button or not, but the matching restore only
    /// walks the taskbar's own list - a group has no taskbar button by design, so it
    /// stays minimized after the shell's own restore unless asked for explicitly.
    /// </summary>
    public bool RestoreGroupsHotkeyEnabled { get; set; } = true;
    public HotkeyModifiers RestoreGroupsHotkeyModifiers { get; set; } = HotkeyModifiers.Windows | HotkeyModifiers.Control | HotkeyModifiers.Alt;
    public string RestoreGroupsHotkeyKey { get; set; } = "D";
    public AppThemeMode AppTheme { get; set; } = AppThemeMode.System;
    public string? Language { get; set; }

    public LauncherBehavior Clone()
    {
        return new LauncherBehavior
        {
            ClickMode = ClickMode,
            GlobalHotkeyEnabled = GlobalHotkeyEnabled,
            GlobalHotkeyModifiers = GlobalHotkeyModifiers,
            GlobalHotkeyKey = GlobalHotkeyKey,
            RestoreGroupsHotkeyEnabled = RestoreGroupsHotkeyEnabled,
            RestoreGroupsHotkeyModifiers = RestoreGroupsHotkeyModifiers,
            RestoreGroupsHotkeyKey = RestoreGroupsHotkeyKey,
            AppTheme = AppTheme,
            Language = Language
        };
    }
}
