using System.Runtime.InteropServices;
using System.Text;
using QuickStacks.Domain;

namespace QuickStacks.Infrastructure;

/// <summary>
/// Implementacao real de <see cref="ILnkResolver"/> via COM (IShellLinkW/IPersistFile) - a
/// mesma API que o proprio Explorer usa para ler um .lnk. Sem nenhum pacote NuGet: o
/// interop COM classico ja funciona direto no .NET 8 (net8.0-windows).
/// </summary>
public sealed class LnkResolver : ILnkResolver
{
    private const int MaxPath = 260;

    public LnkShortcutInfo Resolve(string lnkFilePath)
    {
        var link = (IShellLinkW)new ShellLink();
        ((IPersistFile)link).Load(lnkFilePath, 0 /* STGM_READ */);

        var targetBuilder = new StringBuilder(MaxPath);
        link.GetPath(targetBuilder, targetBuilder.Capacity, IntPtr.Zero, 0);

        var argsBuilder = new StringBuilder(MaxPath);
        link.GetArguments(argsBuilder, argsBuilder.Capacity);

        var workingDirBuilder = new StringBuilder(MaxPath);
        link.GetWorkingDirectory(workingDirBuilder, workingDirBuilder.Capacity);

        var iconBuilder = new StringBuilder(MaxPath);
        link.GetIconLocation(iconBuilder, iconBuilder.Capacity, out _);

        var displayName = Path.GetFileNameWithoutExtension(lnkFilePath);

        return new LnkShortcutInfo(
            DisplayName: displayName,
            TargetPath: targetBuilder.ToString(),
            Arguments: NullIfEmpty(argsBuilder.ToString()),
            WorkingDirectory: NullIfEmpty(workingDirBuilder.ToString()),
            IconLocation: NullIfEmpty(iconBuilder.ToString()));
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);

        void GetIDList(out IntPtr ppidl);

        void SetIDList(IntPtr pidl);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        void GetHotkey(out short pwHotkey);

        void SetHotkey(short wHotkey);

        void GetShowCmd(out int piShowCmd);

        void SetShowCmd(int iShowCmd);

        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

        void Resolve(IntPtr hwnd, uint fFlags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);

        [PreserveSig]
        int IsDirty();

        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);

        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);

        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);

        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
