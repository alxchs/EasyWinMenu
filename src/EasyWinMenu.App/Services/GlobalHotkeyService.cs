using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using EasyWinMenu.Core.Models;

namespace EasyWinMenu.App.Services;

/// <summary>Registers the app's two system-wide hotkeys: open the tray menu, and restore hidden groups.</summary>
internal sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotKey = 0x0312;
    private const int MenuHotkeyId = 0xA1F3;
    private const int RestoreHotkeyId = 0xA1F4;

    private readonly Window _host;
    private readonly HwndSource _source;
    private bool _menuRegistered;
    private bool _restoreRegistered;

    /// <summary>The configured shortcut for opening the tray menu.</summary>
    public event Action? HotkeyPressed;

    /// <summary>
    /// The configured shortcut for un-minimizing groups Windows hid and never brought
    /// back — the fix for Show Desktop's one-way trip on windows with no taskbar button.
    /// </summary>
    public event Action? RestoreGroupsRequested;

    public GlobalHotkeyService(Window host)
    {
        _host = host;
        _source = HwndSource.FromHwnd(new WindowInteropHelper(host).Handle)!;
        _source.AddHook(WndProc);
    }

    public void Apply(LauncherBehavior behavior)
    {
        var handle = new WindowInteropHelper(_host).Handle;

        Unregister(handle, MenuHotkeyId, ref _menuRegistered);
        Unregister(handle, RestoreHotkeyId, ref _restoreRegistered);

        if (behavior.GlobalHotkeyEnabled && Enum.TryParse<Key>(behavior.GlobalHotkeyKey, out var menuKey))
        {
            _menuRegistered = RegisterHotKey(
                handle,
                MenuHotkeyId,
                ToNativeModifiers(behavior.GlobalHotkeyModifiers),
                (uint)KeyInterop.VirtualKeyFromKey(menuKey));
        }

        if (behavior.RestoreGroupsHotkeyEnabled && Enum.TryParse<Key>(behavior.RestoreGroupsHotkeyKey, out var restoreKey))
        {
            _restoreRegistered = RegisterHotKey(
                handle,
                RestoreHotkeyId,
                ToNativeModifiers(behavior.RestoreGroupsHotkeyModifiers),
                (uint)KeyInterop.VirtualKeyFromKey(restoreKey));
        }
    }

    private static void Unregister(IntPtr handle, int id, ref bool registered)
    {
        if (!registered)
        {
            return;
        }

        UnregisterHotKey(handle, id);
        registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotKey)
        {
            return IntPtr.Zero;
        }

        switch (wParam.ToInt32())
        {
            case MenuHotkeyId:
                HotkeyPressed?.Invoke();
                handled = true;
                break;

            case RestoreHotkeyId:
                RestoreGroupsRequested?.Invoke();
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    private static uint ToNativeModifiers(HotkeyModifiers modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            result |= 0x0001;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            result |= 0x0002;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            result |= 0x0004;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            result |= 0x0008;
        }

        return result;
    }

    public void Dispose()
    {
        var handle = new WindowInteropHelper(_host).Handle;
        Unregister(handle, MenuHotkeyId, ref _menuRegistered);
        Unregister(handle, RestoreHotkeyId, ref _restoreRegistered);
        _source.RemoveHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
