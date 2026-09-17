using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace QuickStacks.UI;

/// <summary>
/// Atalhos globais do sistema (Fase 13, modelo GlobalHotkeyService.cs do EasyWinMenu): abrir o
/// popup e restaurar os grupos soltos. WinUI3 nao tem HwndSource (WPF) pra encaixar um WndProc
/// customizado - aqui o HWND real por baixo do TrayIconWindow (sempre viva, mesmo escondida) e'
/// obtido via WindowNative.GetWindowHandle e "subclassado" com SetWindowSubclass (comctl32,
/// compoe com outros subclasses em vez de substituir o WNDPROC inteiro como SetWindowLongPtr
/// faria) so' pra interceptar WM_HOTKEY - o resto das mensagens segue pro WNDPROC original.
/// </summary>
internal sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotKey = 0x0312;
    private const int OpenPopupHotkeyId = 0xA1F3;
    private const int RestoreGroupsHotkeyId = 0xA1F4;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    private readonly IntPtr _hwnd;
    private readonly SubclassProcDelegate _subclassProcDelegate;
    private bool _openPopupRegistered;
    private bool _restoreGroupsRegistered;

    public event Action? OpenPopupRequested;

    public event Action? RestoreGroupsRequested;

    public GlobalHotkeyService(Window host)
    {
        _hwnd = WindowNative.GetWindowHandle(host);
        _subclassProcDelegate = SubclassProc;
        SetWindowSubclass(_hwnd, _subclassProcDelegate, UIntPtr.Zero, IntPtr.Zero);
    }

    public void SetOpenPopupHotkeyEnabled(bool enabled)
    {
        if (enabled == _openPopupRegistered)
        {
            return;
        }

        _openPopupRegistered = enabled
            ? RegisterHotKey(_hwnd, OpenPopupHotkeyId, ModControl | ModAlt, (uint)VirtualKey.Q)
            : UnregisterIfNeeded(OpenPopupHotkeyId);
    }

    public void SetRestoreGroupsHotkeyEnabled(bool enabled)
    {
        if (enabled == _restoreGroupsRegistered)
        {
            return;
        }

        _restoreGroupsRegistered = enabled
            ? RegisterHotKey(_hwnd, RestoreGroupsHotkeyId, ModWin | ModControl | ModAlt, (uint)VirtualKey.D)
            : UnregisterIfNeeded(RestoreGroupsHotkeyId);
    }

    private bool UnregisterIfNeeded(int id)
    {
        UnregisterHotKey(_hwnd, id);
        return false;
    }

    /// <summary>
    /// Uma excecao gerenciada que escapa de um callback nativo (o WNDPROC subclassado, chamado
    /// pelo Windows via P/Invoke) nao tem como ser desenrolada normalmente - vira um fast-fail
    /// fatal (STATUS_STOWED_EXCEPTION, 0xC000027B, reproduzido e confirmado: qualquer coisa que
    /// ShowPopup/RestoreDesktopGroups lance aqui derrubava o processo inteiro sem log nenhum).
    /// Por isso os handlers rodam dentro de um try/catch que nunca deixa nada atravessar de
    /// volta pro codigo nativo do Windows.
    /// </summary>
    private IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (msg == WmHotKey)
        {
            try
            {
                switch (wParam.ToInt32())
                {
                    case OpenPopupHotkeyId:
                        OpenPopupRequested?.Invoke();
                        break;
                    case RestoreGroupsHotkeyId:
                        RestoreGroupsRequested?.Invoke();
                        break;
                }
            }
            catch
            {
                // Ver comentario da funcao: nunca deixar uma excecao atravessar de volta pro WNDPROC nativo.
            }

            return IntPtr.Zero;
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_openPopupRegistered)
        {
            UnregisterHotKey(_hwnd, OpenPopupHotkeyId);
        }

        if (_restoreGroupsRegistered)
        {
            UnregisterHotKey(_hwnd, RestoreGroupsHotkeyId);
        }

        RemoveWindowSubclass(_hwnd, _subclassProcDelegate, UIntPtr.Zero);
    }

    private enum VirtualKey : uint
    {
        Q = 0x51,
        D = 0x44,
    }

    private delegate IntPtr SubclassProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProcDelegate pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProcDelegate pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
