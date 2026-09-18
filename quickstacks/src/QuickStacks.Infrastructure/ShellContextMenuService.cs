using System.Runtime.InteropServices;
using QuickStacks.Domain;

namespace QuickStacks.Infrastructure;

/// <summary>
/// Hospeda o menu de contexto genuíno do Explorer via interfaces COM (IContextMenu, IContextMenu2, IContextMenu3).
/// Exibe todas as opções registradas no sistema operacional (7-Zip, Git, antivírus, etc.) - Fase 19.
/// </summary>
public sealed class ShellContextMenuService : IShellContextMenuService
{
    private const int CmdFirst = 1;
    private const int CmdLast = 0x7FFF;

    private const uint CmfNormal = 0x00000000;
    private const uint CmfExplore = 0x00000004;
    private const uint CmfExtendedVerbs = 0x00000100;

    private const uint TpmLeftAlign = 0x0000;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;

    private const uint WmInitMenuPopup = 0x0117;
    private const uint WmDrawItem = 0x002B;
    private const uint WmMeasureItem = 0x002C;
    private const uint WmMenuChar = 0x0120;

    private const int SwShowNormal = 1;
    private const uint SubclassId = 0x5153; // "QS"

    private static Guid ShellFolderGuid = new("000214E6-0000-0000-C000-000000000046");
    private static Guid ContextMenuGuid = new("000214E4-0000-0000-C000-000000000046");

    private IContextMenu2? _activeContextMenu2;
    private IContextMenu3? _activeContextMenu3;
    private readonly SubclassProc _subclassProc;

    public ShellContextMenuService()
    {
        _subclassProc = SubclassCallback;
    }

    public bool TryShow(IntPtr windowHandle, string path, int screenX, int screenY, bool extendedVerbs = false)
    {
        if (windowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var pidl = IntPtr.Zero;
        var parentPtr = IntPtr.Zero;
        var contextMenuPtr = IntPtr.Zero;
        var menu = IntPtr.Zero;
        object? parentFolder = null;
        IContextMenu? contextMenu = null;
        var subclassInstalled = false;

        try
        {
            if (SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _) != 0 || pidl == IntPtr.Zero)
            {
                return false;
            }

            var folderGuid = ShellFolderGuid;
            if (SHBindToParent(pidl, ref folderGuid, out parentPtr, out var childPidl) != 0 || parentPtr == IntPtr.Zero)
            {
                return false;
            }

            parentFolder = Marshal.GetObjectForIUnknown(parentPtr);
            if (parentFolder is not IShellFolder folder)
            {
                return false;
            }

            var menuGuid = ContextMenuGuid;
            var result = folder.GetUIObjectOf(windowHandle, 1, [childPidl], ref menuGuid, IntPtr.Zero, out contextMenuPtr);
            if (result != 0 || contextMenuPtr == IntPtr.Zero)
            {
                return false;
            }

            contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(contextMenuPtr);
            _activeContextMenu2 = contextMenu as IContextMenu2;
            _activeContextMenu3 = contextMenu as IContextMenu3;

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

            if (_activeContextMenu2 is not null || _activeContextMenu3 is not null)
            {
                subclassInstalled = SetWindowSubclass(windowHandle, _subclassProc, (UIntPtr)SubclassId, UIntPtr.Zero);
            }

            SetForegroundWindow(windowHandle);

            var command = TrackPopupMenuEx(
                menu,
                TpmReturnCmd | TpmRightButton | TpmLeftAlign,
                screenX,
                screenY,
                windowHandle,
                IntPtr.Zero);

            PostMessage(windowHandle, 0, IntPtr.Zero, IntPtr.Zero);

            if (command >= CmdFirst)
            {
                Invoke(contextMenu, windowHandle, command - CmdFirst);
            }

            return true;
        }
        catch (COMException)
        {
            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (subclassInstalled)
            {
                RemoveWindowSubclass(windowHandle, _subclassProc, (UIntPtr)SubclassId);
            }

            _activeContextMenu2 = null;
            _activeContextMenu3 = null;

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

    private IntPtr SubclassCallback(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData)
    {
        if (uMsg is WmInitMenuPopup or WmDrawItem or WmMeasureItem or WmMenuChar)
        {
            try
            {
                if (_activeContextMenu3 is not null)
                {
                    if (_activeContextMenu3.HandleMenuMsg2((int)uMsg, wParam, lParam, out var plResult) == 0)
                    {
                        return plResult;
                    }
                }
                else if (_activeContextMenu2 is not null)
                {
                    if (_activeContextMenu2.HandleMenuMsg((int)uMsg, wParam, lParam) == 0)
                    {
                        return IntPtr.Zero;
                    }
                }
            }
            catch (COMException)
            {
            }
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private static void Invoke(IContextMenu contextMenu, IntPtr hwnd, int offset)
    {
        var invoke = new CmInvokeCommandInfo
        {
            cbSize = Marshal.SizeOf<CmInvokeCommandInfo>(),
            hwnd = hwnd,
            lpVerb = new IntPtr(offset),
            nShow = SwShowNormal,
        };

        contextMenu.InvokeCommand(ref invoke);
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }
    }

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData);

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

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
}
