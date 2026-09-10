namespace EasyWinMenu.Core.Models;

public sealed class LauncherBehavior
{
    public TrayClickMode ClickMode { get; set; } = TrayClickMode.SingleClick;
    public bool GlobalHotkeyEnabled { get; set; }
    public HotkeyModifiers GlobalHotkeyModifiers { get; set; } = HotkeyModifiers.Control | HotkeyModifiers.Shift;
    public string GlobalHotkeyKey { get; set; } = "Space";

    public LauncherBehavior Clone()
    {
        return new LauncherBehavior
        {
            ClickMode = ClickMode,
            GlobalHotkeyEnabled = GlobalHotkeyEnabled,
            GlobalHotkeyModifiers = GlobalHotkeyModifiers,
            GlobalHotkeyKey = GlobalHotkeyKey
        };
    }
}
