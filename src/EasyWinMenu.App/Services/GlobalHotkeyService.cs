using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using EasyWinMenu.Core.Models;

namespace EasyWinMenu.App.Services;

internal sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotKey = 0x0312;
    private const int HotkeyId = 0xA1F3;

    private readonly Window _host;
    private readonly HwndSource _source;
    private bool _registered;

    public event Action? HotkeyPressed;

    public GlobalHotkeyService(Window host)
    {
        _host = host;
        _source = HwndSource.FromHwnd(new WindowInteropHelper(host).Handle)!;
        _source.AddHook(WndProc);
    }

    public void Apply(LauncherBehavior behavior)
    {
        Unregister();

        if (!behavior.GlobalHotkeyEnabled || !Enum.TryParse<Key>(behavior.GlobalHotkeyKey, out var key))
        {
            return;
        }

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        var modifiers = ToNativeModifiers(behavior.GlobalHotkeyModifiers);
        var handle = new WindowInteropHelper(_host).Handle;

        _registered = RegisterHotKey(handle, HotkeyId, modifiers, virtualKey);
    }

    private void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        UnregisterHotKey(new WindowInteropHelper(_host).Handle, HotkeyId);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke();
            handled = true;
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
        Unregister();
        _source.RemoveHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
