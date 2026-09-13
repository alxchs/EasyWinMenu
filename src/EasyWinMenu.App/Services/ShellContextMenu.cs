using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EasyWinMenu.App.Services;

/// <summary>
/// Shows the genuine Explorer context menu for a path — the same one, with whatever
/// entries 7-Zip, Git or an antivirus have registered. It is a Win32 menu drawn by the
/// shell, so it does not follow the app theme; that is the price of it being real.
/// </summary>
internal static class ShellContextMenu
{
    private const int CmdFirst = 1;
    private const int CmdLast = 0x7FFF;

    private const uint CmfNormal = 0x00000000;
    private const uint CmfExplore = 0x00000004;
    private const uint CmfExtendedVerbs = 0x00000100;

    private const uint TpmLeftAlign = 0x0000;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;

    private const int WmInitMenuPopup = 0x0117;
    private const int WmDrawItem = 0x002B;
    private const int WmMeasureItem = 0x002C;
    private const int WmMenuChar = 0x0120;

    private const int SwShowNormal = 1;

    private static Guid _shellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static Guid _contextMenuId = new("000214E4-0000-0000-C000-000000000046");

    /// <summary>
    /// Opens the shell menu at <paramref name="screenPoint"/> (device pixels) and runs
    /// whatever the user picks. Returns false when the shell would not produce a menu
    /// for this path, so the caller can fall back to its own.
    /// </summary>
    public static bool TryShow(Window owner, string path, System.Drawing.Point screenPoint, bool extendedVerbs)
    {
        var hwnd = new WindowInteropHelper(owner).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var pidl = IntPtr.Zero;
        var parentPtr = IntPtr.Zero;
        var contextMenuPtr = IntPtr.Zero;
        var menu = IntPtr.Zero;
        object? parentFolder = null;
        IContextMenu? contextMenu = null;
        IContextMenu2? contextMenu2 = null;
        IContextMenu3? contextMenu3 = null;
        HwndSource? source = null;
        HwndSourceHook? hook = null;

        try
        {
            if (SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _) != 0 || pidl == IntPtr.Zero)
            {
                return false;
            }

            if (SHBindToParent(pidl, ref _shellFolderId, out parentPtr, out var childPidl) != 0 || parentPtr == IntPtr.Zero)
            {
                return false;
            }

            parentFolder = Marshal.GetObjectForIUnknown(parentPtr);
            if (parentFolder is not IShellFolder folder)
            {
                return false;
            }

            var result = folder.GetUIObjectOf(hwnd, 1, [childPidl], ref _contextMenuId, IntPtr.Zero, out contextMenuPtr);
            if (result != 0 || contextMenuPtr == IntPtr.Zero)
            {
                return false;
            }

            contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(contextMenuPtr);
            contextMenu2 = contextMenu as IContextMenu2;
            contextMenu3 = contextMenu as IContextMenu3;

            menu = CreatePopupMenu();
            if (menu == IntPtr.Zero)
            {
                return false;
            }

            var flags = CmfNormal | CmfExplore | (extendedVerbs ? CmfExtendedVerbs : 0);
            if (contextMenu.QueryContextMenu(menu, 0, CmdFirst, CmdLast, flags) < 0)
            {
                return false;
            }

            // Owner-drawn entries (icons, fly-outs from shell extensions) only paint if
            // these messages reach the menu object while it is up.
            if (contextMenu2 is not null || contextMenu3 is not null)
            {
                source = HwndSource.FromHwnd(hwnd);
                if (source is not null)
                {
                    hook = (IntPtr _, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                        ForwardMenuMessage(contextMenu2, contextMenu3, msg, wParam, lParam, ref handled);
                    source.AddHook(hook);
                }
            }

            // The menu dismisses on its own only while our window owns the foreground.
            SetForegroundWindow(hwnd);

            var command = TrackPopupMenuEx(
                menu,
                TpmReturnCmd | TpmRightButton | TpmLeftAlign,
                screenPoint.X,
                screenPoint.Y,
                hwnd,
                IntPtr.Zero);

            PostMessage(hwnd, 0, IntPtr.Zero, IntPtr.Zero);

            if (command >= CmdFirst)
            {
                Invoke(contextMenu, hwnd, command - CmdFirst);
            }

            return true;
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            if (hook is not null)
            {
                source?.RemoveHook(hook);
            }

            if (menu != IntPtr.Zero)
            {
                DestroyMenu(menu);
            }

            Release(contextMenu);
            Release(parentFolder);

            if (contextMenuPtr != IntPtr.Zero)
            {
                Marshal.Release(contextMenuPtr);
            }

            if (parentPtr != IntPtr.Zero)
            {
                Marshal.Release(parentPtr);
            }

            if (pidl != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }
    }

    private static void Invoke(IContextMenu contextMenu, IntPtr hwnd, int offset)
    {
        var invoke = new CmInvokeCommandInfo
        {
            cbSize = Marshal.SizeOf<CmInvokeCommandInfo>(),
            hwnd = hwnd,

            // A verb can be given either as a name or as the command's offset packed
            // into the pointer; the offset is the only form that works for every entry.
            lpVerb = new IntPtr(offset),
            nShow = SwShowNormal
        };

        contextMenu.InvokeCommand(ref invoke);
    }

    private static IntPtr ForwardMenuMessage(
        IContextMenu2? contextMenu2,
        IContextMenu3? contextMenu3,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (msg is not (WmInitMenuPopup or WmDrawItem or WmMeasureItem or WmMenuChar))
        {
            return IntPtr.Zero;
        }

        try
        {
            if (contextMenu3 is not null)
            {
                contextMenu3.HandleMenuMsg2(msg, wParam, lParam, out var result);
                handled = true;
                return result;
            }

            if (contextMenu2 is not null)
            {
                contextMenu2.HandleMenuMsg(msg, wParam, lParam);
                handled = true;
            }
        }
        catch (COMException)
        {
            // A misbehaving extension must not take the window down with it.
        }

        return IntPtr.Zero;
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CmInvokeCommandInfo
    {
        public int cbSize;
        public int fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpDirectory;
        public int nShow;
        public int dwHotKey;
        public IntPtr hIcon;
    }

    // Only GetUIObjectOf is called, but every slot before it has to be declared so the
    // vtable offsets line up.
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, ref uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, int grfFlags, out IntPtr ppenumIDList);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetAttributesOf(uint cidl, [In, MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint rgfInOut);
        [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [In, MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);
        [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, int idCmdFirst, int idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CmInvokeCommandInfo pici);
        [PreserveSig] int GetCommandString(IntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, int cchMax);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F4-0000-0000-C000-000000000046")]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, int idCmdFirst, int idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CmInvokeCommandInfo pici);
        [PreserveSig] int GetCommandString(IntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, int cchMax);
        [PreserveSig] int HandleMenuMsg(int uMsg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, int idCmdFirst, int idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CmInvokeCommandInfo pici);
        [PreserveSig] int GetCommandString(IntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, int cchMax);
        [PreserveSig] int HandleMenuMsg(int uMsg, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(int uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr ppidlLast);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hmenu, uint flags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
